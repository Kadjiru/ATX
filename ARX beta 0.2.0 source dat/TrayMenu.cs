using System.IO;
using System.Windows;
using Forms = System.Windows.Forms;

namespace ArxBeta;

internal sealed class TrayMenu : IDisposable
{
    private readonly Forms.NotifyIcon _tray;
    private readonly Forms.ContextMenuStrip _menu = new();
    private readonly System.Drawing.Icon _icon;

    internal TrayMenu(Action open, Action toggleHuds, Func<bool> soundEnabled, Action toggleSound, Action exit)
    {
        using Stream iconStream = Application.GetResourceStream(new Uri(
            "pack://application:,,,/ARX.Beta;component/Assets/ARX.ico"))!.Stream;
        using var embedded = new System.Drawing.Icon(iconStream);
        _icon = (System.Drawing.Icon)embedded.Clone();
        _menu.Items.Add(Loc.T("Открыть ARX"), null, (_, _) => open());
        _menu.Items.Add(Loc.T("Показать / скрыть HUD"), null, (_, _) => toggleHuds());
        var sound = new Forms.ToolStripMenuItem(Loc.T("Звук напоминаний"));
        sound.Click += (_, _) => toggleSound();
        _menu.Opening += (_, _) => sound.Checked = soundEnabled();
        _menu.Items.Add(sound);
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add(Loc.T("Выйти из ARX"), null, (_, _) => exit());
        _tray = new Forms.NotifyIcon { Text = "ARX", Icon = _icon, ContextMenuStrip = _menu, Visible = true };
        _tray.MouseClick += (_, args) =>
        {
            if (args.Button == Forms.MouseButtons.Left) toggleHuds();
        };
    }

    public void Dispose()
    {
        _tray.Visible = false;
        _tray.Dispose();
        _menu.Dispose();
        _icon.Dispose();
    }
}
