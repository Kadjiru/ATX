using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ArxBeta;

public sealed record RaidNote(string Title, string Entry, string Tactics, string Status);
public sealed record GameReference(string ReferenceDate, string GameBuild, string MetaWindow, string Composition,
    string CraftingDate, string[] Gear, RaidNote Raid, EventRule[] Presets);
public sealed record CatalogManifest(int Schema, int Revision, string CraftingSha256, string ReferenceSha256);

public sealed class CatalogSync
{
    private const string Remote = "https://raw.githubusercontent.com/Kadjiru/ATX/beta/0.2.0/ARX%20beta%200.2.0%20source%20dat/Data/";
    private const int Maximum = 3 * 1024 * 1024;
    private static string CachePath => Path.Combine(LegionStore.Folder, "catalog.json");
    private sealed record Bundle(CatalogManifest Manifest, string Crafting, string Reference);
    private Bundle _bundle;
    public CraftCatalog Crafting { get; private set; }
    public GameReference Reference { get; private set; }
    public string Status { get; private set; } = "Локальная база";
    public int Revision => _bundle.Manifest.Revision;

    private CatalogSync(Bundle bundle)
    {
        _bundle = bundle;
        (Crafting, Reference) = Decode(bundle);
    }

    public static CatalogSync Open(bool preview)
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Data");
        var bundled = new Bundle(Parse<CatalogManifest>(File.ReadAllText(Path.Combine(folder, "CatalogManifest.json"))),
            File.ReadAllText(Path.Combine(folder, "Crafting.json")), File.ReadAllText(Path.Combine(folder, "Reference.json")));
        if (!preview && File.Exists(CachePath))
        {
            try
            {
                var cache = new FileInfo(CachePath);
                if (cache.Length <= Maximum * 3)
                {
                    var bundle = Parse<Bundle>(File.ReadAllText(cache.FullName));
                    if (bundle.Manifest.Revision > bundled.Manifest.Revision)
                        return new CatalogSync(bundle) { Status = "Сохранённая копия базы" };
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
            {
                // A partial or incompatible update never replaces the bundled database.
            }
        }
        return new CatalogSync(bundled);
    }

    public async Task RefreshAsync()
    {
        using var client = new HttpClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            var manifest = Parse<CatalogManifest>(await Download(client, "CatalogManifest.json", 8192, timeout.Token));
            if (manifest.Schema != 1 || manifest.Revision < 1) throw new InvalidDataException("Версия базы не поддерживается.");
            if (manifest == _bundle.Manifest)
            {
                Status = "База синхронизирована";
                return;
            }
            if (manifest.Revision <= Revision) return;
            var files = await Task.WhenAll(
                Download(client, "Crafting.json", Maximum, timeout.Token),
                Download(client, "Reference.json", 64 * 1024, timeout.Token));
            var candidate = new Bundle(manifest, files[0], files[1]);
            var content = Decode(candidate);
            Directory.CreateDirectory(LegionStore.Folder);
            var temporary = CachePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(candidate));
            File.Move(temporary, CachePath, overwrite: true);
            _bundle = candidate;
            (Crafting, Reference) = content;
            Status = "База обновлена";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or HttpRequestException or OperationCanceledException or UnauthorizedAccessException)
        {
            Status = "Локальная база · обновление недоступно";
        }
    }

    private static async Task<string> Download(HttpClient client, string name, int limit, CancellationToken ct)
    {
        using var response = await client.GetAsync(Remote + name, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("Файл базы слишком большой.");
        await using var body = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int count;
        while ((count = await body.ReadAsync(chunk, ct)) != 0)
        {
            if (buffer.Length + count > limit) throw new InvalidDataException("Файл базы слишком большой.");
            buffer.Write(chunk, 0, count);
        }
        return Encoding.UTF8.GetString(buffer.ToArray()).TrimStart('\uFEFF');
    }

    private static (CraftCatalog, GameReference) Decode(Bundle bundle)
    {
        var (manifest, recipes, reference) = bundle;
        if (manifest is not { Schema: 1, Revision: > 0 } ||
            !Verify(recipes, manifest.CraftingSha256) || !Verify(reference, manifest.ReferenceSha256))
            throw new InvalidDataException("Контрольные суммы базы не совпали.");
        var catalog = Parse<CraftCatalog>(recipes);
        var info = Parse<GameReference>(reference);
        if (catalog.Recipes is null || catalog.Items is null || catalog.Recipes.Length == 0 ||
            catalog.Recipes.Any(r => r is null || string.IsNullOrWhiteSpace(r.Name) || r.Yield < 1 ||
                r.Materials is null || r.Materials.Any(m => m is null || m.Count < 1 || string.IsNullOrWhiteSpace(m.Id))) ||
            !DateOnly.TryParse(info.CraftingDate, out _) || !DateOnly.TryParse(info.ReferenceDate, out _) ||
            info.Gear is null || info.Raid is null || info.Presets is null ||
            info.Presets.Any(rule => rule is null || rule.RepeatMinutes is < 0 or > 44640))
            throw new InvalidDataException("Формат справочника не поддерживается.");
        return (catalog, info);
    }

    private static bool Verify(string payload, string digest) => payload is not null &&
        string.Equals(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload.Replace("\r\n", "\n")))),
            digest, StringComparison.OrdinalIgnoreCase);

    private static T Parse<T>(string content) => JsonSerializer.Deserialize<T>(content)
        ?? throw new InvalidDataException("Пустая база.");
}
