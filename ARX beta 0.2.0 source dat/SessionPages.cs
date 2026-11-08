using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using static ArxBeta.Ui;

namespace ArxBeta;

public sealed partial class Dashboard
{
    private UIElement EventsPage()
    {
        var page = Heading("События", "Таймеры работают по расписанию, даже когда игра закрыта");
        var rows = new StackPanel();
        var clocks = new List<(EventRule Rule, TextBlock Clock)>();
        page.Children.Add(Space(Card(Stack(
            Space(Text("Ближайшие активности", 20, bold: true), 7),
            Space(Hint("Стартовые пресеты Global основаны на расписаниях сообщества. Сверьте якорь UTC с вашим сервером."), 10),
            Actions(Button("Открыть отдельный HUD", () => Hud(HudKind.Events), true)), rows)), 12));
        page.Children.Add(Space(ObservedBossPanel(), 12));
        var name = Input();
        var anchor = Input(DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm"), 155);
        var minutes = Input("60", 92, 6);
        var note = Text("", 12, "Coral");
        Guid? editing = null;
        var caption = Text("Добавить таймер", 19, bold: true);
        page.Children.Add(Card(Stack(Space(caption, 10),
            Space(Hint("Введите дату и время UTC в формате ГГГГ-ММ-ДД ЧЧ:ММ. Интервал 0 создаёт одноразовый таймер."), 8),
            Actions(Field("Название", name), Field("Якорь UTC", anchor), Field("Повтор, мин", minutes)),
            Actions(Button("Сохранить", SaveRule, true), Button("Новый таймер", ResetEditor)), note)));
        _refreshTime = RefreshClocks;
        _refreshShared = Refresh;
        Refresh();
        return page;

        void Refresh()
        {
            var now = DateTimeOffset.UtcNow;
            rows.Children.Clear();
            clocks.Clear();
            foreach (var item in _store.AllEvents.OrderBy(e => e.Next(now) ?? DateTimeOffset.MaxValue))
            {
                var shared = _store.SharedEvents.FirstOrDefault(e => e.Id == item.Id);
                var toggle = new CheckBox { IsChecked = item.Enabled, Content = shared is null ? item.Name : item.Name + " · ARX",
                    MinWidth = 205, IsEnabled = shared?.Enabled != false };
                var label = Text("", 15, "Mint", true);
                clocks.Add((item, label));
                var edit = Button("Изменить", () =>
                {
                    editing = item.Id;
                    caption.Text = $"Настроить · {item.Name}";
                    name.Text = item.Name;
                    anchor.Text = item.AnchorUtc.ToString("yyyy-MM-dd HH:mm");
                    minutes.Text = item.RepeatMinutes.ToString();
                    note.Text = "";
                });
                var line = shared is null ? Actions(toggle, label, edit) : Actions(toggle, label);
                toggle.Click += (_, _) =>
                {
                    if (shared is not null)
                    {
                        if (toggle.IsChecked == true) _store.Profile.TeamDisabled.Remove(item.Id);
                        else _store.Profile.TeamDisabled.Add(item.Id);
                    }
                    else
                    {
                        var index = _store.Profile.Events.FindIndex(e => e.Id == item.Id);
                        if (index < 0) return;
                        _store.Profile.Events[index] = item with { Enabled = toggle.IsChecked == true };
                    }
                    _store.Commit();
                };
                rows.Children.Add(line);
            }
            RefreshClocks();
        }

        void RefreshClocks()
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var (rule, label) in clocks)
                label.Text = rule.Next(now) is { } next ? $"{Clock(next - now)} · {next.ToLocalTime():dd.MM HH:mm}"
                    : rule.Enabled ? "Завершён" : "Выключен";
        }

        void ResetEditor()
        {
            editing = null;
            caption.Text = "Добавить таймер";
            name.Clear();
            anchor.Text = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm");
            minutes.Text = "60";
            note.Text = "";
        }

        void SaveRule()
        {
            if (string.IsNullOrWhiteSpace(name.Text) ||
                !DateTimeOffset.TryParseExact(anchor.Text.Trim(), "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var utc) ||
                utc.Year is < 1970 or > 2100 || !int.TryParse(minutes.Text, out var period) || period is < 0 or > 44640)
            {
                note.Text = "Проверьте название, дату UTC и интервал от 0 до 44 640 минут.";
                return;
            }
            var index = editing is { } id ? _store.Profile.Events.FindIndex(e => e.Id == id) : -1;
            if (index >= 0)
                _store.Profile.Events[index] = _store.Profile.Events[index] with { Name = name.Text.Trim(), AnchorUtc = utc, RepeatMinutes = period };
            else
                _store.Profile.Events.Add(new EventRule(Guid.NewGuid(), name.Text.Trim(), utc, period));
            _store.Commit();
            ResetEditor();
        }
    }

    private UIElement SessionPage()
    {
        var profile = _store.Profile;
        var page = Heading("Игровая сессия", "Задачи и учёт кина для текущего выхода в Атрею");
        var summary = Stack(
            Space(Hint("Новая сессия начнёт отдельный список. Предыдущие записи останутся в профиле и сводке кина."), 7),
            Actions(Button("HUD задач", () => Hud(HudKind.Tasks), true), Button("Новая сессия", () =>
            {
                profile.SessionId = Guid.NewGuid();
                _store.Commit();
            })));
        page.Children.Add(Space(Card(summary), 12));
        page.Children.Add(Space(Card(new TaskPanel(_store)), 12));

        var sessionEntries = profile.Ledger.Where(e => e.SessionId == profile.SessionId).ToArray();
        var spending = -sessionEntries.Where(e => e.Amount < 0).Sum(e => e.Amount);
        var revenue = sessionEntries.Where(e => e.Amount > 0).Sum(e => e.Amount);
        var allTime = profile.Ledger.Sum(e => e.Amount);
        var note = Input("", 240, 100);
        var amount = Input("", 136, 15);
        var error = Text("", 12, "Coral");
        var kina = Stack(Space(Text("Учёт кина", 20, bold: true), 5),
            Space(Hint($"Расходы {Number(spending)}   ·   Доходы {Number(revenue)}   ·   Баланс всех записей {Number(allTime)}"), 9),
            Space(Hint("Записывайте сделки вручную. Для расхода используйте минус, например −1500."), 7),
            Actions(Field("Описание", note), Field("Сумма", amount)),
            Actions(Button("Добавить операцию", AddKina, true)), error);
        if (profile.Ledger.Count > 0)
            kina.Children.Add(MakeTable(["Операция", "Кина", "Дата", "Сессия"],
                profile.Ledger.OrderByDescending(e => e.Date).Take(50),
                e => [e.Note, Number(e.Amount), e.Date.ToLocalTime().ToString("dd.MM HH:mm"), e.SessionId == profile.SessionId ? "Текущая" : "Прошлая"]));
        page.Children.Add(Card(kina));
        return page;

        void AddKina()
        {
            if (string.IsNullOrWhiteSpace(note.Text) ||
                !long.TryParse(amount.Text.Trim().Replace('−', '-'), out var value) ||
                value is 0 or < -1_000_000_000_000 or > 1_000_000_000_000)
            {
                error.Text = "Введите описание и ненулевую целую сумму.";
                return;
            }
            profile.Ledger.Add(new KinaEntry(Guid.NewGuid(), profile.SessionId, DateTimeOffset.Now, note.Text.Trim(), value));
            _store.Commit();
        }
    }

    private UIElement SettingsPage()
    {
        var page = Heading("Настройки", "Профиль ARX");
        var name = Input(_store.Profile.Character, 200, 80);
        var server = Input(_store.Profile.Server, 200, 80);
        var saved = Text("", 12, "Mint");
        page.Children.Add(Space(Card(Stack(
            Actions(Field("Персонаж", name), Field("Регион или сервер", server)),
            Actions(Button("Сохранить профиль", () =>
            {
                _store.Profile.Character = name.Text.Trim();
                _store.Profile.Server = server.Text.Trim();
                _store.Commit();
                saved.Text = _store.LoadError is null ? "Профиль сохранён." : "Профиль доступен только в этой сессии.";
            }, true)), saved)), 12));
        page.Children.Add(Space(Card(Stack(Space(Text("Хранение данных", 20, bold: true), 7),
            Space(Hint($"Профиль, карты и отчёты сохраняются на этом компьютере. Папка: {LegionStore.Folder}"), 10),
            Hint(_store.LoadError is null ? "Положение и прозрачность каждого HUD сохраняются отдельно."
                : "Файл профиля повреждён или недоступен. Сделайте копию исходного файла и исправьте его перед следующим запуском."))), 12));
        var captureStatus = Hint("");
        var devices = App.CurrentApp.Live?.Adapters().ToArray() ?? [];
        var adapter = Choice(new[] { "Автоматически" }.Concat(devices.Select(d => d.Description)), 270);
        adapter.SelectedIndex = Math.Max(0, Array.FindIndex(devices, d => d.Name == _store.Profile.CaptureAdapter) + 1);
        var captureButton = Button("Начать захват", () => _ = App.CurrentApp.ToggleCapture(), true);
        var character = Input(_store.Profile.CombatCharacterHint, 235, 40);
        var detection = Text("", 13, "Coral", true);
        var identityFeedback = Hint("");
        page.Children.Add(Space(Card(Stack(Space(Text("Подключение Aion 2", 20, bold: true), 7),
            Space(Hint("Режим читает локальные сетевые пакеты через Npcap. Игра может присылать неполный состав и урон. Запустите захват перед входом в локацию."), 8),
            Space(captureStatus, 7), Actions(Field("Сетевой адаптер", adapter)),
            Actions(captureButton, Button("Применить адаптер", () => _ = ApplyAdapter()),
                Button("Экспорт диагностики", () => ExportJson(new
                {
                    Version = "0.2.2-beta", Windows = Environment.OSVersion.VersionString,
                    Npcap = LiveCombat.NpcapAvailable, State = App.CurrentApp.Live?.Diagnostic, AdapterCount = devices.Length
                }, "arx-diagnostics.json"))),
            Space(Hint("Диагностика содержит счётчики. Персонажи, адреса, отчёты и пакеты в экспорт не входят."), 10),
            Space(detection, 6),
            Actions(Field("Точное имя для резервного поиска", character), Button("Сохранить имя", () =>
            {
                _store.Profile.CombatCharacterHint = character.Text.Trim();
                _store.Save();
                identityFeedback.Text = "Сохранено в локальном профиле.";
            })),
            Hint("Имя используется, если собственный персонаж ещё не определён по данным игры и среди участников найдено ровно одно совпадение."),
            identityFeedback)), 12));
        _refreshLive = RefreshCapture;
        RefreshCapture();
        var app = App.CurrentApp;
        var density = Choice(["Минимальный", "Средний", "Подробный"], 166);
        density.SelectedIndex = Math.Clamp((int)_store.Profile.CombatHudDensity, 0, 2);
        var opacity = new Slider
        {
            Width = 172, Minimum = 35, Maximum = 100, Value = Math.Clamp(app.CombatOpacity * 100, 35, 100),
            TickFrequency = 5, SmallChange = 1, LargeChange = 5
        };
        var percent = Hint($"{opacity.Value:0}%");
        opacity.ValueChanged += (_, _) => percent.Text = $"{opacity.Value:0}%";
        var font = new Slider { Width = 152, Minimum = 12, Maximum = 22, TickFrequency = 1,
            IsSnapToTickEnabled = true, Value = Math.Clamp(_store.Profile.CombatHudTextSize, 12, 22) };
        var size = Hint($"{font.Value:0} пт");
        font.ValueChanged += (_, _) => size.Text = $"{font.Value:0} пт";
        var selfFirst = new CheckBox { Content = "Мой персонаж сверху", IsChecked = _store.Profile.CombatHudSelfFirst };
        var lockHud = new CheckBox
        {
            Content = "Закрепить и пропускать щелчки",
            IsChecked = _store.Profile.CombatHudLocked,
            IsEnabled = app.ClickThroughAvailable
        };
        var hotkeys = Hint(app.HotkeyStatus);
        var activeHud = Hint("");
        page.Children.Insert(2, Space(Card(Stack(Space(Label("ОКНО ПОВЕРХ ИГРЫ"), 4),
            Space(Text("Боевой HUD", 22, bold: true), 7),
            Space(Hint("Минимальный оставляет цветные имена и DPS без фона. Средний добавляет HPS, подробный показывает долю, крит, бурст и доступное HP цели."), 9),
            Actions(Field("Режим", density), Field("Непрозрачность", opacity), percent, Field("Размер текста", font), size),
            Actions(selfFirst, lockHud),
            Actions(Button("Применить HUD", () =>
            {
                _store.Profile.CombatHudTextSize = (int)font.Value;
                _store.Profile.CombatHudSelfFirst = selfFirst.IsChecked == true;
                app.SetCombatDensity((HudDensity)density.SelectedIndex);
                app.SetCombatOpacity(opacity.Value / 100);
                app.SetCombatLock(lockHud.IsChecked == true);
                RefreshHudStatus();
            }, true), Button("Показать / скрыть HUD", () => Hud(HudKind.Combat)),
                Button("Горячие клавиши", () => Navigate("hotkeys"))),
            Space(activeHud, 6),
            hotkeys)), 12));
        RefreshHudStatus();
        var syncStatus = Text("", 13, "Mint", true);
        var folderLabel = Hint("");
        var feedback = Hint("");
        var publishing = false;
        var publish = new Button { Content = "Опубликовать мои метки и таймеры", Background = Ui.Brush("Mint"), Foreground = Ui.Brush("Base") };
        publish.Click += (_, _) => _ = Publish();
        page.Children.Add(Card(Stack(Space(Text("Синхронизация группы", 20, bold: true), 7),
            Space(Hint("Выберите папку, к которой участники уже имеют доступ. Один редактор публикует общий набор. У остальных ARX проверяет его каждые 15 секунд."), 8),
            Space(Hint("При публикации в папку попадут названия и позиции ваших меток и настройки таймеров. Отметки о находках и остальные данные профиля остаются локальными."), 8),
            Space(folderLabel, 4), Space(syncStatus, 8),
            Actions(Button("Выбрать общую папку", PickFolder), publish, Button("Отключить", Disconnect)),
            feedback, Hint($"{_catalog.Status} · ревизия {_catalog.Revision}"))));
        _refreshTime = () => { UpdateTeam(); RefreshHudStatus(); };
        UpdateTeam();
        return page;

        void RefreshHudStatus()
        {
            var mode = _store.Profile.CombatHudDensity switch { HudDensity.Minimal => "минимальный",
                HudDensity.Detailed => "подробный", _ => "средний" };
            activeHud.Text = $"Применено: {mode} · {app.CombatOpacity:P0} · текст {_store.Profile.CombatHudTextSize}" +
                (_store.Profile.CombatHudLocked ? " · закреплён" : "");
        }

        void RefreshCapture()
        {
            var live = App.CurrentApp.Live;
            captureStatus.Text = live?.Diagnostic ?? "Предпросмотр";
            captureButton.Content = live?.IsRunning == true ? "Остановить захват" : "Начать захват";
            captureButton.IsEnabled = live?.IsStopping != true;
            detection.Text = _store.LiveIdentity is { } identity
                ? $"ВЫ · {identity}" + (_store.LiveCharacter?.Confirmed == false ? " · совпадение по имени" : " · данные игры")
                : "Ваш персонаж ещё не определён";
        }

        async Task ApplyAdapter()
        {
            var live = App.CurrentApp.Live;
            if (live is null || live.IsStopping) return;
            _store.Profile.CaptureAdapter = adapter.SelectedIndex > 0 ? devices[adapter.SelectedIndex - 1].Name : null;
            _store.Save();
            if (!live.IsRunning) return;
            await live.StopAsync();
            live.Start();
        }

        void UpdateTeam()
        {
            folderLabel.Text = string.IsNullOrEmpty(_store.Profile.TeamFolder) ? "Общая папка не выбрана" : _store.Profile.TeamFolder;
            syncStatus.Text = _store.TeamStatus;
            publish.IsEnabled = !string.IsNullOrEmpty(_store.Profile.TeamFolder) && !publishing;
        }

        void PickFolder()
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Общая папка ARX" };
            if (dialog.ShowDialog(this) != true || dialog.FolderName == _store.Profile.TeamFolder) return;
            _store.Profile.TeamFolder = dialog.FolderName;
            _store.Profile.Team = null;
            _store.TeamStatus = "Поиск набора группы…";
            _store.Commit();
            UpdateTeam();
            App.CurrentApp.PollTeam();
        }

        void Disconnect()
        {
            _store.Profile.TeamFolder = "";
            _store.Profile.Team = null;
            _store.TeamStatus = "Общая папка не подключена";
            _store.Commit();
            UpdateTeam();
        }

        async Task Publish()
        {
            var folder = _store.Profile.TeamFolder;
            if (string.IsNullOrEmpty(folder) || publishing) return;
            publishing = true;
            UpdateTeam();
            try
            {
                var snapshot = TeamShare.Prepare(_store.Profile);
                await Task.Run(() => TeamShare.Publish(folder, snapshot));
                feedback.Text = $"Опубликовано: {snapshot.Pins.Length} меток и {snapshot.Events.Length} таймеров.";
                App.CurrentApp.PollTeam();
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                feedback.Text = "Публикация не удалась. " + ex.Message;
            }
            finally { publishing = false; UpdateTeam(); }
        }
    }
}

internal sealed class TaskPanel : StackPanel
{
    private readonly LegionStore _store;
    private readonly StackPanel _rows = new();
    private readonly TextBox _note;

    public TaskPanel(LegionStore store)
    {
        _store = store;
        _note = Input("", 175, 140);
        System.Windows.Automation.AutomationProperties.SetName(_note, "Новая задача");
        _note.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) Add(); };
        Children.Add(Space(Text("Задачи сессии", 17, bold: true), 7));
        Children.Add(Actions(_note, Button("Добавить", Add, true)));
        Children.Add(_rows);
        Refresh();
    }

    private void Add()
    {
        var text = _note.Text.Trim();
        if (text.Length == 0) return;
        _store.Profile.Tasks.Add(new SessionTask(Guid.NewGuid(), _store.Profile.SessionId, text, false));
        _note.Clear();
        _store.Commit();
        Refresh();
    }

    public void Refresh()
    {
        _rows.Children.Clear();
        var tasks = _store.SessionTasks.ToArray();
        if (tasks.Length == 0) _rows.Children.Add(Hint("Добавьте первую задачу."));
        foreach (var task in tasks.OrderBy(t => t.Done))
        {
            var check = new CheckBox { Content = Text(task.Text, 13, task.Done ? "Muted" : "Text"), IsChecked = task.Done };
            check.Margin = new Thickness(3, 3, 0, 7);
            check.Click += (_, _) =>
            {
                var index = _store.Profile.Tasks.FindIndex(t => t.Id == task.Id);
                if (index < 0) return;
                _store.Profile.Tasks[index] = task with { Done = check.IsChecked == true };
                _store.Commit();
                Refresh();
            };
            _rows.Children.Add(check);
        }
    }
}
