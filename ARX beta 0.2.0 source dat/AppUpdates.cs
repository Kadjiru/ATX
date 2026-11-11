using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace ArxBeta;

internal enum UpdatePhase { Idle, Checking, Downloading, Verifying, Current, Available, Ready, Failed }
public enum UpdateChannel { Stable, Beta }

internal sealed class AppUpdates : IDisposable
{
    private const string Repo = "https://github.com/Kadjiru/ATX";
    private const string Feed = "https://api.github.com/repos/Kadjiru/ATX/releases";
    private const long MaximumInstallerSize = 256L * 1024 * 1024;
    private readonly HttpClient _http;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly string _cache;
    private readonly string? _installFolder;
    private readonly ReleaseVersion _current;
    private readonly UpdateChannel _channel;
    private Update? _ready;
    private bool _busy;

    private sealed record Release(
        [property: JsonPropertyName("tag_name")] string Tag,
        [property: JsonPropertyName("draft")] bool Draft,
        [property: JsonPropertyName("prerelease")] bool Prerelease,
        [property: JsonPropertyName("assets")] Asset[]? Assets);
    private sealed record Asset(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("state")] string State,
        [property: JsonPropertyName("size")] long Size,
        [property: JsonPropertyName("digest")] string? Digest);
    private sealed record Update(ReleaseVersion Version, Asset Asset)
    {
        public string Page => $"{Repo}/releases/tag/{Version.Tag}";
        public string Download => $"{Repo}/releases/download/{Version.Tag}/{Asset.Name}";
    }

    internal readonly record struct ReleaseVersion(Version Number, bool Beta) : IComparable<ReleaseVersion>
    {
        public string Label => Number.ToString(3) + (Beta ? "-beta" : "");
        public string Tag => "v" + Label;
        public int CompareTo(ReleaseVersion other)
        {
            var number = Number.CompareTo(other.Number);
            return number != 0 ? number : other.Beta.CompareTo(Beta);
        }
        public static bool TryParse(string? value, out ReleaseVersion version)
        {
            version = default;
            if (string.IsNullOrEmpty(value)) return false;
            var beta = value.EndsWith("-beta", StringComparison.Ordinal);
            var number = beta ? value[..^5] : value;
            if (!Version.TryParse(number, out var parsed) || parsed.Build < 0 || parsed.Revision >= 0 ||
                parsed.ToString(3) != number) return false;
            version = new ReleaseVersion(parsed, beta);
            return true;
        }
    }

    internal static string CurrentLabel => typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion.Split('+')[0] ?? "1.0.0-beta";
    internal static bool IsBetaBuild => CurrentLabel.EndsWith("-beta", StringComparison.Ordinal);
    internal static UpdateChannel DefaultChannel => IsBetaBuild ? UpdateChannel.Beta : UpdateChannel.Stable;
    public string Status { get; private set; } = Loc.T("Проверка обновлений ещё не выполнялась.");
    public UpdatePhase Phase { get; private set; }
    public int DownloadPercent { get; private set; }
    public event Action? StatusChanged;
    public bool Installed => _installFolder is not null;
    public bool Ready => _ready is not null;
    public string? ReadyVersion => _ready?.Version.Label;
    public string? ReadyIdentity => _ready is { } update ? $"{update.Version.Label}/{update.Asset.Digest}" : null;
    public string? ReleasePage { get; private set; }

    public AppUpdates(UpdateChannel channel) : this(new HttpClientHandler { AllowAutoRedirect = false }, CurrentLabel,
        DetectInstallation(), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ARX", "Updates"), channel)
    { }

    internal AppUpdates(HttpMessageHandler handler, string current, string? installFolder, string cache, UpdateChannel channel)
    {
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(40) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("ARX-Updater");
        _current = ReleaseVersion.TryParse(current, out var version) ? version : throw new ArgumentException(Loc.T("Неизвестная версия ARX."));
        _installFolder = installFolder;
        _cache = cache;
        _channel = channel;
    }

    public async Task CheckAsync()
    {
        if (_busy || _shutdown.IsCancellationRequested) return;
        _busy = true;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        timeout.CancelAfter(TimeSpan.FromMinutes(15));
        var ct = timeout.Token;
        try
        {
            SetStatus(UpdatePhase.Checking, Loc.T("Проверяем обновления…"));
            var beta = _channel == UpdateChannel.Beta;
            await using var feed = await _http.GetStreamAsync(Feed + (beta ? "?per_page=100" : "/latest"), ct);
            Release?[] releases = beta
                ? await JsonSerializer.DeserializeAsync<Release[]>(feed, cancellationToken: ct) ?? []
                : new Release?[] { await JsonSerializer.DeserializeAsync<Release>(feed, cancellationToken: ct) };
            ct.ThrowIfCancellationRequested();
            var update = releases.Select(FromRelease).OfType<Update>().OrderByDescending(u => u.Version)
                .FirstOrDefault(u => u.Version.Beta == beta &&
                    (u.Version.Beta != _current.Beta || u.Version.CompareTo(_current) > 0));
            if (update is null)
            {
                _ready = null;
                ReleasePage = null;
                var switching = _current.Beta != beta;
                SetStatus(UpdatePhase.Current, switching ? Loc.T("В выбранном канале пока нет подходящей версии.")
                    : Loc.T("Установлена актуальная версия."));
                return;
            }
            ReleasePage = update.Page;
            if (!Installed)
            {
                SetStatus(UpdatePhase.Available, Loc.F($"Доступна версия {update.Version.Label}. Для переносимой копии скачайте новый ZIP."));
                return;
            }
            if (_ready == update)
            {
                SetStatus(UpdatePhase.Ready, ReadyStatus(update));
                return;
            }
            _ready = null;
            Directory.CreateDirectory(_cache);
            var path = Path.Combine(_cache, update.Asset.Name);
            if (File.Exists(path)) SetStatus(UpdatePhase.Verifying, Loc.T("Проверяем целостность установщика…"));
            if (!await VerifyExistingAsync(update, path, ct))
            {
                DownloadPercent = 0;
                SetStatus(UpdatePhase.Downloading, Loc.F($"Загружаем ARX {update.Version.Label}…"));
                await DownloadAsync(update, path, ct);
            }
            ct.ThrowIfCancellationRequested();
            _ready = update;
            SetStatus(UpdatePhase.Ready, ReadyStatus(update));
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or JsonException
            or UnauthorizedAccessException or OperationCanceledException)
        {
            if (!_shutdown.IsCancellationRequested)
            {
                if (_ready is { } ready) SetStatus(UpdatePhase.Ready, ReadyStatus(ready));
                else SetStatus(UpdatePhase.Failed, Loc.T("Не удалось проверить или загрузить обновление. Повторите позже."));
            }
            Debug.WriteLine(ex);
        }
        finally { _busy = false; }
    }

    private void SetStatus(UpdatePhase phase, string message)
    {
        Phase = phase;
        Status = message;
        StatusChanged?.Invoke();
    }

    private string ReadyStatus(Update update) => File.Exists(AttemptPath(update))
        ? Loc.F($"ARX {update.Version.Label} скачана. Если установка прервалась, её можно повторить.")
        : Loc.F($"ARX {update.Version.Label} скачана и проверена.");

    private static Update? FromRelease(Release? release)
    {
        if (release is not { Draft: false, Tag: { Length: > 1 } tag } || tag[0] != 'v' ||
            !ReleaseVersion.TryParse(tag[1..], out var version) || version.Beta != release.Prerelease) return null;
        // The old updater only recognizes ARX-Setup-* and must migrate to Stable first.
        var name = version.Beta ? $"ARX-Beta-Setup-{version.Label}.exe" : $"ARX-Setup-{version.Label}.exe";
        var asset = release.Assets?.FirstOrDefault(a => a is not null && a.Name == name && a.State == "uploaded" &&
            a.Size is > 0 and <= MaximumInstallerSize &&
            a.Digest is { Length: 71 } digest && digest.StartsWith("sha256:", StringComparison.Ordinal) &&
            digest.AsSpan(7).IndexOfAnyExcept("0123456789abcdefABCDEF") == -1);
        return asset is null ? null : new Update(version, asset);
    }

    private static async Task<bool> VerifyExistingAsync(Update update, string path, CancellationToken ct)
    {
        if (!File.Exists(path)) return false;
        await using var file = File.OpenRead(path);
        return file.Length == update.Asset.Size && CheckHash(update, await SHA256.HashDataAsync(file, ct));
    }

    private async Task DownloadAsync(Update update, string target, CancellationToken ct)
    {
        var url = new Uri(update.Download);
        var part = target + "." + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            for (var redirect = 0; redirect < 4; redirect++)
            {
                using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or
                    HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
                {
                    var location = response.Headers.Location;
                    if (location is null || !location.IsAbsoluteUri || location.Scheme != Uri.UriSchemeHttps ||
                        !location.IsDefaultPort || location.UserInfo.Length != 0 ||
                        location.Host is not ("release-assets.githubusercontent.com" or "objects.githubusercontent.com"))
                        throw new InvalidDataException(Loc.T("Неожиданный адрес загрузки ARX."));
                    url = location;
                    continue;
                }
                response.EnsureSuccessStatusCode();
                await using var source = await response.Content.ReadAsStreamAsync(ct);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[65536];
                long size = 0;
                await using (var file = new FileStream(part, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
                {
                    int read;
                    while ((read = await source.ReadAsync(buffer, ct)) > 0)
                    {
                        size += read;
                        if (size > update.Asset.Size) throw new InvalidDataException(Loc.T("Установщик больше объявленного размера."));
                        hash.AppendData(buffer, 0, read);
                        await file.WriteAsync(buffer.AsMemory(0, read), ct);
                        var percent = (int)(size * 100 / update.Asset.Size);
                        if (percent != DownloadPercent)
                        {
                            DownloadPercent = percent;
                            StatusChanged?.Invoke();
                        }
                    }
                }
                SetStatus(UpdatePhase.Verifying, Loc.T("Проверяем целостность установщика…"));
                if (size != update.Asset.Size || !CheckHash(update, hash.GetHashAndReset()))
                    throw new InvalidDataException(Loc.T("Установщик ARX не прошёл проверку SHA-256."));
                ct.ThrowIfCancellationRequested();
                File.Move(part, target, true);
                return;
            }
            throw new InvalidDataException(Loc.T("Слишком много перенаправлений."));
        }
        finally { if (File.Exists(part)) File.Delete(part); }
    }

    public bool InstallAfterClose()
    {
        if (_ready is not { } update || _installFolder is not { } folder) return false;
        var attempt = AttemptPath(update);
        var path = Path.Combine(_cache, update.Asset.Name);
        try
        {
            using var file = File.OpenRead(path);
            if (file.Length != update.Asset.Size || !CheckHash(update, SHA256.HashData(file))) return false;
            var launch = new ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            foreach (var arg in new[] { "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/NOCLOSEAPPLICATIONS",
                "/NORESTARTAPPLICATIONS", "/ARXUPDATE", "/SP-", $"/LANG={Loc.Language}", $"/DIR={folder}",
                $"/LOG={Path.Combine(_cache, "install.log")}" })
                launch.ArgumentList.Add(arg);
            File.WriteAllText(attempt, DateTimeOffset.UtcNow.ToString("O"));
            using var process = Process.Start(launch);
            return process is not null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
        {
            Debug.WriteLine(ex);
            return false;
        }
    }

    private static bool CheckHash(Update update, byte[] hash) =>
        string.Equals(Convert.ToHexString(hash), update.Asset.Digest![7..], StringComparison.OrdinalIgnoreCase);
    private string AttemptPath(Update update) => Path.Combine(_cache, update.Asset.Name + ".attempt");

    private static string? DetectInstallation()
    {
        const string keyName = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{9F5CB164-814E-4346-A21A-E3171454CEFA}_is1";
        try
        {
            var here = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                using var hive = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view);
                using var key = hive.OpenSubKey(keyName);
                if (key?.GetValue("InstallLocation") is string folder &&
                    string.Equals(here, Path.TrimEndingDirectorySeparator(folder), StringComparison.OrdinalIgnoreCase))
                    return here;
            }
        }
        catch (Exception ex) when (ex is IOException or SecurityException or UnauthorizedAccessException) { Debug.WriteLine(ex); }
        return null;
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        _http.Dispose();
        _shutdown.Dispose();
    }
}
