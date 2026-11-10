using System.Collections.Concurrent;
using System.IO;
using System.Windows.Threading;
using AionMeter.Core.Capture;
using AionMeter.Core.Combat;
using AionMeter.Core.Events;
using AionMeter.Core.Game;

namespace ArxBeta;

public sealed record ExpeditionSegment(Guid FightId, string Target, double Seconds, long Damage, long Healing);
public sealed record Expedition(Guid Id, string Name, string Build, DateTimeOffset StartedAt,
    List<ExpeditionSegment> Segments, DateTimeOffset? EndedAt = null, bool Interrupted = false);
public sealed record ObservedBoss(string Key, string Name, string Zone, bool Alive, DateTimeOffset When, string? CatalogId = null)
{
    internal string SelectionId => CatalogId ?? Key;
}

internal sealed class LiveCombat : IDisposable
{
    private readonly LegionStore _store;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly GameData _data;
    private readonly Dictionary<int, WorldBossCatalog.Zone> _bossMaps = [];
    private readonly ConcurrentQueue<(FightRecord Record, PartyRosterEvent? Roster, bool HasSelf)> _completed = new();
    private readonly ConcurrentDictionary<(int Server, int Map), FieldBossListEvent> _bossLists = new();
    private readonly object _sessionGate = new();
    private PartyRosterEvent? _roster;
    private LiveCapture? _source;
    private CombatTracker? _tracker;
    private bool _stopping;
    private Task? _stopTask;
    private string? _error;

    public LiveCombat(LegionStore store)
    {
        _store = store;
        var folder = Path.Combine(AppContext.BaseDirectory, "GameData");
        _data = GameData.Load(folder, "ru");
        _clock.Tick += (_, _) => Refresh();
    }

    public bool IsRunning => _source is not null;
    public bool IsStopping => _stopping;
    public static bool NpcapAvailable => LiveCapture.IsNpcapInstalled();
    public string Diagnostic => _source?.Status is { } s
        ? $"Состояние: {StateText(s.State)} · TCP-пакеты: {s.Packets:N0} · События игры: {s.Events:N0}"
        : _error ?? (NpcapAvailable ? "Npcap найден. Захват остановлен." : "Npcap не установлен.");

    public IReadOnlyList<(string Name, string Description)> Adapters()
    {
        if (!NpcapAvailable) return [];
        try { return LiveCapture.ListDevices(); }
        catch (Exception) { return []; }
    }

    public bool Start()
    {
        if (IsRunning || _stopping) return IsRunning;
        _stopTask = null;
        _error = null;
        if (!NpcapAvailable)
        {
            _error = "Для живого режима установите Npcap.";
            _store.SetFollowLive(true);
            Refresh();
            return false;
        }

        var tracker = new CombatTracker(_data, new MeterOptions { TargetMode = TargetMode.All, MinSavedFightMs = 0 });
        tracker.RosterReceived += roster => _roster = roster;
        tracker.EncounterFinished += record => _completed.Enqueue((record, _roster, tracker.LocalCharacter.HasValue));
        tracker.FieldBossListed += list =>
        {
            if (list.Complete && list.ServerId > 0) _bossLists[(list.ServerId, list.MapId)] = list;
        };
        var source = new LiveCapture(_data, new LiveCaptureOptions { DeviceName = _store.Profile.CaptureAdapter });
        source.EventDecoded += e => Process(tracker, e);
        _tracker = tracker;
        _source = source;
        source.Start();
        _clock.Start();
        _store.SetFollowLive(true);
        Refresh();
        return true;
    }

    public Task StopAsync() => _stopTask ??= _source is { } source ? StopCoreAsync(source) : Task.CompletedTask;

    private async Task StopCoreAsync(LiveCapture source)
    {
        _stopping = true;
        _clock.Stop();
        try { await Task.Run(source.Dispose); }
        finally
        {
            _source = null;
            _tracker?.Reset();
            Drain();
            _tracker = null;
            _roster = null;
            _stopping = false;
            Refresh();
        }
    }

    public void FinishSegment()
    {
        lock (_sessionGate) _tracker?.Reset();
        Drain();
        Refresh();
    }

    public void Dispose()
    {
        _clock.Stop();
        if (!_stopping) _source?.Dispose();
        _tracker?.Reset();
        Drain();
    }

    private void Process(CombatTracker tracker, GameEvent e)
    {
        lock (_sessionGate)
        {
            if (e is SelfIdentifiedEvent self && tracker.LocalCharacter is { } previous &&
                self.Name.Length > 0 && previous.Name.Length > 0 &&
                (!string.Equals(self.Name, previous.Name, StringComparison.OrdinalIgnoreCase) ||
                 (self.ServerId > 0 && previous.ServerId > 0 && self.ServerId != previous.ServerId)))
            {
                tracker.Reset();
                _roster = null;
            }
            if (e is PartyRosterEvent updated && _roster is { } prior)
            {
                var members = prior.Members.Select(m => (m.Name.ToUpperInvariant(), m.ServerId)).ToHashSet();
                if (!members.SetEquals(updated.Members.Select(m => (m.Name.ToUpperInvariant(), m.ServerId))))
                    tracker.Reset();
            }
            tracker.Process(e);
        }
    }

    private void Refresh()
    {
        (uint ActorId, string Name, int ServerId)? local;
        PartyRosterEvent? roster;
        FightRecord? record;
        lock (_sessionGate)
        {
            _tracker?.Tick(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            local = _tracker?.LocalCharacter;
            roster = _roster;
            record = _tracker?.Record(null, includeCasts: false);
        }
        Drain();
        var server = local?.ServerId ?? 0;
        var source = _source;
        var character = local is { } p ? new CharacterIdentity(p.ActorId, p.Name, p.ServerId) : null;
        FightAnalysis? analysis = null;
        if (record is not null)
        {
            analysis = new FightAnalysis(ToFight(record, roster, _store.Profile.DeclaredBuild,
                local.HasValue ? null : _store.Profile.CombatCharacterHint));
            if (character is null && analysis.Players.FirstOrDefault(player => player.IsSelf) is { ActorId: { } actor } matched)
                character = new CharacterIdentity(actor, matched.Player, matched.ServerId, false);
        }
        var identity = character?.DisplayName;
        if (identity is not null && character is { ServerId: > 0 } && _data.Servers.TryGetValue(character.ServerId, out var realm))
            identity += $" · {realm}";
        var label = source is null ? _error ?? "Захват выключен" : StateText(source.Status.State);
        if (source?.Status.State == CaptureState.Capturing)
            label += roster switch { null => " · состав ожидается", { Members.Count: 0 } => " · вне группы",
                _ => $" · группа: {roster.Members.Count}" };
        _store.UpdateLive(analysis, label, identity, Bosses(server), character, roster);
    }

    private void Drain()
    {
        while (_completed.TryDequeue(out var item))
        {
            var summary = item.Record.Summary;
            if (summary.TotalDamage <= 0) continue;
            var fight = ToFight(item.Record, item.Roster, _store.Profile.CurrentExpedition?.Build ?? _store.Profile.DeclaredBuild,
                item.HasSelf ? null : _store.Profile.CombatCharacterHint);
            if (fight.Captured!.Summary.Combatants.Any(c => c.Damage > 0 || c.Healing > 0)) _store.Archive(fight);
        }
    }

    internal static Fight ToFight(FightRecord record, PartyRosterEvent? roster, string build, string? characterHint = null)
    {
        var summary = record.Summary;
        var combatants = summary.Combatants.Where(c => !c.IsUnknownSummons).ToArray();
        var names = combatants.ToLookup(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var own = combatants.FirstOrDefault(c => c.IsSelf);
        if (own is null && !string.IsNullOrWhiteSpace(characterHint) && names[characterHint].Take(2).Count() == 1)
            own = names[characterHint].Single();
        var selected = combatants.Where(c => c.ActorId == own?.ActorId || (roster is not null &&
            PartyCombat.Match(roster, c.Name, c.ServerId, names[c.Name].Take(2).Count() == 1) is not null)).ToArray();
        var ids = selected.Select(c => c.ActorId).ToHashSet();
        var damage = selected.Sum(c => c.Damage);
        var duration = Math.Max(1, summary.CombatMs / 1000.0);
        record = record with
        {
            Summary = summary with { Combatants = selected.Select(c => c with { IsSelf = c.ActorId == own?.ActorId,
                Share = (double)c.Damage / Math.Max(1, damage) }).ToArray(), TotalDamage = damage, PartyDps = damage / duration },
            Details = record.Details.Where(d => ids.Contains(d.ActorId)).Select(d => d with
                { IsSelf = d.ActorId == own?.ActorId, Share = (double)d.Damage / Math.Max(1, damage) }).ToArray()
        };
        var self = own?.Name ?? "";
        return new Fight(summary.Id, summary.Title, summary.StartedAt, duration,
            self, [], [], Captured: record, Build: string.IsNullOrWhiteSpace(build) ? null : build);
    }

    private IReadOnlyList<ObservedBoss> Bosses(int server)
    {
        if (server == 0) return [];
        var results = new List<ObservedBoss>();
        foreach (var pair in _bossLists.Where(pair => pair.Key.Server == server))
        {
            var list = pair.Value;
            var mapping = _bossMaps.GetValueOrDefault(list.MapId);
            if (mapping?.Bosses.Length != list.Count && WorldBossCatalog.Identify(list) is { } identified)
                _bossMaps[list.MapId] = mapping = identified;
            var zone = mapping?.Name ?? $"Карта {list.MapId}";
            foreach (var slot in list.Slots)
            {
                var position = slot.SlotId - list.MapId * 100 - 1;
                var entry = mapping?.Bosses.Length == list.Count && position >= 0 && position < list.Count
                    ? mapping.Bosses[position] : null;
                var name = entry?.Name ?? $"Босс #{slot.SlotId}";
                if (slot.AtMs <= 0 || slot.AtMs > 253402300799999L) continue;
                results.Add(new ObservedBoss($"{server}/{slot.SlotId}", name, zone, slot.Alive,
                    DateTimeOffset.FromUnixTimeMilliseconds(slot.AtMs),
                    entry is not null && mapping is not null ? WorldBossCatalog.Key(mapping, entry) : null));
            }
        }
        return results.OrderBy(b => b.Alive).ThenBy(b => b.When).ToArray();
    }

    private static string StateText(CaptureState state) => state switch
    {
        CaptureState.Capturing => "Aion 2 Global обнаружена",
        CaptureState.WaitingForGame => "Ожидание пакетов Aion 2 Global",
        CaptureState.Starting => "Открытие сетевого адаптера",
        CaptureState.Error => "Ошибка захвата · проверьте Npcap и адаптер",
        _ => "Захват выключен"
    };

}
