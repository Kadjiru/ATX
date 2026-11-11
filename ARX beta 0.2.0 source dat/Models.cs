using System.Globalization;
using System.IO;
using System.Text.Json;
using Microsoft.VisualBasic.FileIO;
using AionMeter.Core.Combat;
using AionMeter.Core.Events;

namespace ArxBeta;

public sealed record Hit(double Second, string Player, string Skill, long Damage, long Healing, bool Critical);
public sealed record Totals(string Player, long Damage, long Healing);
public sealed record Fight(Guid Id, string Name, DateTimeOffset Date, double Duration, string Self,
    Hit[] Hits, Totals[] Entered, bool Demo = false, FightRecord? Captured = null, string? Build = null)
{
    public static Fight Empty { get; } = new(Guid.Empty, Loc.T("Ожидание отчёта"), DateTimeOffset.MinValue, 0, "", [], []);
}

public sealed record PlayerStats(string Player, long Damage, long Healing, int? Hits, int? Crits, double? Burst,
    uint? ActorId = null, long CombatPower = 0, long MaxHit = 0, double? ObservedCrit = null,
    int ServerId = 0, bool IsSelf = false)
{
    public double? CritRate => ObservedCrit ?? (Hits is > 0 && Crits is { } count ? (double)count / Hits.Value : null);
}

public sealed class FightAnalysis
{
    public Fight Fight { get; }
    public PlayerStats[] Players { get; }
    public long Damage { get; }
    public long Healing { get; }
    public double? Burst { get; }
    public double[] GroupCurve { get; }
    public double[] SelfCurve { get; }
    public double Dps => Rate(Damage);

    public FightAnalysis(Fight fight)
    {
        Fight = fight;
        if (fight.Captured is { } captured)
        {
            Players = captured.Summary.Combatants.Select(c => new PlayerStats(c.Name, c.Damage, c.Healing ?? 0,
                c.Hits, c.Hits > 0 ? (int)Math.Round(c.CritRate * c.Hits) : null, c.BurstDps,
                c.ActorId, c.CombatPower, c.MaxHit, c.Hits > 0 ? c.CritRate : null, c.ServerId, c.IsSelf)).ToArray();
            Damage = Players.Sum(p => p.Damage);
            Healing = Players.Sum(p => p.Healing);
            var width = Math.Max(1, captured.Details.Select(d => d.PerSecond.Count).DefaultIfEmpty().Max());
            var group = new long[width];
            var personal = new long[width];
            foreach (var player in captured.Details)
                for (var i = 0; i < player.PerSecond.Count; i++)
                {
                    group[i] += player.PerSecond[i];
                    if (player.IsSelf) personal[i] += player.PerSecond[i];
                }
            Burst = fight.Duration >= 5 ? BucketPeak(group) : null;
            GroupCurve = Curve(group, fight.Duration);
            SelfCurve = Curve(personal, fight.Duration);
            return;
        }
        Players = fight.Entered.Select(t => new PlayerStats(t.Player, t.Damage, t.Healing, null, null, null))
            .Concat(fight.Hits.GroupBy(h => h.Player, StringComparer.OrdinalIgnoreCase).Select(g => Summarize(g.Key, g)))
            .Select(p => p with { IsSelf = string.Equals(p.Player, fight.Self, StringComparison.OrdinalIgnoreCase) })
            .OrderByDescending(p => p.Damage).ToArray();
        Damage = Players.Sum(p => p.Damage);
        Healing = Players.Sum(p => p.Healing);
        Burst = Peak(fight.Hits, fight.Duration);
        var bin = Math.Max(1, Math.Ceiling(fight.Duration / 80));
        var bins = Math.Max(1, (int)Math.Ceiling(fight.Duration / bin));
        GroupCurve = fight.Hits.Length == 0 ? [] : new double[bins];
        SelfCurve = fight.Hits.Length == 0 ? [] : new double[bins];
        foreach (var hit in fight.Hits)
        {
            var slot = Math.Min(bins - 1, (int)(hit.Second / bin));
            var interval = Math.Min(bin, Math.Max(0.001, fight.Duration - slot * bin));
            GroupCurve[slot] += hit.Damage / interval;
            if (string.Equals(hit.Player, fight.Self, StringComparison.OrdinalIgnoreCase))
                SelfCurve[slot] += hit.Damage / interval;
        }
    }

    public double Rate(long amount) => Fight.Duration > 0 ? amount / Fight.Duration : 0;

    public PlayerStats[] Skills(string player, uint? actorId = null)
    {
        if (Fight.Captured is { } captured)
        {
            var detail = captured.Details.FirstOrDefault(c => actorId is { } id ? c.ActorId == id : c.Name == player);
            if (detail is null) return [];
            var heals = (detail.Heals ?? []).ToDictionary(row => row.SkillCode);
            var skills = detail.Skills.Select(s => new PlayerStats(s.Name, s.Damage, heals.GetValueOrDefault(s.SkillCode)?.Total ?? 0,
                s.Hits, s.CritRate is { } crit ? (int)Math.Round(crit * s.Hits) : null, null, MaxHit: s.Max, ObservedCrit: s.CritRate));
            return skills.Concat(heals.Values.Where(h => detail.Skills.All(s => s.SkillCode != h.SkillCode))
                .Select(h => new PlayerStats(h.Name, 0, h.Total, null, null, null)))
                .OrderByDescending(s => s.Damage + s.Healing).ToArray();
        }
        return Fight.Hits
        .Where(h => string.Equals(h.Player, player, StringComparison.OrdinalIgnoreCase))
        .GroupBy(h => h.Skill, StringComparer.OrdinalIgnoreCase).Select(g => Summarize(g.Key, g))
        .OrderByDescending(p => p.Damage).ThenByDescending(p => p.Healing).ToArray();
    }

    private static double[] Curve(long[] seconds, double duration)
    {
        var bin = Math.Max(1, (int)Math.Ceiling(seconds.Length / 80.0));
        var values = new double[(seconds.Length + bin - 1) / bin];
        for (var i = 0; i < seconds.Length; i++)
            values[i / bin] += seconds[i] / Math.Max(1, Math.Min(bin, duration - i / bin * bin));
        return values;
    }

    private static double BucketPeak(long[] seconds)
    {
        long total = 0, peak = 0;
        for (var i = 0; i < seconds.Length; i++)
        {
            total += seconds[i];
            if (i >= 5) total -= seconds[i - 5];
            peak = Math.Max(peak, total);
        }
        return peak / 5.0;
    }

    private PlayerStats Summarize(string name, IEnumerable<Hit> events)
    {
        var hits = events.ToArray();
        return new PlayerStats(name, hits.Sum(h => h.Damage), hits.Sum(h => h.Healing),
            hits.Count(h => h.Damage > 0), hits.Count(h => h.Damage > 0 && h.Critical), Peak(hits, Fight.Duration));
    }

    public static double? Peak(IEnumerable<Hit> events, double duration)
    {
        var hits = events.Where(h => h.Damage > 0).OrderBy(h => h.Second).ToArray();
        if (duration < 5 || hits.Length == 0) return null;
        long sum = 0, max = 0;
        for (int right = 0, left = 0; right < hits.Length; right++)
        {
            sum += hits[right].Damage;
            while (hits[right].Second - hits[left].Second >= 5) sum -= hits[left++].Damage;
            max = Math.Max(max, sum);
        }
        return max / 5.0;
    }
}

public static class FightImport
{
    private static readonly string[] Header = ["seconds", "player", "skill", "damage", "healing", "critical"];

    public static Fight Csv(string path, string name, string self)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > 5 * 1024 * 1024) throw new InvalidDataException(Loc.T("Файл больше 5 МБ."));
        using var parser = new TextFieldParser(stream) { Delimiters = [","], HasFieldsEnclosedInQuotes = true };
        if (parser.ReadFields() is not { } header || !header.SequenceEqual(Header, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException(Loc.T("Заголовок CSV: seconds,player,skill,damage,healing,critical"));
        var rows = new List<Hit>();
        while (!parser.EndOfData)
        {
            var line = parser.LineNumber;
            var fields = parser.ReadFields();
            if (fields is not { Length: 6 } ||
                !double.TryParse(fields[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var time) ||
                !double.IsFinite(time) || time is < 0 or > 43200 ||
                fields[1].Length is 0 or > 80 || fields[2].Length is 0 or > 100 ||
                !long.TryParse(fields[3], out var damage) || damage is < 0 or > 100_000_000_000 ||
                !long.TryParse(fields[4], out var healing) || healing is < 0 or > 100_000_000_000 ||
                !ParseCrit(fields[5], out var crit) || (damage == 0 && healing == 0))
                throw new InvalidDataException(Loc.F($"Проверьте данные в строке {line}."));
            rows.Add(new Hit(time, fields[1], fields[2], damage, healing, crit));
        }
        if (rows.Count == 0) throw new InvalidDataException(Loc.T("В CSV нет событий."));
        var duration = Math.Max(1, Math.Ceiling(rows.Max(h => h.Second)));
        return new Fight(Guid.NewGuid(), name, DateTimeOffset.Now, duration, self, rows.ToArray(), []);
    }

    private static bool ParseCrit(string field, out bool value)
    {
        value = field == "1";
        return field is "0" or "1" || bool.TryParse(field, out value);
    }

    public static Fight Sample()
    {
        var rng = new Random(45);
        string[] players = ["Aster", "Liora", "Kestrel", "Mira", "Riven"];
        string[] skills = [Loc.T("Первый удар"), Loc.T("Удар по области"), Loc.T("Серия атак"), Loc.T("Завершающий удар")];
        var rows = new List<Hit>();
        for (var second = 0; second < 96; second++)
        {
            for (var player = 0; player < players.Length; player++)
            {
                var burst = second % 28 < 5 ? 1.9 : 1.0;
                var crit = rng.NextDouble() < 0.3;
                var damage = (long)(rng.Next(120_000, 350_000) * burst * (crit ? 1.4 : 1) / (1 + 0.2 * player));
                rows.Add(new Hit(second + player * 0.14, players[player], skills[rng.Next(skills.Length)], damage,
                    player == 3 ? rng.Next(2500, 15000) : 0, crit));
            }
        }
        return new Fight(Guid.NewGuid(), Loc.T("Тренировочная экспедиция"), DateTimeOffset.Now, 96, "Aster", rows.ToArray(), [], true);
    }
}

public sealed record EventRule(Guid Id, string Name, DateTimeOffset AnchorUtc, int RepeatMinutes, bool Enabled = true)
{
    internal static readonly Guid RiftId = new("55025e60-21df-42de-9e89-a22f94b8c426");
    internal static readonly EventRule DefaultRift = new(RiftId, "Пространственный разлом", DateTimeOffset.UnixEpoch, 180);
    internal static readonly Guid ShugoFestivalId = new("c885ed94-84c1-47e1-b8aa-11ff600f4c16");

    internal string DisplayName =>
        (Id == RiftId && Name == "Пространственный разлом") || (Id == ShugoFestivalId && Name == "Фестиваль шуго")
            ? Loc.T(Name) : Name;

    public DateTimeOffset? Next(DateTimeOffset now)
    {
        if (!Enabled) return null;
        if (AnchorUtc >= now) return AnchorUtc;
        if (RepeatMinutes <= 0) return null;
        var elapsed = (now - AnchorUtc).Ticks;
        var period = TimeSpan.FromMinutes(RepeatMinutes).Ticks;
        return AnchorUtc.AddTicks((elapsed / period + (elapsed % period == 0 ? 0 : 1)) * period);
    }
}

public sealed record SessionTask(Guid Id, Guid SessionId, string Text, bool Done);
public sealed record KinaEntry(Guid Id, Guid SessionId, DateTimeOffset Date, string Note, long Amount);
public sealed record MapPin(Guid Id, string Zone, double X, double Y, string Category, string Name, bool Found, string? Layer = null);
public sealed record HudLayout(double Left, double Top, double Width, double Height, double Opacity, HudDensity? Density = null);

public sealed record CharacterBuild(Guid Id, string Name, string Server, string ClassId, bool Solo)
{
    public override string ToString() => Server.Length == 0 ? Name : $"{Name} · {Server}";
}

public sealed class Profile
{
    public Guid SessionId { get; set; } = Guid.NewGuid();
    public string Character { get; set; } = "";
    public string Server { get; set; } = "";
    public string Language { get; set; } = "ru";
    public string AppTheme { get; set; } = ThemePalette.Default;
    public string HudTheme { get; set; } = ThemePalette.FollowApp;
    public List<Fight> History { get; set; } = [];
    public List<SessionTask> Tasks { get; set; } = [];
    public List<KinaEntry> Ledger { get; set; } = [];
    public List<MapPin> Pins { get; set; } = [];
    public Dictionary<string, string> AtlasLayers { get; set; } = [];
    public string AtlasZone { get; set; } = "altgard";
    public AtlasSelection? AtlasHudView { get; set; }
    public bool AtlasHudLocked { get; set; }
    public HashSet<Guid> AtlasFound { get; set; } = [];
    public string? CaptureAdapter { get; set; }
    public string DeclaredBuild { get; set; } = "";
    public List<CharacterBuild> CharacterBuilds { get; set; } = [];
    public Guid? SelectedBuildCharacterId { get; set; }
    public HudDensity CombatHudDensity { get; set; } = HudDensity.Detailed;
    public bool AutoUpdate { get; set; } = true;
    public UpdateChannel? UpdateChannel { get; set; }
    public string? LastSeenVersion { get; set; }
    public bool AutoExpeditions { get; set; } = true;
    public double? CombatHudOpacity { get; set; }
    public bool CombatHudLocked { get; set; }
    public int CombatHudTextSize { get; set; } = 15;
    public bool CombatHudSelfFirst { get; set; } = true;
    public CombatHudScope CombatHudScope { get; set; } = CombatHudScope.Party;
    public Dictionary<HudDensity, double> CombatHudHeights { get; set; } = [];
    public string CombatCharacterHint { get; set; } = "";
    public Expedition? CurrentExpedition { get; set; }
    public List<Expedition> Expeditions { get; set; } = [];
    public string ProgressCharacter { get; set; } = "";
    public List<ProgressGoal> Goals { get; set; } = [];
    public Dictionary<string, int> BossAlarms { get; set; } = [];
    public HashSet<Guid> FavoriteEvents { get; set; } = [];
    public Dictionary<Guid, int> EventAlarms { get; set; } = [];
    public bool ReminderSound { get; set; } = true;
    public string? BossHudSelection { get; set; }
    public Dictionary<string, HudLayout> HudLayouts { get; set; } = [];
    public string TeamFolder { get; set; } = "";
    public Guid TeamClientId { get; set; } = Guid.NewGuid();
    public TeamSnapshot? Team { get; set; }
    public HashSet<Guid> TeamFound { get; set; } = [];
    public HashSet<Guid> TeamDisabled { get; set; } = [];
    public List<EventRule> Events { get; set; } =
    [
        EventRule.DefaultRift,
        new(EventRule.ShugoFestivalId, "Фестиваль шуго", DateTimeOffset.UnixEpoch, 60)
    ];
}

public sealed class LegionStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public static string Folder { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ARX Legion Beta");
    private static string PathName => Path.Combine(Folder, "profile.json");
    public Profile Profile { get; }
    public string? LoadError { get; }
    public bool Preview { get; }
    public bool NewProfile { get; }
    public string TeamStatus { get; set; } = Loc.T("Общая папка не подключена");
    public FightAnalysis Analysis { get; private set; }
    public FightAnalysis? LiveAnalysis { get; private set; }
    private static readonly FightAnalysis NoFight = new(Fight.Empty);
    public bool FollowLive { get; private set; }
    public FightAnalysis DisplayAnalysis => FollowLive ? LiveAnalysis ?? NoFight : Analysis;
    internal FightAnalysis OverlayAnalysis => LiveAnalysis ?? (DisplayAnalysis.Fight.Demo ? DisplayAnalysis : NoFight);
    public string CaptureLabel { get; private set; } = Loc.T("Захват выключен");
    public string? LiveIdentity { get; private set; }
    internal CharacterIdentity? LiveCharacter { get; private set; }
    internal PartyRosterEvent? LiveRoster { get; private set; }
    public IReadOnlyList<ObservedBoss> LiveBosses { get; private set; } = [];
    public string? BossNotice { get; private set; }
    public event Action? LiveChanged;
    public event Action? Changed;

    public LegionStore(bool preview)
    {
        Preview = preview;
        NewProfile = !File.Exists(PathName);
        try
        {
            Profile = !preview && File.Exists(PathName)
                ? JsonSerializer.Deserialize<Profile>(File.ReadAllText(PathName), Json) ?? new()
                : new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            LoadError = ex.Message;
            Profile = new();
        }
        Analysis = new FightAnalysis(preview ? FightImport.Sample() : Profile.History.FirstOrDefault() ?? Fight.Empty);
    }

    public void SetFight(Fight fight, bool save)
    {
        var result = new FightAnalysis(fight);
        FollowLive = false;
        if (save && !fight.Demo)
        {
            Profile.History.Insert(0, fight);
            if (Profile.History.Count > 30) Profile.History.RemoveAt(30);
            Save();
        }
        Analysis = result;
        Changed?.Invoke();
    }

    public void SetFollowLive(bool value)
    {
        FollowLive = value;
        Changed?.Invoke();
    }

    public void UpdateLive(FightAnalysis? analysis, string label, string? identity, IReadOnlyList<ObservedBoss> bosses)
        => UpdateLive(analysis, label, identity, bosses, null, null);

    internal void UpdateLive(FightAnalysis? analysis, string label, string? identity, IReadOnlyList<ObservedBoss> bosses,
        CharacterIdentity? character, PartyRosterEvent? roster)
    {
        LiveAnalysis = analysis;
        CaptureLabel = label;
        LiveIdentity = identity;
        LiveCharacter = character;
        LiveRoster = roster;
        LiveBosses = bosses;
        LiveChanged?.Invoke();
    }

    public void Archive(Fight fight, bool attachToRun = true, bool confirmedWin = false)
    {
        if (Profile.History.Any(f => f.Id == fight.Id)) return;
        Profile.History.Insert(0, fight);
        if (Profile.History.Count > 30) Profile.History.RemoveAt(30);
        var started = attachToRun && Profile.CurrentExpedition is { Automatic: true, Segments.Count: 0 };
        if (attachToRun && Profile.CurrentExpedition is { } run)
        {
            var stats = new FightAnalysis(fight);
            var captured = fight.Captured;
            var bossReport = captured?.Summary.Boss is null ? null : fight with
            {
                Captured = captured with { Details = captured.Details.Select(detail => detail with { Casts = [] }).ToArray() }
            };
            run.Segments.Add(new ExpeditionSegment(fight.Id, fight.Name, fight.Duration, stats.Damage, stats.Healing,
                bossReport, captured?.Summary.Reason));
            if (confirmedWin)
            {
                CreditProgress(run);
                FinishExpedition(run, interrupted: false, confirmedWin: true);
            }
        }
        Save();
        if (started || confirmedWin) Changed?.Invoke();
        LiveChanged?.Invoke();
    }

    public void EndExpedition(bool interrupted)
    {
        if (Profile.CurrentExpedition is not { } run) return;
        FinishExpedition(run, interrupted, confirmedWin: false);
        Commit();
    }

    private void FinishExpedition(Expedition run, bool interrupted, bool confirmedWin)
    {
        if (!run.Automatic || run.Segments.Count > 0)
        {
            Profile.Expeditions.Insert(0, run with { EndedAt = DateTimeOffset.Now, Interrupted = interrupted, ConfirmedWin = confirmedWin });
            if (Profile.Expeditions.Count > 25) Profile.Expeditions.RemoveAt(25);
        }
        Profile.CurrentExpedition = null;
    }

    private void CreditProgress(Expedition run)
    {
        for (var i = 0; i < Profile.Goals.Count; i++)
        {
            var goal = Profile.Goals[i];
            if (goal.ActivityId == run.ActivityId && goal.ServerId == run.ServerId &&
                string.Equals(goal.OwnerName, run.Character, StringComparison.OrdinalIgnoreCase) && goal.Count < goal.Target)
                Profile.Goals[i] = goal with { Count = goal.Count + 1 };
        }
    }

    public void SetBossNotice(string? notice)
    {
        BossNotice = notice;
    }

    public void Commit()
    {
        Save();
        Changed?.Invoke();
    }

    public void Save()
    {
        if (Preview || LoadError is not null) return;
        Directory.CreateDirectory(Folder);
        var temporary = PathName + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(Profile, Json));
        File.Move(temporary, PathName, overwrite: true);
    }

    public IEnumerable<(EventRule Rule, DateTimeOffset Due)> Upcoming(DateTimeOffset now) =>
        AllEvents.Select(rule => (Rule: rule, Due: rule.Next(now)))
            .Where(item => item.Due.HasValue).Select(item => (item.Rule, item.Due!.Value)).OrderBy(item => item.Value);

    internal EventRule? Rift => AllEvents.FirstOrDefault(rule => rule.Id == EventRule.RiftId);

    public IEnumerable<SessionTask> SessionTasks => Profile.Tasks.Where(t => t.SessionId == Profile.SessionId);
    public IEnumerable<MapPin> AllPins => Profile.Pins.Concat(
        (Profile.Team?.Pins ?? []).Select(p => p with { Found = Profile.TeamFound.Contains(p.Id) })).DistinctBy(p => p.Id);
    public IEnumerable<EventRule> SharedEvents => Profile.Team is { } team && team.Publisher != Profile.TeamClientId ? team.Events : [];
    public IEnumerable<EventRule> AllEvents => SharedEvents.Select(e => e with { Enabled = e.Enabled && !Profile.TeamDisabled.Contains(e.Id) })
        .Concat(Profile.Events).DistinctBy(e => e.Id);
}
