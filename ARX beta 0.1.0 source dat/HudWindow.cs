using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using static ArxBeta.Ui;

namespace ArxBeta;

public enum HudKind { Combat, Events, Tasks }

public sealed class HudWindow : Window
{
    private readonly LegionStore _store;
    private readonly HudKind _kind;
    private readonly StackPanel _body = new();
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private TaskPanel? _tasks;

    public HudWindow(LegionStore store, HudKind kind)
    {
        _store = store;
        _kind = kind;
        Title = $"ARX · {kind switch { HudKind.Combat => "Урон", HudKind.Events => "События", _ => "Задачи" }}";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowActivated = false;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        MinWidth = 290;
        MinHeight = 145;
        WindowStartupLocation = WindowStartupLocation.Manual;
        var area = SystemParameters.WorkArea;
        var defaultLayout = new HudLayout(area.Right - 425, area.Top + 56 + (int)kind * 200,
            kind == HudKind.Combat ? 405 : 365, kind == HudKind.Combat ? 244 : 193, 0.94);
        var layout = store.Profile.HudLayouts.GetValueOrDefault(kind.ToString(), defaultLayout);
        Width = Math.Clamp(layout.Width, MinWidth, Math.Max(MinWidth, area.Width));
        Height = Math.Clamp(layout.Height, MinHeight, Math.Max(MinHeight, area.Height));
        Left = Math.Clamp(layout.Left, area.Left, Math.Max(area.Left, area.Right - Width));
        Top = Math.Clamp(layout.Top, area.Top, Math.Max(area.Top, area.Bottom - Height));
        Opacity = Math.Clamp(layout.Opacity, .42, 1);

        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 9) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var button in new[] { Button("ARX", App.CurrentApp.ShowDashboard), Button("◐", ChangeOpacity), Button("×", Close) })
        {
            button.MinHeight = 26;
            button.Padding = new Thickness(7, 3, 7, 3);
            button.Margin = new Thickness(4, 0, 0, 0);
            buttons.Children.Add(button);
        }
        DockPanel.SetDock(buttons, Dock.Right);
        bar.Children.Add(buttons);
        var handle = Text(Title, 14, "Mint", true);
        handle.VerticalAlignment = VerticalAlignment.Center;
        handle.Cursor = Cursors.SizeAll;
        handle.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        bar.Children.Add(handle);
        DockPanel.SetDock(bar, Dock.Top);
        var panel = new DockPanel();
        panel.Children.Add(bar);
        panel.Children.Add(new ScrollViewer { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = Card(panel, 12);

        if (kind == HudKind.Tasks)
        {
            _tasks = new TaskPanel(store);
            _body.Children.Add(_tasks);
        }
        Refresh();
        _store.Changed += Refresh;
        if (kind == HudKind.Events)
        {
            _clock.Tick += (_, _) => Refresh();
            _clock.Start();
        }
        Closed += (_, _) =>
        {
            _clock.Stop();
            _store.Changed -= Refresh;
            Persist();
        };
    }

    private void ChangeOpacity()
    {
        Opacity = Opacity <= .48 ? .96 : Math.Max(.45, Opacity - .16);
        Persist();
    }

    private void Persist()
    {
        _store.Profile.HudLayouts[_kind.ToString()] = new HudLayout(Left, Top, ActualWidth, ActualHeight, Opacity);
        _store.Save();
    }

    private void Refresh()
    {
        if (_kind == HudKind.Tasks)
        {
            _tasks?.Refresh();
            return;
        }
        _body.Children.Clear();
        if (_kind == HudKind.Events)
        {
            var now = DateTimeOffset.UtcNow;
            var upcoming = _store.Upcoming(now).Take(5).ToArray();
            foreach (var item in upcoming)
                _body.Children.Add(Space(Text($"{Clock(item.Due - now)}   ·   {item.Rule.Name}", 14, "Text", true), 7));
            if (upcoming.Length == 0) _body.Children.Add(Hint("Нет активных таймеров"));
            return;
        }

        var current = _store.Analysis;
        _body.Children.Add(Space(Hint(current.Fight.Demo ? "ДЕМО · учебные данные" : current.Fight.Name), 5));
        _body.Children.Add(Space(Text($"Группа   {Compact(current.Dps)} DPS    ·    {Compact(current.Damage)} урона", 16, "Mint", true), 9));
        foreach (var member in current.Players.Take(8))
        {
            var self = string.Equals(member.Player, current.Fight.Self, StringComparison.OrdinalIgnoreCase);
            _body.Children.Add(Space(Text($"{member.Player}{(self ? " · вы" : "")}    {Compact(current.Rate(member.Damage))} DPS    {Compact(current.Rate(member.Healing))} HPS",
                12, self ? "Gold" : "Text"), 5));
        }
    }
}
