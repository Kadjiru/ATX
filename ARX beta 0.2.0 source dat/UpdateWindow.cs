using System.Windows;
using static ArxBeta.Ui;

namespace ArxBeta;

internal sealed class UpdateWindow : Window
{
    public UpdateWindow(Window? owner, string version, Action<bool?> approve)
    {
        if (owner is not null) Owner = owner;
        Title = Loc.T("Обновление ARX");
        Width = 470;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        ShowActivated = false;
        var notice = version.EndsWith("-beta", StringComparison.Ordinal)
            ? Loc.T("Бета-версия может содержать ошибки. Для повседневной игры можно вернуться на Stable.")
            : AppUpdates.IsBetaBuild ? Loc.T("Переход на Stable заменит установленную бета-версию.") : "";
        var warning = Text(notice, 13, "Coral");
        if (notice.Length == 0) warning.Visibility = Visibility.Collapsed;
        Content = Card(Stack(
            Space(Label($"ARX {version}"), 5),
            Space(Text(Loc.T("Обновление готово. Установить?"), 23, bold: true), 8),
            Space(Hint(Loc.T("ARX завершит запись боя, покажет ход установки и автоматически откроет новую версию.")), 14),
            Space(warning, 8),
            Actions(Button(Loc.T("Установить"), () => Choose(true), true),
                Button(Loc.T("При закрытии"), () => Choose(false)), Button(Loc.T("Позже"), () => Choose(null)))), 20);

        void Choose(bool? now)
        {
            Close();
            approve(now);
        }
    }
}
