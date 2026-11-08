using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AionMeter.App.Services;
using AionMeter.Core;
using AionMeter.Core.Capture;
using AionMeter.Core.Combat;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace AionMeter.App.Windows;

public partial class DashboardWindow : Window
{
    private const string MapOrigin = "daevahub.com";
    private static readonly Uri MapUrl = new($"https://{MapOrigin}/map/");
    private static readonly Uri Schedule = new($"https://{MapOrigin}/");
    private readonly MeterService _meter;
    private WebView2? _map;
    private Guid? _fightId;
    private bool _closed;

    public DashboardWindow(MeterService meter)
    {
        _meter = meter;
        InitializeComponent();
        CombatNav.IsChecked = true;
        VersionLabel.Text = $"ATX {_meter.Updates.Current.ToString(3)}";
        ApplyLanguage();
        Closed += (_, _) =>
        {
            _closed = true;
            _map?.Dispose();
        };
    }

    private static string L(string en, string ru) => UiText.Current.Code == "ru" ? ru : en;
    public void ApplyLanguage()
    {
        CombatNav.Content = L("01   Combat", "01   Бой");
        MapNav.Content = L("02   World map", "02   Карта мира");
        TimersNav.Content = L("03   Activities", "03   Активности");
        HistoryButton.Content = L("Fight history", "История боёв");
        SettingsButton.Content = L("Settings", "Настройки");
        LanguageButton.Content = L("EN  /  Русский", "RU  /  English");
        HudButton.Content = L("Toggle combat HUD", "Показать / скрыть HUD");
        Eyebrow.Text = L("COMBAT MONITOR", "БОЕВОЙ МОНИТОР");
        DamageLabel.Text = L("Observed DPS", "Наблюдаемый DPS");
        HealingLabel.Text = L("Observed HPS", "Наблюдаемый HPS");
        SelfLabel.Text = L("Your character", "Ваш персонаж");
        NameColumn.Header = L("Character", "Персонаж");
        DamageColumn.Header = L("Damage", "Урон");
        HealingColumn.Header = L("Healing", "Лечение");
        CritColumn.Header = L("Crit", "Крит");
        BurstColumn.Header = L("Peak 5s", "Пик за 5 с");
        MapTitle.Text = L("Explore Atreia", "Исследуйте Атрею");
        MapHint.Text = L("Interactive map by Daeva Hub. Choose a zone and filter locations on the map.",
            "Интерактивная карта Daeva Hub. Выберите регион и нужные категории меток.");
        BrowserButton.Content = L("Open in browser ↗", "Открыть в браузере ↗");
        TimersTitle.Text = L("Activity timers", "Таймеры активностей");
        TimersHint.Text = L("Set a countdown for your server. ATX sends a Windows notification when it ends.",
            "Задайте отсчёт для своего сервера. ATX отправит уведомление Windows по его окончании.");
        TimerHudButton.Content = L("Timer HUD · Ctrl+Alt+T", "Таймер в HUD · Ctrl+Alt+T");
        BossButton.Content = L("Field bosses", "Полевые боссы");
        ScheduleButton.Content = L("Server schedule ↗", "Расписание сервера ↗");
        AddTimerLabel.Text = L("New countdown · name and minutes", "Новый отсчёт · название и минуты");
        AddTimerButton.Content = L("Start timer", "Запустить");
        RemoveTimerButton.Content = L("Remove selected", "Удалить выбранный");
        TimerError.Text = "";
        Refresh();
    }

    public void Refresh()
    {
        var status = _meter.CaptureStatus;
        var key = status.State;
        StatusText.Text = _meter.DemoRunning || _meter.Settings.Transient ? L("Demo session", "Демонстрация") : key switch
        {
            CaptureState.Capturing => L("Game traffic detected", "Трафик игры обнаружен"),
            CaptureState.WaitingForGame => L("Waiting for Aion 2 Global", "Ожидание Aion 2 Global"),
            CaptureState.Starting => L("Starting capture", "Запуск захвата"),
            CaptureState.Error => L("Capture error · see Settings → Capture", "Ошибка захвата · Настройки → Захват"),
            _ => L("Capture stopped", "Захват остановлен"),
        };
        StatusText.ToolTip = status.Message;
        StatusDot.Fill = (System.Windows.Media.Brush)FindResource(_meter.DemoRunning || key == CaptureState.Capturing
            ? "Green" : key == CaptureState.Error ? "Red" : "Amber");

        if (TimersPage.Visibility == Visibility.Visible) RefreshTimers();
        if (CombatPage.Visibility != Visibility.Visible) return;
        var fight = _meter.Tracker.Snapshot(null, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        _fightId = fight?.Id;
        var roster = _meter.RecentRoster;
        var selfName = _meter.Tracker.SelfName;
        var self = fight?.Combatants.FirstOrDefault(c => c.IsSelf);
        FightTitle.Text = fight?.Title ?? L("Ready for combat", "Готовность к бою");
        FightSubtitle.Text = fight is null
            ? L("Launch the game and enter a zone. Combat statistics appear after the first hits.",
                "Запустите игру и войдите в локацию. Статистика появится после первых попаданий.")
            : $"{TimeSpan.FromMilliseconds(fight.CombatMs):hh\\:mm\\:ss}    ·    {fight.Zone ?? L("Encounter", "Сражение")}";
        DamageValue.Text = fight is null ? "—" : Format.Compact(fight.PartyDps);
        HealingValue.Text = fight is null ? "—" : Format.Compact(Hps(fight.Combatants.Sum(c => c.Healing ?? 0), fight));
        SelfValue.Text = selfName ?? L("Waiting", "Ожидание");
        SelfValue.ToolTip = self is null ? selfName : $"{self.Name} · {Format.Compact(self.Dps)} DPS";
        PartyTitle.Text = roster is null ? L("Observed players", "Наблюдаемые игроки") : L("Your party", "Ваша группа");
        PartyHint.Text = fight?.MostDamageUnsent == true
            ? L("Partial data. The server sent damage for only some players.", "Неполные данные. Сервер передал урон только части игроков.")
            : roster is null && fight is null
                ? L("Join a party in the game to see its roster here.", "Вступите в группу в игре, чтобы увидеть её состав.")
                : L("Double-click a combatant for skills and timeline.", "Двойной щелчок по участнику открывает навыки и график.");

        var rows = (fight?.Combatants ?? []).Where(c => !c.IsUnknownSummons).ToArray();
        var table = roster is null
            ? rows.Select(c => Row(c.Name, c.IsSelf, c, fight)).ToArray()
            : roster.Members.Select(member =>
            {
                var match = rows.FirstOrDefault(c => c.Name == member.Name && c.ServerId == member.ServerId);
                var isSelf = member.Name == selfName && member.ServerId == _meter.Tracker.SelfServerId;
                return Row(member.Name, isSelf, match, fight);
            }).ToArray();
        if (PartyGrid.ItemsSource is PartyRow[] previous && previous.SequenceEqual(table)) return;
        var selected = (PartyGrid.SelectedItem as PartyRow)?.Name;
        PartyGrid.ItemsSource = table;
        PartyGrid.SelectedItem = table.FirstOrDefault(row => row.Name == selected);
    }

    private static double Hps(long healing, EncounterSnapshot fight) => healing / Math.Max(1, fight.CombatMs / 1000.0);

    private static PartyRow Row(string name, bool isSelf, CombatantSnapshot? player, EncounterSnapshot? fight) =>
        new(isSelf ? $"{name} · {L("you", "вы")}" : name, player?.ActorId,
            player is null ? "—" : Format.Compact(player.Dps),
            player is null ? "—" : Format.Compact(player.Damage),
            player?.Healing is { } healing && fight is not null ? Format.Compact(Hps(healing, fight)) : "—",
            player?.Healing is { } amount ? Format.Compact(amount) : "—",
            player is { Hits: > 0 } ? player.CritRate.ToString("P0") : "—",
            player?.BurstDps is { } burst ? Format.Compact(burst) : "—");

    private sealed record PartyRow(string Name, uint? ActorId, string Dps, string Damage, string Hps, string Healing, string Crit, string Burst);
    private sealed record TimerRow(Guid Id, string Display);

    private void SetPage(FrameworkElement page)
    {
        CombatPage.Visibility = page == CombatPage ? Visibility.Visible : Visibility.Collapsed;
        MapPage.Visibility = page == MapPage ? Visibility.Visible : Visibility.Collapsed;
        TimersPage.Visibility = page == TimersPage ? Visibility.Visible : Visibility.Collapsed;
        Refresh();
    }

    private void ShowCombat(object sender, RoutedEventArgs e) => SetPage(CombatPage);
    private void ShowTimers(object sender, RoutedEventArgs e) => SetPage(TimersPage);
    private void ShowHistory(object sender, RoutedEventArgs e) => AppHost.Current.ShowHistory();
    private void ShowSettings(object sender, RoutedEventArgs e) => AppHost.Current.ShowSettings();
    private void ShowBosses(object sender, RoutedEventArgs e) => AppHost.Current.ShowBossTimers();
    private void ToggleLanguage(object sender, RoutedEventArgs e) => AppHost.Current.ToggleLanguage();
    private void ToggleHud(object sender, RoutedEventArgs e) => AppHost.Current.ToggleCombatHud();
    private void ToggleTimerHud(object sender, RoutedEventArgs e) => AppHost.Current.ToggleActivityHud();
    private void OpenSchedule(object sender, RoutedEventArgs e) => OpenExternal(Schedule);
    private void OpenMapBrowser(object sender, RoutedEventArgs e) => OpenExternal(MapUrl);

    private void OpenPlayer(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject origin &&
            ItemsControl.ContainerFromElement(PartyGrid, origin) is DataGridRow)
            OpenSelectedPlayer();
    }

    private void ResizePartyColumns(object sender, SizeChangedEventArgs e)
    {
        if (sender is not DataGrid grid || grid.Columns.Count == 0 || e.NewSize.Width < 200) return;
        var width = (e.NewSize.Width - SystemParameters.VerticalScrollBarWidth) / (grid.Columns.Count + 1);
        foreach (var column in grid.Columns)
            column.Width = new DataGridLength(ReferenceEquals(column, NameColumn) ? width * 2 : width);
    }

    private void PartyKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) OpenSelectedPlayer();
    }

    private void OpenSelectedPlayer()
    {
        if (_fightId is { } fight && PartyGrid.SelectedItem is PartyRow { ActorId: { } actor })
            AppHost.Current.ShowBreakdown(fight, actor);
    }

    private async void ShowMap(object sender, RoutedEventArgs e)
    {
        SetPage(MapPage);
        if (_map is not null) return;
        MapStatus.Text = L("Loading map…", "Загрузка карты…");
        _map = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.FromArgb(14, 20, 33) };
        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(
                userDataFolder: Path.Combine(AppSettings.AppDataDir, "map-browser"));
            if (_closed) return;
            MapHost.Children.Add(_map);
            await _map.EnsureCoreWebView2Async(environment);
            if (_closed) return;
            var core = _map.CoreWebView2;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.IsWebMessageEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
            core.DownloadStarting += (_, args) => args.Cancel = true;
            core.NewWindowRequested += (_, args) => args.Handled = true;
            core.NavigationStarting += (_, args) => args.Cancel = !Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps || !string.Equals(uri.Host, MapOrigin, StringComparison.OrdinalIgnoreCase);
            core.NavigationCompleted += (_, args) =>
            {
                if (args.HttpStatusCode >= 400 ||
                    (!args.IsSuccess && args.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled))
                    MapFailure();
            };
            core.Navigate(MapUrl.AbsoluteUri);
        }
        catch (Exception ex)
        {
            if (_closed) return;
            Log.Error("Map browser initialization failed", ex);
            MapFailure();
        }
    }

    private void MapFailure()
    {
        if (_map is null) return;
        MapHost.Children.Remove(_map);
        _map.Dispose();
        _map = null;
        MapStatus.Text = L("Map unavailable. Open it in your browser or select this tab to retry.",
            "Карта недоступна. Откройте её в браузере или нажмите на вкладку для повтора.");
    }

    private static void OpenExternal(Uri uri)
    {
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Win32Exception ex) { Log.Error("Could not open " + uri.Host, ex); }
    }

    private void RefreshTimers()
    {
        var now = DateTimeOffset.UtcNow;
        var rows = _meter.Settings.Activities.OrderBy(t => t.DueUtc)
            .Select(t => new TimerRow(t.Id, $"{(t.DueUtc <= now ? L("Ended", "Завершён") : t.Countdown(now))}    ·    {t.Name}"))
            .ToArray();
        if (TimerList.ItemsSource is TimerRow[] previous && previous.SequenceEqual(rows)) return;
        var selected = (TimerList.SelectedItem as TimerRow)?.Id;
        TimerList.ItemsSource = rows;
        TimerList.SelectedItem = rows.FirstOrDefault(row => row.Id == selected);
    }

    private void AddTimer(object sender, RoutedEventArgs e)
    {
        var name = TimerName.Text.Trim();
        if (name.Length == 0 || !int.TryParse(TimerMinutes.Text, out var minutes) || minutes is < 1 or > 10080)
        {
            TimerError.Text = L("Enter a name and a duration from 1 to 10,080 minutes.",
                "Введите название и длительность от 1 до 10 080 минут.");
            return;
        }
        _meter.Settings.Activities.Add(new ActivityTimer(Guid.NewGuid(), name, DateTimeOffset.UtcNow.AddMinutes(minutes)));
        _meter.Settings.Save();
        TimerName.Clear();
        TimerError.Text = "";
        RefreshTimers();
    }

    private void RemoveTimer(object sender, RoutedEventArgs e)
    {
        if (TimerList.SelectedItem is not TimerRow row) return;
        _meter.Settings.Activities.RemoveAll(t => t.Id == row.Id);
        _meter.Settings.Save();
        RefreshTimers();
    }
}
