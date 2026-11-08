using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace ArxBeta;

internal sealed class Hotkeys : IDisposable
{
    private readonly HwndSource _window = new(new HwndSourceParameters("ARX Hotkeys") { ParentWindow = new IntPtr(-3) });
    private readonly Dictionary<int, Action> _registered = new();
    public List<string> Unavailable { get; } = [];
    public bool ClickThroughRegistered => _registered.ContainsKey(2);

    public Hotkeys(Action showCombat, Action toggleClickThrough, Action archive,
        Action<HudDensity> density, Action<double> opacity)
    {
        _window.AddHook(Handle);
        Register(1, 0x0001, 0x77, "Alt+F8", showCombat);
        Register(2, 0x0003, 0x4C, "Ctrl+Alt+L", toggleClickThrough);
        Register(3, 0x0001, 0x52, "Alt+R", archive);
        Register(4, 0x0003, 0x31, "Ctrl+Alt+1", () => density(HudDensity.Minimal));
        Register(5, 0x0003, 0x32, "Ctrl+Alt+2", () => density(HudDensity.Medium));
        Register(6, 0x0003, 0x33, "Ctrl+Alt+3", () => density(HudDensity.Detailed));
        Register(7, 0x0003, 0x21, "Ctrl+Alt+PgUp", () => opacity(.05));
        Register(8, 0x0003, 0x22, "Ctrl+Alt+PgDn", () => opacity(-.05));
    }

    private void Register(int id, uint modifiers, uint key, string label, Action action)
    {
        if (RegisterHotKey(_window.Handle, id, modifiers | 0x4000, key)) _registered.Add(id, action);
        else Unavailable.Add(label);
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
