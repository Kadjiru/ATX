using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace ArxBeta;

internal sealed class Hotkeys : IDisposable
{
    internal sealed record Shortcut(int Id, uint Modifiers, uint Key, string Label, string Description);

    internal static IReadOnlyList<Shortcut> Shortcuts { get; } =
    [
        new(1, 0x0001, 0x77, "Alt+F8", "Показать или скрыть боевой HUD"),
        new(4, 0x0003, 0x31, "Ctrl+Alt+1", "Открыть минимальный HUD с прозрачным фоном"),
        new(5, 0x0003, 0x32, "Ctrl+Alt+2", "Открыть средний HUD со списком участников"),
        new(6, 0x0003, 0x33, "Ctrl+Alt+3", "Открыть подробный HUD"),
        new(7, 0x0003, 0x21, "Ctrl+Alt+PgUp", "Увеличить непрозрачность фона HUD на 5 п. п."),
        new(8, 0x0003, 0x22, "Ctrl+Alt+PgDn", "Уменьшить непрозрачность фона HUD на 5 п. п."),
        new(2, 0x0003, 0x4C, "Ctrl+Alt+L", "Закрепить HUD с пропуском щелчков или разблокировать"),
        new(3, 0x0001, 0x52, "Alt+R", "Завершить текущий боевой сегмент")
    ];

    private readonly HwndSource _window = new(new HwndSourceParameters("ARX Hotkeys") { ParentWindow = new IntPtr(-3) });
    private readonly Dictionary<int, Action> _registered = new();
    public List<string> Unavailable { get; } = [];
    public bool ClickThroughRegistered => _registered.ContainsKey(2);

    public Hotkeys(Action showCombat, Action toggleClickThrough, Action archive,
        Action<HudDensity> density, Action<double> opacity)
    {
        _window.AddHook(Handle);
        Action[] actions = [showCombat, toggleClickThrough, archive, () => density(HudDensity.Minimal),
            () => density(HudDensity.Medium), () => density(HudDensity.Detailed), () => opacity(.05), () => opacity(-.05)];
        foreach (var shortcut in Shortcuts) Register(shortcut, actions[shortcut.Id - 1]);
    }

    internal bool IsRegistered(Shortcut shortcut) => _registered.ContainsKey(shortcut.Id);

    private void Register(Shortcut shortcut, Action action)
    {
        if (RegisterHotKey(_window.Handle, shortcut.Id, shortcut.Modifiers | 0x4000, shortcut.Key))
            _registered.Add(shortcut.Id, action);
        else Unavailable.Add(shortcut.Label);
    }

    private nint Handle(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x0312 && _registered.TryGetValue((int)wParam, out var action))
        {
            action();
            handled = true;
        }
        return 0;
    }

    public void Dispose()
    {
        foreach (var id in _registered.Keys) UnregisterHotKey(_window.Handle, id);
        _window.Dispose();
    }

    public static void SetClickThrough(nint hwnd, bool enabled)
    {
        const int extendedStyle = -20, transparent = 0x20;
        var style = GetWindowLong(hwnd, extendedStyle);
        SetWindowLong(hwnd, extendedStyle, enabled ? style | transparent : style & ~transparent);
    }

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(nint hWnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint hWnd, int id);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(nint hWnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(nint hWnd, int index, int value);
}
