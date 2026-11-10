using System.IO;
using System.Text.Json;
using AionMeter.Core.Events;

namespace ArxBeta;

internal static class WorldBossCatalog
{
    internal sealed record Entry(int Npc, string Name, string En, double X, double Y);
    internal sealed record Zone(int Id, string Key, string Name, Entry[] Bosses);
    internal sealed record Choice(string Id, string Name, string Zone)
    {
        public override string ToString() => Zone.Length == 0 ? Name : $"{Name} · {Zone}";
    }

    private static readonly Lazy<Zone[]> Zones = new(Load);
    internal static IReadOnlyList<Choice> Choices { get; } = Zones.Value.SelectMany(zone =>
        zone.Bosses.Select(entry => new Choice(Key(zone, entry), Loc.Name(entry.Name, entry.En), Loc.T(zone.Name)))).ToArray();

    internal static string Key(Zone zone, Entry boss) => $"{zone.Key}/{boss.Npc}";

    internal static Zone? Identify(FieldBossListEvent list)
    {
        var known = Zones.Value.FirstOrDefault(zone => zone.Id == list.MapId);
        if (known is not null) return known.Bosses.Length == list.Count ? known : null;
        var positions = list.Slots.Where(slot => slot.Alive && (slot.X != 0 || slot.Y != 0)).ToArray();
        if (positions.Length == 0) return null;
        var candidates = Zones.Value.Where(zone => zone.Id == 0 && zone.Bosses.Length == list.Count &&
            positions.All(slot => MatchesPosition(zone, list.MapId, slot))).Take(2).ToArray();
        return candidates.Length == 1 ? candidates[0] : null;
    }

    private static bool MatchesPosition(Zone zone, int mapId, FieldBossSlot slot)
    {
        var index = slot.SlotId - mapId * 100 - 1;
        if (index < 0 || index >= zone.Bosses.Length) return false;
        var entry = zone.Bosses[index];
        var dx = entry.X - slot.X;
        var dy = entry.Y - slot.Y;
        const double tolerance = 12_000;
        return dx * dx + dy * dy <= tolerance * tolerance;
    }

    private static Zone[] Load()
    {
        try
        {
            using var file = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Data", "WorldBosses.json"));
            return JsonSerializer.Deserialize<Zone[]>(file, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return []; }
    }
}
