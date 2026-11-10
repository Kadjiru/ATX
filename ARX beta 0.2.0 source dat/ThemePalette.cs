using System.Windows;
using System.Windows.Media;

namespace ArxBeta;

internal sealed record ThemePalette(string Id, string Name, int Base, int Surface, int Raised, int Line,
    int Text, int Muted, int Accent, int Warning, int Secondary)
{
    public const string Default = "atreia";
    public const string FollowApp = "app";

    public static IReadOnlyList<ThemePalette> Options { get; } =
    [
        new(Default, "Атрея", 0x0C1518, 0x142327, 0x203238, 0x30474D, 0xEFF7F5, 0xA9BEC0, 0x85EACA, 0xFFD295, 0xB5C5FE),
        new("abyss", "Бездна", 0x141225, 0x211D35, 0x2F2747, 0x50446F, 0xF6F0FF, 0xC1B4DA, 0xC3ACFF, 0xFFD28F, 0xA6D9FF),
        new("ember", "Жар", 0x1C120E, 0x2A1B16, 0x3D281F, 0x634232, 0xFFF2E8, 0xD2B8A7, 0xFFAD83, 0xFFDD95, 0xE8BCFF),
        new("frost", "Иней", 0x0D1826, 0x152637, 0x213950, 0x395776, 0xEEF7FF, 0xB6CDDF, 0x9CDDFF, 0xFFD5A3, 0xC9B9FF),
        new("graphite", "Графит", 0x15171A, 0x202327, 0x2D3238, 0x4C535C, 0xF4F5F8, 0xBBC1CA, 0xE0EBF5, 0xFFD19D, 0xA5D5FF)
    ];

    public string Label => Loc.T(Name);

    public static ThemePalette ForApp(Profile profile) => Find(profile.AppTheme) ?? Options[0];
    public static ThemePalette ForHud(Profile profile) => Find(profile.HudTheme) ?? ForApp(profile);
    public static ThemePalette? Find(string? id) => Options.FirstOrDefault(theme => theme.Id == id);

    public void Apply(ResourceDictionary resources)
    {
        Set("Base", Base);
        Set("Surface", Surface);
        Set("Raised", Raised);
        Set("Line", Line);
        Set("Text", Text);
        Set("Muted", Muted);
        Set("Mint", Accent);
        Set("Coral", Warning);
        Set("Gold", Secondary);

        void Set(string key, int rgb)
        {
            var color = FromRgb(rgb);
            if (resources[key] is SolidColorBrush current && current.Color == color) return;
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            resources[key] = brush;
        }
    }

    public static Color FromRgb(int rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
}
