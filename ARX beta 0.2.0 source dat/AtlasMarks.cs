namespace ArxBeta;

public sealed record AtlasSelection(string Zone, string? Layer, Guid? PinId);

internal sealed record AtlasLayer(string? Key, string Label)
{
    public override string ToString() => Label;
}

internal static class AtlasMarks
{
    public static readonly string[] Categories =
        ["Все категории", "Телепорт", "След", "Сбор", "Куб", "Питомец", "Босс", "Подземелье", "Заметка"];

    public static IReadOnlyList<AtlasLayer> Layers(LegionStore store, AtlasZone zone)
    {
        var layers = new List<AtlasLayer> { new(AtlasCatalog.WorldLayer, "Игровые координаты") };
        var custom = store.Profile.AtlasLayers.GetValueOrDefault(zone.Id);
        if (AtlasLayers.Exists(custom)) layers.Add(new AtlasLayer(custom, "Мой снимок"));
        if (store.AllPins.Any(pin => pin.Zone == zone.Id && pin.Layer is null))
            layers.Add(new AtlasLayer(null, "Архив старых меток"));
        return layers;
    }

    public static MapPin[] All(LegionStore store, AtlasZone zone, string? layer, AtlasCatalog.AtlasMap? world)
    {
        var pins = store.AllPins.Where(pin => pin.Zone == zone.Id && pin.Layer == layer);
        if (world is not null)
            pins = pins.Concat(world.Pins.Select(pin => store.Profile.AtlasFound.Contains(pin.Id) ? pin with { Found = true } : pin));
        return pins.ToArray();
    }

    public static bool Matches(MapPin pin, AtlasCatalog.AtlasMap? world, string query) =>
        query.Length == 0 || Contains(pin.Name, query) || Contains(world?.English(pin.Id) ?? "", query);

    private static bool Contains(string value, string query) =>
        value.Replace('ё', 'е').Replace('Ё', 'Е')
            .Contains(query.Replace('ё', 'е').Replace('Ё', 'Е'), StringComparison.OrdinalIgnoreCase);

    public static void ToggleFound(LegionStore store, MapPin pin, AtlasCatalog.AtlasMap? world)
    {
        var profile = store.Profile;
        var index = profile.Pins.FindIndex(item => item.Id == pin.Id);
        if (index >= 0) profile.Pins[index] = pin with { Found = !pin.Found };
        else
        {
            var found = world?.English(pin.Id) is not null ? profile.AtlasFound : profile.TeamFound;
            if (!found.Add(pin.Id)) found.Remove(pin.Id);
        }
        store.Commit();
    }
}
