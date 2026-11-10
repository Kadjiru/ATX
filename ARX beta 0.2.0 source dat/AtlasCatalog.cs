using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;

namespace ArxBeta;

internal sealed record AtlasZone(string Id, string Name, string Region)
{
    public override string ToString() => $"{Name}  ·  {Region}";

    public static AtlasZone[] All { get; } =
    [
        new("altgard", Loc.T("Альтгард"), Loc.T("Асмодея")),
        new("verteron", Loc.T("Бертрон"), Loc.T("Элизея")),
        new("ishalgen", Loc.T("Ишальген"), Loc.T("Асмодея")),
        new("poeta", Loc.T("Поэта"), Loc.T("Элизея")),
        new("lower", Loc.T("Нижняя Решанта"), Loc.T("Бездна")),
        new("middle", Loc.T("Средняя Решанта"), Loc.T("Бездна")),
        new("upper", Loc.T("Верхняя Решанта"), Loc.T("Бездна")),
        new("outer", Loc.T("Внешняя Решанта"), Loc.T("Бездна"))
    ];
}

internal static class AtlasCatalog
{
    public const string WorldLayer = "arx-world-v1";
    public const double Width = 1000, Height = 600;
    private static readonly Lazy<Dictionary<string, AtlasMap>> Maps = new(Load);

    public static AtlasMap? For(AtlasZone zone) => Maps.Value.GetValueOrDefault(zone.Id);

    private static Dictionary<string, AtlasMap> Load()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Data", "Atlas.json.gz");
            using var file = File.OpenRead(path);
            using var content = new GZipStream(file, CompressionMode.Decompress);
            var maps = JsonSerializer.Deserialize<AtlasMap[]>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return (maps ?? []).ToDictionary(m => m.Zone);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    internal sealed record AtlasPoint(string Key, string Category, string Name, string En, double X, double Y);

    internal sealed record AtlasMap(string Zone, double West, double North, double Span, int Columns, int Rows,
        byte[] Relief, AtlasPoint[] Points)
    {
        private MapPin[]? _pins;
        private BitmapSource? _terrain;
        private Dictionary<Guid, string>? _english;
        public BitmapSource Terrain => _terrain ??= AtlasTerrain.Render(Relief, Columns, Rows);
        public IReadOnlyList<MapPin> Pins => _pins ??= Points.Select(BuildPin).ToArray();
        public string? English(Guid id) => (_english ??= Points.ToDictionary(p => PointId(p.Key), p => p.En)).GetValueOrDefault(id);

        public Point ToGame(Point point) => new(West + point.X * Span / Width, North + point.Y * Span / Width);
        public double Metres(double mapUnits) => mapUnits * Span / (Width * 100);

        private MapPin BuildPin(AtlasPoint point) => new(PointId(point.Key), Zone, (point.X - West) * Width / Span,
            (point.Y - North) * Width / Span, point.Category, point.Name, false, WorldLayer);

        private Guid PointId(string key) => new(SHA256.HashData(Encoding.UTF8.GetBytes($"{Zone}/{key}")).AsSpan(0, 16));

    }
}
