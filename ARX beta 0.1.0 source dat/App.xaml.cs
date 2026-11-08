using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ArxBeta;

public partial class App : Application
{
    private LegionStore _store = null!;
    private Dashboard? _dashboard;
    private readonly Dictionary<HudKind, HudWindow> _huds = [];
    private Mutex? _instance;
    public static App CurrentApp => (App)Current;

    protected override void OnStartup(StartupEventArgs e)
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
        _store = new LegionStore(render);
        if (render)
        {
            var preview = new Dashboard(_store);
            if (e.Args.Length > 2) preview.Navigate(e.Args[2]);
            Render(preview, e.Args[1]);
            Shutdown();
            return;
        }
        ShowDashboard();
    }

    public void ShowDashboard()
    {
        if (_dashboard is not null)
        {
            if (_dashboard.WindowState == WindowState.Minimized) _dashboard.WindowState = WindowState.Normal;
            _dashboard.Activate();
            return;
        }
        _dashboard = new Dashboard(_store);
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
        _instance?.Dispose();
        base.OnExit(e);
    }

    private static void Render(Dashboard preview, string path)
    {
        var root = (FrameworkElement)preview.Content;
        var size = new Size(1340, 900);
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
