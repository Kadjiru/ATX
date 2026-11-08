using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using AionMeter.Core.Capture;
using AionMeter.Core.Combat;
using AionMeter.Core.Events;
using AionMeter.Core.Game;

namespace ArxBeta;

public sealed record ExpeditionSegment(Guid FightId, string Target, double Seconds, long Damage, long Healing);
public sealed record Expedition(Guid Id, string Name, string Build, DateTimeOffset StartedAt,
    List<ExpeditionSegment> Segments, DateTimeOffset? EndedAt = null, bool Interrupted = false);
public sealed record ObservedBoss(string Key, string Name, string Zone, bool Alive, DateTimeOffset When);

internal sealed class LiveCombat : IDisposable
{
    private readonly LegionStore _store;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly GameData _data;
    private readonly Dictionary<int, BossMap> _mapNames;
    private readonly ConcurrentQueue<(FightRecord Record, PartyRosterEvent? Roster)> _completed = new();
    private readonly ConcurrentDictionary<(int Server, int Map), FieldBossListEvent> _bossLists = new();
    private volatile PartyRosterEvent? _roster;
    private LiveCapture? _source;
    private CombatTracker? _tracker;
    private bool _stopping;
    private string? _error;

    public LiveCombat(LegionStore store)
    {
        _store = store;
        var folder = Path.Combine(AppContext.BaseDirectory, "GameData");
        _data = GameData.Load(folder, "ru");
        _mapNames = ReadBossMaps(Path.Combine(folder, "field_boss_maps.json"));
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
        _error = null;
        if (!NpcapAvailable)
        {
            _error = "Для живого режима установите Npcap.";
            Refresh();
            return false;
        }

        var tracker = new CombatTracker(_data, new MeterOptions { TargetMode = TargetMode.All, MinSavedFightMs = 0 });
        tracker.RosterReceived += roster => _roster = roster;
        tracker.EncounterFinished += record => _completed.Enqueue((record, _roster));
        tracker.FieldBossListed += list =>
        {
            if (list.Complete && list.ServerId > 0) _bossLists[(list.ServerId, list.MapId)] = list;
        };
        var source = new LiveCapture(_data, new LiveCaptureOptions { DeviceName = _store.Profile.CaptureAdapter });
        source.EventDecoded += tracker.Process;
        _tracker = tracker;
        _source = source;
        source.Start();
        _clock.Start();
        _store.SetFollowLive(true);
        Refresh();
        return true;
    }

    public async Task StopAsync()
    {
        if (_source is not { } source || _stopping) return;
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
        _tracker?.Reset();
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

    private void Refresh()
    {
        var now = DateTimeOffset.UtcNow;
        _tracker?.Tick(now.ToUnixTimeMilliseconds());
        Drain();
        var tracker = _tracker;
        var server = tracker?.SelfServerId ?? 0;
        var self = tracker?.SelfName;
        var roster = RecentRoster(_roster, now.ToUnixTimeMilliseconds());
        var source = _source;
        FightAnalysis? analysis = null;
        if (tracker?.Record(null, includeCasts: false) is { } record)
            analysis = new FightAnalysis(ToFight(record, roster, _store.Profile.DeclaredBuild));
        var identity = string.IsNullOrWhiteSpace(self) ? null : server > 0 && _data.Servers.TryGetValue(server, out var realm)
            ? $"{self} · {realm}" : self;
        var label = source is null ? _error ?? "Захват выключен" : StateText(source.Status.State);
        if (source?.Status.State == CaptureState.Capturing)
            label += roster is null ? " · наблюдаемые игроки" : $" · состав группы: {roster.Members.Count}";
        _store.UpdateLive(analysis, label, identity, Bosses(server));
    }

    private void Drain()
    {
        while (_completed.TryDequeue(out var item))
        {
            var summary = item.Record.Summary;
            if (summary.TotalDamage <= 0) continue;
            var roster = RecentRoster(item.Roster, summary.StartedAt.ToUnixTimeMilliseconds() + summary.CombatMs);
            var fight = ToFight(item.Record, roster, _store.Profile.CurrentExpedition?.Build ?? _store.Profile.DeclaredBuild);
            if (fight.Captured!.Summary.Combatants.Any(c => c.Damage > 0 || c.Healing > 0)) _store.Archive(fight);
        }
    }

    private static PartyRosterEvent? RecentRoster(PartyRosterEvent? roster, long time) =>
        roster is { Members.Count: > 0 } && time - roster.TimeMs <= 60_000 ? roster : null;

    internal static Fight ToFight(FightRecord record, PartyRosterEvent? roster, string build)
    {
        var summary = record.Summary;
        if (roster is not null)
        {
            var identities = roster.Members.Select(m => (m.Name.ToUpperInvariant(), m.ServerId)).ToHashSet();
            var uniqueNames = identities.GroupBy(i => i.Item1).Where(g => g.Count() == 1).Select(g => g.Key).ToHashSet();
            var observedNames = summary.Combatants.GroupBy(c => c.Name.ToUpperInvariant()).Where(g => g.Count() == 1)
                .Select(g => g.Key).ToHashSet();
            var selected = summary.Combatants.Where(c => c.IsSelf || (!c.IsUnknownSummons &&
                (identities.Contains((c.Name.ToUpperInvariant(), c.ServerId)) ||
                 c.ServerId == 0 && uniqueNames.Contains(c.Name.ToUpperInvariant()) && observedNames.Contains(c.Name.ToUpperInvariant())))).ToArray();
            var ids = selected.Select(c => c.ActorId).ToHashSet();
            var damage = selected.Sum(c => c.Damage);
            record = record with
            {
                Summary = summary with { Combatants = selected.Select(c => c with { Share = (double)c.Damage / Math.Max(1, damage) }).ToArray(),
                    TotalDamage = damage, PartyDps = damage / Math.Max(1, summary.CombatMs / 1000.0) },
                Details = record.Details.Where(d => ids.Contains(d.ActorId))
                    .Select(d => d with { Share = (double)d.Damage / Math.Max(1, damage) }).ToArray()
            };
        }
        var self = summary.Combatants.FirstOrDefault(c => c.IsSelf)?.Name ?? "";
        return new Fight(summary.Id, summary.Title, summary.StartedAt, Math.Max(1, summary.CombatMs / 1000.0),
            self, [], [], Captured: record, Build: string.IsNullOrWhiteSpace(build) ? null : build);
    }

    private IReadOnlyList<ObservedBoss> Bosses(int server)
    {
        if (server == 0) return [];
        var results = new List<ObservedBoss>();
        foreach (var pair in _bossLists.Where(pair => pair.Key.Server == server))
        {
            var list = pair.Value;
            var mapping = _mapNames.GetValueOrDefault(list.MapId);
            var zone = mapping?.Ru ?? $"Карта {list.MapId}";
            var npcs = mapping is null ? [] : _data.Npcs.Values
                .Where(npc => npc.IsBoss && npc.Code / 1000 == mapping.Block).OrderBy(npc => npc.Code).ToArray();
            var trusted = npcs.Length == list.Count;
            foreach (var slot in list.Slots)
            {
                var position = slot.SlotId - list.MapId * 100 - 1;
                var name = trusted && position >= 0 && position < npcs.Length ? npcs[position].Name : $"Босс #{slot.SlotId}";
                if (slot.AtMs <= 0 || slot.AtMs > 253402300799999L) continue;
                results.Add(new ObservedBoss($"{server}/{slot.SlotId}", name, zone, slot.Alive,
                    DateTimeOffset.FromUnixTimeMilliseconds(slot.AtMs)));
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

    private sealed record BossMap(int Block, string Ru);
    private sealed record BossMaps(Dictionary<int, BossMap> Maps);

    private static Dictionary<int, BossMap> ReadBossMaps(string file)
    {
        try { return JsonSerializer.Deserialize<BossMaps>(File.ReadAllText(file),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })?.Maps ?? []; }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return []; }
    }
}
