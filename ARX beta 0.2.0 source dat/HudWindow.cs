using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
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
    private readonly Border _frame;
    private readonly StackPanel _buttons;
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
            kind == HudKind.Combat ? 405 : 365, kind == HudKind.Combat ? 282 : 193, 0.94);
        var layout = store.Profile.HudLayouts.GetValueOrDefault(kind.ToString(), defaultLayout);
        Width = Math.Clamp(layout.Width, MinWidth, Math.Max(MinWidth, area.Width));
        Height = Math.Clamp(layout.Height, MinHeight, Math.Max(MinHeight, area.Height));
        Left = Math.Clamp(layout.Left, area.Left, Math.Max(area.Left, area.Right - Width));
        Top = Math.Clamp(layout.Top, area.Top, Math.Max(area.Top, area.Bottom - Height));
        Opacity = Math.Clamp(layout.Opacity, .42, 1);

        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 9) };
        _buttons = new StackPanel { Orientation = Orientation.Horizontal };
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
            _buttons.Children.Add(button);
        }
        DockPanel.SetDock(_buttons, Dock.Right);
        bar.Children.Add(_buttons);
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
        _frame = Card(panel, 12);
        Content = _frame;

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
        if (_kind == HudKind.Combat && _density != HudDensity.Minimal)
            _store.Profile.CombatHudHeights[_density] = Height;
        _store.Profile.HudLayouts[_kind.ToString()] = new HudLayout(Left, Top, ActualWidth, ActualHeight, Opacity,
            _kind == HudKind.Combat ? _density : null);
        _store.Save();
    }

    private void SetDensity(HudDensity density, bool restoreHeight)
    {
        if (_density != density && _density != HudDensity.Minimal)
            _store.Profile.CombatHudHeights[_density] = Height;
        _density = density;
        var minimal = density == HudDensity.Minimal;
        _frame.Background = minimal ? Brushes.Transparent : Brush("Surface");
        _frame.BorderThickness = minimal ? new Thickness(0) : new Thickness(1);
        _frame.Padding = new Thickness(minimal ? 8 : 12);
        _buttons.Visibility = minimal ? Visibility.Collapsed : Visibility.Visible;
        _handle.Effect = minimal ? TextShadow() : null;
        _scroll.VerticalScrollBarVisibility = minimal ? ScrollBarVisibility.Hidden : ScrollBarVisibility.Auto;
        ResizeMode = minimal ? ResizeMode.NoResize : ResizeMode.CanResizeWithGrip;
        MinHeight = minimal ? 65 : 145;
        if (!restoreHeight || Height < MinHeight)
        {
            var area = SystemParameters.WorkArea;
            var defaultHeight = density switch { HudDensity.Minimal => 90, HudDensity.Detailed => 406, _ => 282 };
            Height = Math.Clamp(_store.Profile.CombatHudHeights.GetValueOrDefault(density, defaultHeight),
                MinHeight, Math.Max(MinHeight, area.Height));
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

        var tracking = _store.FollowLive || _store.LiveAnalysis is not null;
        var current = _store.LiveAnalysis ?? _store.DisplayAnalysis;
        var character = tracking ? _store.LiveCharacter : null;
        var roster = tracking ? _store.LiveRoster : null;
        var rows = PartyCombat.Rows(current, character, roster, _store.Profile.CombatHudSelfFirst);
        var minimal = _density == HudDensity.Minimal;
        var detailed = _density == HudDensity.Detailed;
        var font = Math.Clamp(_store.Profile.CombatHudTextSize, 12, 22);
        var mode = current.Fight.Demo ? "ДЕМО" : !tracking ? "ОТЧЁТ" : roster switch
        {
            null => "СОСТАВ ОЖИДАЕТСЯ",
            { Members.Count: 0 } => "СОЛО",
            _ => $"ГРУППА · {rows.Count}"
        };
        _handle.Text = minimal ? $"ARX / DPS · {mode}" : _clickThrough ? "DPS · закреплён" : "ARX / DPS";
        _handle.FontSize = minimal ? 11 : 14;
        _handle.Foreground = Brush(minimal ? "Text" : "Mint");
        if (!minimal)
        {
            var context = current.Fight.Id == Guid.Empty ? mode : $"{mode}   /   {current.Fight.Name}";
            _body.Children.Add(Space(Label(context), 8));
        }
        if (detailed)
        {
            var own = rows.FirstOrDefault(r => r.IsSelf);
            var total = rows.Any(r => r.Stats is not null) ? Compact(current.Dps) : "—";
            var personal = own?.Stats is { } stats ? Compact(current.Rate(stats.Damage)) : "—";
            _body.Children.Add(Space(Hint($"Сумма {total} DPS  /  Вы {personal} DPS  /  {Clock(TimeSpan.FromSeconds(current.Fight.Duration))}"), 7));
            if (current.Healing > 0)
                _body.Children.Add(Space(Hint($"Лечение {Compact(current.Healing)} · {Compact(current.Rate(current.Healing))} HPS"), 7));
            if (current.Fight.Captured?.Summary.Boss is { MaxHpKnown: true } boss)
                _body.Children.Add(Space(Hint($"{boss.Name} · {boss.HpFraction:P0} HP"), 7));
        }
        foreach (var member in rows)
            _body.Children.Add(Space(CombatRow(current, rows, member, minimal, detailed, font), minimal ? 2 : 5));
        if (rows.Count == 0)
        {
            var empty = Text("Ожидание данных боя", font, "Text", true);
            if (minimal) empty.Effect = TextShadow();
            _body.Children.Add(empty);
        }
        if (minimal)
        {
            var area = SystemParameters.WorkArea;
            var height = Math.Clamp(43 + (font * 1.35 + 3) * Math.Max(1, rows.Count), MinHeight, Math.Max(MinHeight, area.Height));
            if (Height != height)
            {
                Height = height;
                Top = Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - Height));
            }
        }
    }

    private static FrameworkElement CombatRow(FightAnalysis current, IReadOnlyList<PartyCombatRow> rows,
        PartyCombatRow member, bool minimal, bool detailed, int font)
    {
        var memberName = PartyCombat.Label(rows, member);
        var name = Text(memberName, font, bold: true);
        name.Foreground = member.Color;
        name.TextWrapping = TextWrapping.NoWrap;
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        name.ToolTip = memberName;
        var dps = member.Stats is { } stats ? Compact(current.Rate(stats.Damage)) : "—";
        var hps = member.Stats is { } healing ? Compact(current.Rate(healing.Healing)) : "—";
        var rate = Text(minimal ? dps : $"{dps} DPS  ·  {hps} HPS", font, bold: true);
        rate.Foreground = member.Color;
        rate.Margin = new Thickness(9, 0, 0, 0);
        rate.TextWrapping = TextWrapping.NoWrap;
        var line = new DockPanel();
        DockPanel.SetDock(rate, Dock.Right);
        line.Children.Add(rate);
        line.Children.Add(name);
        if (minimal)
        {
            name.Effect = TextShadow();
            rate.Effect = TextShadow();
            return line;
        }
        var content = Stack(line);
        if (detailed && member.Stats is { } player)
        {
            var share = current.Damage > 0 ? $"{(double)player.Damage / current.Damage:P0}" : "—";
            var crit = player.CritRate is { } value ? $"{value:P0}" : "—";
            content.Children.Add(Hint($"{Compact(player.Damage)} урона · {share} · крит {crit} · пик " +
                (player.Burst is { } peak ? Compact(peak) : "—") +
                (player.CombatPower > 0 ? $" · сила {Compact(player.CombatPower)}" : "")));
        }
        return new Border { Background = Brush("Raised"), BorderBrush = member.Color, BorderThickness = new Thickness(2, 0, 0, 0),
            CornerRadius = new CornerRadius(5), Padding = new Thickness(8, 4, 8, 4), Child = content };
    }

    private static DropShadowEffect TextShadow() => new() { Color = Colors.Black, BlurRadius = 4, ShadowDepth = 0, Opacity = 1 };
}
