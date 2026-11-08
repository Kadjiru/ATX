using System.Windows;
using System.Windows.Input;
using AionMeter.App.Services;

namespace AionMeter.App.Windows;

public partial class ActivityHudWindow : Window
{
    public ActivityHudWindow() => InitializeComponent();

    public void Refresh(ActivityTimer? next, DateTimeOffset now)
    {
        var ru = UiText.Current.Code == "ru";
        Kicker.Text = ru ? "ATX  /  СЛЕДУЮЩАЯ АКТИВНОСТЬ" : "ATX  /  NEXT ACTIVITY";
        CountdownText.Text = next?.Countdown(now) ?? "—";
        ActivityName.Text = next?.Name ?? (ru ? "Нет активных таймеров" : "No active timers");
    }

    private void MoveHud(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void CloseHud(object sender, RoutedEventArgs e) => Close();
}
