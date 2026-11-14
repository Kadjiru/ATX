using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static ArxBeta.Ui;

namespace ArxBeta;

public sealed partial class Dashboard
{
    private UIElement EventsPage()
    {
        var page = Heading(Loc.T("События"), Loc.T("Таймеры работают по расписанию, даже когда игра закрыта"));
        var rows = new StackPanel();
        var clocks = new List<(EventRule Rule, TextBlock Clock)>();
        var riftClock = Text("", 20, "Mint", true);
        var eventNotice = Text("", 13, "Gold", true);
        var favoritesOnly = new CheckBox { Content = Loc.T("Только избранное") };
        var reminderSound = new CheckBox { Content = Loc.T("Звук напоминаний"), IsChecked = _store.Profile.ReminderSound };
        reminderSound.Click += (_, _) =>
        {
            _store.Profile.ReminderSound = reminderSound.IsChecked == true;
            _store.Save();
        };
        var restoreRift = Button(Loc.T("Добавить пресет разлома"), () =>
        {
            if (_store.Rift is not null) return;
            _store.Profile.Events.Add(_catalog.Reference.Presets.FirstOrDefault(rule => rule.Id == EventRule.RiftId) ?? EventRule.DefaultRift);
            _store.Commit();
        });
        page.Children.Add(Space(Card(Stack(
            Space(Text(Loc.T("Пространственный разлом"), 20, bold: true), 5),
            Space(riftClock, 5),
            Space(Hint(Loc.T("Отсчёт по настроенному расписанию. Сверьте время открытия на своём сервере.")), 8),
            Actions(Button(Loc.T("Открыть HUD разлома"), () => Hud(HudKind.Rift), true), restoreRift))), 12));
        page.Children.Add(Space(Card(Stack(
            Space(Text(Loc.T("Ближайшие активности"), 20, bold: true), 7),
            Space(Hint(Loc.T("Стартовые пресеты Global основаны на расписаниях сообщества. Сверьте якорь UTC с вашим сервером.")), 10),
            Actions(Button(Loc.T("Открыть отдельный HUD"), () => Hud(HudKind.Events), true), favoritesOnly, reminderSound),
            eventNotice, rows)), 12));
        page.Children.Add(Space(ObservedBossPanel(), 12));
        var name = Input();
        var anchor = Input(DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm"), 155);
        var minutes = Input("60", 92, 6);
        var note = Text("", 12, "Coral");
        Guid? editing = null;
        var caption = Text(Loc.T("Добавить таймер"), 19, bold: true);
        page.Children.Add(Card(Stack(Space(caption, 10),
            Space(Hint(Loc.T("Введите дату и время UTC в формате ГГГГ-ММ-ДД ЧЧ:ММ. Интервал 0 создаёт одноразовый таймер.")), 8),
            Actions(Field(Loc.T("Название"), name), Field(Loc.T("Якорь UTC"), anchor), Field(Loc.T("Повтор, мин"), minutes)),
            Actions(Button(Loc.T("Сохранить"), SaveRule, true), Button(Loc.T("Новый таймер"), ResetEditor)), note)));
        _refreshTime = RefreshClocks;
        _refreshShared = Refresh;
        favoritesOnly.Click += (_, _) => Refresh();
        Refresh();
        return page;

        void Refresh()
        {
            var now = DateTimeOffset.UtcNow;
            restoreRift.Visibility = _store.Rift is null ? Visibility.Visible : Visibility.Collapsed;
            rows.Children.Clear();
            clocks.Clear();
            foreach (var item in _store.AllEvents
                .Where(e => favoritesOnly.IsChecked != true || _store.Profile.FavoriteEvents.Contains(e.Id))
                .OrderByDescending(e => _store.Profile.FavoriteEvents.Contains(e.Id))
                .ThenBy(e => e.Next(now) ?? DateTimeOffset.MaxValue))
            {
                var shared = _store.SharedEvents.FirstOrDefault(e => e.Id == item.Id);
                var toggle = new CheckBox { IsChecked = item.Enabled, Content = shared is null ? item.DisplayName : item.DisplayName + " · ARX",
                    MinWidth = 205, IsEnabled = shared?.Enabled != false };
                var label = Text("", 15, "Mint", true);
                clocks.Add((item, label));
                var edit = Button(Loc.T("Изменить"), () =>
                {
                    editing = item.Id;
                    caption.Text = Loc.F($"Настроить · {item.DisplayName}");
                    name.Text = item.Name;
                    anchor.Text = item.AnchorUtc.ToString("yyyy-MM-dd HH:mm");
                    minutes.Text = item.RepeatMinutes.ToString();
                    note.Text = "";
                });
                var favorite = new CheckBox { Content = Loc.T("Избранное"), IsChecked = _store.Profile.FavoriteEvents.Contains(item.Id) };
                favorite.Click += (_, _) =>
                {
                    if (favorite.IsChecked == true) _store.Profile.FavoriteEvents.Add(item.Id);
                    else _store.Profile.FavoriteEvents.Remove(item.Id);
                    _store.Commit();
                };
                var reminder = Choice([Loc.T("Без напоминания"), Loc.T("В момент события"),
                    Loc.T("За 5 мин"), Loc.T("За 10 мин")], 165, localize: false);
                reminder.IsEnabled = item.Enabled;
                reminder.SelectedIndex = _store.Profile.EventAlarms.GetValueOrDefault(item.Id, -1) switch
                {
                    0 => 1, 5 => 2, 10 => 3, _ => 0
                };
                reminder.SelectionChanged += (_, _) =>
                {
                    if (reminder.SelectedIndex <= 0) _store.Profile.EventAlarms.Remove(item.Id);
                    else _store.Profile.EventAlarms[item.Id] = reminder.SelectedIndex switch { 2 => 5, 3 => 10, _ => 0 };
                    _store.Save();
                };
                var actions = Actions(favorite, Field(Loc.T("Напоминание"), reminder));
                if (shared is null) actions.Children.Add(edit);
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
                rows.Children.Add(Space(Card(Stack(Actions(toggle, label), actions), 10), 7));
            }
            if (rows.Children.Count == 0) rows.Children.Add(Hint(Loc.T("Нет событий по выбранному фильтру.")));
            RefreshClocks();
        }

        void RefreshClocks()
        {
            var now = DateTimeOffset.UtcNow;
            riftClock.Text = RiftCountdown(_store.Rift, now);
            eventNotice.Text = App.CurrentApp.EventNotice() ?? "";
            reminderSound.IsChecked = _store.Profile.ReminderSound;
            foreach (var (rule, label) in clocks)
                label.Text = rule.Next(now) is { } next ? $"{Clock(next - now)} · {LocalEventTime(next)}"
                    : rule.Enabled ? Loc.T("Завершён") : Loc.T("Выключен");
        }

        void ResetEditor()
        {
            editing = null;
            caption.Text = Loc.T("Добавить таймер");
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
                note.Text = Loc.T("Проверьте название, дату UTC и интервал от 0 до 44 640 минут.");
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
        var page = Heading(Loc.T("Игровая сессия"), Loc.T("Задачи и учёт кина для текущего выхода в Атрею"));
        var summary = Stack(
            Space(Hint(Loc.T("Новая сессия начнёт отдельный список. Предыдущие записи останутся в профиле и сводке кина.")), 7),
            Actions(Button(Loc.T("HUD задач"), () => Hud(HudKind.Tasks), true), Button(Loc.T("Новая сессия"), () =>
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
        var kina = Stack(Space(Text(Loc.T("Учёт кина"), 20, bold: true), 5),
            Space(Hint(Loc.F($"Расходы {Number(spending)}   ·   Доходы {Number(revenue)}   ·   Баланс всех записей {Number(allTime)}")), 9),
            Space(Hint(Loc.T("Записывайте сделки вручную. Для расхода используйте минус, например −1500.")), 7),
            Actions(Field(Loc.T("Описание"), note), Field(Loc.T("Сумма"), amount)),
            Actions(Button(Loc.T("Добавить операцию"), AddKina, true)), error);
        if (profile.Ledger.Count > 0)
            kina.Children.Add(MakeTable([Loc.T("Операция"), Loc.T("Кина"), Loc.T("Дата"), Loc.T("Сессия")],
                profile.Ledger.OrderByDescending(e => e.Date).Take(50),
                e => [e.Note, Number(e.Amount), e.Date.ToLocalTime().ToString("dd.MM HH:mm"), e.SessionId == profile.SessionId ? Loc.T("Текущая") : Loc.T("Прошлая")]));
        page.Children.Add(Card(kina));
        return page;

        void AddKina()
        {
            if (string.IsNullOrWhiteSpace(note.Text) ||
                !long.TryParse(amount.Text.Trim().Replace('−', '-'), out var value) ||
                value is 0 or < -1_000_000_000_000 or > 1_000_000_000_000)
            {
                error.Text = Loc.T("Введите описание и ненулевую целую сумму.");
                return;
            }
            profile.Ledger.Add(new KinaEntry(Guid.NewGuid(), profile.SessionId, DateTimeOffset.Now, note.Text.Trim(), value));
            _store.Commit();
        }
    }

    private UIElement SettingsPage()
    {
        var page = Heading(Loc.T("Настройки"), Loc.T("Профиль ARX"));
        var name = Input(_store.Profile.Character, 200, 80);
        var server = Input(_store.Profile.Server, 200, 80);
        var saved = Text("", 12, "Mint");
        var language = Choice(["Русский", "English"], 165, localize: false);
        language.SelectedIndex = _store.Profile.Language == "en" ? 1 : 0;
        var languageStatus = Hint("");
        page.Children.Add(Space(Card(Stack(
            Space(Text("Язык / Language", 20, bold: true), 7),
            Actions(language, Button(Loc.T("Сохранить язык"), () =>
            {
                var selected = language.SelectedIndex == 1 ? "en" : "ru";
                if (_store.LoadError is not null)
                {
                    languageStatus.Text = Loc.T("Язык не сохранён. Файл профиля недоступен.");
                    return;
                }

                var previous = _store.Profile.Language;
                try
                {
                    _store.Profile.Language = selected;
                    _store.Save();
                    languageStatus.Text = selected == Loc.Language ? Loc.T("Язык сохранён.")
                        : selected == "en" ? "Language saved. Restart ARX to switch to English."
                        : "Язык сохранён. Перезапустите ARX, чтобы переключиться на русский.";
                }
                catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
                {
                    _store.Profile.Language = previous;
                    languageStatus.Text = Loc.T("Язык не сохранён. Файл профиля недоступен.");
                }
            }, true)), languageStatus)), 12));
        page.Children.Add(Space(Card(Stack(
            Actions(Field(Loc.T("Персонаж"), name), Field(Loc.T("Регион или сервер"), server)),
            Actions(Button(Loc.T("Сохранить профиль"), () =>
            {
                _store.Profile.Character = name.Text.Trim();
                _store.Profile.Server = server.Text.Trim();
                _store.Commit();
                saved.Text = _store.LoadError is null ? Loc.T("Профиль сохранён.") : Loc.T("Профиль доступен только в этой сессии.");
            }, true)), saved)), 12));
        page.Children.Add(Space(Card(Stack(Space(Text(Loc.T("Хранение данных"), 20, bold: true), 7),
            Space(Hint(Loc.F($"Профиль, карты и отчёты сохраняются на этом компьютере. Папка: {LegionStore.Folder}")), 10),
            Hint(_store.LoadError is null ? Loc.T("Положение и прозрачность каждого HUD сохраняются отдельно.")
                : Loc.T("Файл профиля повреждён или недоступен. Сделайте копию исходного файла и исправьте его перед следующим запуском.")))), 12));
        var captureStatus = Hint("");
        var devices = App.CurrentApp.Live?.Adapters().ToArray() ?? [];
        var adapter = Choice(new[] { Loc.T("Автоматически") }.Concat(devices.Select(d => d.Description)), 270, localize: false);
        adapter.SelectedIndex = Math.Max(0, Array.FindIndex(devices, d => d.Name == _store.Profile.CaptureAdapter) + 1);
        var character = Input(_store.Profile.CombatCharacterHint, 235, 40);
        var detection = Text("", 13, "Coral", true);
        var identityFeedback = Hint("");
        page.Children.Add(Space(Card(Stack(Space(Text(Loc.T("Подключение Aion 2"), 20, bold: true), 7),
            Space(Hint(Loc.T("Считывание через Npcap включается при запуске ARX. Для определения персонажа лучше открыть ARX перед входом в игру или сменить локацию.")), 8),
            Space(captureStatus, 7), Actions(Field(Loc.T("Сетевой адаптер"), adapter)),
            Actions(Button(Loc.T("Применить адаптер"), () => _ = ApplyAdapter()),
                Button(Loc.T("Мастер подключения"), App.CurrentApp.ShowConnectionGuide),
                Button(Loc.T("Экспорт диагностики"), () => ExportJson(new
                {
                    Version = AppUpdates.CurrentLabel, Windows = Environment.OSVersion.VersionString,
                    Npcap = LiveCombat.NpcapAvailable, State = App.CurrentApp.Live?.Diagnostic, AdapterCount = devices.Length
                }, "arx-diagnostics.json"))),
            Space(Hint(Loc.T("Диагностика содержит счётчики. Персонажи, адреса, отчёты и пакеты в экспорт не входят.")), 10),
            Space(detection, 6),
            Actions(Field(Loc.T("Точное имя для резервного поиска"), character), Button(Loc.T("Сохранить имя"), () =>
            {
                _store.Profile.CombatCharacterHint = character.Text.Trim();
                _store.Save();
                identityFeedback.Text = Loc.T("Сохранено в локальном профиле.");
            })),
            Hint(Loc.T("Имя используется, если собственный персонаж ещё не определён по данным игры и среди участников найдено ровно одно совпадение.")),
            identityFeedback)), 12));
        _refreshLive = RefreshCapture;
        RefreshCapture();
        var app = App.CurrentApp;
        var density = Choice([Loc.T("Минимальный"), Loc.T("Средний"), Loc.T("Подробный")], 166);
        density.SelectedIndex = Math.Clamp((int)_store.Profile.CombatHudDensity, 0, 2);
        var opacity = new Slider
        {
            Width = 152, Minimum = 0, Maximum = 100, Value = Math.Clamp(app.CombatOpacity * 100, 0, 100),
            TickFrequency = 5, SmallChange = 1, LargeChange = 5
        };
        var percent = Hint($"{opacity.Value:0}%");
        opacity.ValueChanged += (_, _) => percent.Text = $"{opacity.Value:0}%";
        var font = new Slider { Width = 152, Minimum = 12, Maximum = 22, TickFrequency = 1,
            IsSnapToTickEnabled = true, Value = Math.Clamp(_store.Profile.CombatHudTextSize, 12, 22) };
        var size = Hint(Loc.F($"{font.Value:0} пт"));
        font.ValueChanged += (_, _) => size.Text = Loc.F($"{font.Value:0} пт");
        var scope = Choice([Loc.T("Только мой персонаж"), Loc.T("Я и моя группа")], 175);
        scope.SelectedIndex = _store.Profile.CombatHudScope == CombatHudScope.Self ? 0 : 1;
        var lockHud = new CheckBox
        {
            Content = Loc.T("Закрепить и пропускать щелчки"),
            IsChecked = _store.Profile.CombatHudLocked,
            IsEnabled = app.ClickThroughAvailable
        };
        var hotkeys = Hint(app.HotkeyStatus);
        var activeHud = Hint("");
        var activePreset = app.ActiveHudPreset;
        var presetStatus = Hint("");
        var presetOne = Button(Loc.T("Раскладка 1"), () => app.SwitchHudPreset(1));
        var presetTwo = Button(Loc.T("Раскладка 2"), () => app.SwitchHudPreset(2));
        page.Children.Insert(3, Space(Card(Stack(Space(Label(Loc.T("ОКНО ПОВЕРХ ИГРЫ")), 4),
            Space(Text(Loc.T("Боевой HUD"), 22, bold: true), 7),
            Space(Hint(Loc.T("Пока игра не прислала состав, HUD показывает только вашего персонажа. Прозрачность меняет фон и обводку. В минимальном режиме фон всегда прозрачный.")), 9),
            Actions(Field(Loc.T("Кого показывать"), scope), Field(Loc.T("Режим"), density)),
            Actions(Field(Loc.T("Непрозрачность фона"), opacity), percent, Field(Loc.T("Размер текста"), font), size),
            Actions(lockHud),
            Actions(Button(Loc.T("Применить HUD"), () =>
            {
                _store.Profile.CombatHudTextSize = (int)font.Value;
                app.SetCombatScope(scope.SelectedIndex == 0 ? CombatHudScope.Self : CombatHudScope.Party);
                app.SetCombatDensity((HudDensity)density.SelectedIndex);
                app.SetCombatOpacity(opacity.Value / 100);
                app.SetCombatLock(lockHud.IsChecked == true);
                RefreshHudStatus();
            }, true), Button(Loc.T("Показать / скрыть HUD"), () => Hud(HudKind.Combat)),
                Button(Loc.T("Горячие клавиши"), () => Navigate("hotkeys"))),
            Space(activeHud, 6),
            hotkeys)), 12));
        page.Children.Insert(4, Space(Card(Stack(
            Space(Text(Loc.T("Раскладки HUD"), 20, bold: true), 6),
            Space(Hint(Loc.T("Расставьте нужные окна и сохраните раскладку. ARX запоминает изменения при переключении и выходе.")), 7),
            Actions(presetOne, presetTwo, Button(Loc.T("Сохранить текущую раскладку"), () =>
            {
                app.SaveHudPreset();
                RefreshHudStatus();
            }, true)), presetStatus)), 12));
        RefreshHudStatus();
        var updateStatus = Text("", 13, "Mint", true);
        var channels = Choice([Loc.T("Стабильный (Stable)"), Loc.T("Бета (Beta)")], 215, localize: false);
        channels.SelectedIndex = app.SelectedUpdateChannel == UpdateChannel.Beta ? 1 : 0;
        var betaWarning = Text(Loc.T("Бета-версия может содержать ошибки. Для повседневной игры можно вернуться на Stable."), 13, "Coral");
        var channelError = Text("", 13, "Coral");
        var checkUpdate = Button(Loc.T("Проверить сейчас"), () => _ = app.CheckAppUpdatesAsync());
        var updateAction = Button(Loc.T("Обновление"), () => _ = app.OpenUpdateAsync(), true);
        channels.SelectionChanged += (_, _) =>
        {
            var selected = channels.SelectedIndex == 1 ? UpdateChannel.Beta : UpdateChannel.Stable;
            if (selected == app.SelectedUpdateChannel) return;
            try
            {
                if (_store.LoadError is not null) throw new IOException();
                app.ChangeUpdateChannel(selected);
                channelError.Text = "";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                channels.SelectedIndex = app.SelectedUpdateChannel == UpdateChannel.Beta ? 1 : 0;
                channelError.Text = Loc.T("Не удалось сохранить канал обновлений.");
            }
            RefreshUpdate();
        };
        var autoUpdate = new CheckBox { Content = Loc.T("Проверять обновления автоматически"), IsChecked = _store.Profile.AutoUpdate };
        autoUpdate.Click += (_, _) =>
        {
            _store.Profile.AutoUpdate = autoUpdate.IsChecked == true;
            _store.Save();
            if (_store.Profile.AutoUpdate) _ = app.CheckAppUpdatesAsync();
        };
        page.Children.Insert(4, Space(Card(Stack(Space(Text(Loc.T("Обновление ARX"), 20, bold: true), 6),
            Space(Hint(Loc.F($"Версия {AppUpdates.CurrentLabel}. После скачивания установщика ARX спросит, когда его запустить.")), 8),
            Space(Field(Loc.T("Канал обновлений"), channels), 6), betaWarning, channelError,
            Space(autoUpdate, 8), Actions(checkUpdate, updateAction, Button(Loc.T("Что нового"), app.ShowChangelog)), updateStatus)), 12));
        RefreshUpdate();
        var syncStatus = Text("", 13, "Mint", true);
        var folderLabel = Hint("");
        var feedback = Hint("");
        var publishing = false;
        var publish = new Button { Content = Loc.T("Опубликовать мои метки и таймеры") }
            .UseBrush(Control.BackgroundProperty, "Mint").UseBrush(Control.ForegroundProperty, "Base");
        publish.Click += (_, _) => _ = Publish();
        page.Children.Add(Card(Stack(Space(Text(Loc.T("Синхронизация группы"), 20, bold: true), 7),
            Space(Hint(Loc.T("Выберите папку, к которой участники уже имеют доступ. Один редактор публикует общий набор. У остальных ARX проверяет его каждые 15 секунд.")), 8),
            Space(Hint(Loc.T("При публикации в папку попадут названия и позиции ваших меток и настройки таймеров. Отметки о находках и остальные данные профиля остаются локальными.")), 8),
            Space(folderLabel, 4), Space(syncStatus, 8),
            Actions(Button(Loc.T("Выбрать общую папку"), PickFolder), publish, Button(Loc.T("Отключить"), Disconnect)),
            feedback, Hint(Loc.F($"{_catalog.Status} · ревизия {_catalog.Revision}")))));
        _refreshTime = () => { UpdateTeam(); RefreshHudStatus(); RefreshUpdate(); };
        UpdateTeam();
        page.Children.Insert(2, Space(AppearanceSettings(), 12));
        return page;

        void RefreshUpdate()
        {
            var updates = app.Updates;
            betaWarning.Visibility = AppUpdates.IsBetaBuild || app.SelectedUpdateChannel == UpdateChannel.Beta
                ? Visibility.Visible : Visibility.Collapsed;
            checkUpdate.IsEnabled = updates is not null;
            updateAction.Visibility = updates?.Ready == true || updates?.ReleasePage is not null
                ? Visibility.Visible : Visibility.Collapsed;
            updateAction.Content = updates?.Ready == true ? Loc.T("Открыть окно установки") : Loc.T("Открыть релиз");
            updateStatus.Text = app.UpdateApproved ? Loc.T("Обновление установится при закрытии ARX.")
                : updates?.Status ?? Loc.T("Предпросмотр");
        }

        void RefreshHudStatus()
        {
            var selected = app.ActiveHudPreset;
            if (activePreset != selected)
            {
                activePreset = selected;
                density.SelectedIndex = Math.Clamp((int)_store.Profile.CombatHudDensity, 0, 2);
                opacity.Value = Math.Clamp(app.CombatOpacity * 100, 0, 100);
            }
            presetOne.IsEnabled = selected != 1;
            presetTwo.IsEnabled = selected != 2;
            presetStatus.Text = _store.Profile.ActiveHudPreset is null
                ? Loc.T("Сохраните первую раскладку, чтобы восстанавливать окна при запуске.")
                : Loc.F($"Активна раскладка {selected} · Ctrl+Alt+4");
            var mode = _store.Profile.CombatHudDensity switch { HudDensity.Minimal => Loc.T("минимальный"),
                HudDensity.Detailed => Loc.T("подробный"), _ => Loc.T("средний") };
            var audience = _store.Profile.CombatHudScope == CombatHudScope.Self ? Loc.T("только вы") : Loc.T("вы и ваша группа");
            activeHud.Text = Loc.F($"Применено: {mode} · {audience} · фон {app.CombatOpacity:P0} · текст {_store.Profile.CombatHudTextSize}") +
                (_store.Profile.CombatHudLocked ? Loc.T(" · закреплён") : "");
        }

        void RefreshCapture()
        {
            var live = App.CurrentApp.Live;
            captureStatus.Text = live?.Diagnostic ?? Loc.T("Предпросмотр");
            detection.Text = _store.LiveIdentity is { } identity
                ? Loc.F($"ВЫ · {identity}") + (_store.LiveCharacter?.Confirmed == false ? Loc.T(" · совпадение по имени") : Loc.T(" · данные игры"))
                : Loc.T("Ваш персонаж ещё не определён");
        }

        async Task ApplyAdapter()
        {
            var live = App.CurrentApp.Live;
            if (live is null || live.IsStopping) return;
            _store.Profile.CaptureAdapter = adapter.SelectedIndex > 0 ? devices[adapter.SelectedIndex - 1].Name : null;
            _store.Save();
            await live.StopAsync();
            if (!App.CurrentApp.IsClosing) live.Start();
        }

        void UpdateTeam()
        {
            folderLabel.Text = string.IsNullOrEmpty(_store.Profile.TeamFolder) ? Loc.T("Общая папка не выбрана") : _store.Profile.TeamFolder;
            syncStatus.Text = Loc.T(_store.TeamStatus);
            publish.IsEnabled = !string.IsNullOrEmpty(_store.Profile.TeamFolder) && !publishing;
        }

        void PickFolder()
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = Loc.T("Общая папка ARX") };
            if (dialog.ShowDialog(this) != true || dialog.FolderName == _store.Profile.TeamFolder) return;
            _store.Profile.TeamFolder = dialog.FolderName;
            _store.Profile.Team = null;
            _store.TeamStatus = Loc.T("Поиск набора группы…");
            _store.Commit();
            UpdateTeam();
            App.CurrentApp.PollTeam();
        }

        void Disconnect()
        {
            _store.Profile.TeamFolder = "";
            _store.Profile.Team = null;
            _store.TeamStatus = Loc.T("Общая папка не подключена");
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
                feedback.Text = Loc.F($"Опубликовано: {snapshot.Pins.Length} меток и {snapshot.Events.Length} таймеров.");
                App.CurrentApp.PollTeam();
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                feedback.Text = Loc.T("Публикация не удалась. ") + ex.Message;
            }
            finally { publishing = false; UpdateTeam(); }
        }
    }

    private Border AppearanceSettings()
    {
        var themes = ThemePalette.Options.ToArray();
        var profile = _store.Profile;
        var client = Choice(themes.Select(theme => theme.Label), 180, localize: false);
        var hud = Choice([Loc.T("Как в приложении"), .. themes.Select(theme => theme.Label)], 180, localize: false);
        client.SelectedIndex = Array.IndexOf(themes, ThemePalette.ForApp(profile));
        hud.SelectedIndex = Array.FindIndex(themes, theme => theme.Id == profile.HudTheme) + 1;
        var clientPreview = new ContentControl();
        var hudPreview = new ContentControl();
        var status = Hint("");
        var pickers = Actions(
            Space(Stack(Field(Loc.T("Тема приложения"), client), clientPreview), 7),
            Space(Stack(Field(Loc.T("Тема HUD"), hud), hudPreview), 7));
        client.SelectionChanged += (_, _) => Preview();
        hud.SelectionChanged += (_, _) => Preview();
        Preview();
        return Card(Stack(Space(Text(Loc.T("Оформление"), 20, bold: true), 4),
            Space(Hint(Loc.T("Выберите палитры отдельно. Тема HUD применяется ко всем окнам поверх игры.")), 10),
            pickers, Actions(Button(Loc.T("Применить темы"), Apply, true)), status));

        void Preview()
        {
            var selected = themes[Math.Max(0, client.SelectedIndex)];
            clientPreview.Content = ThemeSample(selected);
            hudPreview.Content = ThemeSample(hud.SelectedIndex > 0 ? themes[hud.SelectedIndex - 1] : selected);
            status.Text = "";
        }

        void Apply()
        {
            var error = Loc.T("Темы не сохранены. Файл профиля недоступен.");
            if (_store.LoadError is not null)
            {
                status.Text = error;
                return;
            }

            var previous = (profile.AppTheme, profile.HudTheme);
            profile.AppTheme = themes[Math.Max(0, client.SelectedIndex)].Id;
            profile.HudTheme = hud.SelectedIndex > 0 ? themes[hud.SelectedIndex - 1].Id : ThemePalette.FollowApp;
            try { _store.Save(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                (profile.AppTheme, profile.HudTheme) = previous;
                status.Text = error;
                return;
            }

            App.CurrentApp.ApplyThemes();
            status.Text = Loc.T("Темы применены и сохранены.");
        }
    }

    private static Border ThemeSample(ThemePalette theme)
    {
        var text = Text(theme.Label, 12, bold: true);
        text.Foreground = new SolidColorBrush(ThemePalette.FromRgb(theme.Text));
        var accent = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(5),
            Margin = new Thickness(0, 0, 7, 0), Background = new SolidColorBrush(ThemePalette.FromRgb(theme.Accent)) };
        var row = new DockPanel();
        DockPanel.SetDock(accent, Dock.Left);
        row.Children.Add(accent);
        row.Children.Add(text);
        return new Border
        {
            Width = 180, Padding = new Thickness(8), Margin = new Thickness(0, 5, 0, 0), CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(ThemePalette.FromRgb(theme.Line)),
            Background = new SolidColorBrush(ThemePalette.FromRgb(theme.Raised)), Child = row
        };
    }
}

internal sealed class TaskPanel : StackPanel
{
    private readonly LegionStore _store;
    private readonly StackPanel _rows = new();
    private readonly TextBox _note;
    private readonly Button _add;
    private readonly bool _compact;
    private double _surfaceOpacity = 1;

    public TaskPanel(LegionStore store, bool compact = false)
    {
        _store = store;
        _compact = compact;
        _note = Input("", 175, 140);
        System.Windows.Automation.AutomationProperties.SetName(_note, Loc.T("Новая задача"));
        _note.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) Add(); };
        if (!compact) Children.Add(Space(Text(Loc.T("Задачи сессии"), 17, bold: true), 7));
        _add = Button(Loc.T("Добавить"), Add, !compact);
        Children.Add(Actions(_note, _add));
        Children.Add(_rows);
        Refresh();
    }

    public void SetSurfaceOpacity(double opacity)
    {
        _surfaceOpacity = opacity;
        _note.Background = HudWindow.Faded(Ui.Brush("Base", this), opacity);
        _note.BorderBrush = HudWindow.Faded(Ui.Brush("Line", this), opacity);
        _add.Background = HudWindow.Faded(Ui.Brush("Raised", this), opacity);
        _add.BorderBrush = HudWindow.Faded(Ui.Brush("Mint", this), opacity * .5);
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
        if (tasks.Length == 0) _rows.Children.Add(_compact ? HudWindow.Ink(Hint(Loc.T("Добавьте первую задачу."))) : Hint(Loc.T("Добавьте первую задачу.")));
        foreach (var task in tasks.OrderBy(t => t.Done))
        {
            var label = Text(task.Text, 13, task.Done ? "Muted" : "Text");
            var check = new CheckBox { Content = _compact ? HudWindow.Ink(label) : label, IsChecked = task.Done };
            if (!_compact) check.Margin = new Thickness(3, 3, 0, 7);
            check.Click += (_, _) =>
            {
                var index = _store.Profile.Tasks.FindIndex(t => t.Id == task.Id);
                if (index < 0) return;
                _store.Profile.Tasks[index] = task with { Done = check.IsChecked == true };
                _store.Commit();
                Refresh();
            };
            _rows.Children.Add(_compact ? Space(HudWindow.Plate(check, Ui.Brush("Raised", this), Ui.Brush("Mint", this), _surfaceOpacity), 5) : check);
        }
    }
}
