using System.Windows;
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
        => Render(relief, columns, rows, new Rect(0, 0, 1, 1), PixelWidth, PixelHeight);

    public static BitmapSource Render(byte[] relief, int columns, int rows, Rect region, int width, int height)
    {
        // Interpolate elevation and coverage separately so unmeasured terrain cannot pull the visible edge down.
        var stepX = region.Width / width;
        var stepY = region.Height / height;
        // Include a halo for shading so a view clipped at the window edge matches the surrounding terrain.
        var dx = Math.Max(1, (int)Math.Round(1 / (180 * stepX)));
        var dy = Math.Max(1, (int)Math.Round(1 / (108 * stepY)));
        var sampleWidth = width + dx;
        var sampleHeight = height + dy;
        var horizontal = Filters(columns, sampleWidth, region.X - dx * stepX, stepX);
        var vertical = Filters(rows, sampleHeight, region.Y - dy * stepY, stepY);
        var firstRow = vertical[0].A;
        var lastRow = vertical[^1].D;
        var scanlines = new Sample[(lastRow - firstRow + 1) * sampleWidth];
        for (var y = firstRow; y <= lastRow; y++)
        for (var x = 0; x < sampleWidth; x++)
        {
            var f = horizontal[x];
            var row = y * columns;
            var a = relief[row + f.A];
            var b = relief[row + f.B];
            var c = relief[row + f.C];
            var d = relief[row + f.D];
            scanlines[(y - firstRow) * sampleWidth + x] = new Sample(f.Blend(a, b, c, d),
                f.Blend(a > 0 ? 1 : 0, b > 0 ? 1 : 0, c > 0 ? 1 : 0, d > 0 ? 1 : 0));
        }

        var pixels = new byte[width * height * 4];
        var heights = new float[sampleWidth * sampleHeight];
        for (var y = 0; y < sampleHeight; y++)
        for (var x = 0; x < sampleWidth; x++)
        {
            var f = vertical[y];
            var a = scanlines[(f.A - firstRow) * sampleWidth + x];
            var b = scanlines[(f.B - firstRow) * sampleWidth + x];
            var c = scanlines[(f.C - firstRow) * sampleWidth + x];
            var d = scanlines[(f.D - firstRow) * sampleWidth + x];
            var coverage = f.Blend(a.Coverage, b.Coverage, c.Coverage, d.Coverage);
            if (coverage <= 0) continue;
            heights[y * sampleWidth + x] = f.Blend(a.Height, b.Height, c.Height, d.Height) / coverage;
            if (x < dx || y < dy) continue;
            var index = (y - dy) * width + x - dx;
            var edge = Math.Clamp((coverage - .32f) / .36f, 0, 1);
            pixels[index * 4 + 3] = Tone(255 * edge * edge * (3 - 2 * edge));
        }

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var index = (y + dy) * sampleWidth + x + dx;
            var offset = (y * width + x) * 4;
            if (pixels[offset + 3] == 0) continue;
            var elevation = heights[index];
            var west = heights[index - dx];
            var north = heights[index - dy * sampleWidth];
            var shade = Math.Clamp(((west > 0 ? west - elevation : 0) + (north > 0 ? north - elevation : 0)) * .87f, -23, 25);
            var level = elevation / 254;
            var contour = Math.Max(0, 5 - 3 * Math.Abs(elevation - 26 * MathF.Round(elevation / 26)));
            pixels[offset] = Tone(60 + level * 39 + shade + contour);
            pixels[offset + 1] = Tone(76 + level * 55 + shade + contour);
            pixels[offset + 2] = Tone(39 + level * 77 + shade + contour);
        }
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private static Filter[] Filters(int sourceLength, int targetLength, double start, double step)
    {
        var filters = new Filter[targetLength];
        for (var i = 0; i < targetLength; i++)
        {
            var source = (float)((start + (i + .5) * step) * sourceLength - .5);
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
