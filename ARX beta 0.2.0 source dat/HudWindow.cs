using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using static ArxBeta.Ui;

namespace ArxBeta;

public enum HudKind { Combat, Events, Tasks, Bosses, Atlas, Rift }
public enum HudDensity { Minimal, Medium, Detailed }
public enum CombatHudScope { Self, Party }

public sealed class HudWindow : Window
{
    private static readonly Brush AltMouseSurface = CreateAltMouseSurface();
    private readonly LegionStore _store;
    private readonly HudKind _kind;
    private readonly StackPanel _body = new();
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _alt = new(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(25) };
    private TaskPanel? _tasks;
    private readonly BossHudPanel? _bosses;
    private readonly AtlasHudPanel? _atlas;
    private readonly ScrollViewer _scroll;
    private readonly TextBlock _handle;
    private readonly Border _frame;
    private readonly StackPanel _buttons;
    private readonly Button? _scopeButton;
    private readonly Button? _modeButton;
    private readonly Button? _atlasLock;
    private ThemePalette _theme;
    private double _surfaceOpacity;
    private bool _mouseLocked;
    private bool? _mousePassesThrough;
    private HudDensity _density;

    public HudWindow(LegionStore store, HudKind kind)
    {
        _store = store;
        _kind = kind;
        _theme = ThemePalette.ForHud(store.Profile);
        _theme.Apply(Resources);
        _alt.Tick += (_, _) => ApplyMouseInput(Hotkeys.IsAltDown);
        var name = kind switch { HudKind.Combat => Loc.T("Урон"), HudKind.Events => Loc.T("События"), HudKind.Bosses => Loc.T("Боссы"),
            HudKind.Atlas => Loc.T("Миникарта"), HudKind.Rift => Loc.T("Пространственный разлом"), _ => Loc.T("Задачи") };
        Title = $"ARX · {name}";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowActivated = false;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        MinWidth = kind == HudKind.Atlas ? 340 : 290;
        MinHeight = kind == HudKind.Atlas ? 395 : 145;
        WindowStartupLocation = WindowStartupLocation.Manual;
        var area = SystemParameters.WorkArea;
        var defaultLayout = new HudLayout(area.Right - 425, area.Top + 56 + (int)kind * 200,
            kind == HudKind.Combat ? 405 : 365, kind == HudKind.Combat ? 406 : kind == HudKind.Bosses ? 213 : 193, 0.94,
            kind == HudKind.Combat ? HudDensity.Detailed : null);
        if (kind == HudKind.Atlas) defaultLayout = new HudLayout(area.Left + 26, area.Top + 60, 388, 500, .94);
        var layout = store.Profile.HudLayouts.GetValueOrDefault(kind.ToString(), defaultLayout);
        Width = Math.Clamp(layout.Width, MinWidth, Math.Max(MinWidth, area.Width));
        Height = Math.Clamp(layout.Height, MinHeight, Math.Max(MinHeight, area.Height));
        Left = Math.Clamp(layout.Left, area.Left, Math.Max(area.Left, area.Right - Width));
        Top = Math.Clamp(layout.Top, area.Top, Math.Max(area.Top, area.Bottom - Height));
        _surfaceOpacity = Math.Clamp(layout.Opacity, 0, 1);

        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 9) };
        _buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var actions = new List<Button> { Button("ARX", App.CurrentApp.ShowDashboard), Button("◐", ChangeOpacity) };
        if (kind == HudKind.Combat)
        {
            _scopeButton = Button(Loc.T("Группа"), () => App.CurrentApp.SetCombatScope(
                _store.Profile.CombatHudScope == CombatHudScope.Party ? CombatHudScope.Self : CombatHudScope.Party));
            _scopeButton.ToolTip = Loc.T("Переключить между своим уроном и уроном своей группы");
            actions.Add(_scopeButton);
            var lockButton = Button("⇥", () => App.CurrentApp.SetCombatLock(!_store.Profile.CombatHudLocked));
            lockButton.ToolTip = Loc.T("Закрепить HUD и пропускать щелчки · Ctrl+Alt+L");
            actions.Add(lockButton);
            _modeButton = Button("↕", () => App.CurrentApp.SetCombatDensity((HudDensity)(((int)_density + 1) % 3)));
            _modeButton.ToolTip = Loc.T("Сменить плотность HUD · Ctrl+Alt+1/2/3");
            System.Windows.Automation.AutomationProperties.SetName(_modeButton, Loc.T("Сменить режим HUD"));
            actions.Add(_modeButton);
        }
        if (kind == HudKind.Atlas)
        {
            _atlasLock = Button("⇥", App.CurrentApp.ToggleAtlasLock);
            actions.Add(_atlasLock);
            SourceInitialized += (_, _) => ApplyAtlasLock();
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
        _handle = Text($"◇  {name.ToUpperInvariant()}", 14, "Mint", true);
        _handle.Background = Brushes.Transparent;
        _handle.VerticalAlignment = VerticalAlignment.Center;
        _handle.Cursor = Cursors.SizeAll;
        _handle.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        bar.Children.Add(_handle);
        DockPanel.SetDock(bar, Dock.Top);
        var panel = new DockPanel();
        panel.Children.Add(bar);
        _scroll = new ScrollViewer { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        if (kind == HudKind.Atlas)
        {
            _atlas = new AtlasHudPanel(store, App.CurrentApp.ShowAtlas);
            panel.Children.Add(_atlas);
            ApplyAtlasLock();
        }
        else panel.Children.Add(_scroll);
        _frame = Card(panel, 12);
        Content = _frame;

        if (kind == HudKind.Combat)
        {
            _density = store.Profile.CombatHudDensity;
            SetDensity(_density, restoreHeight: (layout.Density ?? HudDensity.Medium) == _density);
            _surfaceOpacity = Math.Clamp(store.Profile.CombatHudOpacity ?? layout.Opacity, 0, 1);
            SourceInitialized += (_, _) => ApplyCombatPreferences();
        }

        if (kind == HudKind.Tasks)
        {
            _tasks = new TaskPanel(store, compact: true);
            _body.Children.Add(_tasks);
        }
        if (kind == HudKind.Bosses)
        {
            _bosses = new BossHudPanel(store);
            _body.Children.Add(_bosses);
        }
        ApplySurface();
        Refresh();
        _store.Changed += Refresh;
        if (kind is HudKind.Combat or HudKind.Bosses) _store.LiveChanged += Refresh;
        if (kind is HudKind.Events or HudKind.Bosses or HudKind.Rift)
        {
            _clock.Tick += (_, _) => Refresh();
            _clock.Start();
        }
        Closed += (_, _) =>
        {
            _alt.Stop();
            _clock.Stop();
            _store.Changed -= Refresh;
            _store.LiveChanged -= Refresh;
            Persist();
        };
    }

    internal void OpenAtlas(AtlasSelection selection) => _atlas?.Open(selection);

    internal void ApplyAtlasLock()
    {
        if (_atlasLock is null) return;
        var available = App.CurrentApp.AtlasLockAvailable;
        var locked = available && _store.Profile.AtlasHudLocked;
        SetMouseLock(locked);
        _atlasLock.IsEnabled = available;
        _atlasLock.SetResourceReference(Control.ForegroundProperty, locked ? "Mint" : "Text");
        _atlasLock.ToolTip = !available ? Loc.T("Закрепление недоступно: Ctrl+Alt+F11 занята.") :
            locked ? Loc.T("Удерживайте Alt для управления мышью · Ctrl+Alt+F11 снимает закрепление") : Loc.T("Закрепить миникарту · Ctrl+Alt+F11");
        System.Windows.Automation.AutomationProperties.SetName(_atlasLock, locked ? Loc.T("Разблокировать миникарту") : Loc.T("Закрепить миникарту"));
    }

    private void ChangeOpacity()
    {
        var opacity = _surfaceOpacity <= .01 ? 1 : Math.Max(0, _surfaceOpacity - .15);
        if (_kind == HudKind.Combat) App.CurrentApp.SetCombatOpacity(opacity);
        else
        {
            _surfaceOpacity = opacity;
            ApplySurface();
            Refresh();
        }
        Persist();
    }

    private void Persist()
    {
        if (_atlas is not null) _store.Profile.AtlasHudView = _atlas.View;
        if (_kind == HudKind.Combat && _density != HudDensity.Minimal)
            _store.Profile.CombatHudHeights[_density] = Height;
        _store.Profile.HudLayouts[_kind.ToString()] = new HudLayout(Left, Top, ActualWidth, ActualHeight, _surfaceOpacity,
            _kind == HudKind.Combat ? _density : null);
        try { _store.Save(); }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    private void SetDensity(HudDensity density, bool restoreHeight)
    {
        if (_density != density && _density != HudDensity.Minimal)
            _store.Profile.CombatHudHeights[_density] = Height;
        _density = density;
        var minimal = density == HudDensity.Minimal;
        _frame.Padding = new Thickness(minimal ? 8 : 12);
        foreach (Button button in _buttons.Children)
            button.Visibility = minimal && button != _modeButton ? Visibility.Collapsed : Visibility.Visible;
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
        _surfaceOpacity = Math.Clamp(_store.Profile.CombatHudOpacity ?? _surfaceOpacity, 0, 1);
        SetMouseLock(_store.Profile.CombatHudLocked && app.ClickThroughAvailable);
        _handle.ToolTip = app.ClickThroughAvailable ? Loc.T("Удерживайте Alt для управления мышью · Ctrl+Alt+L меняет закрепление") :
            Loc.T("Закрепление недоступно: Ctrl+Alt+L занята другим приложением.");
        ApplySurface();
        Refresh();
    }

    private void SetMouseLock(bool locked)
    {
        _mouseLocked = locked;
        ApplyMouseInput(locked && Hotkeys.IsAltDown);
        if (locked) _alt.Start();
        else _alt.Stop();
    }

    private void ApplyMouseInput(bool altDown)
    {
        var temporaryInput = _mouseLocked && altDown;
        var background = temporaryInput ? AltMouseSurface : Brushes.Transparent;
        if (Background != background) Background = background;

        var passThrough = _mouseLocked && !altDown;
        if (_mousePassesThrough == passThrough) return;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == 0) return;
        Hotkeys.SetClickThrough(handle, passThrough);
        _mousePassesThrough = passThrough;
    }

    private static Brush CreateAltMouseSurface()
    {
        // A layered window needs nonzero alpha for clicks on its transparent regions.
        var surface = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        surface.Freeze();
        return surface;
    }

    internal void ApplyTheme(ThemePalette theme)
    {
        if (_theme == theme) return;
        _theme = theme;
        theme.Apply(Resources);
        ApplySurface();
        Refresh();
    }

    private void ApplySurface()
    {
        var minimal = _kind == HudKind.Combat && _density == HudDensity.Minimal;
        _frame.Background = minimal ? Brushes.Transparent : new LinearGradientBrush(
            Brush("Raised", this).Color, Brush("Base", this).Color, 65)
            { Opacity = _surfaceOpacity };
        _frame.BorderBrush = Faded(Brush("Mint", this), _surfaceOpacity * .42);
        _frame.BorderThickness = minimal ? new Thickness(0) : new Thickness(1, 2, 1, 1);
        _handle.Effect = TextShadow();
        foreach (Button button in _buttons.Children)
        {
            button.Background = minimal ? Brushes.Transparent : Faded(Brush("Raised", this), _surfaceOpacity);
            button.BorderBrush = minimal ? Brushes.Transparent : Faded(Brush("Line", this), _surfaceOpacity);
            button.Effect = TextShadow();
        }
        _tasks?.SetSurfaceOpacity(_surfaceOpacity);
        _bosses?.SetSurfaceOpacity(_surfaceOpacity);
    }

    private void Refresh()
    {
        if (_atlas is not null)
        {
            _atlas.Refresh();
            return;
        }
        if (_kind == HudKind.Tasks)
        {
            _tasks?.Refresh();
            return;
        }
        if (_bosses is not null)
        {
            _bosses.Refresh();
            return;
        }
        _body.Children.Clear();
        if (_kind == HudKind.Rift)
        {
            _body.Children.Add(Plate(Stack(
                Space(Ink(Label(Loc.T("Следующее открытие по расписанию"))), 5),
                Ink(Text(RiftCountdown(_store.Rift, DateTimeOffset.UtcNow), 20, "Mint", true))), Brush("Mint", this)));
            if (App.CurrentApp.EventNotice(EventRule.RiftId) is { } riftNotice)
                _body.Children.Add(Ink(Text(riftNotice, 13, "Gold", true)));
            return;
        }
        if (_kind == HudKind.Events)
        {
            var now = DateTimeOffset.UtcNow;
            if (_store.BossNotice is { } notice) _body.Children.Add(Space(Ink(Text(notice, 13, "Gold", true)), 7));
            if (App.CurrentApp.EventNotice() is { } eventNotice) _body.Children.Add(Space(Ink(Text(eventNotice, 13, "Gold", true)), 7));
            var upcoming = _store.Upcoming(now).OrderByDescending(item => _store.Profile.FavoriteEvents.Contains(item.Rule.Id)).Take(5).ToArray();
            foreach (var item in upcoming)
                _body.Children.Add(Space(EventRow(item.Rule.DisplayName, Clock(item.Due - now), "Mint"), 5));
            var bosses = _store.LiveBosses.Where(b => !b.Alive && b.When > now).Take(3).ToArray();
            foreach (var boss in bosses)
                _body.Children.Add(Space(EventRow(boss.Name, Clock(boss.When - now), "Coral"), 5));
            if (upcoming.Length == 0 && bosses.Length == 0) _body.Children.Add(Ink(Hint(Loc.T("Нет активных таймеров"))));
            return;
        }

        var current = _store.OverlayAnalysis;
        var scope = _store.Profile.CombatHudScope;
        var roster = _store.LiveRoster;
        var rows = PartyCombat.Rows(current, _store.LiveCharacter, roster, _store.Profile.CombatHudSelfFirst, scope);
        if (_scopeButton is not null) _scopeButton.Content = scope == CombatHudScope.Self ? Loc.T("Я") : Loc.T("Группа");
        var minimal = _density == HudDensity.Minimal;
        var detailed = _density == HudDensity.Detailed;
        var font = Math.Clamp(_store.Profile.CombatHudTextSize, 12, 22);
        var mode = current.Fight.Demo ? Loc.T("ДЕМО") : scope == CombatHudScope.Self ? Loc.T("ТОЛЬКО ВЫ") : roster switch
        {
            { Members.Count: > 0 } => Loc.F($"СВОЯ ГРУППА · {rows.Count}"),
            _ => Loc.T("ТОЛЬКО ВЫ")
        };
        _handle.Text = minimal ? $"◇  ARX · {mode}" : _mouseLocked ? Loc.T("◇  БОЙ · закреплён") : Loc.T("◇  БОЙ");
        _handle.FontSize = minimal ? 11 : 14;
        _handle.SetResourceReference(TextBlock.ForegroundProperty, minimal ? "Text" : "Mint");
        if (!minimal)
        {
            var context = current.Fight.Id == Guid.Empty ? mode : $"{mode}   /   {current.Fight.Name}";
            _body.Children.Add(Space(Ink(Label(context)), 8));
        }
        var damage = rows.Sum(r => r.Stats?.Damage ?? 0);
        if (detailed)
        {
            var own = rows.FirstOrDefault(r => r.IsSelf);
            var total = rows.Any(r => r.Stats is not null) ? Compact(current.Rate(damage)) : "—";
            var personal = own?.Stats is { } stats ? Compact(current.Rate(stats.Damage)) : "—";
            var totalDamage = rows.Any(r => r.Stats is not null) ? Compact(damage) : "—";
            var duration = Clock(TimeSpan.FromSeconds(current.Fight.Duration));
            _body.Children.Add(Space(Ink(Hint(scope == CombatHudScope.Self ? Loc.F($"Вы {personal} DPS · {totalDamage} DMG · {duration}")
                : Loc.F($"Группа {total} DPS · {totalDamage} DMG · {duration}"))), 7));
            if (current.Fight.Captured?.Summary.Boss is { MaxHpKnown: true } boss)
                _body.Children.Add(Space(Ink(Hint($"{boss.Name} · {boss.HpFraction:P0} HP")), 7));
        }
        foreach (var member in rows)
            _body.Children.Add(Space(CombatRow(current, rows, member, minimal, detailed, font, damage), minimal ? 2 : 5));
        if (rows.Count == 0)
        {
            _body.Children.Add(Ink(Text(Loc.T("Ожидание вашего персонажа"), font, "Text", true)));
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

    private FrameworkElement CombatRow(FightAnalysis current, IReadOnlyList<PartyCombatRow> rows,
        PartyCombatRow member, bool minimal, bool detailed, int font, long damage)
    {
        var memberName = PartyCombat.Label(rows, member);
        var name = Ink(Text(memberName, font, bold: true));
        name.Foreground = member.Color;
        name.TextWrapping = TextWrapping.NoWrap;
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        name.ToolTip = memberName;
        var dps = member.Stats is { } stats ? Compact(current.Rate(stats.Damage)) : "—";
        var total = member.Stats is { } measured ? Compact(measured.Damage) : "—";
        var rate = Ink(Text($"{dps} DPS  ·  {total} DMG", minimal ? Math.Max(11, font - 2) : font, bold: true));
        rate.Foreground = member.Color;
        rate.Margin = new Thickness(9, 0, 0, 0);
        rate.TextWrapping = TextWrapping.NoWrap;
        var line = new DockPanel();
        DockPanel.SetDock(rate, Dock.Right);
        line.Children.Add(rate);
        line.Children.Add(name);
        if (minimal) return line;
        var content = Stack(line);
        if (detailed && member.Stats is { } player)
        {
            var share = damage > 0 ? $"{(double)player.Damage / damage:P0}" : "—";
            var crit = player.CritRate is { } value ? $"{value:P0}" : "—";
            content.Children.Add(Ink(Hint(Loc.F($"{share} · крит {crit} · пик ") +
                (player.Burst is { } peak ? Compact(peak) : "—") +
                (player.CombatPower > 0 ? Loc.F($" · сила {Compact(player.CombatPower)}") : ""))));
        }
        return Plate(content, member.Color);
    }

    private Border EventRow(string name, string time, string color)
    {
        var row = new DockPanel();
        var countdown = Ink(Text(time, 14, color, true));
        countdown.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(countdown, Dock.Right);
        row.Children.Add(countdown);
        row.Children.Add(Ink(Text(name, 13, bold: true)));
        return Plate(row, Brush(color, this));
    }

    private Border Plate(UIElement content, Brush accent) => Plate(content, Brush("Raised", this), accent, _surfaceOpacity);

    internal static Border Plate(UIElement content, Brush surface, Brush accent, double opacity)
    {
        var plate = new Border { BorderThickness = new Thickness(2, 0, 0, 0), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 5, 8, 5), Child = content };
        StylePlate(plate, surface, accent, opacity);
        return plate;
    }

    internal static void StylePlate(Border plate, Brush surface, Brush accent, double opacity)
    {
        plate.Background = Faded(surface, opacity * .82);
        plate.BorderBrush = Faded(accent, opacity);
    }

    internal static Brush Faded(Brush source, double opacity)
    {
        var brush = source.CloneCurrentValue();
        brush.Opacity = opacity;
        brush.Freeze();
        return brush;
    }

    internal static TextBlock Ink(TextBlock text)
    {
        text.Effect = TextShadow();
        return text;
    }

    private static DropShadowEffect TextShadow() => new() { Color = Colors.Black, BlurRadius = 4, ShadowDepth = 0, Opacity = 1 };
}
