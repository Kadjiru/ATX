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
        MinWidth = 975;
        MinHeight = 610;
        WindowStyle = WindowStyle.None;
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 49, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false
        });
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var shell = new Grid { Background = Ui.Brush("Base") };
        shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(184) });
        shell.ColumnDefinitions.Add(new ColumnDefinition());
        shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(50) });
        shell.RowDefinitions.Add(new RowDefinition());
        shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });

        var brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        brand.Children.Add(new Border { Background = Ui.Brush("Mint"), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(7, 2, 7, 4), Child = Text("◇", 21, "Base", true), Margin = new Thickness(0, 0, 10, 0) });
        brand.Children.Add(Text("A R X", 16, bold: true));
        var product = Label("AION 2 GLOBAL");
        product.Margin = new Thickness(16, 2, 0, 0);
        product.VerticalAlignment = VerticalAlignment.Center;
        brand.Children.Add(product);
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
        var topFrame = new Border { Background = Ui.Brush("Surface"), Child = top,
            BorderBrush = Ui.Brush("Line"), BorderThickness = new Thickness(0, 0, 0, 1) };
        Grid.SetColumnSpan(topFrame, 2);
        Place(shell, topFrame, 0, 0);

        var nav = new StackPanel();
        NavHeading("БОЕВОЙ ЖУРНАЛ");
        AddNav(nav, "combat", "◇", "Монитор");
        AddNav(nav, "analysis", "≋", "Отчёты");
        AddNav(nav, "expeditions", "▤", "Забеги");
        NavHeading("МИР И СЕССИЯ");
        AddNav(nav, "atlas", "⌖", "Атлас");
        AddNav(nav, "events", "◷", "События");
        AddNav(nav, "session", "☰", "Сессия");
        AddNav(nav, "progress", "✓", "Прогресс");
        AddNav(nav, "codex", "✧", "Билды");
        AddNav(nav, "craft", "⬡", "Крафт");
        NavHeading("УПРАВЛЕНИЕ");
        AddNav(nav, "hotkeys", "⌨", "Горячие клавиши");
        AddNav(nav, "settings", "⚙", "Настройки");
        var sidebar = new Border { Background = Ui.Brush("Surface"), Padding = new Thickness(12, 8, 11, 5),
            BorderBrush = Ui.Brush("Line"), BorderThickness = new Thickness(0, 0, 1, 0),
            Child = new ScrollViewer { Content = nav, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } };
        Place(shell, sidebar, 1, 0);

        _scroll = new ScrollViewer
        {
            Padding = new Thickness(24, 17, 23, 12), VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, CanContentScroll = false
        };
        Place(shell, _scroll, 1, 1);
        var footer = Label("ARX 0.2.5 BETA     /     GLOBAL     /     ALT+F8  БОЕВОЙ HUD");
        var footerFrame = new Border { Child = footer, Padding = new Thickness(18, 7, 0, 0),
            Background = Ui.Brush("Surface"), BorderBrush = Ui.Brush("Line"), BorderThickness = new Thickness(0, 1, 0, 0) };
        Grid.SetColumnSpan(footerFrame, 2);
        Place(shell, footerFrame, 2, 0);
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
        void NavHeading(string text)
        {
            var heading = Label(text);
            heading.Margin = new Thickness(9, 11, 0, 5);
            nav.Children.Add(heading);
        }
    }

    private void AddNav(Panel parent, string key, string icon, string title)
    {
        var button = Button(title, () => Navigate(key));
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        var glyph = Text(icon, 15, "Mint");
        glyph.Width = 25;
        content.Children.Add(glyph);
        content.Children.Add(Text(title, 13, bold: true));
        button.Content = content;
        button.Margin = new Thickness(0, 0, 0, 3);
        button.Padding = new Thickness(8, 4, 5, 4);
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
        _status.Text = "●  " + (_store.Preview ? "Предпросмотр" : _store.CaptureLabel);
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
        var page = Stack(Space(Label("БОЕВОЙ ЖУРНАЛ    /    01"), 4), Space(Text("Монитор боя", 29, bold: true), 13));
        var controls = Actions(Button("Живой бой", () => _store.SetFollowLive(true), true),
            Button("Боевой HUD", () => Hud(HudKind.Combat)), Button("Демо", () => _store.SetFight(FightImport.Sample(), false)));

        var title = Text("", 30, bold: true);
        var source = Text("", 11, "Mint", true);
        var sub = Hint("");
        var art = new CombatSigil
        {
            Width = 400, HorizontalAlignment = HorizontalAlignment.Right, IsHitTestVisible = false,
            OpacityMask = new LinearGradientBrush(Colors.Transparent, Colors.Black, 0)
        };
        var hero = new Grid { MinHeight = 183, ClipToBounds = true };
        hero.Children.Add(art);
        hero.Children.Add(new Border
        {
            Child = Stack(Space(source, 7), Space(title, 5), Space(sub, 13), controls),
            Padding = new Thickness(21, 16, 17, 10)
        });
        var banner = Card(hero, 0);
        banner.Background = new LinearGradientBrush(Color.FromRgb(20, 41, 44), Color.FromRgb(23, 53, 55), 0);
        page.Children.Add(Space(banner, 12));

        var personal = Text("—", 26, "Coral", true);
        var numbers = Text("—", 26, "Mint", true);
        var damage = Text("—", 26, bold: true);
        var burst = Text("—", 26, "Gold", true);
        var identity = Label("");
        var context = Label("");
        var metrics = new UniformGrid { Columns = 4 };
        AddMetric("ВАШ DPS", personal, identity);
        AddMetric("СУММА DPS", numbers, context);
        AddMetric("УРОН", damage, Label("За боевой сегмент"));
        AddMetric("БУРСТ", burst, Label("Пиковые 5 секунд"));
        page.Children.Add(Space(metrics, 12));

        var chart = new BattleChart(_store.DisplayAnalysis) { Height = 213 };
        var report = Button("Открыть отчёт", () =>
        {
            var fight = _store.DisplayAnalysis.Fight;
            if (fight.Id != Guid.Empty)
            {
                _actor = null;
                _player = null;
                _store.SetFight(fight, false);
            }
            Navigate("analysis");
        });
        var battle = Card(Stack(Space(Text("Темп боя", 18, bold: true), 4),
            Space(Actions(Text("●  Сумма", 12, "Mint"), Text("●  Вы", 12, "Coral")), 6),
            Space(chart, 12), Actions(report)), 15);
        battle.Margin = new Thickness(0, 0, 11, 0);

        var party = new StackPanel();
        var partyStatus = Label("");
        var stats = Card(Stack(Space(Text("Участники", 18, bold: true), 4), Space(partyStatus, 11), party), 15);
        var split = new Grid();
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
        split.ColumnDefinitions.Add(new ColumnDefinition { MinWidth = 268 });
        split.Children.Add(battle);
        Place(split, stats, 0, 1);
        page.Children.Add(Space(split, 12));

        var runInfo = Hint("");
        page.Children.Add(Card(Stack(Space(Label("ЗАБЕГ И АТЛАС"), 6), Space(runInfo, 8),
            Actions(Button("Забеги", () => Navigate("expeditions")), Button("Атлас", () => Navigate("atlas")),
                Button("HUD событий", () => Hud(HudKind.Events)), Button("HUD задач", () => Hud(HudKind.Tasks)),
                Button("HUD боссов", () => Hud(HudKind.Bosses)))), 15));
        _refreshLive = Refresh;
        Refresh();
        return page;

        void AddMetric(string caption, TextBlock number, TextBlock note)
        {
            var card = Card(Stack(Space(Label(caption), 5), Space(number, 4), note), 13);
            card.Margin = new Thickness(0, 0, metrics.Children.Count < 3 ? 9 : 0, 0);
            note.TextWrapping = TextWrapping.NoWrap;
            note.TextTrimming = TextTrimming.CharacterEllipsis;
            metrics.Children.Add(card);
        }

        void Refresh()
        {
            var current = _store.DisplayAnalysis;
            var fight = current.Fight;
            var snapshot = fight.Captured?.Summary;
            var roster = _store.FollowLive ? _store.LiveRoster : null;
            var character = _store.FollowLive ? _store.LiveCharacter : null;
            var rows = PartyCombat.Rows(current, character, roster);
            var own = rows.FirstOrDefault(r => r.IsSelf);
            source.Text = fight.Demo ? "01   /   ДЕМОНСТРАЦИЯ" : snapshot is not null
                ? snapshot.IsActive && _store.FollowLive ? "01   /   В БОЮ" : "01   /   ЗАПИСЬ БОЯ"
                : fight.Id == Guid.Empty ? "01   /   ОЖИДАНИЕ БОЯ" : "01   /   ЛОКАЛЬНЫЙ ОТЧЁТ";
            title.Text = fight.Id == Guid.Empty ? "Оперативный обзор" : fight.Name;
            sub.Text = snapshot?.MostDamageUnsent == true ? "Сервер прислал только часть урона участников."
                : snapshot?.Boss is { MaxHpKnown: true } boss ? $"Цель: {boss.HpFraction:P0} HP   ·   {Clock(TimeSpan.FromSeconds(fight.Duration))}"
                : fight.Id == Guid.Empty ? "ARX автоматически ждёт данные игры и состав группы."
                : $"{Clock(TimeSpan.FromSeconds(fight.Duration))}    /    {rows.Count} участников";
            var self = _store.FollowLive ? _store.LiveIdentity : own?.Name;
            identity.Text = self is null ? "Персонаж ещё не определён" : self + (character?.Confirmed == false ? " · по имени" : "");
            identity.ToolTip = identity.Text;
            personal.Text = own?.Stats is { } ownStats ? Compact(current.Rate(ownStats.Damage)) : "—";
            context.Text = _store.FollowLive && roster is null ? "Наблюдаемые игроки"
                : roster?.Members.Count == 0 ? "Одиночный бой" : "За боевой сегмент";
            numbers.Text = rows.Any(r => r.Stats is not null) ? Compact(current.Dps) : "—";
            damage.Text = rows.Any(r => r.Stats is not null) ? Compact(current.Damage) : "—";
            burst.Text = current.Burst is { } peak ? Compact(peak) : "—";
            chart.Update(current);

            partyStatus.Text = roster switch
            {
                { Members.Count: > 0 } => $"СОСТАВ ГРУППЫ  ·  {rows.Count}   /   DPS",
                { Members.Count: 0 } => "ВНЕ ГРУППЫ   /   DPS",
                _ => _store.FollowLive ? "СОСТАВ ОЖИДАЕТСЯ   /   DPS" : "ОТЧЁТ   /   DPS"
            };
            party.Children.Clear();
            foreach (var member in rows)
            {
                var measured = member.Stats;
                var share = measured is null || current.Damage == 0 ? 0 : (double)measured.Damage / current.Damage;
                var row = new DockPanel();
                var value = Text(measured is null ? "—" : Compact(current.Rate(measured.Damage)), 13, bold: true);
                value.Foreground = member.Color;
                value.Margin = new Thickness(10, 0, 0, 0);
                DockPanel.SetDock(value, Dock.Right);
                row.Children.Add(value);
                var name = Text(PartyCombat.Label(rows, member), 13, bold: true);
                name.TextWrapping = TextWrapping.NoWrap;
                name.TextTrimming = TextTrimming.CharacterEllipsis;
                name.ToolTip = name.Text;
                name.Foreground = member.Color;
                row.Children.Add(name);
                var meter = new ProgressBar { Value = share * 100, Height = 3, Foreground = member.Color,
                    Background = Ui.Brush("Raised"), BorderThickness = new Thickness(0) };
                party.Children.Add(Space(new Border { Child = Stack(Space(row, 6), meter), Padding = new Thickness(8, 5, 8, 7),
                    CornerRadius = new CornerRadius(5), Background = Ui.Brush(member.IsSelf ? "Raised" : "Surface") }, 3));
            }
            if (rows.Count == 0) party.Children.Add(Hint("Имена и DPS появятся после получения данных игры."));
            var pins = _store.AllPins.ToArray();
            runInfo.Text = (_store.Profile.CurrentExpedition is { } expedition
                ? $"Активный забег: {expedition.Name} · {expedition.Segments.Count} завершённых боёв" : "Активный забег не начат") +
                $"     /     Атлас: {pins.Count(p => p.Found)} из {pins.Length} меток";
        }
    }
}
