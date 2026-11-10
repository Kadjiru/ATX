using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace ArxBeta;

internal static class Loc
{
    private static readonly Lazy<Dictionary<string, string>> Translations = new(Load);

    public static bool English { get; private set; }
    public static string Language => English ? "en" : "ru";

    public static void Configure(string? language)
    {
        English = string.Equals(language, "en", StringComparison.OrdinalIgnoreCase);
        if (!English) return;

        var culture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    public static string T(string source) => English ? Translations.Value.GetValueOrDefault(source, source) : source;

    public static string F(FormattableString source) =>
        string.Format(CultureInfo.CurrentCulture, T(source.Format), source.GetArguments());

    public static string Name(string russian, string? english) =>
        English && !string.IsNullOrWhiteSpace(english) ? english : russian;

    public static ComboBox LocalizeItems(ComboBox combo)
    {
        if (!English) return combo;

        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding { Converter = new ItemText() });
        combo.ItemTemplate = new DataTemplate { VisualTree = text };
        return combo;
    }

    private static Dictionary<string, string> Load()
    {
        using var file = typeof(Loc).Assembly.GetManifestResourceStream("ARX.English.json")
            ?? throw new InvalidDataException("Missing ARX English translations.");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(file) ?? [];
    }

    private sealed class ItemText : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is string source ? T(source) : value?.ToString() ?? "";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            Binding.DoNothing;
    }
}
