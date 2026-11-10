using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using static ArxBeta.Ui;

namespace ArxBeta;

internal sealed class AtlasHudPanel : DockPanel
{
    private sealed record MarkerRow(MapPin Pin)
    {
        public override string ToString() => $"{(Pin.Found ? "✓" : "•")}  {Pin.Name}  ·  {Pin.Category}";
    }

    private readonly LegionStore _store;
    private readonly AtlasCanvas _map = new() { Compact = true, MinHeight = 90 };
    private readonly ComboBox _zone = new() { ItemsSource = AtlasZone.All, Width = 158, MinHeight = 29 };
    private readonly ComboBox _layers = new() { Width = 153, MinHeight = 29 };
    private readonly ComboBox _category = new() { ItemsSource = AtlasMarks.Categories, SelectedIndex = 0, Width = 130, MinHeight = 29 };
    private readonly TextBox _search = new() { MinHeight = 30, MaxLength = 120, ToolTip = "Русское или английское имя точки" };
    private readonly CheckBox _personal = new() { Content = "Только мои метки" };
    private readonly ListBox _results = new() { Height = 90, MaxHeight = 90 };
    private readonly TextBlock _status = Hint("");
    private readonly TextBlock _point = Text("", 13, "Mint", true);
    private readonly TextBlock _empty = Hint("");
    private readonly Button _found;
    private readonly Button _center;
    private AtlasCatalog.AtlasMap? _world;
    private MapPin[] _visible = [];
    private bool _updating;
    private string? _layer = AtlasCatalog.WorldLayer;
    private Guid? _selected;

    private AtlasZone Zone => (AtlasZone)_zone.SelectedItem;
    public AtlasSelection View => new(Zone.Id, _layer, _selected);

    public AtlasHudPanel(LegionStore store, Action<AtlasSelection> openAtlas)
    {
        _store = store;
        var filters = Stack(
            Actions(Field("Зона", _zone), Field("Категория", _category)),
            Space(Actions(_layers, _personal), 2),
            Space(Label("ПОИСК ПО МЕТКАМ"), 4), Space(_search, 5), Space(_status, 4), Space(_results, 6));
        _results.ItemContainerStyle = (Style)Application.Current.FindResource("ReferenceRow");
        System.Windows.Automation.AutomationProperties.SetName(_search, "Поиск по меткам миникарты");
        System.Windows.Automation.AutomationProperties.SetName(_layers, "Слой миникарты");
        var searchToggle = new Button { Content = "Поиск ▾" };
        searchToggle.Click += (_, _) =>
        {
            var show = filters.Visibility != Visibility.Visible;
            filters.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            searchToggle.Content = show ? "Поиск ▾" : "Поиск ▸";
            if (show) _search.Focus();
        };
        var tools = Actions(searchToggle, Button("−", () => _map.Zoom(1 / 1.3)),
            Button("+", () => _map.Zoom(1.3)), Button("Вписать", _map.Fit));
        var top = Stack(tools, filters);
        SetDock(top, Dock.Top);
        Children.Add(top);

        _found = Button("Найдено", () =>
        {
            if (Selected() is { } pin) AtlasMarks.ToggleFound(_store, pin, _world);
        });
        _center = Button("К метке", () => { if (Selected() is { } pin) _map.FocusPin(pin); });
        var footer = Stack(Space(_point, 4),
            Actions(_center, _found, Button("В Атлас ↗", () => openAtlas(View))));
        footer.Margin = new Thickness(0, 7, 0, 0);
        SetDock(footer, Dock.Bottom);
        Children.Add(footer);

        _empty.HorizontalAlignment = HorizontalAlignment.Center;
        _empty.VerticalAlignment = VerticalAlignment.Center;
        _empty.Margin = new Thickness(13);
        _empty.IsHitTestVisible = false;
        var area = new Grid();
        area.Children.Add(_map);
        area.Children.Add(_empty);
        Children.Add(area);

        _zone.SelectionChanged += (_, _) => { if (!_updating) LoadZone(AtlasCatalog.WorldLayer, null); };
        _layers.SelectionChanged += (_, _) =>
        {
            if (!_updating && _layers.SelectedItem is AtlasLayer layer) LoadLayer(layer.Key, null);
        };
        _category.SelectionChanged += (_, _) => { if (!_updating) Refresh(); };
        _personal.Click += (_, _) => Refresh();
        _search.TextChanged += (_, _) => { if (!_updating) Refresh(); };
        _search.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && _results.Items.Count > 0)
            {
                if (_results.SelectedItem is not MarkerRow) _results.SelectedIndex = 0;
                else if (Selected() is { } pin) _map.FocusPin(pin);
                e.Handled = true;
            }
        };
        _results.SelectionChanged += (_, _) =>
        {
            if (!_updating && _results.SelectedItem is MarkerRow row) Select(row.Pin, true);
        };
        _map.PinSelected += pin => Select(pin, false);
        var saved = store.Profile.AtlasHudView ?? new AtlasSelection(store.Profile.AtlasZone, AtlasCatalog.WorldLayer, null);
        Open(saved);
    }

    public void Open(AtlasSelection view)
    {
        _updating = true;
        _zone.SelectedItem = AtlasZone.All.FirstOrDefault(zone => zone.Id == view.Zone) ?? AtlasZone.All[0];
        _category.SelectedIndex = 0;
        _personal.IsChecked = false;
        _search.Clear();
        _updating = false;
        LoadZone(view.Layer, view.PinId);
    }

    public void Refresh()
    {
        var layers = AtlasMarks.Layers(_store, Zone);
        if (_layers.ItemsSource is not IReadOnlyList<AtlasLayer> previous || !layers.SequenceEqual(previous))
        {
            LoadZone(_layer, _selected);
            return;
        }
        var query = _search.Text.Trim();
        var all = AtlasMarks.All(_store, Zone, _layer, _world);
        var personal = _personal.IsChecked == true ? _store.Profile.Pins.Select(pin => pin.Id).ToHashSet() : null;
        _visible = all.Where(pin => (personal is null || personal.Contains(pin.Id)) &&
            (_category.SelectedIndex <= 0 || pin.Category == _category.SelectedItem as string)
            && AtlasMarks.Matches(pin, _world, query)).ToArray();
        var current = Selected();
        _selected = current?.Id;
        _updating = true;
        var rows = query.Length == 0 ? [] : _visible.OrderBy(pin => pin.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(pin => pin.Id).Take(150).Select(pin => new MarkerRow(pin)).ToArray();
        _results.ItemsSource = rows;
        _results.SelectedItem = rows.FirstOrDefault(row => row.Pin.Id == _selected);
        _results.Visibility = rows.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        _status.Text = query.Length == 0 ? $"{Zone.Name}  ·  меток: {_visible.Length}" :
            _visible.Length > rows.Length ? $"Совпадений: {_visible.Length} · уточните поиск" :
            _visible.Length == 0 ? "Нет совпадений в этой зоне" : $"Совпадений: {_visible.Length} · Enter к первой метке";
        _updating = false;
        _map.SetPins(_visible, []);
        UpdateSelection(current);
    }

    private void LoadZone(string? preferred, Guid? selected)
    {
        var options = AtlasMarks.Layers(_store, Zone);
        var layer = options.FirstOrDefault(option => option.Key == preferred) ?? options[0];
        _updating = true;
        _layers.ItemsSource = options;
        _layers.SelectedItem = layer;
        _layers.Visibility = options.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        _updating = false;
        LoadLayer(layer.Key, selected);
    }

    private void LoadLayer(string? layer, Guid? selected)
    {
        _layer = layer;
        _selected = selected;
        _world = layer == AtlasCatalog.WorldLayer ? AtlasCatalog.For(Zone) : null;
        var image = AtlasLayers.Open(layer);
        _map.SetLayer(_world, image);
        _empty.Text = _world is not null || image is not null ? "" : layer is null
            ? "Архив старых меток" : "Добавьте снимок этой зоны в Атласе";
        Refresh();
        if (Selected() is { } pin) _map.FocusPin(pin);
    }

    private MapPin? Selected() => _selected is { } id ? _visible.FirstOrDefault(pin => pin.Id == id) : null;

    private void Select(MapPin pin, bool center)
    {
        _selected = pin.Id;
        _updating = true;
        _results.SelectedItem = _results.Items.OfType<MarkerRow>().FirstOrDefault(row => row.Pin.Id == pin.Id);
        if (_results.SelectedItem is { } row) _results.ScrollIntoView(row);
        _updating = false;
        UpdateSelection(pin);
        if (center) _map.FocusPin(pin);
    }

    private void UpdateSelection(MapPin? pin)
    {
        _center.IsEnabled = _found.IsEnabled = pin is not null;
        _found.Content = pin?.Found == true ? "Снять отметку" : "Найдено";
        var coordinate = pin is null ? null : _world?.ToGame(new Point(pin.X, pin.Y));
        _point.Text = pin is null ? "Выберите метку · колесо меняет масштаб" : pin.Name +
            (coordinate is { } c ? $"\nX {c.X / 100:0} м · Y {c.Y / 100:0} м" : "");
        _map.Selected = pin?.Id;
        _map.InvalidateVisual();
    }
}
