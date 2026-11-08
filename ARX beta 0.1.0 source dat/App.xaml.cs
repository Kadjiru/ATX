using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ArxBeta;

public partial class App : Application
{
    private LegionStore _store = null!;
    private CatalogSync _catalog = null!;
    private Dashboard? _dashboard;
    private readonly Dictionary<HudKind, HudWindow> _huds = [];
    private Mutex? _instance;
    public static App CurrentApp => (App)Current;

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
            else
            {
                var preview = new Dashboard(_store, _catalog);
                preview.Navigate(section);
                Render(preview, e.Args[1], new Size(1340, 900));
            }
            Shutdown();
            return;
        }
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var splash = new LaunchWindow();
        splash.Show();
        try
        {
            splash.Stage(24, "Загрузка локального профиля");
            _store = await Task.Run(() => new LegionStore(false));
            splash.Stage(48, "Проверка базы Aion 2 Global");
            _catalog = await Task.Run(() => CatalogSync.Open(false));
            splash.Stage(76, "Сверка версии базы с репозиторием ARX");
            await Task.WhenAll(_catalog.RefreshAsync(), Task.Delay(620));
            if (_store.NewProfile) _store.Profile.Events = [.. _catalog.Reference.Presets];
            splash.Stage(100, $"{_catalog.Status} · ревизия {_catalog.Revision}");
            await Task.Delay(290);
            ShowDashboard();
            splash.Close();
            ShutdownMode = ShutdownMode.OnLastWindowClose;
            BeginTeamSync();
        }
        catch (Exception ex)
        {
            splash.Close();
            MessageBox.Show("Не удалось запустить ARX. " + ex.Message, "ARX", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    public void ShowDashboard()
    {
        if (_dashboard is not null)
        {
            if (_dashboard.WindowState == WindowState.Minimized) _dashboard.WindowState = WindowState.Normal;
            _dashboard.Activate();
            return;
        }
        _dashboard = new Dashboard(_store, _catalog);
        _dashboard.Closed += (_, _) => _dashboard = null;
        MainWindow = _dashboard;
        _dashboard.Show();
    }

    public void ToggleHud(HudKind kind)
    {
        if (_huds.TryGetValue(kind, out var existing))
        {
            existing.Close();
            return;
        }
        var hud = new HudWindow(_store, kind);
        _huds.Add(kind, hud);
        hud.Closed += (_, _) => _huds.Remove(kind);
        hud.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _teamPoll?.Stop();
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
