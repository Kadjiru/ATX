using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using static ArxBeta.Ui;

namespace ArxBeta;

public sealed partial class Dashboard : Window
{
    private readonly LegionStore _store;
    private readonly CatalogSync _catalog;
    private readonly ScrollViewer _scroll;
    private readonly TextBlock _status;
    private readonly Dictionary<string, Button> _navigation = [];
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private Action? _refreshTime;
    private Action? _refreshShared;
    private string _page = "combat";
    private string? _player;

    public Dashboard(LegionStore store, CatalogSync catalog)
    {
        _store = store;
        _catalog = catalog;
        Title = "ARX · Legion Companion";
        Width = 1340;
        Height = 920;
        MinWidth = 920;
        MinHeight = 610;
        MaxWidth = SystemParameters.WorkArea.Width;
        MaxHeight = SystemParameters.WorkArea.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var shell = new Grid { Background = Ui.Brush("Base") };
        shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(67) });
        shell.RowDefinitions.Add(new RowDefinition());
        shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
        shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(214) });
        shell.ColumnDefinitions.Add(new ColumnDefinition());

        var logo = Text("◈  ARX", 27, "Mint", true);
        logo.VerticalAlignment = VerticalAlignment.Center;
        var brand = Stack(logo, Label("LEGION COMPANION  /  AION 2"));
        brand.Margin = new Thickness(23, 7, 0, 0);
        shell.Children.Add(brand);
        _status = Text("", 12, "Mint", true);
        _status.VerticalAlignment = VerticalAlignment.Center;
        var top = new DockPanel { Margin = new Thickness(10, 0, 25, 0) };
        var hud = Actions(Button("Урон", () => Hud(HudKind.Combat)), Button("События", () => Hud(HudKind.Events)),
            Button("Задачи", () => Hud(HudKind.Tasks)));
        hud.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(hud, Dock.Right);
        top.Children.Add(hud);
        top.Children.Add(_status);
        Place(shell, top, 0, 1);

        var nav = Stack(Space(Label("  АТЛАС ЛЕГИОНА"), 10));
        AddNav(nav, "combat", "01", "Обзор боя");
        AddNav(nav, "analysis", "02", "Разбор и история");
        AddNav(nav, "atlas", "03", "Атлас и прогресс");
        AddNav(nav, "events", "04", "События");
        AddNav(nav, "session", "05", "Сессия и кина");
        AddNav(nav, "codex", "06", "Мета и билды");
        AddNav(nav, "craft", "07", "Предметы и крафт");
        AddNav(nav, "settings", "08", "Настройки");
        var rail = Card(nav, 11);
        rail.VerticalAlignment = VerticalAlignment.Stretch;
        rail.Margin = new Thickness(16, 0, 0, 6);
        Place(shell, rail, 1, 0);

        _scroll = new ScrollViewer
        {
            Padding = new Thickness(20, 0, 21, 12), VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, CanContentScroll = false
        };
        Place(shell, _scroll, 1, 1);
        var footer = Label("ARX  /  0.1.0 BETA       •       GLOBAL       •       ЛОКАЛЬНЫЙ ПРОФИЛЬ");
        footer.Margin = new Thickness(21, 6, 0, 0);
        Grid.SetColumnSpan(footer, 2);
        Place(shell, footer, 2, 0);
        Content = shell;
        Navigate("combat");
        _store.Changed += OnStoreChanged;
        _clock.Tick += (_, _) => _refreshTime?.Invoke();
        if (!_store.Preview) _clock.Start();
        Closed += (_, _) =>
        {
            _clock.Stop();
            _store.Changed -= OnStoreChanged;
        };
    }

    private void AddNav(Panel parent, string key, string index, string title)
    {
        var button = Space(Button($"{index}   {title}", () => Navigate(key)), 5);
        button.HorizontalContentAlignment = HorizontalAlignment.Left;
        button.MinHeight = 42;
        parent.Children.Add(button);
        _navigation[key] = button;
    }

    private void Hud(HudKind kind) => App.CurrentApp.ToggleHud(kind);

    public void Navigate(string page)
    {
        _page = page;
        _scroll.ScrollToTop();
        RenderPage();
    }

    private void OnStoreChanged()
    {
        UpdateStatus();
        if (_page is "combat" or "analysis" or "session") RenderPage();
        else _refreshShared?.Invoke();
    }

    private void UpdateStatus()
    {
        var selected = _store.Analysis.Fight;
        _status.Text = selected.Demo ? "●  ДЕМО · учебные данные" : selected.Id == Guid.Empty
            ? "●  ЛОКАЛЬНЫЙ РЕЖИМ" : "●  ЛОКАЛЬНЫЙ ОТЧЁТ";
        if (!string.IsNullOrWhiteSpace(_store.Profile.Server)) _status.Text += "  /  " + _store.Profile.Server;
    }

    private void RenderPage()
    {
        _refreshTime = null;
        _refreshShared = null;
        UpdateStatus();
        foreach (var (key, button) in _navigation)
        {
            var selected = key == _page;
            button.Background = Ui.Brush(selected ? "Raised" : "Surface");
            button.Foreground = Ui.Brush(selected ? "Mint" : "Muted");
            button.BorderBrush = Ui.Brush(selected ? "Line" : "Surface");
        }
        _scroll.Content = _page switch
        {
            "analysis" => AnalysisPage(),
            "atlas" => AtlasPage(),
            "events" => EventsPage(),
            "session" => SessionPage(),
            "codex" => CodexPage(),
            "craft" => CraftPage(),
            "settings" => SettingsPage(),
            _ => CombatPage()
        };
    }

    private static void Place(Grid grid, UIElement element, int row, int column)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }

    private static StackPanel Heading(string title, string subtitle) =>
        Stack(Space(Text(title, 27, bold: true), 4), Space(Hint(subtitle), 15));

    private UIElement CombatPage()
    {
        var current = _store.Analysis;
        var fight = current.Fight;
        var page = Heading("Боевой обзор", "Ход боя, вклад группы и личные результаты");
        var art = new ImageBrush(new BitmapImage(new Uri("pack://application:,,,/Assets/Atreia.jpg")))
        {
            Stretch = Stretch.UniformToFill, AlignmentX = AlignmentX.Right, AlignmentY = AlignmentY.Center
        };
        var hero = new Grid { MinHeight = 190 };
        hero.Background = new LinearGradientBrush(
            new GradientStopCollection { new(Color.FromArgb(250, 10, 22, 24), 0),
                new(Color.FromArgb(186, 10, 22, 24), 0.49), new(Color.FromArgb(12, 10, 22, 24), 1) }, 0);
        var summary = Stack(
            Space(Text("◆   ЛЕГИОН ARX    /    AION 2 GLOBAL", 11, "Mint", true), 9),
            Space(Text(fight.Name, 32, bold: true), 5),
            Space(Hint(fight.Id == Guid.Empty ? "Добавьте результаты из Combat Analysis или откройте пример."
                : $"{current.Players.Length} участников  ·  {(fight.Demo ? "Учебный бой" : fight.Date.ToLocalTime().ToString("dd.MM.yyyy  HH:mm"))}"), 17),
            Actions(Button("Внести результаты", () => Navigate("analysis"), true),
                Button("Демо боя", () => _store.SetFight(FightImport.Sample(), false)),
                Button("Открыть HUD урона", () => Hud(HudKind.Combat))));
        summary.Margin = new Thickness(22, 18, 12, 9);
        summary.VerticalAlignment = VerticalAlignment.Center;
        hero.Children.Add(summary);
        var heroCard = Card(hero, 0);
        heroCard.Background = art;
        heroCard.ClipToBounds = true;
        hero.Clip = new RectangleGeometry(new Rect(0, 0, 4000, 1000), 16, 16);
        page.Children.Add(Space(heroCard, 13));

        var cards = new UniformGrid { Columns = 4 };
        AddMetric(cards, "СРЕДНИЙ DPS ГРУППЫ", fight.Id == Guid.Empty ? "—" : Compact(current.Dps), "за весь отчёт", "Mint");
        AddMetric(cards, "ОБЩИЙ УРОН", fight.Id == Guid.Empty ? "—" : Compact(current.Damage), "все участники", "Text");
        AddMetric(cards, "БУРСТ ГРУППЫ · 5 С", current.Burst is { } burst ? Compact(burst) : "—", "скользящее окно", "Coral");
        AddMetric(cards, "ДЛИТЕЛЬНОСТЬ", fight.Id == Guid.Empty ? "—" : Clock(TimeSpan.FromSeconds(fight.Duration)), "период отчёта", "Gold");
        page.Children.Add(Space(cards, 13));

        var details = new Grid();
        details.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.17, GridUnitType.Star) });
        details.ColumnDefinitions.Add(new ColumnDefinition());
        var chart = Card(Stack(Space(Label("БОЕВОЙ ТЕМП"), 5),
            Space(Hint("Группа  /  ваш персонаж"), 8),
            new BattleChart(current) { Height = 170 }));
        chart.Margin = new Thickness(0, 0, 10, 0);
        details.Children.Add(chart);
        var party = Stack(Space(Label($"ВКЛАД ГРУППЫ   /   {current.Players.Length}"), 9));
        if (current.Players.Length == 0)
            party.Children.Add(Hint("Результаты группы появятся после внесения отчёта."));
        foreach (var member in current.Players.Take(6))
        {
            var button = Space(Button($"{member.Player}     ·     {Compact(current.Rate(member.Damage))} DPS", () =>
            {
                _player = member.Player;
                Navigate("analysis");
            }), 5);
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            party.Children.Add(button);
        }
        Place(details, Card(party), 0, 1);
        page.Children.Add(Space(details, 13));
        var summaryCards = new UniformGrid { Columns = 3 };
        var self = current.Players.FirstOrDefault(p => string.Equals(p.Player, fight.Self, StringComparison.OrdinalIgnoreCase));
        var now = DateTimeOffset.UtcNow;
        var next = _store.Upcoming(now).FirstOrDefault();
        AddMetric(summaryCards, "ВАШ ВКЛАД", self is null ? "—" : Compact(current.Rate(self.Damage)) + " DPS",
            self?.Player ?? "укажите персонажа в отчёте", "Mint");
        AddMetric(summaryCards, "БЛИЖАЙШЕЕ СОБЫТИЕ", next.Rule is null ? "—" : next.Due.ToLocalTime().ToString("HH:mm"),
            next.Rule?.Name ?? "активируйте таймер", "Gold");
        var pins = _store.AllPins.ToArray();
        AddMetric(summaryCards, "ПРОГРЕСС АТЛАСА", $"{pins.Count(p => p.Found)} / {pins.Length}", "найденные метки", "Coral");
        page.Children.Add(summaryCards);
        return page;
    }

    private static void AddMetric(Panel panel, string label, string value, string caption, string tint)
    {
        var card = Card(Stack(Space(Label(label), 7), Space(Text(value, 30, tint, true), 4), Hint(caption)), 15);
        card.Margin = new Thickness(0, 0, 7, 0);
        panel.Children.Add(card);
    }
}
