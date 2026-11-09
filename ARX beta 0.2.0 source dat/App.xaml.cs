using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ArxBeta;

public partial class App : Application
{
    private LegionStore _store = null!;
    private CatalogSync _catalog = null!;
    private LiveCombat? _live;
    private Hotkeys? _hotkeys;
    private Dashboard? _dashboard;
    private readonly Dictionary<HudKind, HudWindow> _huds = [];
    private Mutex? _instance;
    private bool _closing;
    public static App CurrentApp => (App)Current;
    internal LiveCombat? Live => _live;
    internal bool IsClosing => _closing;
    internal bool ClickThroughAvailable => _hotkeys?.ClickThroughRegistered == true;
    internal string HotkeyStatus => _hotkeys is null ? "Глобальные сочетания проверяются при запуске" :
        _hotkeys.Unavailable.Count > 0 ? "Заняты сочетания: " + string.Join(", ", _hotkeys.Unavailable) : "Глобальные сочетания готовы";
    internal bool? IsHotkeyRegistered(Hotkeys.Shortcut shortcut) => _hotkeys?.IsRegistered(shortcut);

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var render = e.Args.Length >= 2 && e.Args[0] == "--render";
        if (!render)
        {
            _instance = new Mutex(true, "Local\\ARX.Legion.Beta", out var first);
            if (!first)
            {
                MessageBox.Show("ARX уже запущен. Главное окно можно открыть из любого HUD кнопкой ARX.", "ARX");
                Shutdown();
                return;
            }
        }
        if (render)
        {
            _store = new LegionStore(true);
            _catalog = CatalogSync.Open(true);
            var section = e.Args.Length > 2 ? e.Args[2] : "combat";
            if (section == "splash")
            {
                var splashPreview = new LaunchWindow();
                splashPreview.Stage(76, "Сверка версии базы с репозиторием ARX", false);
                Render(splashPreview, e.Args[1], new Size(720, 360));
            }
            else if (section.StartsWith("hud-", StringComparison.Ordinal))
            {
                _store.Profile.CombatHudDensity = section switch
                {
                    "hud-minimal" => HudDensity.Minimal, "hud-detailed" => HudDensity.Detailed, _ => HudDensity.Medium
                };
                var kind = section switch
                {
                    "hud-events" => HudKind.Events, "hud-tasks" => HudKind.Tasks,
                    "hud-bosses" => HudKind.Bosses, _ => HudKind.Combat
                };
                if (kind == HudKind.Bosses && WorldBossCatalog.Choices.FirstOrDefault() is { } boss)
                {
                    _store.Profile.BossHudSelection = boss.Id;
                    _store.UpdateLive(null, "Демонстрация", null,
                        [new ObservedBoss("preview", boss.Name, boss.Zone, false, DateTimeOffset.UtcNow.AddMinutes(38), boss.Id)]);
                }
                if (kind == HudKind.Tasks)
                    _store.Profile.Tasks.Add(new SessionTask(Guid.NewGuid(), _store.Profile.SessionId, "Проверить задания Бездны", false));
                var hudPreview = new HudWindow(_store, kind);
                Render(hudPreview, e.Args[1], new Size(hudPreview.Width, hudPreview.Height));
            }
            else
            {
                var preview = new Dashboard(_store, _catalog);
                preview.Navigate(section);
                Render(preview, e.Args[1], new Size(1340, 900));
            }
            Shutdown();
            return;
        }
        var splash = new LaunchWindow();
        splash.Show();
        try
        {
            splash.Stage(24, "Загрузка локального профиля");
            _store = await Task.Run(() => new LegionStore(false));
            splash.Stage(48, "Проверка базы Aion 2 Global");
            _catalog = await Task.Run(() => CatalogSync.Open(false));
            _live = new LiveCombat(_store);
            _store.LiveChanged += CheckBossAlerts;
            splash.Stage(76, "Сверка версии базы с репозиторием ARX");
            await Task.WhenAll(_catalog.RefreshAsync(), Task.Delay(620));
            if (_store.NewProfile) _store.Profile.Events = [.. _catalog.Reference.Presets];
            splash.Stage(100, $"{_catalog.Status} · ревизия {_catalog.Revision}");
            await Task.Delay(290);
            ShowDashboard();
            splash.Close();
            BeginTeamSync();
            _hotkeys = new Hotkeys(() => ToggleHud(HudKind.Combat),
                () => SetCombatLock(!_store.Profile.CombatHudLocked), () => _live?.FinishSegment(),
                mode =>
                {
                    SetCombatDensity(mode);
                    if (!_huds.ContainsKey(HudKind.Combat)) ToggleHud(HudKind.Combat);
                },
                delta => SetCombatOpacity(CombatOpacity + delta), () => ToggleHud(HudKind.Bosses));
            if (!ClickThroughAvailable && _store.Profile.CombatHudLocked)
            {
                _store.Profile.CombatHudLocked = false;
                _store.Save();
            }
            _live.Start();
        }
        catch (Exception ex)
        {
            _closing = true;
            splash.Close();
            MessageBox.Show("Не удалось запустить ARX. " + ex.Message, "ARX", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    public void ShowDashboard()
    {
        if (_closing) return;
        if (_dashboard is not null)
        {
            if (_dashboard.WindowState == WindowState.Minimized) _dashboard.WindowState = WindowState.Normal;
            _dashboard.Activate();
            return;
        }
        _dashboard = new Dashboard(_store, _catalog);
        _dashboard.Closing += (_, args) =>
        {
            args.Cancel = !_closing;
            if (args.Cancel) _ = ExitApplicationAsync();
        };
        MainWindow = _dashboard;
        _dashboard.Show();
    }

    public void ToggleHud(HudKind kind)
    {
        if (_closing) return;
        if (_huds.TryGetValue(kind, out var existing))
        {
            existing.Close();
            return;
        }
        ShowHud(kind);
    }

    internal void TrackBoss(ObservedBoss boss)
    {
        if (_closing) return;
        _store.Profile.BossHudSelection = boss.SelectionId;
        _store.Commit();
        ShowHud(HudKind.Bosses);
    }

    private void ShowHud(HudKind kind)
    {
        if (_huds.ContainsKey(kind)) return;
        var hud = new HudWindow(_store, kind);
        _huds.Add(kind, hud);
        hud.Closed += (_, _) => _huds.Remove(kind);
        hud.Show();
    }

    internal double CombatOpacity => _store.Profile.CombatHudOpacity ??
        _store.Profile.HudLayouts.GetValueOrDefault(HudKind.Combat.ToString())?.Opacity ?? .94;

    internal void SetCombatDensity(HudDensity density)
    {
        _store.Profile.CombatHudDensity = density;
        if (_huds.TryGetValue(HudKind.Combat, out var hud)) hud.ApplyCombatPreferences();
        _store.Save();
    }

    internal void SetCombatOpacity(double opacity)
    {
        _store.Profile.CombatHudOpacity = Math.Clamp(Math.Round(opacity, 2), 0, 1);
        if (_huds.TryGetValue(HudKind.Combat, out var hud)) hud.ApplyCombatPreferences();
        _store.Save();
    }

    internal void SetCombatLock(bool value)
    {
        _store.Profile.CombatHudLocked = value && ClickThroughAvailable;
        if (_huds.TryGetValue(HudKind.Combat, out var hud)) hud.ApplyCombatPreferences();
        _store.Save();
    }

    internal void SetCombatScope(CombatHudScope scope)
    {
        _store.Profile.CombatHudScope = scope;
        if (_huds.TryGetValue(HudKind.Combat, out var hud)) hud.ApplyCombatPreferences();
        _store.Save();
    }

    private async Task ExitApplicationAsync()
    {
        _closing = true;
        // Let the first Window.Closing notification finish before Shutdown closes the main window again.
        await System.Windows.Threading.Dispatcher.Yield();
        _teamPoll?.Stop();
        _hotkeys?.Dispose();
        _hotkeys = null;
        try
        {
            foreach (var hud in _huds.Values.ToArray()) hud.Close();
            if (_live is { } live) await live.StopAsync();
        }
        finally { Shutdown(); }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _teamPoll?.Stop();
        if (_store is not null) _store.LiveChanged -= CheckBossAlerts;
        _hotkeys?.Dispose();
        _live?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }

    private static void Render(Window preview, string path, Size size)
    {
        var root = (FrameworkElement)preview.Content;
        root.Measure(size);
        root.Arrange(new Rect(size));
        root.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }
}
