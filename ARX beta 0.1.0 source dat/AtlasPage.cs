using System.Windows;
using System.Windows.Controls;
using static ArxBeta.Ui;

namespace ArxBeta;

public sealed partial class Dashboard
{
    private UIElement AtlasPage()
    {
        var profile = _store.Profile;
        var page = Heading("Атлас Атреи", "Авторская схема мира Aion 2 Global. Отмечайте собственные находки и маршруты.");
        var toolbar = new WrapPanel();
        var filter = Choice(["Все метки", "Сбор", "Босс", "Подземелье", "Телепорт", "Заметка"], 160);
        var category = Choice(["Заметка", "Сбор", "Босс", "Подземелье", "Телепорт"], 153);
        var name = Input("", 153, 52);
        var zoneTitle = Text("Атрея", 21, bold: true);
        var progress = Text("", 13, "Mint", true);
        var hint = Hint("");
        var map = new AtlasCanvas(VisiblePins) { Height = 473 };
        var add = new Button { Content = "Поставить метку", Background = Ui.Brush("Mint"), Foreground = Ui.Brush("Base") };
        var selectedName = Text("", 14, "Gold", true);
        var markerList = new ListBox { Height = 115 };
        var selected = Guid.Empty;
        var listRefreshing = false;
        var found = new Button { Content = "Отметить находку" };
        var delete = new Button { Content = "Удалить метку" };
        add.Click += (_, _) => { map.Placing = !map.Placing; Update(); };
        found.Click += (_, _) => ToggleFound();
        delete.Click += (_, _) => DeleteSelected();
        markerList.SelectionChanged += (_, _) =>
        {
            if (!listRefreshing && markerList.SelectedItem is ListBoxItem { Tag: Guid id })
            {
                selected = id;
                Update();
            }
        };
        filter.SelectionChanged += (_, _) => Update();
        map.ZoneSelected += zone => SelectZone(zone);
        map.PinSelected += pin =>
        {
            selected = pin.Id;
            Update();
        };
        map.PinRequested += point =>
        {
            if (map.Zone is not { } zone) return;
            var type = (string)category.SelectedItem;
            var label = name.Text.Trim();
            selected = Guid.NewGuid();
            profile.Pins.Add(new MapPin(selected, zone.Id, point.X, point.Y, type,
                label.Length == 0 ? type : label, false));
            name.Clear();
            map.Placing = false;
            _store.Commit();
            Update();
        };
        foreach (var button in AtlasZone.All.Select(zone => Button(zone.Name, () => SelectZone(zone)))
            .Prepend(Button("Вся Атрея", () => SelectZone(null))))
        {
            button.Margin = new Thickness(0, 0, 6, 6);
            toolbar.Children.Add(button);
        }
        page.Children.Add(Space(toolbar, 7));
        var zoom = Actions(Button("−", () => map.Zoom(1 / 1.25)), Button("+", () => map.Zoom(1.25)),
            Button("Вписать", map.Fit));
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(214) });
        var frame = Card(Stack(zoom, map), 8);
        frame.Margin = new Thickness(0, 0, 10, 0);
        grid.Children.Add(frame);
        var side = Stack(Space(zoneTitle, 5), Space(progress, 7), Space(hint, 9),
            Space(Field("Фильтр", filter), 7), Space(markerList, 7),
            Space(selectedName, 5), Actions(found, delete),
            Space(Field("Название новой метки", name), 7), Space(Field("Категория", category), 7),
            add);
        Place(grid, Card(side, 12), 0, 1);
        page.Children.Add(grid);
        _refreshShared = Update;
        Update();
        return page;

        IEnumerable<MapPin> VisiblePins(AtlasZone? zone) => _store.AllPins.Where(p => p.Zone == zone?.Id &&
            (filter.SelectedIndex <= 0 || p.Category == (string)filter.SelectedItem));

        void SelectZone(AtlasZone? zone)
        {
            selected = Guid.Empty;
            map.ChangeZone(zone);
            Update();
        }

        void Update()
        {
            zoneTitle.Text = map.Zone?.Name ?? "Атрея";
            var pins = map.Zone is null ? _store.AllPins.ToArray() : _store.AllPins.Where(p => p.Zone == map.Zone.Id).ToArray();
            progress.Text = $"{pins.Count(p => p.Found)} / {pins.Length} отмечено";
            hint.Text = map.Zone is null ? "Выберите зону на схеме или в панели. Положение зон условно."
                : map.Placing ? "Щёлкните по карте, чтобы разместить метку."
                : "Колесо — масштаб, перетаскивание — перемещение.";
            add.IsEnabled = map.Zone is not null;
            add.Content = map.Placing ? "Отменить размещение" : "Поставить метку";
            listRefreshing = true;
            markerList.Items.Clear();
            foreach (var pin in VisiblePins(map.Zone))
            {
                var row = new ListBoxItem { Content = (pin.Found ? "✓  " : "○  ") + pin.Name, Tag = pin.Id };
                markerList.Items.Add(row);
                if (pin.Id == selected) markerList.SelectedItem = row;
            }
            listRefreshing = false;
            var current = pins.FirstOrDefault(p => p.Id == selected);
            selectedName.Text = current?.Name ?? "";
            found.IsEnabled = current is not null;
            delete.IsEnabled = current is not null && profile.Pins.Any(p => p.Id == current.Id);
            found.Content = current?.Found == true ? "Снять отметку" : "Отметить находку";
            map.Selected = selected == Guid.Empty ? null : selected;
            map.InvalidateVisual();
        }

        void ToggleFound()
        {
            var index = profile.Pins.FindIndex(p => p.Id == selected);
            if (index >= 0)
            {
                var pin = profile.Pins[index];
                profile.Pins[index] = pin with { Found = !pin.Found };
            }
            else if (!profile.TeamFound.Add(selected)) profile.TeamFound.Remove(selected);
            _store.Commit();
            Update();
        }

        void DeleteSelected()
        {
            if (selected == Guid.Empty) return;
            profile.Pins.RemoveAll(p => p.Id == selected);
            selected = Guid.Empty;
            _store.Commit();
            Update();
        }
    }

    private static ComboBox Choice(IEnumerable<string> items, double width)
    {
        return new ComboBox
        {
            ItemsSource = items, SelectedIndex = 0, Width = width, MinHeight = 31,
            Padding = new Thickness(6, 4, 6, 4)
        };
    }
}
