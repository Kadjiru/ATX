using System.IO;
using System.Text.Json;
using System.Windows.Threading;

namespace ArxBeta;

public sealed record TeamSnapshot(int Schema, Guid Revision, Guid Publisher, DateTimeOffset PublishedAt, EventRule[] Events, MapPin[] Pins);

public static class TeamShare
{
    private const string Name = "arx-legion.json";
    private const long Limit = 2 * 1024 * 1024;

    public static TeamSnapshot Read(string folder)
    {
        using var stream = File.OpenRead(Path.Combine(folder, Name));
        if (stream.Length > Limit) throw new InvalidDataException(Loc.T("Общий набор превышает 2 МБ."));
        var data = JsonSerializer.Deserialize<TeamSnapshot>(stream);
        if (data is not { Schema: 1 or 2 } || data.Events is null || data.Pins is null ||
            data.Events.Length > 250 || data.Pins.Length > 5000 ||
            data.Events.Any(e => e is null || e.Id == Guid.Empty || e.Name is null || e.Name.Length is 0 or > 120 ||
                e.RepeatMinutes is < 0 or > 44640 || e.AnchorUtc.Year is < 1970 or > 2100) ||
            data.Pins.Any(p => p is null || p.Id == Guid.Empty || p.Name is null || p.Name.Length is 0 or > 120 ||
                p.Category is null || p.Category.Length > 32 ||
                (p.Layer is not null && (data.Schema != 2 || p.Layer != AtlasCatalog.WorldLayer)) ||
                !double.IsFinite(p.X) || !double.IsFinite(p.Y) ||
                p.X is < 0 or > 1000 || p.Y is < 0 or > 600 || !AtlasZone.All.Any(z => z.Id == p.Zone)) ||
            data.Events.Select(e => e.Id).Distinct().Count() != data.Events.Length ||
            data.Pins.Select(p => p.Id).Distinct().Count() != data.Pins.Length)
            throw new InvalidDataException(Loc.T("Формат общего набора не поддерживается."));
        return data;
    }

    public static TeamSnapshot Prepare(Profile profile)
    {
        var events = profile.Events.ToArray();
        var pins = profile.Pins.Where(p => p.Layer is null or AtlasCatalog.WorldLayer)
            .Select(p => p with { Found = false }).ToArray();
        if (events.Length > 250 || pins.Length > 5000) throw new InvalidDataException(Loc.T("Слишком много общих меток или таймеров."));
        var schema = pins.Any(p => p.Layer == AtlasCatalog.WorldLayer) ? 2 : 1;
        return new TeamSnapshot(schema, Guid.NewGuid(), profile.TeamClientId, DateTimeOffset.UtcNow, events, pins);
    }

    public static void Publish(string folder, TeamSnapshot data)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(data);
        if (bytes.Length > Limit) throw new InvalidDataException(Loc.T("Общий набор превышает 2 МБ."));
        var path = Path.Combine(folder, Name);
        var temporary = Path.Combine(folder, $".arx-{data.Revision:N}.tmp");
        File.WriteAllBytes(temporary, bytes);
        try { File.Move(temporary, path, overwrite: true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

public partial class App
{
    private DispatcherTimer? _teamPoll;
    private bool _teamBusy;

    private void BeginTeamSync()
    {
        _teamPoll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _teamPoll.Tick += (_, _) => PollTeam();
        _teamPoll.Start();
        PollTeam();
    }

    public async void PollTeam()
    {
        var folder = _store.Profile.TeamFolder;
        if (_closing || _teamBusy || string.IsNullOrEmpty(folder)) return;
        _teamBusy = true;
        try
        {
            var snapshot = await Task.Run(() => TeamShare.Read(folder));
            if (_closing || _store.Profile.TeamFolder != folder) return;
            _store.TeamStatus = Loc.F($"Получен набор от {snapshot.PublishedAt.ToLocalTime():dd.MM HH:mm}");
            if (_store.Profile.Team?.Revision == snapshot.Revision) return;
            _store.Profile.Team = snapshot;
            _store.Commit();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        {
            if (!_closing && _store.Profile.TeamFolder == folder)
                _store.TeamStatus = Loc.T("Общий набор недоступен. Используется последняя копия, если она есть.");
        }
        finally { _teamBusy = false; }
    }
}
