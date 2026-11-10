using System.Windows;
using System.Windows.Controls;
using static ArxBeta.Ui;

namespace ArxBeta;

public sealed partial class Dashboard
{
    private UIElement HotkeysPage()
    {
        var page = Heading("Горячие клавиши", "Управление боевым HUD, миникартой и таймерами");
        var entries = new StackPanel();
        foreach (var shortcut in Hotkeys.Shortcuts)
        {
            var active = App.CurrentApp.IsHotkeyRegistered(shortcut);
            var row = new Grid { MinHeight = 47 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(115) });

            var key = new Border
            {
                Background = Ui.Brush("Raised"), CornerRadius = new CornerRadius(6), Padding = new Thickness(9, 5, 9, 5),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
                Child = Text(shortcut.Label, 13, "Mint", true)
            };
            var description = Text(shortcut.Description);
            description.VerticalAlignment = VerticalAlignment.Center;
            description.Margin = new Thickness(0, 4, 8, 4);
            var status = Text(active switch { true => "Доступно", false => "Занято", null => "При запуске" },
                12, active == false ? "Coral" : "Muted");
            status.VerticalAlignment = VerticalAlignment.Center;
            Place(row, key, 0, 0);
            Place(row, description, 0, 1);
            Place(row, status, 0, 2);
            entries.Children.Add(new Border { BorderBrush = Ui.Brush("Line"), BorderThickness = new Thickness(0, 0, 0, 1), Child = row });
        }
        page.Children.Add(Card(Stack(Space(Label("СОЧЕТАНИЯ ARX"), 6), Space(entries, 14),
            Space(Hint("При конфликте с другой программой воспользуйтесь кнопками ARX. Если HUD закреплён, Ctrl+Alt+L возвращает управление мышью."), 10),
            Actions(Button("К настройкам HUD", () => Navigate("settings"))))));
        return page;
    }
}
