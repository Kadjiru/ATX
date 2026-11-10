using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using static ArxBeta.Ui;

namespace ArxBeta;

internal sealed class LaunchWindow : Window
{
    private readonly ProgressBar _progress = new() { Height = 4, Minimum = 0, Maximum = 100 };
    private readonly TextBlock _message = Text("", 13, "Muted");

    public LaunchWindow()
    {
        Title = Loc.T("ARX · Загрузка");
        Width = 720;
        Height = 360;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        WindowStyle = WindowStyle.None;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        var grid = new Grid();
        grid.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(24),
            Background = new ImageBrush(new BitmapImage(new Uri("pack://application:,,,/Assets/Atreia.jpg")))
            { Stretch = Stretch.UniformToFill, AlignmentX = AlignmentX.Right }
        });
        var tint = Brush("Base").Color;
        grid.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(24),
            Background = new LinearGradientBrush(Color.FromArgb(245, tint.R, tint.G, tint.B), Color.FromArgb(19, tint.R, tint.G, tint.B), 0)
        });
        _progress.Foreground = Ui.Brush("Mint");
        _progress.Background = Ui.Brush("Raised");
        _progress.BorderThickness = new Thickness(0);
        var pane = Stack(
            Space(Label("ARX  /  AION 2 GLOBAL"), 11),
            Space(Text("◈  ARX", 55, "Mint", true), 4),
            Space(Text(Loc.T("Врата Атреи"), 22, "Text", true), 29),
            Space(_progress, 10), _message);
        pane.Width = 328;
        pane.Margin = new Thickness(35, 0, 0, 0);
        pane.HorizontalAlignment = HorizontalAlignment.Left;
        pane.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(pane);
        Content = grid;
        Loaded += (_, _) => BeginAnimation(OpacityProperty, new DoubleAnimation(0.1, 1, TimeSpan.FromMilliseconds(330)));
    }

    public void Stage(double value, string message, bool animate = true)
    {
        _message.Text = message;
        if (animate) _progress.BeginAnimation(ProgressBar.ValueProperty, new DoubleAnimation(value, TimeSpan.FromMilliseconds(250)));
        else _progress.Value = value;
    }
}
