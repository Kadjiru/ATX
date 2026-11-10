using System.IO;
using System.Security.Cryptography;
using System.Windows.Media.Imaging;

namespace ArxBeta;

internal static class AtlasLayers
{
    private static string Folder => Path.Combine(LegionStore.Folder, "atlas-images");

    public static string Import(string path)
    {
        using var source = File.OpenRead(path);
        if (source.Length is < 1 or > 30 * 1024 * 1024) throw new InvalidDataException("Изображение должно занимать не более 30 МБ.");
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.StreamSource = source;
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.DecodePixelWidth = 2048;
        bitmap.EndInit();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        var bytes = stream.ToArray();
        var key = Convert.ToHexString(SHA256.HashData(bytes));
        Directory.CreateDirectory(Folder);
        var file = Path.Combine(Folder, key + ".png");
        if (!File.Exists(file))
        {
            var temporary = file + ".tmp";
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, file, overwrite: true);
        }
        return key;
    }

    private static string? ImagePath(string? key) =>
        key is { Length: 64 } && key.All(Uri.IsHexDigit) ? Path.Combine(Folder, key + ".png") : null;

    public static bool Exists(string? key) => ImagePath(key) is { } file && File.Exists(file);

    public static BitmapImage? Open(string? key)
    {
        if (ImagePath(key) is not { } file || !File.Exists(file)) return null;
        try
        {
            using var source = File.OpenRead(file);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.StreamSource = source;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) { return null; }
    }
}
