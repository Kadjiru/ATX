using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ArxBeta;

internal static class AtlasTerrain
{
    internal const int PixelWidth = 2000;
    internal const int PixelHeight = 1200;

    private readonly record struct Sample(float Height, float Coverage);

    private readonly record struct Filter(int A, int B, int C, int D, float Wa, float Wb, float Wc, float Wd)
    {
        public float Blend(float a, float b, float c, float d) => a * Wa + b * Wb + c * Wc + d * Wd;
    }

    public static BitmapSource Render(byte[] relief, int columns, int rows)
    {
        // Interpolate elevation and coverage separately so unmeasured terrain cannot pull the visible edge down.
        var horizontal = Filters(columns, PixelWidth);
        var scanlines = new Sample[rows * PixelWidth];
        for (var y = 0; y < rows; y++)
        for (var x = 0; x < PixelWidth; x++)
        {
            var f = horizontal[x];
            var row = y * columns;
            var a = relief[row + f.A];
            var b = relief[row + f.B];
            var c = relief[row + f.C];
            var d = relief[row + f.D];
            scanlines[y * PixelWidth + x] = new Sample(f.Blend(a, b, c, d),
                f.Blend(a > 0 ? 1 : 0, b > 0 ? 1 : 0, c > 0 ? 1 : 0, d > 0 ? 1 : 0));
        }

        var vertical = Filters(rows, PixelHeight);
        var pixels = new byte[PixelWidth * PixelHeight * 4];
        var heights = new float[PixelWidth * PixelHeight];
        for (var y = 0; y < PixelHeight; y++)
        for (var x = 0; x < PixelWidth; x++)
        {
            var f = vertical[y];
            var a = scanlines[f.A * PixelWidth + x];
            var b = scanlines[f.B * PixelWidth + x];
            var c = scanlines[f.C * PixelWidth + x];
            var d = scanlines[f.D * PixelWidth + x];
            var coverage = f.Blend(a.Coverage, b.Coverage, c.Coverage, d.Coverage);
            if (coverage <= 0) continue;
            var index = y * PixelWidth + x;
            heights[index] = f.Blend(a.Height, b.Height, c.Height, d.Height) / coverage;
            var edge = Math.Clamp((coverage - .32f) / .36f, 0, 1);
            pixels[index * 4 + 3] = Tone(255 * edge * edge * (3 - 2 * edge));
        }

        var dx = Math.Max(1, PixelWidth / columns);
        var dy = Math.Max(1, PixelHeight / rows);
        for (var y = 0; y < PixelHeight; y++)
        for (var x = 0; x < PixelWidth; x++)
        {
            var index = y * PixelWidth + x;
            var offset = index * 4;
            if (pixels[offset + 3] == 0) continue;
            var height = heights[index];
            var west = heights[index - Math.Min(dx, x)];
            var north = heights[index - Math.Min(dy, y) * PixelWidth];
            var shade = Math.Clamp(((west > 0 ? west - height : 0) + (north > 0 ? north - height : 0)) * .87f, -23, 25);
            var level = height / 254;
            var contour = Math.Max(0, 5 - 3 * Math.Abs(height - 26 * MathF.Round(height / 26)));
            pixels[offset] = Tone(60 + level * 39 + shade + contour);
            pixels[offset + 1] = Tone(76 + level * 55 + shade + contour);
            pixels[offset + 2] = Tone(39 + level * 77 + shade + contour);
        }
        var bitmap = BitmapSource.Create(PixelWidth, PixelHeight, 96, 96, PixelFormats.Bgra32, null, pixels, PixelWidth * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private static Filter[] Filters(int sourceLength, int targetLength)
    {
        var filters = new Filter[targetLength];
        for (var i = 0; i < targetLength; i++)
        {
            var source = (i + .5f) * sourceLength / targetLength - .5f;
            var index = (int)MathF.Floor(source);
            var t = source - index;
            var u = 1 - t;
            int At(int offset) => Math.Clamp(index + offset, 0, sourceLength - 1);
            filters[i] = new Filter(At(-1), At(0), At(1), At(2), u * u * u / 6,
                (4 + t * t * (3 * t - 6)) / 6, (4 + u * u * (3 * u - 6)) / 6, t * t * t / 6);
        }
        return filters;
    }

    private static byte Tone(float channel) => (byte)Math.Clamp(channel, 0, 255);
}
