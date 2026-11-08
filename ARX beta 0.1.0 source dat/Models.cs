using System.Globalization;
using System.IO;
using System.Text.Json;
using Microsoft.VisualBasic.FileIO;

namespace ArxBeta;

public sealed record Hit(double Second, string Player, string Skill, long Damage, long Healing, bool Critical);
public sealed record Totals(string Player, long Damage, long Healing);
public sealed record Fight(Guid Id, string Name, DateTimeOffset Date, double Duration, string Self,
    Hit[] Hits, Totals[] Entered, bool Demo = false)
{
    public static Fight Empty { get; } = new(Guid.Empty, "Ожидание отчёта", DateTimeOffset.MinValue, 0, "", [], []);
}

public sealed record PlayerStats(string Player, long Damage, long Healing, int? Hits, int? Crits, double? Burst)
{
    public double? CritRate => Hits is > 0 ? (double)Crits!.Value / Hits.Value : null;
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
        Players = fight.Entered.Select(t => new PlayerStats(t.Player, t.Damage, t.Healing, null, null, null))
            .Concat(fight.Hits.GroupBy(h => h.Player, StringComparer.OrdinalIgnoreCase).Select(g => Summarize(g.Key, g)))
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

    public PlayerStats[] Skills(string player) => Fight.Hits
        .Where(h => string.Equals(h.Player, player, StringComparison.OrdinalIgnoreCase))
        .GroupBy(h => h.Skill, StringComparer.OrdinalIgnoreCase).Select(g => Summarize(g.Key, g))
        .OrderByDescending(p => p.Damage).ThenByDescending(p => p.Healing).ToArray();

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
        if (stream.Length > 5 * 1024 * 1024) throw new InvalidDataException("Файл больше 5 МБ.");
        using var parser = new TextFieldParser(stream) { Delimiters = [","], HasFieldsEnclosedInQuotes = true };
        if (parser.ReadFields() is not { } header || !header.SequenceEqual(Header, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("Заголовок CSV: seconds,player,skill,damage,healing,critical");
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
                throw new InvalidDataException($"Проверьте данные в строке {line}.");
            rows.Add(new Hit(time, fields[1], fields[2], damage, healing, crit));
        }
        if (rows.Count == 0) throw new InvalidDataException("В CSV нет событий.");
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
        string[] skills = ["Первый удар", "Удар по области", "Серия атак", "Завершающий удар"];
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
        return new Fight(Guid.NewGuid(), "Тренировочная экспедиция", DateTimeOffset.Now, 96, "Aster", rows.ToArray(), [], true);
    }
}

public sealed record EventRule(Guid Id, string Name, DateTimeOffset AnchorUtc, int RepeatMinutes, bool Enabled = true)
{
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
public sealed record MapPin(Guid Id, string Zone, double X, double Y, string Category, string Name, bool Found);
public sealed record HudLayout(double Left, double Top, double Width, double Height, double Opacity);

public sealed class Profile
{
    public Guid SessionId { get; set; } = Guid.NewGuid();
    public string Character { get; set; } = "";
    public string Server { get; set; } = "";
    public List<Fight> History { get; set; } = [];
    public List<SessionTask> Tasks { get; set; } = [];
    public List<KinaEntry> Ledger { get; set; } = [];
    public List<MapPin> Pins { get; set; } = [];
    public Dictionary<string, HudLayout> HudLayouts { get; set; } = [];
    public List<EventRule> Events { get; set; } =
    [
        new(new Guid("55025e60-21df-42de-9e89-a22f94b8c426"), "Пространственный разлом", DateTimeOffset.UnixEpoch, 180),
        new(new Guid("c885ed94-84c1-47e1-b8aa-11ff600f4c16"), "Фестиваль шуго", DateTimeOffset.UnixEpoch, 60)
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
    public FightAnalysis Analysis { get; private set; }
    public event Action? Changed;

    public LegionStore(bool preview)
    {
        Preview = preview;
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
        if (save && !fight.Demo)
        {
            Profile.History.Insert(0, fight);
            if (Profile.History.Count > 30) Profile.History.RemoveAt(30);
            Save();
        }
        Analysis = result;
        Changed?.Invoke();
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
        Profile.Events.Select(rule => (Rule: rule, Due: rule.Next(now)))
            .Where(item => item.Due.HasValue).Select(item => (item.Rule, item.Due!.Value)).OrderBy(item => item.Value);

    public IEnumerable<SessionTask> SessionTasks => Profile.Tasks.Where(t => t.SessionId == Profile.SessionId);
}
