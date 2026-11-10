using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static ArxBeta.Ui;

namespace ArxBeta;

internal sealed class ChangelogWindow : Window
{
    private static readonly (string Title, string Summary)[] Notes =
    [
        (Loc.T("Карточки питомцев"),
            Loc.T("Семейства получили свои значки. На карточке отдельно видны места обитания, бонусы и стоимость прокачки."))
    ];

    public ChangelogWindow(Window? owner, string version)
    {
        if (owner is not null) Owner = owner;
        Title = Loc.F($"Что нового в ARX {version}");
        Width = 585;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Icon = AppIcon();
        Background = Brush("Base");

        var image = new ImageBrush(new BitmapImage(new Uri("pack://application:,,,/ARX.Beta;component/Assets/Atreia.jpg")))
        {
            Stretch = Stretch.UniformToFill, AlignmentX = AlignmentX.Right
        };
        var hero = new Border
        {
            Background = image, CornerRadius = new CornerRadius(12),
            Child = new Border
            {
                Background = new LinearGradientBrush(Color.FromArgb(250, 9, 22, 25), Color.FromArgb(44, 9, 22, 25), 0),
                CornerRadius = new CornerRadius(12), Padding = new Thickness(17, 20, 17, 18),
                Child = Stack(Space(Text("ARX  /  AION 2 GLOBAL", 11, "Mint", true), 5),
                    Space(Text(Loc.F($"Что нового в {version.Replace("-beta", " beta")}"), 24, bold: true), 3),
                    Hint(Loc.T("Обновление ARX")))
            }
        };
        var body = Stack(Space(hero, 13));
        foreach (var (title, summary) in Notes)
            body.Children.Add(Space(Card(Stack(Space(Text(title, 15, "Mint", true), 4), Hint(summary)), 10), 7));
        var close = Button(Loc.T("Открыть ARX"), Close, true);
        close.IsDefault = true;
        close.IsCancel = true;
        close.HorizontalAlignment = HorizontalAlignment.Right;
        body.Children.Add(close);
        Content = Card(body, 16);
    }
}
