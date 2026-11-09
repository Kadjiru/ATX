using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ArxBeta;

internal sealed record AtlasZone(string Id, string Name, string Region)
{
    public override string ToString() => $"{Name}  ·  {Region}";

    public static AtlasZone[] All { get; } =
    [
        new("altgard", "Альтгард", "Асмодея"),
        new("verteron", "Бертрон", "Элизея"),
        new("ishalgen", "Ишальген", "Асмодея"),
        new("poeta", "Поэта", "Элизея"),
        new("lower", "Нижняя Решанта", "Бездна"),
        new("middle", "Средняя Решанта", "Бездна"),
        new("upper", "Верхняя Решанта", "Бездна"),
        new("outer", "Внешняя Решанта", "Бездна")
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
        public BitmapSource Terrain => _terrain ??= DrawTerrain();
        public IReadOnlyList<MapPin> Pins => _pins ??= Points.Select(BuildPin).ToArray();
        public string? English(Guid id) => (_english ??= Points.ToDictionary(p => PointId(p.Key), p => p.En)).GetValueOrDefault(id);

        public Point ToGame(Point point) => new(West + point.X * Span / Width, North + point.Y * Span / Width);
        public double Metres(double mapUnits) => mapUnits * Span / (Width * 100);

        private MapPin BuildPin(AtlasPoint point) => new(PointId(point.Key), Zone, (point.X - West) * Width / Span,
            (point.Y - North) * Width / Span, point.Category, point.Name, false, WorldLayer);

        private Guid PointId(string key) => new(SHA256.HashData(Encoding.UTF8.GetBytes($"{Zone}/{key}")).AsSpan(0, 16));

        private BitmapSource DrawTerrain()
        {
            var pixels = new byte[Columns * Rows * 4];
            for (var y = 0; y < Rows; y++)
            for (var x = 0; x < Columns; x++)
            {
                var index = y * Columns + x;
                var elevation = Relief[index];
                if (elevation == 0) continue;
                var west = x > 0 && Relief[index - 1] > 0 ? Relief[index - 1] : elevation;
                var north = y > 0 && Relief[index - Columns] > 0 ? Relief[index - Columns] : elevation;
                var shade = Math.Clamp((west + north - 2 * elevation) * .87, -23, 25);
                var level = elevation / 254d;
                var contour = west / 26 != elevation / 26 || north / 26 != elevation / 26 ? 8 : 0;
                var offset = index * 4;
                pixels[offset] = Tone(60 + level * 39 + shade + contour);
                pixels[offset + 1] = Tone(76 + level * 55 + shade + contour);
                pixels[offset + 2] = Tone(39 + level * 77 + shade + contour);
                pixels[offset + 3] = 255;
            }
            var terrain = BitmapSource.Create(Columns, Rows, 96, 96, PixelFormats.Bgra32, null, pixels, Columns * 4);
            terrain.Freeze();
            return terrain;
        }

        private static byte Tone(double channel) => (byte)Math.Clamp(channel, 0, 255);
    }
}
