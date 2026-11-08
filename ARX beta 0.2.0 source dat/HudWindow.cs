using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using static ArxBeta.Ui;

namespace ArxBeta;

public enum HudKind { Combat, Events, Tasks }
public enum HudDensity { Minimal, Medium, Detailed }

public sealed class HudWindow : Window
{
    private readonly LegionStore _store;
    private readonly HudKind _kind;
    private readonly StackPanel _body = new();
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private TaskPanel? _tasks;
    private readonly ScrollViewer _scroll;
    private readonly TextBlock _handle;
    private bool _clickThrough;
    private HudDensity _density;

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
        var actions = new List<Button> { Button("ARX", App.CurrentApp.ShowDashboard), Button("◐", ChangeOpacity) };
        if (kind == HudKind.Combat)
        {
            var lockButton = Button("⇥", () => App.CurrentApp.SetCombatLock(!_store.Profile.CombatHudLocked));
            lockButton.ToolTip = "Закрепить HUD и пропускать щелчки · Ctrl+Alt+L";
            actions.Add(lockButton);
            var modes = Button("↕", () => App.CurrentApp.SetCombatDensity((HudDensity)(((int)_density + 1) % 3)));
            modes.ToolTip = "Сменить плотность HUD · Ctrl+Alt+1/2/3";
            actions.Add(modes);
        }
        actions.Add(Button("×", Close));
        foreach (var button in actions)
        {
            button.MinHeight = 26;
            button.Padding = new Thickness(7, 3, 7, 3);
            button.Margin = new Thickness(4, 0, 0, 0);
            buttons.Children.Add(button);
        }
        DockPanel.SetDock(buttons, Dock.Right);
        bar.Children.Add(buttons);
        _handle = Text(Title, 14, "Mint", true);
        _handle.VerticalAlignment = VerticalAlignment.Center;
        _handle.Cursor = Cursors.SizeAll;
        _handle.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        bar.Children.Add(_handle);
        DockPanel.SetDock(bar, Dock.Top);
        var panel = new DockPanel();
        panel.Children.Add(bar);
        _scroll = new ScrollViewer { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(_scroll);
        Content = Card(panel, 12);

        if (kind == HudKind.Combat)
        {
            _density = store.Profile.CombatHudDensity;
            SetDensity(_density, restoreHeight: (layout.Density ?? HudDensity.Medium) == _density);
            Opacity = Math.Clamp(store.Profile.CombatHudOpacity ?? layout.Opacity, .35, 1);
            SourceInitialized += (_, _) => ApplyCombatPreferences();
        }

        if (kind == HudKind.Tasks)
        {
            _tasks = new TaskPanel(store);
            _body.Children.Add(_tasks);
        }
        Refresh();
        _store.Changed += Refresh;
        if (kind == HudKind.Combat) _store.LiveChanged += Refresh;
        if (kind == HudKind.Events)
        {
            _clock.Tick += (_, _) => Refresh();
            _clock.Start();
        }
        Closed += (_, _) =>
        {
            _clock.Stop();
            _store.Changed -= Refresh;
            _store.LiveChanged -= Refresh;
            Persist();
        };
    }

    private void ChangeOpacity()
    {
        var opacity = Opacity <= .41 ? .96 : Math.Max(.35, Opacity - .12);
        if (_kind == HudKind.Combat) App.CurrentApp.SetCombatOpacity(opacity);
        else Opacity = opacity;
        Persist();
    }

    private void Persist()
    {
        _store.Profile.HudLayouts[_kind.ToString()] = new HudLayout(Left, Top, ActualWidth, ActualHeight, Opacity,
            _kind == HudKind.Combat ? _density : null);
        _store.Save();
    }

    private void SetDensity(HudDensity density, bool restoreHeight)
    {
        _density = density;
        MinHeight = density == HudDensity.Minimal ? 106 : 145;
        if (!restoreHeight || Height < MinHeight)
        {
            Height = density switch { HudDensity.Minimal => 115, HudDensity.Detailed => 363, _ => 244 };
            var area = SystemParameters.WorkArea;
            Top = Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - Height));
        }
    }

    public void ApplyCombatPreferences()
    {
        if (_kind != HudKind.Combat) return;
        var app = App.CurrentApp;
        if (_density != _store.Profile.CombatHudDensity) SetDensity(_store.Profile.CombatHudDensity, restoreHeight: false);
        Opacity = Math.Clamp(_store.Profile.CombatHudOpacity ?? Opacity, .35, 1);
        _clickThrough = _store.Profile.CombatHudLocked && app.ClickThroughAvailable;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != 0) Hotkeys.SetClickThrough(handle, _clickThrough);
        _handle.ToolTip = app.ClickThroughAvailable ? "Пропуск щелчков · Ctrl+Alt+L" :
            "Закрепление недоступно: Ctrl+Alt+L занята другим приложением.";
        Refresh();
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
            if (_store.BossNotice is { } notice) _body.Children.Add(Space(Text(notice, 13, "Gold", true), 7));
            var upcoming = _store.Upcoming(now).Take(5).ToArray();
            foreach (var item in upcoming)
                _body.Children.Add(Space(Text($"{Clock(item.Due - now)}   ·   {item.Rule.Name}", 14, "Text", true), 7));
            var bosses = _store.LiveBosses.Where(b => !b.Alive && b.When > now).Take(3).ToArray();
            foreach (var boss in bosses)
                _body.Children.Add(Space(Text($"{Clock(boss.When - now)}   ·   {boss.Name}", 13, "Gold"), 6));
            if (upcoming.Length == 0 && bosses.Length == 0) _body.Children.Add(Hint("Нет активных таймеров"));
            return;
        }

        var current = _store.LiveAnalysis ?? _store.DisplayAnalysis;
        var liveIdle = ReferenceEquals(_store.LiveAnalysis, current) && current.Fight.Captured?.Summary is { } live &&
            (!live.IsActive || DateTimeOffset.UtcNow - live.StartedAt.ToUniversalTime() > TimeSpan.FromMilliseconds(live.CombatMs + 2000));
        _handle.Text = _clickThrough ? "Закреплён · Ctrl+Alt+L" : Title;
        if (_density != HudDensity.Minimal)
            _body.Children.Add(Space(Hint(current.Fight.Demo ? "ДЕМО · учебные данные" : current.Fight.Name), 5));
        _body.Children.Add(Space(Text($"{Compact(liveIdle ? 0 : current.Dps)} DPS    ·    {Compact(current.Damage)} урона", 16, "Mint", true), 9));
        if (_density == HudDensity.Minimal) return;
        if (_density == HudDensity.Detailed)
        {
            _body.Children.Add(Space(Hint($"Лечение {Compact(current.Healing)} · {Compact(current.Rate(current.Healing))} HPS  /  " +
                $"Бурст {(current.Burst is { } peak ? Compact(peak) : "—")}"), 7));
            if (current.Fight.Captured?.Summary.Boss is { MaxHpKnown: true } boss)
                _body.Children.Add(Space(Hint($"Цель: {boss.Name} · {boss.HpFraction:P0} HP"), 5));
        }
        foreach (var member in current.Players)
        {
            var self = string.Equals(member.Player, current.Fight.Self, StringComparison.OrdinalIgnoreCase);
            _body.Children.Add(Space(Text($"{member.Player}{(self ? " · вы" : "")}    {Compact(liveIdle ? 0 : current.Rate(member.Damage))} DPS    {Compact(current.Rate(member.Healing))} HPS",
                12, self ? "Gold" : "Text"), 5));
            if (_density == HudDensity.Detailed)
            {
                var share = current.Damage > 0 ? $"{(double)member.Damage / current.Damage:P0}" : "—";
                var crit = member.CritRate is { } value ? $"{value:P0}" : "—";
                _body.Children.Add(Space(Text($"{Compact(member.Damage)} урона · {share} · крит {crit} · " +
                    $"пик {(member.Burst is { } peak ? Compact(peak) : "—")} · сила {(member.CombatPower > 0 ? Compact(member.CombatPower) : "—")}", 11, "Muted"), 7));
            }
        }
    }
}
