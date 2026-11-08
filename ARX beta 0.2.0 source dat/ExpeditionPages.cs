using System.Windows;
using System.Windows.Controls;
using static ArxBeta.Ui;

namespace ArxBeta;

public sealed record ProgressGoal(Guid Id, string Character, string Period, string Name, int Count, int Target);

public sealed partial class Dashboard
{
    private UIElement ExpeditionsPage()
    {
        var profile = _store.Profile;
        var page = Heading("Журнал забегов", "Собирайте несколько боёв в один забег и сохраняйте заявленный билд");
        var caption = Text("", 18, bold: true);
        var totals = Hint("");
        var targets = new ContentControl();
        var current = Stack(Space(caption, 7), Space(totals, 11), targets);
        if (profile.CurrentExpedition is null)
        {
            var name = Input("", 230, 80);
            var build = Input(profile.DeclaredBuild, 230, 100);
            current.Children.Add(Actions(Field("Название забега", name), Field("Заявленный билд", build)));
            current.Children.Add(Actions(Button("Начать забег", () =>
            {
                App.CurrentApp.Live?.FinishSegment();
                profile.DeclaredBuild = build.Text.Trim();
                profile.CurrentExpedition = new Expedition(Guid.NewGuid(), name.Text.Trim() is { Length: > 0 } text ? text : "Новый забег",
                    profile.DeclaredBuild, DateTimeOffset.Now, []);
                _store.Commit();
                RenderPage();
            }, true)));
        }
        else
        {
            current.Children.Add(Actions(Button("Завершить и сохранить", () => Finish(false), true),
                Button("Прервать и сохранить", () => Finish(true))));
        }
        page.Children.Add(Space(Card(current), 13));
        var list = Stack(Space(Text("Последние забеги", 18, bold: true), 8));
        if (profile.Expeditions.Count == 0) list.Children.Add(Hint("Завершённые забеги появятся здесь."));
        foreach (var run in profile.Expeditions)
        {
            var expander = new Expander
            {
                Header = $"{run.Name} · {run.StartedAt.ToLocalTime():dd.MM HH:mm} · {run.Segments.Count} боёв" +
                    (run.Interrupted ? " · прерван" : ""),
                Foreground = Ui.Brush("Text"),
                Content = Stack(Space(Hint(RunSummary(run)), 6), RunTable(run),
                    Actions(Button("Экспорт JSON", () => ExportJson(run, "arx-run.json"))))
            };
            list.Children.Add(Space(expander, 10));
        }
        page.Children.Add(Space(Card(list), 13));

        var comparisons = profile.Expeditions.Where(r => r.Segments.Count > 0).ToArray();
        if (comparisons.Length > 1)
        {
            var labels = comparisons.Select(r => $"{r.StartedAt:dd.MM HH:mm} · {r.Build} · {r.Name}").ToArray();
            var left = Choice(labels, 285);
            var right = Choice(labels, 285);
            right.SelectedIndex = 1;
            var result = Text("", 15, "Gold", true);
            left.SelectionChanged += (_, _) => Compare();
            right.SelectionChanged += (_, _) => Compare();
            Compare();
            page.Children.Add(Card(Stack(Space(Text("Сравнение билдов по забегам", 18, bold: true), 8),
                Actions(Field("Забег A", left), Field("Забег B", right)), Space(result, 6),
                Hint("Сравнение использует средний DPS группы. Результат зависит от целей, состава и длительности."))));

            void Compare()
            {
                if (left.SelectedIndex < 0 || right.SelectedIndex < 0) return;
                var a = RunRate(comparisons[left.SelectedIndex]);
                var b = RunRate(comparisons[right.SelectedIndex]);
                result.Text = $"A: {Compact(a)} DPS  ·  B: {Compact(b)} DPS  ·  Разница {b - a:+#,0;-#,0;0} DPS";
            }
        }
        _refreshLive = Refresh;
        Refresh();
        return page;

        void Refresh()
        {
            var run = profile.CurrentExpedition;
            caption.Text = run?.Name ?? "Новый забег";
            totals.Text = run is null ? "Сегменты записываются при включённом захвате." : RunSummary(run);
            if (run is not null && (int?)targets.Tag != run.Segments.Count)
            {
                targets.Content = RunTable(run);
                targets.Tag = run.Segments.Count;
            }
        }

        void Finish(bool interrupted)
        {
            if (profile.CurrentExpedition is not { } run) return;
            App.CurrentApp.Live?.FinishSegment();
            profile.Expeditions.Insert(0, run with { EndedAt = DateTimeOffset.Now, Interrupted = interrupted });
            if (profile.Expeditions.Count > 25) profile.Expeditions.RemoveAt(25);
            profile.CurrentExpedition = null;
            _store.Commit();
            RenderPage();
        }
    }

    private static string RunSummary(Expedition run) =>
        $"{run.Segments.Count} боёв · {Compact(run.Segments.Sum(s => s.Damage))} урона · {Compact(RunRate(run))} DPS · " +
        $"{Clock(TimeSpan.FromSeconds(run.Segments.Sum(s => s.Seconds)))} боя" +
        (string.IsNullOrWhiteSpace(run.Build) ? "" : $" · {run.Build}");

    private static double RunRate(Expedition run) => run.Segments.Sum(s => s.Damage) / Math.Max(1, run.Segments.Sum(s => s.Seconds));

    private static StackPanel RunTable(Expedition run) =>
        MakeTable(["Цель", "Урон", "Лечение", "Время"], run.Segments,
            s => [s.Target, Compact(s.Damage), Compact(s.Healing), Clock(TimeSpan.FromSeconds(s.Seconds))]);

    private UIElement ProgressPage()
    {
        var page = Heading("Прогресс персонажа", "Личные ежедневные, еженедельные и постоянные цели");
        var profile = _store.Profile;
        var character = Input(profile.ProgressCharacter.Length == 0
            ? _store.LiveIdentity ?? profile.Character : profile.ProgressCharacter, 220, 120);
        var task = Input("", 245, 100);
        var goal = Input("1", 75, 5);
        var scope = Choice(["Ежедневные", "Еженедельные", "Постоянные"], 155);
        var hide = new CheckBox { Content = "Скрывать завершённые" };
        var message = Hint("");
        var rows = new StackPanel();
        page.Children.Add(Space(Card(Stack(Actions(Field("Профиль персонажа", character), Field("Период", scope)),
            Actions(Field("Новая цель", task), Field("Количество", goal)), Actions(Button("Добавить цель", Add, true), hide),
            Space(message, 7), Hint("Сброс периода выполняется вручную после сброса на вашем сервере."))), 12));
        page.Children.Add(Card(Stack(Actions(Button("Сбросить выбранный период", Reset)), rows)));
        character.TextChanged += (_, _) => Refresh();
        character.LostFocus += (_, _) =>
        {
            profile.ProgressCharacter = character.Text.Trim();
            _store.Save();
        };
        scope.SelectionChanged += (_, _) => Refresh();
        hide.Click += (_, _) => Refresh();
        Refresh();
        return page;

        IEnumerable<ProgressGoal> Current() => profile.Goals.Where(t => t.Character == character.Text.Trim() && t.Period == (string)scope.SelectedItem);

        void Add()
        {
            if (string.IsNullOrWhiteSpace(character.Text) || string.IsNullOrWhiteSpace(task.Text) ||
                !int.TryParse(goal.Text, out var count) || count is < 1 or > 10000)
            {
                message.Text = "Укажите персонажа, название и количество от 1 до 10 000.";
                return;
            }
            profile.ProgressCharacter = character.Text.Trim();
            profile.Goals.Add(new ProgressGoal(Guid.NewGuid(), profile.ProgressCharacter, (string)scope.SelectedItem, task.Text.Trim(), 0, count));
            task.Clear();
            message.Text = "";
            _store.Commit();
            Refresh();
        }

        void Reset()
        {
            if (scope.SelectedIndex == 2) return;
            var ids = Current().Select(t => t.Id).ToHashSet();
            for (var i = 0; i < profile.Goals.Count; i++)
                if (ids.Contains(profile.Goals[i].Id)) profile.Goals[i] = profile.Goals[i] with { Count = 0 };
            _store.Commit();
            Refresh();
        }

        void Refresh()
        {
            rows.Children.Clear();
            foreach (var item in Current().Where(g => hide.IsChecked != true || g.Count < g.Target))
                rows.Children.Add(Actions(Text($"{item.Name}   ·   {item.Count} / {item.Target}", 14, "Text", true),
                    Button("−", () => Change(item, -1)), Button("+", () => Change(item, 1))));
            if (rows.Children.Count == 0) rows.Children.Add(Hint("Нет целей в выбранном списке."));
        }

        void Change(ProgressGoal entry, int delta)
        {
            var index = profile.Goals.FindIndex(g => g.Id == entry.Id);
            if (index < 0) return;
            profile.Goals[index] = entry with { Count = Math.Clamp(entry.Count + delta, 0, entry.Target) };
            _store.Commit();
            Refresh();
        }
    }
}
