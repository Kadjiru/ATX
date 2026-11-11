using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ArxBeta;

internal static class Ui
{
    public static ImageSource AppIcon() => new BitmapImage(new Uri("pack://application:,,,/ARX.Beta;component/Assets/ARX.ico"));

    public static SolidColorBrush Brush(string name, FrameworkElement? scope = null) =>
        (SolidColorBrush)(scope?.TryFindResource(name) ?? Application.Current.FindResource(name));

    public static T UseBrush<T>(this T element, DependencyProperty property, string name) where T : FrameworkElement
    {
        element.SetResourceReference(property, name);
        return element;
    }

    public static TextBlock Text(string value, double size = 13, string color = "Text", bool bold = false)
    {
        var text = new TextBlock
        {
            Text = value, FontSize = size,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, color);
        return text;
    }

    public static TextBlock Label(string value) => Text(value, 11, "Muted", true);

    public static TextBlock Hint(string value) => Text(value, 13, "Muted");

    public static StackPanel Stack(params UIElement[] elements)
    {
        var panel = new StackPanel();
        foreach (var element in elements) panel.Children.Add(element);
        return panel;
    }

    public static WrapPanel Actions(params UIElement[] elements)
    {
        var panel = new WrapPanel();
        foreach (var element in elements)
        {
            if (element is FrameworkElement e) e.Margin = new Thickness(0, 0, 8, 7);
            panel.Children.Add(element);
        }
        return panel;
    }

    public static Border Card(UIElement child, double padding = 18)
    {
        var card = new Border
        {
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(17),
            Padding = new Thickness(padding), Child = child
        };
        card.SetResourceReference(Border.BackgroundProperty, "Surface");
        card.SetResourceReference(Border.BorderBrushProperty, "Line");
        return card;
    }

    public static Button Button(string text, Action action, bool primary = false)
    {
        var button = new Button { Content = text };
        if (primary)
        {
            button.SetResourceReference(Control.BackgroundProperty, "Mint");
            button.SetResourceReference(Control.BorderBrushProperty, "Mint");
            button.SetResourceReference(Control.ForegroundProperty, "Base");
        }
        button.Click += (_, _) => action();
        return button;
    }

    public static TextBox Input(string text = "", double width = 210, int max = 120) =>
        new() { Text = text, Width = width, MaxLength = max };

    public static FrameworkElement Field(string label, FrameworkElement control)
    {
        var caption = Label(label);
        caption.Margin = new Thickness(0, 0, 0, 5);
        control.HorizontalAlignment = HorizontalAlignment.Left;
        System.Windows.Automation.AutomationProperties.SetName(control, label);
        return Stack(caption, control);
    }

    public static T Space<T>(T element, double bottom = 12) where T : FrameworkElement
    {
        element.Margin = new Thickness(0, 0, 0, bottom);
        return element;
    }

    public static string Number(double value) => value.ToString("N0");
    public static string Compact(double value) => value switch
    {
        >= 1_000_000_000 => $"{value / 1_000_000_000:0.##}B",
        >= 1_000_000 => $"{value / 1_000_000:0.##}M",
        >= 1_000 => $"{value / 1_000:0.#}K",
        _ => $"{value:N0}"
    };

    public static string Clock(TimeSpan time)
    {
        var s = Math.Max(0, (long)Math.Ceiling(time.TotalSeconds));
        return s >= 3600 ? $"{s / 3600:00}:{s / 60 % 60:00}:{s % 60:00}" : $"{s / 60:00}:{s % 60:00}";
    }

    internal static string RiftCountdown(EventRule? rule, DateTimeOffset now)
    {
        if (rule is null) return Loc.T("Добавьте таймер в разделе «События».");
        if (!rule.Enabled) return Loc.T("Таймер выключен.");
        return rule.Next(now) is { } next ? $"{Clock(next - now)} · {LocalEventTime(next)}"
            : Loc.T("Завершён");
    }

    internal static string LocalEventTime(DateTimeOffset time) => $"{time.ToLocalTime():dd.MM HH:mm 'UTC'zzz}";
}
