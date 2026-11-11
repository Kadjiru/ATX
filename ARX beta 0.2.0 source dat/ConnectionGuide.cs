using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AionMeter.Core.Capture;
using static ArxBeta.Ui;

namespace ArxBeta;

internal sealed class ConnectionGuide : Window
{
    private readonly LiveCombat? _live;
    private readonly bool _preview;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly TextBlock _driver = Text("", 15, bold: true);
    private readonly TextBlock _adapter = Text("", 15, bold: true);
    private readonly TextBlock _game = Text("", 15, bold: true);
    private readonly TextBlock _next = Text("", 14, "Mint", true);
    private readonly TextBlock _counters = Hint("");
    private readonly TextBlock _feedback = Hint("");
    private readonly TextBox _json = new() { IsReadOnly = true, AcceptsReturn = true, Height = 148, FontFamily = new FontFamily("Consolas"),
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private bool _hasDriver;
    private int _adapterCount;

    public ConnectionGuide(Window? owner, LiveCombat? live, bool preview = false)
    {
        _live = live;
        _preview = preview;
        if (owner is not null) Owner = owner;
        Title = Loc.T("Мастер подключения");
        Width = 620;
        Height = 540;
        MinWidth = 430;
        MinHeight = 330;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Icon = AppIcon();
        SetResourceReference(BackgroundProperty, "Base");

        var diagnostics = new Expander
        {
            Header = Text(Loc.T("Предпросмотр диагностики"), 13, bold: true),
            IsExpanded = _preview,
            Content = Stack(Space(Hint(Loc.T("Сводка содержит только версии, статусы и счётчики.")), 7),
                Space(_json, 7), Actions(Button(Loc.T("Копировать JSON"), CopyJson),
                    Button(Loc.T("Поддержка на GitHub"), OpenSupport)))
        };
        var close = Button(Loc.T("Готово"), Close, true);
        close.IsCancel = true;
        Content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = Card(Stack(Space(Text(Loc.T("Подключение Aion 2"), 24, bold: true), 7),
                Space(Hint(Loc.T("Для HUD поверх игры выберите оконный режим без рамки.")), 13),
                Space(Card(Stack(Space(_driver, 7), Space(_adapter, 7), _game), 13), 12),
                Space(_next, 6), Space(_counters, 10),
                Actions(Button(Loc.T("Перепроверить"), CheckDevices),
                    Button(Loc.T("Копировать сводку"), CopySummary), close),
                Space(_feedback, 8), diagnostics), 18)
        };
        _clock.Tick += (_, _) => Refresh();
        Loaded += (_, _) =>
        {
            CheckDevices();
            if (!_preview) _clock.Start();
        };
        Closed += (_, _) => _clock.Stop();
        if (_preview) CheckDevices();
    }

    private void CheckDevices()
    {
        _hasDriver = _preview || LiveCombat.NpcapAvailable;
        _adapterCount = _preview ? 1 : _hasDriver ? _live?.Adapters().Count ?? 0 : 0;
        Refresh();
    }

    private void Refresh()
    {
        var status = _preview ? new CaptureStatus(CaptureState.WaitingForGame, "") :
            _live?.Status ?? CaptureStatus.Stopped;
        var gameRunning = !_preview && (IsProcessRunning("Aion2") || IsProcessRunning("Aion2-Win64-Shipping"));
        var gameDetected = gameRunning || status.State == CaptureState.Capturing;
        _driver.Text = Loc.T(_hasDriver ? "Npcap установлен" : "Npcap не найден");
        _adapter.Text = !_hasDriver ? Loc.T("Проверка адаптеров ожидает Npcap") :
            Loc.F($"Доступные адаптеры: {_adapterCount}");
        _game.Text = Loc.T(gameDetected ? "Aion 2 обнаружен" : "Aion 2 пока не обнаружен");
        _next.Text = Loc.T((!_hasDriver, _adapterCount == 0, status.State, gameDetected, status.Events > 0) switch
        {
            (true, _, _, _, _) => "Установите Npcap через установщик ARX, затем перезапустите ARX.",
            (_, true, _, _, _) => "Проверьте сетевое подключение и нажмите «Перепроверить».",
            (_, _, CaptureState.Error, _, _) => "Ошибка захвата. Проверьте выбранный адаптер в настройках ARX.",
            (_, _, CaptureState.Starting, _, _) => "ARX подключается к сетевым адаптерам.",
            (_, _, CaptureState.Stopped, _, _) => "Захват остановлен. Проверьте подключение в настройках ARX.",
            (_, _, CaptureState.Capturing, _, true) => "ARX получает события игры. Откройте боевой HUD для проверки урона.",
            (_, _, _, false, _) => "Запустите Aion 2 и войдите в игру.",
            (_, _, _, _, _) => "Смените локацию или начните короткий бой, чтобы проверить боевой HUD."
        });
        _counters.Text = Loc.F($"Пакеты: {status.Packets:N0} · События игры: {status.Events:N0}");
        var json = JsonSerializer.Serialize(new
        {
            Version = AppUpdates.CurrentLabel,
            Windows = Environment.OSVersion.VersionString,
            Npcap = _hasDriver,
            AdapterCount = _adapterCount,
            GameDetected = gameDetected,
            CaptureState = status.State.ToString(),
            status.Packets,
            status.Events
        }, new JsonSerializerOptions { WriteIndented = true });
        if (!_json.IsKeyboardFocusWithin && _json.Text != json) _json.Text = json;
    }

    private static bool IsProcessRunning(string name)
    {
        var processes = Process.GetProcessesByName(name);
        foreach (var process in processes) process.Dispose();
        return processes.Length > 0;
    }

    private void CopySummary() =>
        Copy(string.Join(Environment.NewLine, $"ARX {AppUpdates.CurrentLabel}", _driver.Text, _adapter.Text,
            _game.Text, _next.Text, _counters.Text));

    private void CopyJson() => Copy(_json.Text);

    private void Copy(string value)
    {
        try
        {
            Clipboard.SetText(value);
            _feedback.Text = Loc.T("Скопировано.");
        }
        catch (COMException)
        {
            _feedback.Text = Loc.T("Буфер обмена занят. Попробуйте ещё раз.");
        }
    }

    private void OpenSupport()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("https://github.com/Kadjiru/ATX/issues") { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            _feedback.Text = Loc.T("Не удалось открыть страницу поддержки.");
        }
    }
}
