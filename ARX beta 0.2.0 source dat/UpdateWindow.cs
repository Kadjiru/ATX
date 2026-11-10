using System.Windows;
using static ArxBeta.Ui;

namespace ArxBeta;

internal sealed class UpdateWindow : Window
{
    public UpdateWindow(Window owner, string version, Action<bool?> approve)
    {
        Owner = owner;
        Title = "Обновление ARX";
        Width = 470;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        ShowActivated = false;
        Content = Card(Stack(
            Space(Label($"ARX {version}"), 5),
            Space(Text("Обновление готово. Установить?", 23, bold: true), 8),
            Space(Hint("ARX завершит запись боя, покажет ход установки и автоматически откроет новую версию."), 14),
            Actions(Button("Установить", () => Choose(true), true),
                Button("При закрытии", () => Choose(false)), Button("Позже", () => Choose(null)))), 20);

        void Choose(bool? now)
        {
            Close();
            approve(now);
        }
    }
}
