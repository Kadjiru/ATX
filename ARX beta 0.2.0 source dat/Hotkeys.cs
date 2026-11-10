using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace ArxBeta;

internal sealed class Hotkeys : IDisposable
{
    internal const int AtlasLockId = 11;
    internal sealed record Shortcut(int Id, uint Modifiers, uint Key, string Label, string Description);

    internal static IReadOnlyList<Shortcut> Shortcuts { get; } =
    [
        new(1, 0x0001, 0x77, "Alt+F8", Loc.T("Показать или скрыть боевой HUD")),
        new(9, 0x0003, 0x78, "Ctrl+Alt+F9", Loc.T("Показать или скрыть HUD мировых боссов")),
        new(10, 0x0003, 0x79, "Ctrl+Alt+F10", Loc.T("Показать или скрыть миникарту")),
        new(AtlasLockId, 0x0003, 0x7A, "Ctrl+Alt+F11", Loc.T("Закрепить или разблокировать миникарту")),
        new(4, 0x0003, 0x31, "Ctrl+Alt+1", Loc.T("Открыть минимальный HUD с прозрачным фоном")),
        new(5, 0x0003, 0x32, "Ctrl+Alt+2", Loc.T("Открыть средний HUD со списком участников")),
        new(6, 0x0003, 0x33, "Ctrl+Alt+3", Loc.T("Открыть подробный HUD")),
        new(7, 0x0003, 0x21, "Ctrl+Alt+PgUp", Loc.T("Увеличить непрозрачность фона HUD на 5 п. п.")),
        new(8, 0x0003, 0x22, "Ctrl+Alt+PgDn", Loc.T("Уменьшить непрозрачность фона HUD на 5 п. п.")),
        new(2, 0x0003, 0x4C, "Ctrl+Alt+L", Loc.T("Закрепить HUD с пропуском щелчков или разблокировать")),
        new(3, 0x0001, 0x52, "Alt+R", Loc.T("Завершить текущий боевой сегмент"))
    ];

    private readonly HwndSource _window = new(new HwndSourceParameters("ARX Hotkeys") { ParentWindow = new IntPtr(-3) });
    private readonly Dictionary<int, Action> _registered = new();
    public List<string> Unavailable { get; } = [];
    public bool ClickThroughRegistered => _registered.ContainsKey(2);
    public bool AtlasLockRegistered => _registered.ContainsKey(AtlasLockId);
    public static bool IsAltDown => GetAsyncKeyState(0x12) < 0;

    public Hotkeys(Action showCombat, Action toggleClickThrough, Action archive,
        Action<HudDensity> density, Action<double> opacity, Action showBosses, Action showAtlas, Action lockAtlas)
    {
        _window.AddHook(Handle);
        Action[] actions = [showCombat, toggleClickThrough, archive, () => density(HudDensity.Minimal),
            () => density(HudDensity.Medium), () => density(HudDensity.Detailed), () => opacity(.05), () => opacity(-.05),
            showBosses, showAtlas, lockAtlas];
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
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(nint hWnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(nint hWnd, int index, int value);
}
