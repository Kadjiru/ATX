using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shell;
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
    private Action? _refreshLive;
    private string _page = "combat";
    private string? _player;
    private uint? _actor;

    public Dashboard(LegionStore store, CatalogSync catalog)
    {
        _store = store;
        _catalog = catalog;
        Title = "ARX · Aion 2 Companion";
        Width = 1260;
        Height = 830;
        MinWidth = 850;
        MinHeight = 610;
        WindowStyle = WindowStyle.None;
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 49, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false
        });
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var shell = new Grid { Background = Ui.Brush("Base") };
        shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(50) });
        shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        shell.RowDefinitions.Add(new RowDefinition());
        shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });

        var brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var badge = new Border { Background = Ui.Brush("Mint"), CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 4, 8, 4),
            Child = Text("ARX", 17, "Base", true), Margin = new Thickness(0, 0, 14, 0) };
        brand.Children.Add(badge);
        brand.Children.Add(new TextBlock { Text = "Aion 2 Global", Foreground = Ui.Brush("Text"), VerticalAlignment = VerticalAlignment.Center });
        _status = Text("", 12, "Mint", true);
        _status.VerticalAlignment = VerticalAlignment.Center;
        _status.Margin = new Thickness(22, 0, 0, 0);
        var top = new DockPanel { Margin = new Thickness(16, 0, 8, 0) };
        var chrome = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var button in new[] { Button("−", () => WindowState = WindowState.Minimized),
                     Button("□", () => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized),
                     Button("×", Close) })
        {
            button.Width = 40;
            button.MinHeight = 31;
            button.Padding = new Thickness(5);
            button.Margin = new Thickness(3, 0, 0, 0);
            WindowChrome.SetIsHitTestVisibleInChrome(button, true);
            chrome.Children.Add(button);
        }
        DockPanel.SetDock(chrome, Dock.Right);
        DockPanel.SetDock(brand, Dock.Left);
        top.Children.Add(chrome);
        top.Children.Add(brand);
        top.Children.Add(_status);
        Place(shell, top, 0, 0);

        var nav = new WrapPanel();
        AddNav(nav, "combat", "Монитор");
        AddNav(nav, "analysis", "Отчёты");
        AddNav(nav, "expeditions", "Забеги");
        AddNav(nav, "atlas", "Карта");
        AddNav(nav, "events", "События");
        AddNav(nav, "session", "Сессия");
        AddNav(nav, "progress", "Прогресс");
        AddNav(nav, "codex", "Билды");
        AddNav(nav, "craft", "Крафт");
        AddNav(nav, "hotkeys", "Горячие клавиши");
        AddNav(nav, "settings", "Настройки");
        Place(shell, new Border { Background = Ui.Brush("Surface"), Child = nav,
            Padding = new Thickness(16, 7, 8, 5), BorderBrush = Ui.Brush("Line"), BorderThickness = new Thickness(0, 1, 0, 1) }, 1, 0);

        _scroll = new ScrollViewer
        {
            Padding = new Thickness(24, 17, 23, 12), VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, CanContentScroll = false
        };
        Place(shell, _scroll, 2, 0);
        var footer = Label("0.2.1 BETA     /     GLOBAL     /     ALT+F8 HUD УРОНА");
        footer.Margin = new Thickness(21, 6, 0, 0);
        Place(shell, footer, 3, 0);
        Content = shell;
        Navigate("combat");
        _store.Changed += OnStoreChanged;
        _store.LiveChanged += OnLiveChanged;
        StateChanged += (_, _) => { if (WindowState != WindowState.Minimized) OnLiveChanged(); };
        _clock.Tick += (_, _) => _refreshTime?.Invoke();
        if (!_store.Preview) _clock.Start();
        Closed += (_, _) =>
        {
            _clock.Stop();
            _store.Changed -= OnStoreChanged;
            _store.LiveChanged -= OnLiveChanged;
        };
    }

    private void AddNav(Panel parent, string key, string title)
    {
        var button = Button(title, () => Navigate(key));
        button.Margin = new Thickness(0, 0, 4, 3);
        button.HorizontalContentAlignment = HorizontalAlignment.Left;
        button.MinHeight = 34;
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
        var selected = _store.DisplayAnalysis.Fight;
        _status.Text = selected.Demo ? "●  Демо · учебные данные" : "●  " + _store.CaptureLabel;
    }

    private void OnLiveChanged()
    {
        if (WindowState == WindowState.Minimized) return;
        UpdateStatus();
        _refreshLive?.Invoke();
    }

    private void RenderPage()
    {
        _refreshTime = null;
        _refreshShared = null;
        _refreshLive = null;
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
            "expeditions" => ExpeditionsPage(),
            "atlas" => AtlasPage(),
            "events" => EventsPage(),
            "session" => SessionPage(),
            "progress" => ProgressPage(),
            "codex" => CodexPage(),
            "craft" => CraftPage(),
            "hotkeys" => HotkeysPage(),
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
        var page = Heading("Боевой монитор", "Aion 2 Global  /  живой поток и локальные отчёты");
        var capture = Button("Начать захват", () => _ = App.CurrentApp.ToggleCapture(), true);
        var identity = Text("", 12, "Muted");
        var controls = Actions(capture, Button("К живому бою", () => _store.SetFollowLive(true)),
            Button("HUD урона", () => Hud(HudKind.Combat)), Button("HUD событий", () => Hud(HudKind.Events)),
            Button("HUD задач", () => Hud(HudKind.Tasks)), Button("Демо", () => _store.SetFight(FightImport.Sample(), false)));
        page.Children.Add(controls);
        page.Children.Add(Space(identity, 13));

        var title = Text("", 33, bold: true);
        var source = Text("", 11, "Coral", true);
        var sub = Hint("");
        var analysis = _store.DisplayAnalysis;
        var chart = new BattleChart(analysis) { Height = 240 };
        var report = Button("Разобрать текущий бой", () =>
        {
            var fight = _store.DisplayAnalysis.Fight;
            if (fight.Id == Guid.Empty) { Navigate("analysis"); return; }
            _actor = null;
            _player = null;
            _store.SetFight(fight, false);
            Navigate("analysis");
        });
        var panel = Stack(Space(source, 6), Space(title, 4), Space(sub, 18),
            Space(Label("ТЕМП БОЯ   ·   ОБЩИЙ УРОН / ВАШ ПЕРСОНАЖ"), 9), Space(chart, 12), Actions(report));
        var battle = Card(panel, 22);
        battle.Margin = new Thickness(0, 0, 13, 0);

        var numbers = Text("", 40, "Mint", true);
        var damage = Text("", 15, bold: true);
        var burst = Text("", 15, "Coral", true);
        var healing = Text("", 15, "Gold", true);
        var party = new StackPanel();
        var stats = Card(Stack(Space(Label("DPS УЧАСТНИКОВ"), 5), Space(numbers, 10), Space(damage, 6),
            Space(burst, 6), Space(healing, 15), Space(Label("РАСПРЕДЕЛЕНИЕ УРОНА"), 7), party));
        var split = new Grid();
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.15, GridUnitType.Star) });
        split.ColumnDefinitions.Add(new ColumnDefinition { MinWidth = 235 });
        split.Children.Add(battle);
        Place(split, stats, 0, 1);
        page.Children.Add(Space(split, 14));

        var runInfo = Hint("");
        page.Children.Add(Card(Stack(Space(Label("ЗАБЕГ И ПРОГРЕСС"), 7), Space(runInfo, 9),
            Actions(Button("Забеги и сравнение", () => Navigate("expeditions")),
                Button("Открыть карту", () => Navigate("atlas")), Button("Активности", () => Navigate("progress"))))));
        _refreshLive = Refresh;
        Refresh();
        return page;

        void Refresh()
        {
            var current = _store.DisplayAnalysis;
            var fight = current.Fight;
            var snapshot = fight.Captured?.Summary;
            capture.Content = App.CurrentApp.Live?.IsRunning == true ? "Остановить захват" : "Начать захват";
            capture.IsEnabled = App.CurrentApp.Live?.IsStopping != true;
            identity.Text = _store.LiveIdentity is { } self ? $"Персонаж: {self}" : _store.CaptureLabel;
            source.Text = fight.Demo ? "ДЕМОНСТРАЦИЯ" : fight.Captured is not null
                ? snapshot!.IsActive && _store.FollowLive ? "В БОЮ · NPCAP" : "ЗАПИСЬ БОЯ · NPCAP"
                : fight.Id == Guid.Empty ? "ОЖИДАНИЕ БОЯ" : "ЛОКАЛЬНЫЙ ОТЧЁТ";
            title.Text = fight.Id == Guid.Empty ? "Оперативный обзор" : fight.Name;
            sub.Text = snapshot?.MostDamageUnsent == true ? "Сервер прислал только часть урона участников." :
                snapshot?.Boss is { MaxHpKnown: true } boss ? $"Цель: {boss.HpFraction:P0} HP  ·  {Clock(TimeSpan.FromSeconds(fight.Duration))}"
                : fight.Id == Guid.Empty ? "Запустите захват или откройте отчёт в соседней вкладке."
                : $"{current.Players.Length} участников  ·  {Clock(TimeSpan.FromSeconds(fight.Duration))}";
            numbers.Text = fight.Id == Guid.Empty ? "—" : Compact(current.Dps);
            damage.Text = $"Урон   {Compact(current.Damage)}";
            healing.Text = $"Лечение   {Compact(current.Healing)} · {Compact(current.Rate(current.Healing))} HPS";
            burst.Text = $"Пик за 5 с   {(current.Burst is { } peak ? Compact(peak) : "—")}";
            chart.Update(current);
            party.Children.Clear();
            foreach (var member in current.Players.Take(7))
            {
                var share = current.Damage == 0 ? 0 : (double)member.Damage / current.Damage;
                var row = new DockPanel();
                var value = Text($"{Compact(current.Rate(member.Damage))} DPS", 12, "Muted");
                DockPanel.SetDock(value, Dock.Right);
                row.Children.Add(value);
                row.Children.Add(Text(member.Player, 13, "Text", true));
                party.Children.Add(row);
                party.Children.Add(Space(new ProgressBar { Value = share * 100, Height = 3,
                    Foreground = Ui.Brush("Mint"), Background = Ui.Brush("Raised"), BorderThickness = new Thickness(0) }, 9));
            }
            if (current.Players.Length == 0) party.Children.Add(Hint("Пока нет данных о попаданиях."));
            var pins = _store.AllPins.ToArray();
            runInfo.Text = (_store.Profile.CurrentExpedition is { } expedition
                ? $"Активный забег: {expedition.Name} · {expedition.Segments.Count} завершённых боёв" : "Активный забег не начат") +
                $"     /     Атлас: {pins.Count(p => p.Found)} из {pins.Length} меток";
        }
    }
}
