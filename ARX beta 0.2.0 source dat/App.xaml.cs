using System.IO;
using System.Windows.Threading;
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
    private AppUpdates? _updates;
    private DispatcherTimer? _updateTimer;
    private UpdateWindow? _updateWindow;
    private ChangelogWindow? _changelogWindow;
    private string? _offeredUpdate;
    private string? _approvedUpdate;
    public static App CurrentApp => (App)Current;
    internal LiveCombat? Live => _live;
    internal bool IsClosing => _closing;
    internal AppUpdates? Updates => _updates;
    internal bool UpdateApproved => _approvedUpdate is not null && _approvedUpdate == _updates?.ReadyIdentity;
    internal bool ClickThroughAvailable => _hotkeys?.ClickThroughRegistered == true;
    internal bool AtlasLockAvailable => _hotkeys?.AtlasLockRegistered == true;
    internal string HotkeyStatus => _hotkeys is null ? Loc.T("Глобальные сочетания проверяются при запуске") :
        _hotkeys.Unavailable.Count > 0 ? Loc.T("Заняты сочетания: ") + string.Join(", ", _hotkeys.Unavailable) : Loc.T("Глобальные сочетания готовы");
    internal bool? IsHotkeyRegistered(Hotkeys.Shortcut shortcut) => _hotkeys?.IsRegistered(shortcut);

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var render = e.Args.Length >= 2 && e.Args[0] == "--render";
        if (!render)
        {
            for (var attempt = 0; InstallerRunning(); attempt++)
            {
                if (attempt >= 120)
                {
                    MessageBox.Show(Loc.T("Установщик ARX ещё работает. Запустите приложение после завершения установки."), "ARX");
                    Shutdown();
                    return;
                }
                await Task.Delay(250);
            }
            _instance = new Mutex(true, "Local\\ARX.Legion.Beta", out var first);
            if (!first)
            {
                MessageBox.Show(Loc.T("ARX уже запущен. Главное окно можно открыть из любого HUD кнопкой ARX."), "ARX");
                Shutdown();
                return;
            }
        }
        if (render)
        {
            Loc.Configure(e.Args.Length > 3 ? e.Args[3] : null);
            _store = new LegionStore(true);
            _store.Profile.Language = Loc.Language;
            _catalog = CatalogSync.Open(true);
            var section = e.Args.Length > 2 ? e.Args[2] : "combat";
            if (section == "changelog")
            {
                Render(new ChangelogWindow(null, AppUpdates.CurrentLabel), e.Args[1], new Size(575, 546));
            }
            else if (section == "update")
            {
                Render(new UpdateWindow(null, AppUpdates.CurrentLabel, _ => { }), e.Args[1], new Size(470, 250));
            }
            else if (section == "splash")
            {
                var splashPreview = new LaunchWindow();
                splashPreview.Stage(76, Loc.T("Сверка версии базы с репозиторием ARX"), false);
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
                    "hud-bosses" => HudKind.Bosses, "hud-atlas" => HudKind.Atlas, _ => HudKind.Combat
                };
                if (kind == HudKind.Bosses && WorldBossCatalog.Choices.FirstOrDefault() is { } boss)
                {
                    _store.Profile.BossHudSelection = boss.Id;
                    _store.UpdateLive(null, Loc.T("Демонстрация"), null,
                        [new ObservedBoss("preview", boss.Name, boss.Zone, false, DateTimeOffset.UtcNow.AddMinutes(38), boss.Id)]);
                }
                if (kind == HudKind.Tasks)
                    _store.Profile.Tasks.Add(new SessionTask(Guid.NewGuid(), _store.Profile.SessionId, Loc.T("Проверить задания Бездны"), false));
                var hudPreview = new HudWindow(_store, kind);
                if (kind == HudKind.Atlas && AtlasCatalog.For(AtlasZone.All[0])?.Pins.FirstOrDefault(pin => pin.Category == "Телепорт") is { } marker)
                    hudPreview.OpenAtlas(new AtlasSelection(marker.Zone, marker.Layer, marker.Id));
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
        LaunchWindow? splash = null;
        try
        {
            _store = await Task.Run(() => new LegionStore(false));
            Loc.Configure(_store.Profile.Language);
            splash = new LaunchWindow();
            splash.Show();
            splash.Stage(24, Loc.T("Загрузка локального профиля"));
            splash.Stage(48, Loc.T("Проверка базы Aion 2 Global"));
            _catalog = await Task.Run(() => CatalogSync.Open(false));
            _live = new LiveCombat(_store);
            _store.LiveChanged += CheckBossAlerts;
            splash.Stage(76, Loc.T("Сверка версии базы с репозиторием ARX"));
            await Task.WhenAll(_catalog.RefreshAsync(), Task.Delay(620));
            if (_store.NewProfile) _store.Profile.Events = [.. _catalog.Reference.Presets];
            splash.Stage(100, Loc.F($"{_catalog.Status} · ревизия {_catalog.Revision}"));
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
                delta => SetCombatOpacity(CombatOpacity + delta), () => ToggleHud(HudKind.Bosses),
                () => ToggleHud(HudKind.Atlas), ToggleAtlasLock);
            if (!ClickThroughAvailable && _store.Profile.CombatHudLocked)
            {
                _store.Profile.CombatHudLocked = false;
                _store.Save();
            }
            _live.Start();
            var version = AppUpdates.CurrentLabel;
            if ((e.Args.Contains("--updated", StringComparer.OrdinalIgnoreCase) || !_store.NewProfile) &&
                _store.Profile.LastSeenVersion != version)
                ShowChangelog();
            else if (_store.Profile.LastSeenVersion is null)
            {
                _store.Profile.LastSeenVersion = version;
                _store.Save();
            }
            StartAppUpdates();
        }
        catch (Exception ex)
        {
            _closing = true;
            splash?.Close();
            MessageBox.Show(Loc.T("Не удалось запустить ARX. ") + ex.Message, "ARX", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static bool InstallerRunning()
    {
        if (!Mutex.TryOpenExisting("Local\\ARX.Setup", out var mutex)) return false;
        mutex.Dispose();
        return true;
    }

    internal void ShowChangelog()
    {
        if (_closing || _dashboard is null) return;
        if (_changelogWindow is { } existing)
        {
            existing.Activate();
            return;
        }
        var version = AppUpdates.CurrentLabel;
        var window = new ChangelogWindow(_dashboard, version);
        _changelogWindow = window;
        window.Closed += (_, _) =>
        {
            _changelogWindow = null;
            _store.Profile.LastSeenVersion = version;
            _store.Save();
        };
        window.Show();
    }

    private void StartAppUpdates()
    {
        _updates = new AppUpdates();
        _updates.StatusChanged += () => _dashboard?.RefreshUpdateStatus();
        _dashboard?.RefreshUpdateStatus();
        _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(2) };
        _updateTimer.Tick += (_, _) => { if (_store.Profile.AutoUpdate) _ = CheckAppUpdatesAsync(); };
        _updateTimer.Start();
        if (_store.Profile.AutoUpdate) _ = CheckAppUpdatesAsync();
    }

    internal async Task CheckAppUpdatesAsync()
    {
        if (_closing || _updates is not { } updates) return;
        await updates.CheckAsync();
        if (!_closing && updates.ReadyIdentity is { } identity && identity != _offeredUpdate) ShowUpdateOffer();
    }

    internal async Task OpenUpdateAsync()
    {
        if (_closing || _updates is not { } updates) return;
        if (updates.Ready) ShowUpdateOffer();
        else if (updates.Phase == UpdatePhase.Available && updates.ReleasePage is { } page)
        {
            try
            {
                using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(page) { UseShellExecute = true });
            }
            catch (System.ComponentModel.Win32Exception)
            {
                MessageBox.Show(Loc.T("Не удалось открыть страницу релиза."), Loc.T("Обновление ARX"));
            }
        }
        else await CheckAppUpdatesAsync();
    }

    internal void ShowUpdateOffer()
    {
        if (_closing || _updates?.ReadyVersion is not { } version || _updates.ReadyIdentity is not { } identity ||
            _dashboard is not { } dashboard) return;
        if (_updateWindow is { } existing)
        {
            existing.Activate();
            return;
        }
        _offeredUpdate = identity;
        var window = new UpdateWindow(dashboard, version, now =>
        {
            if (now is null)
            {
                if (_approvedUpdate == identity) _approvedUpdate = null;
                dashboard.RefreshUpdateStatus();
                return;
            }
            if (_updates?.ReadyIdentity != identity)
            {
                ShowUpdateOffer();
                return;
            }
            _approvedUpdate = identity;
            dashboard.RefreshUpdateStatus();
            if (now.Value) dashboard.Close();
        });
        _updateWindow = window;
        window.Closed += (_, _) => _updateWindow = null;
        window.Show();
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

    internal void ShowAtlas(AtlasSelection selection)
    {
        ShowDashboard();
        if (!_closing) _dashboard?.OpenAtlas(selection);
    }

    internal void ShowAtlasHud(AtlasSelection selection)
    {
        if (_closing) return;
        ShowHud(HudKind.Atlas);
        _huds[HudKind.Atlas].OpenAtlas(selection);
    }

    internal void ToggleAtlasLock()
    {
        if (!AtlasLockAvailable || !_huds.TryGetValue(HudKind.Atlas, out var hud)) return;
        _store.Profile.AtlasHudLocked = !_store.Profile.AtlasHudLocked;
        hud.ApplyAtlasLock();
        _store.Save();
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
        _updateTimer?.Stop();
        _hotkeys?.Dispose();
        _hotkeys = null;
        try
        {
            foreach (var hud in _huds.Values.ToArray()) hud.Close();
            if (_live is { } live) await live.StopAsync();
            if (UpdateApproved && _updates?.InstallAfterClose() != true)
                MessageBox.Show(Loc.T("Не удалось запустить проверенный установщик. Откройте ARX и повторите обновление."),
                    Loc.T("Обновление ARX"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { Shutdown(); }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _teamPoll?.Stop();
        _updateTimer?.Stop();
        _updates?.Dispose();
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
