using System.Globalization;
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
        page.Children.Add(Space(Card(Stack(
            Space(Text("Ближайшие активности", 20, bold: true), 7),
            Space(Hint("Стартовые пресеты Global основаны на расписаниях сообщества. Сверьте якорь UTC с вашим сервером."), 10),
            Actions(Button("Открыть отдельный HUD", () => Hud(HudKind.Events), true)), rows)), 12));
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
        _refreshTime = Refresh;
        Refresh();
        return page;

        void Refresh()
        {
            var now = DateTimeOffset.UtcNow;
            rows.Children.Clear();
            foreach (var item in _store.Profile.Events.OrderBy(e => e.Next(now) ?? DateTimeOffset.MaxValue))
            {
                var due = item.Next(now);
                var info = due is { } next ? $"{Clock(next - now)} · {next.ToLocalTime():dd.MM HH:mm}" : item.Enabled ? "Завершён" : "Выключен";
                var toggle = new CheckBox { IsChecked = item.Enabled, Content = item.Name, MinWidth = 205 };
                var line = Actions(toggle, Text(info, 15, "Mint", true), Button("Изменить", () =>
                {
                    editing = item.Id;
                    caption.Text = $"Настроить · {item.Name}";
                    name.Text = item.Name;
                    anchor.Text = item.AnchorUtc.ToString("yyyy-MM-dd HH:mm");
                    minutes.Text = item.RepeatMinutes.ToString();
                    note.Text = "";
                }));
                toggle.Click += (_, _) =>
                {
                    var index = _store.Profile.Events.FindIndex(e => e.Id == item.Id);
                    if (index < 0) return;
                    _store.Profile.Events[index] = item with { Enabled = toggle.IsChecked == true };
                    _store.Commit();
                    Refresh();
                };
                rows.Children.Add(line);
            }
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
            Refresh();
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
        var page = Heading("Настройки", "Профиль легиона ARX");
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
        page.Children.Add(Card(Stack(Space(Text("Хранение данных", 20, bold: true), 7),
            Space(Hint($"Профиль, карты и отчёты сохраняются на этом компьютере. Папка: {LegionStore.Folder}"), 10),
            Hint(_store.LoadError is null ? "Положение и прозрачность каждого HUD сохраняются отдельно."
                : "Файл профиля повреждён или недоступен. Сделайте копию исходного файла и исправьте его перед следующим запуском."))));
        return page;
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
