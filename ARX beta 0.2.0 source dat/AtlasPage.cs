using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static ArxBeta.Ui;

namespace ArxBeta;

public sealed partial class Dashboard
{
    private AtlasSelection? _atlasJump;

    internal void OpenAtlas(AtlasSelection selection)
    {
        _atlasJump = selection;
        Navigate("atlas");
    }

    private sealed record AtlasListRow(MapPin Pin, string Name)
    {
        public override string ToString() => $"{(Pin.Found ? "✓" : Pin.Category == "Телепорт" ? "◆" : "•")}  {Name}";
    }

    private UIElement AtlasPage()
    {
        var profile = _store.Profile;
        var jump = _atlasJump;
        _atlasJump = null;
        var page = Heading(Loc.T("Атлас Aion 2"), Loc.T("Точки интереса, личные отметки и маршруты по Атреи"));
        var region = new ComboBox { ItemsSource = AtlasZone.All, Width = 222, MinHeight = 32,
            SelectedItem = AtlasZone.All.FirstOrDefault(z => z.Id == (jump?.Zone ?? profile.AtlasZone)) ?? AtlasZone.All[0] };
        var layers = new ComboBox { Width = 168, MinHeight = 32 };
        var search = Input("", 197, 120);
        var category = Choice(AtlasMarks.Categories, 145);
        var type = Choice(["Заметка", "Телепорт", "След", "Сбор", "Босс", "Подземелье", "Куб"], 118);
        var pending = new CheckBox { Content = Loc.T("Скрыть найденные") };
        var title = Text("", 19, bold: true);
        var progress = Text("", 12, "Mint", true);
        var description = Hint("");
        var inspector = Text(Loc.T("Выберите точку"), 16, bold: true);
        var details = Hint("");
        var name = Input("", 111, 52);
        var feedback = Hint("");
        var list = new ListBox { Height = 182 };
        var map = new AtlasCanvas { MinHeight = 480 };
        var selection = Guid.Empty;
        var route = new List<Guid>();
        var changingLayer = false;
        var refreshingList = false;
        AtlasZone Zone() => (AtlasZone)region.SelectedItem;
        AtlasCatalog.AtlasMap? World() => AtlasCatalog.For(Zone());
        string? layer = AtlasCatalog.WorldLayer;

        var import = new Button { Content = Loc.T("Снимок зоны…") };
        var add = new Button { Content = Loc.T("Новая метка") }
            .UseBrush(Control.BackgroundProperty, "Mint").UseBrush(Control.ForegroundProperty, "Base");
        var found = new Button { Content = Loc.T("Найдено") };
        var delete = new Button { Content = Loc.T("Удалить") };
        var center = Button(Loc.T("На карте"), () => { if (Selected() is { } pin) map.FocusPin(pin); });
        var waypoint = new Button { Content = Loc.T("В маршрут") };
        var clearRoute = new Button { Content = Loc.T("Сбросить маршрут") };
        foreach (var action in new[] { center, found, delete, waypoint, clearRoute })
            action.Padding = new Thickness(8, 5, 8, 5);
        var routeStatus = Hint("");
        var empty = Text("", 16, "Muted", true);
        empty.HorizontalAlignment = HorizontalAlignment.Center;
        empty.VerticalAlignment = VerticalAlignment.Center;
        empty.IsHitTestVisible = false;
        var mapArea = new Grid();
        mapArea.Children.Add(map);
        mapArea.Children.Add(empty);

        var miniMap = Button(Loc.T("Миникарта HUD"), () => App.CurrentApp.ShowAtlasHud(
            new AtlasSelection(Zone().Id, layer, selection == Guid.Empty ? null : selection)), true);
        miniMap.ToolTip = Loc.T("Открыть миникарту ARX в текущей зоне · Ctrl+Alt+F10");
        miniMap.VerticalAlignment = VerticalAlignment.Bottom;
        page.Children.Add(Space(Actions(Field(Loc.T("Зона"), region), Field(Loc.T("Слой"), layers), Field(Loc.T("Поиск по имени"), search),
            Field(Loc.T("Категория"), category), miniMap), 6));
        var split = new Grid();
        split.ColumnDefinitions.Add(new ColumnDefinition());
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(270) });
        var tools = Actions(Button("−", () => map.Zoom(1 / 1.25)), Button("+", () => map.Zoom(1.25)),
            Button(Loc.T("Вписать"), map.Fit), import);
        var main = Card(Stack(tools, mapArea,
            Space(Actions(Text(Loc.T("◆ Телепорт"), 12, "Mint"), Text(Loc.T("● След"), 12, "Gold"),
                Text(Loc.T("● Сбор"), 12, "Coral"), new TextBlock { Text = Loc.T("● Куб"), Foreground = Brushes.LightPink },
                Text(Loc.T("● Питомец"), 12, "Gold"),
                Hint(Loc.T("Колесо — масштаб · перетаскивание — движение"))), 1),
            description, feedback), 11);
        main.Margin = new Thickness(0, 0, 10, 0);
        split.Children.Add(main);
        Place(split, Card(Stack(Space(title, 5), Space(progress, 7), Space(pending, 7), Space(list, 9),
            Space(inspector, 4), Space(details, 7), Actions(center, found, delete), Actions(waypoint, clearRoute),
            Space(routeStatus, 7), Space(Label(Loc.T("ЛИЧНАЯ МЕТКА")), 5), Actions(Field(Loc.T("Название"), name), Field(Loc.T("Тип"), type)),
            add), 12), 0, 1);
        page.Children.Add(split);

        import.Click += (_, _) => Import();
        add.Click += (_, _) => { map.Placing = !map.Placing; Update(); };
        found.Click += (_, _) => ToggleFound();
        delete.Click += (_, _) => Delete();
        waypoint.Click += (_, _) =>
        {
            if (selection == Guid.Empty) return;
            if (!route.Remove(selection)) route.Add(selection);
            Update();
        };
        clearRoute.Click += (_, _) => { route.Clear(); Update(); };
        region.SelectionChanged += (_, _) => { profile.AtlasZone = Zone().Id; _store.Save(); SetZone(); };
        layers.SelectionChanged += (_, _) =>
        {
            if (!changingLayer && layers.SelectedItem is AtlasLayer option) SetLayer(option.Key);
        };
        category.SelectionChanged += (_, _) => { if (!changingLayer) Update(); };
        search.TextChanged += (_, _) => Update();
        pending.Click += (_, _) => Update();
        list.SelectionChanged += (_, _) =>
        {
            if (refreshingList || list.SelectedItem is not AtlasListRow row) return;
            selection = row.Pin.Id;
            UpdateSelection();
        };
        list.MouseDoubleClick += (_, _) => { if (Selected() is { } pin) map.FocusPin(pin); };
        map.PinSelected += pin =>
        {
            selection = pin.Id;
            UpdateSelection();
            if (list.SelectedItem is AtlasListRow row) list.ScrollIntoView(row);
        };
        map.PinRequested += point =>
        {
            if (layer is null || layers.SelectedItem is not AtlasLayer) return;
            var group = (string)type.SelectedItem;
            var label = name.Text.Trim();
            selection = Guid.NewGuid();
            profile.Pins.Add(new MapPin(selection, Zone().Id, point.X, point.Y, group,
                label.Length == 0 ? Loc.T(group) : label, false, layer));
            name.Clear();
            map.Placing = false;
            _store.Commit();
        };
        _refreshShared = Update;
        SetZone(jump is null ? AtlasCatalog.WorldLayer : jump.Layer);
        if (jump is { PinId: { } id } && All().FirstOrDefault(pin => pin.Id == id) is { } target)
        {
            category.SelectedIndex = 0;
            selection = id;
            UpdateSelection();
            map.FocusPin(target);
            if (list.SelectedItem is AtlasListRow row) list.ScrollIntoView(row);
        }
        return page;

        MapPin[] All() => AtlasMarks.All(_store, Zone(), layer, layer == AtlasCatalog.WorldLayer ? World() : null);

        MapPin? Selected() => selection == Guid.Empty ? null : All().FirstOrDefault(p => p.Id == selection);

        void SetZone(string? preferred = AtlasCatalog.WorldLayer)
        {
            var options = AtlasMarks.Layers(_store, Zone());
            changingLayer = true;
            layers.ItemsSource = options;
            layers.SelectedItem = options.FirstOrDefault(l => l.Key == preferred) ?? options[0];
            changingLayer = false;
            SetLayer(((AtlasLayer)layers.SelectedItem).Key);
        }

        void SetLayer(string? key)
        {
            layer = key;
            changingLayer = true;
            category.SelectedIndex = layer == AtlasCatalog.WorldLayer && World()?.Pins.Any(p => p.Category == "Телепорт") == true ? 1 : 0;
            changingLayer = false;
            selection = Guid.Empty;
            route.Clear();
            feedback.Text = "";
            map.SetLayer(layer == AtlasCatalog.WorldLayer ? World() : null, AtlasLayers.Open(key));
            Update();
        }

        void Import()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = Loc.T("Изображения (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg") };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                var key = AtlasLayers.Import(dialog.FileName);
                profile.AtlasLayers[Zone().Id] = key;
                _store.Commit();
                SetZone(key);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
            {
                feedback.Text = ex.Message;
            }
        }

        void Update()
        {
            title.Text = Zone().Name;
            var all = All();
            var query = search.Text.Trim();
            var world = layer == AtlasCatalog.WorldLayer ? World() : null;
            var visible = all.Where(p => (pending.IsChecked != true || !p.Found) &&
                (category.SelectedIndex <= 0 || p.Category == (string)category.SelectedItem) &&
                AtlasMarks.Matches(p, world, query))
                .OrderBy(p => p.Category == "Телепорт" ? 1 : 0).ToArray();
            var selectedPin = all.FirstOrDefault(p => p.Id == selection);
            if (selectedPin is null || visible.All(p => p.Id != selection)) selection = Guid.Empty;
            var visibleRows = visible.Select(p => new AtlasListRow(p, AtlasMarks.DisplayName(p, world)))
                .OrderBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(row => row.Pin.Id).ToArray();
            refreshingList = true;
            list.ItemsSource = visibleRows;
            refreshingList = false;
            var byId = all.ToLookup(p => p.Id);
            map.SetPins(visible, route.Where(byId.Contains).Select(id => byId[id].First()).ToArray());
            progress.Text = Loc.F($"Найдено {all.Count(p => p.Found)} из {all.Length}   ·   видно {visible.Length}");
            add.IsEnabled = layer is not null && (layer != AtlasCatalog.WorldLayer || world is not null);
            add.Content = map.Placing ? Loc.T("Отмена") : Loc.T("Новая метка");
            map.Cursor = map.Placing ? System.Windows.Input.Cursors.Cross : System.Windows.Input.Cursors.Hand;
            empty.Text = layer == AtlasCatalog.WorldLayer && world is null ? Loc.T("Для этой зоны добавьте снимок карты") : "";
            description.Text = map.Placing ? Loc.T("Щёлкните на карте, чтобы добавить метку.") : layer switch
            {
                null => Loc.T("Архив старой схемы. Эти отметки сохранили прежние условные координаты."),
                AtlasCatalog.WorldLayer when world is not null => Loc.T("Точки в игровых координатах. Рельеф восстановлен приближённо по высотам объектов."),
                AtlasCatalog.WorldLayer => Loc.T("Координатный слой для этой зоны пока отсутствует."),
                _ => Loc.T("Локальный снимок. Отметки этого слоя привязаны к изображению.")
            };
            UpdateSelection();
        }

        void UpdateSelection()
        {
            var current = Selected();
            refreshingList = true;
            list.SelectedItem = (list.ItemsSource as AtlasListRow[])?.FirstOrDefault(row => row.Pin.Id == selection);
            refreshingList = false;
            var world = layer == AtlasCatalog.WorldLayer ? World() : null;
            inspector.Text = current is null ? Loc.T("Выберите точку") : AtlasMarks.DisplayName(current, world);
            var coordinate = current is not null && world is not null ? world.ToGame(new Point(current.X, current.Y)) : (Point?)null;
            details.Text = current is null ? Loc.T("Щелчок по метке откроет её здесь.") :
                (current.Category == "Куб" && coordinate is not null ? Loc.T("Возможное место куба") : Loc.T(current.Category)) +
                (coordinate is { } c ? Loc.F($" · X {c.X / 100:0} м / Y {c.Y / 100:0} м") : "");
            center.IsEnabled = found.IsEnabled = waypoint.IsEnabled = current is not null;
            clearRoute.IsEnabled = route.Count > 0;
            routeStatus.Text = route.Count == 0 ? Loc.T("Маршрут пуст") : Loc.F($"В маршруте: {route.Count} · по порядку добавления");
            delete.IsEnabled = current is not null && profile.Pins.Any(p => p.Id == current.Id);
            found.Content = current?.Found == true ? Loc.T("Снять отметку") : Loc.T("Отметить");
            waypoint.Content = route.Contains(selection) ? Loc.T("Из маршрута") : Loc.T("В маршрут");
            map.Selected = current?.Id;
            map.InvalidateVisual();
        }

        void ToggleFound()
        {
            if (Selected() is not { } pin) return;
            AtlasMarks.ToggleFound(_store, pin, layer == AtlasCatalog.WorldLayer ? World() : null);
        }

        void Delete()
        {
            if (profile.Pins.RemoveAll(p => p.Id == selection) == 0) return;
            route.Remove(selection);
            selection = Guid.Empty;
            _store.Commit();
        }
    }

    private static ComboBox Choice(IEnumerable<string> items, double width, bool localize = true)
    {
        var combo = new ComboBox
        {
            ItemsSource = items, SelectedIndex = 0, Width = width, MinHeight = 31,
            Padding = new Thickness(6, 4, 6, 4)
        };
        return localize ? Loc.LocalizeItems(combo) : combo;
    }
}
