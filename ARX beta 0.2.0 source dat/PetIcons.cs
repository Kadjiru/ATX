using System.Windows;
using System.Windows.Media;

namespace ArxBeta;

internal static class PetIcons
{
    public static ImageSource Cogni { get; } = Badge(0xB5C5FE,
        "M7,24 Q24,6 41,24 Q24,42 7,24 Z M19,24 A5,5 0 1 1 29,24 A5,5 0 1 1 19,24 Z M24,8 L24,12 M24,36 L24,40");
    public static ImageSource Fera { get; } = Badge(0xFFD295,
        "M14,32 C14,27 19,23 24,23 C29,23 34,27 34,32 C34,39 28,35 24,35 C20,35 14,39 14,32 Z " +
        "M9,22 A3,4 0 1 1 15,22 A3,4 0 1 1 9,22 Z M16,15 A3,4 0 1 1 22,15 A3,4 0 1 1 16,15 Z " +
        "M26,15 A3,4 0 1 1 32,15 A3,4 0 1 1 26,15 Z M33,22 A3,4 0 1 1 39,22 A3,4 0 1 1 33,22 Z");
    public static ImageSource Natura { get; } = Badge(0x85EACA,
        "M12,34 C9,19 20,13 38,11 C37,28 29,39 15,35 Z M10,39 Q21,25 33,16 M20,29 L18,22 M24,25 L31,25");
    public static ImageSource Varian { get; } = Badge(0xF7AFD7,
        "M24,36 C21,22 11,12 6,13 Q8,31 24,36 Q40,31 42,13 C37,12 27,22 24,36 Z M10,22 L20,28 M38,22 L28,28 M24,9 L24,17");
    public static ImageSource Special { get; } = Badge(0xFFE2A3,
        "M24,8 Q27,21 40,24 Q27,27 24,40 Q21,27 8,24 Q21,21 24,8 Z M24,19 L29,24 24,29 19,24 Z");
    public static ImageSource Location { get; } = Badge(0x85EACA,
        "M24,39 C18,32 13,26 13,21 A11,11 0 0 1 35,21 C35,26 30,32 24,39 Z M20,21 A4,4 0 1 1 28,21 A4,4 0 1 1 20,21 Z");
    public static ImageSource Levels { get; } = Badge(0xB5C5FE,
        "M11,35 L20,26 27,30 37,15 M29,15 L37,15 37,23 M11,38 L37,38");
    public static ImageSource Soul { get; } = Badge(0x9FCFFF,
        "M24,8 L36,19 32,34 24,40 16,34 12,19 Z M12,19 L36,19 M24,8 L19,19 24,40 29,19 Z");
    public static ImageSource Board { get; } = Badge(0x85EACA,
        "M11,11 H37 V37 H11 Z M11,24 H37 M24,11 V37 M18,18 L18,18.1 M30,30 L30,30.1");
    public static ImageSource Lock { get; } = Badge(0xFFD295,
        "M14,22 H34 V37 H14 Z M18,22 V17 A6,6 0 0 1 30,17 V22 M24,28 V32");

    private static DrawingImage Badge(int rgb, string path)
    {
        var color = Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        var drawing = new DrawingGroup();
        using (var dc = drawing.Open())
        {
            dc.DrawRoundedRectangle(new LinearGradientBrush(Color.FromArgb(58, color.R, color.G, color.B),
                Color.FromRgb(18, 32, 37), 60), new Pen(new SolidColorBrush(Color.FromArgb(95, color.R, color.G, color.B)), 1),
                new Rect(.5, .5, 47, 47), 11, 11);
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(color), 2.1)
            {
                StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round
            }, Geometry.Parse(path));
        }
        var image = new DrawingImage(drawing);
        image.Freeze();
        return image;
    }
}
