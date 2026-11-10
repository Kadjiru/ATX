using System.Windows;
using System.Windows.Controls;
using static ArxBeta.Ui;

namespace ArxBeta;

public sealed record ProgressGoal(Guid Id, string Character, string Period, string Name, int Count, int Target,
    string? ActivityId = null, int ServerId = 0, string? OwnerName = null);

public sealed partial class Dashboard
{
    private UIElement ExpeditionsPage()
    {
        var profile = _store.Profile;
        var page = Heading(Loc.T("Журнал забегов"), Loc.T("Умные экспедиции и разбор отдельных боёв"));
        var automatic = new CheckBox { Content = Loc.T("Записывать поддерживаемые экспедиции автоматически"), IsChecked = profile.AutoExpeditions };
        var openRunId = profile.CurrentExpedition?.Id;
        var latestRunId = profile.Expeditions.FirstOrDefault()?.Id;
        automatic.Click += (_, _) =>
        {
            profile.AutoExpeditions = automatic.IsChecked == true;
            if (!profile.AutoExpeditions && profile.CurrentExpedition?.Automatic == true) Finish(true);
            else _store.Save();
        };
        page.Children.Add(Space(Card(Stack(Space(automatic, 7),
            Hint(Loc.T("ARX начинает запись после первого полученного боя вашего персонажа в распознанной экспедиции. Победа засчитывается по финальному боссу.")),
            Space(Hint(string.Join(" · ", ExpeditionActivity.All.Select(activity => activity.ToString()))), 0)), 13), 12));
        var caption = Text("", 18, bold: true);
        var totals = Hint("");
        var targets = new ContentControl();
        var current = Stack(Space(caption, 7), Space(totals, 11), targets);
        if (profile.CurrentExpedition is null)
        {
            var name = Input("", 230, 80);
            var build = Input(profile.DeclaredBuild, 230, 100);
            current.Children.Add(Actions(Field(Loc.T("Название забега"), name), Field(Loc.T("Заявленный билд"), build)));
            current.Children.Add(Actions(Button(Loc.T("Начать забег"), () =>
            {
                App.CurrentApp.Live?.FinishSegment();
                if (profile.CurrentExpedition is not null) return;
                profile.DeclaredBuild = build.Text.Trim();
                profile.CurrentExpedition = new Expedition(Guid.NewGuid(), name.Text.Trim() is { Length: > 0 } text ? text : Loc.T("Новый забег"),
                    profile.DeclaredBuild, DateTimeOffset.Now, []);
                _store.Commit();
            }, true)));
        }
        else
        {
            current.Children.Add(Actions(Button(Loc.T("Завершить и сохранить"), () => Finish(false), true),
                Button(Loc.T("Прервать и сохранить"), () => Finish(true))));
        }
        page.Children.Add(Space(Card(current), 13));
        var list = Stack(Space(Text(Loc.T("Последние забеги"), 18, bold: true), 8));
        if (profile.Expeditions.Count == 0) list.Children.Add(Hint(Loc.T("Завершённые забеги появятся здесь.")));
        foreach (var run in profile.Expeditions)
        {
            var expander = new Expander
            {
                Header = Loc.F($"{run.Name} · {run.StartedAt.ToLocalTime():dd.MM HH:mm} · {run.Segments.Count} боёв") +
                    (run.ConfirmedWin ? Loc.T(" · победа подтверждена") : run.Interrupted ? Loc.T(" · прерван") : ""),
                Foreground = Ui.Brush("Text"),
                Content = Stack(Space(Hint(RunSummary(run)), 6), RunTable(run),
                    Actions(Button(Loc.T("Экспорт JSON"), () => ExportJson(run, "arx-run.json"))))
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
            page.Children.Add(Card(Stack(Space(Text(Loc.T("Сравнение билдов по забегам"), 18, bold: true), 8),
                Actions(Field(Loc.T("Забег A"), left), Field(Loc.T("Забег B"), right)), Space(result, 6),
                Hint(Loc.T("Сравнение использует средний DPS группы. Результат зависит от целей, состава и длительности.")))));

            void Compare()
            {
                if (left.SelectedIndex < 0 || right.SelectedIndex < 0) return;
                var a = RunRate(comparisons[left.SelectedIndex]);
                var b = RunRate(comparisons[right.SelectedIndex]);
                result.Text = Loc.F($"A: {Compact(a)} DPS  ·  B: {Compact(b)} DPS  ·  Разница {b - a:+#,0;-#,0;0} DPS");
            }
        }
        _refreshLive = Refresh;
        _refreshShared = () =>
        {
            if (openRunId != profile.CurrentExpedition?.Id || latestRunId != profile.Expeditions.FirstOrDefault()?.Id)
                RenderPage();
            else Refresh();
        };
        Refresh();
        return page;

        void Refresh()
        {
            var run = profile.CurrentExpedition;
            caption.Text = run?.Name ?? Loc.T("Новый забег");
            totals.Text = run is null ? Loc.T("Сегменты записываются при включённом захвате.") : RunSummary(run);
            if (run is not null && (int?)targets.Tag != run.Segments.Count)
            {
                targets.Content = RunTable(run);
                targets.Tag = run.Segments.Count;
            }
        }

        void Finish(bool interrupted)
        {
            if (profile.CurrentExpedition is null) return;
            if (App.CurrentApp.Live is { } live) live.EndExpedition(interrupted);
            else _store.EndExpedition(interrupted);
        }
    }

    private static string RunSummary(Expedition run) =>
        Loc.F($"{run.Segments.Count} боёв · {Compact(run.Segments.Sum(s => s.Damage))} урона · {Compact(RunRate(run))} DPS · ") +
        Loc.F($"{Clock(TimeSpan.FromSeconds(run.Segments.Sum(s => s.Seconds)))} боя") +
        (string.IsNullOrWhiteSpace(run.Build) ? "" : $" · {run.Build}");

    private static double RunRate(Expedition run) => run.Segments.Sum(s => s.Damage) / Math.Max(1, run.Segments.Sum(s => s.Seconds));

    private StackPanel RunTable(Expedition run)
    {
        var summary = MakeTable([Loc.T("Цель"), Loc.T("Итог"), Loc.T("Урон"), "DPS", Loc.T("Время")], run.Segments,
            s => [s.Target, OutcomeLabel(s.Outcome), Compact(s.Damage), Compact(s.Damage / Math.Max(1, s.Seconds)),
                Clock(TimeSpan.FromSeconds(s.Seconds))]);
        var bosses = run.Segments.Select(segment => (segment, report: segment.BossReport ??
            _store.Profile.History.FirstOrDefault(fight => fight.Id == segment.FightId && fight.Captured?.Summary.Boss is not null)))
            .Where(item => item.report is not null).ToArray();
        if (bosses.Length == 0) return summary;

        var buttons = Stack(Space(Label(Loc.T("БОССЫ И ПОПЫТКИ")), 6));
        foreach (var (segment, report) in bosses)
        {
            var open = Button($"{segment.Target} · {OutcomeLabel(segment.Outcome ?? report!.Captured?.Summary.Reason)}  ↗", () =>
            {
                _actor = null;
                _player = null;
                _store.SetFight(report!, false);
                Navigate("analysis");
            });
            open.HorizontalContentAlignment = HorizontalAlignment.Left;
            buttons.Children.Add(Space(open, 5));
        }
        return Stack(Space(summary, 10), buttons);
    }

    private static string OutcomeLabel(AionMeter.Core.Combat.EncounterEndReason? outcome) => outcome switch
    {
        AionMeter.Core.Combat.EncounterEndReason.Kill => Loc.T("Победа"),
        AionMeter.Core.Combat.EncounterEndReason.Wipe => Loc.T("Сброс босса"),
        AionMeter.Core.Combat.EncounterEndReason.Manual => Loc.T("Вручную"),
        AionMeter.Core.Combat.EncounterEndReason.ZoneChange => Loc.T("Выход из зоны"),
        AionMeter.Core.Combat.EncounterEndReason.Idle => Loc.T("Бой затих"),
        _ => "—"
    };

    private UIElement ProgressPage()
    {
        var page = Heading(Loc.T("Прогресс персонажа"), Loc.T("Личные ежедневные, еженедельные и постоянные цели"));
        var profile = _store.Profile;
        var character = Input(profile.ProgressCharacter.Length == 0
            ? _store.LiveIdentity ?? profile.Character : profile.ProgressCharacter, 220, 120);
        var task = Input("", 245, 100);
        var goal = Input("1", 75, 5);
        var scope = Choice(["Ежедневные", "Еженедельные", "Постоянные"], 155);
        var activities = new ComboBox
        {
            ItemsSource = new object[] { Loc.T("Ручная цель") }.Concat(ExpeditionActivity.All), SelectedIndex = 0,
            MinHeight = 32, Width = 216
        };
        var hide = new CheckBox { Content = Loc.T("Скрывать завершённые") };
        var message = Hint("");
        var rows = new StackPanel();
        page.Children.Add(Space(Card(Stack(Actions(Field(Loc.T("Профиль персонажа"), character), Field(Loc.T("Период"), scope)),
            Actions(Field(Loc.T("Новая цель"), task), Field(Loc.T("Количество"), goal), Field(Loc.T("Отслеживание"), activities)),
            Actions(Button(Loc.T("Добавить цель"), Add, true), Button(Loc.T("Текущий персонаж"), () =>
                character.Text = CharacterLabel() ?? profile.Character), hide),
            Space(message, 7), Hint(Loc.T("Сброс периода выполняется вручную после сброса на вашем сервере.")))), 12));
        page.Children.Add(Card(Stack(Actions(Button(Loc.T("Сбросить выбранный период"), Reset)), rows)));
        character.TextChanged += (_, _) => Refresh();
        character.LostFocus += (_, _) =>
        {
            profile.ProgressCharacter = character.Text.Trim();
            _store.Save();
        };
        scope.SelectionChanged += (_, _) => Refresh();
        hide.Click += (_, _) => Refresh();
        _refreshShared = Refresh;
        Refresh();
        return page;

        string? CharacterLabel() => _store.LiveCharacter is { ServerId: > 0 } player
            ? _store.LiveIdentity ?? $"{player.Name} · S{player.ServerId}" : null;

        IEnumerable<ProgressGoal> Current() => profile.Goals.Where(t =>
            string.Equals(t.Character, character.Text.Trim(), StringComparison.OrdinalIgnoreCase) && t.Period == (string)scope.SelectedItem);

        void Add()
        {
            var activity = activities.SelectedItem as ExpeditionActivity;
            var player = _store.LiveCharacter;
            if (activity is not null && (!SmartExpeditions.KnownCharacter(player) || !string.Equals(character.Text.Trim(), CharacterLabel(),
                StringComparison.OrdinalIgnoreCase)))
            {
                message.Text = Loc.T("Для автоцели войдите в игру и выберите «Текущий персонаж», когда ARX распознает персонажа и сервер.");
                return;
            }
            if (string.IsNullOrWhiteSpace(character.Text) || (activity is null && string.IsNullOrWhiteSpace(task.Text)) ||
                !int.TryParse(goal.Text, out var count) || count is < 1 or > 10000)
            {
                message.Text = Loc.T("Укажите персонажа, название и количество от 1 до 10 000.");
                return;
            }
            profile.ProgressCharacter = character.Text.Trim();
            var name = string.IsNullOrWhiteSpace(task.Text) ? activity!.Name : task.Text.Trim();
            profile.Goals.Add(new ProgressGoal(Guid.NewGuid(), profile.ProgressCharacter, (string)scope.SelectedItem, name, 0, count,
                activity?.Id, activity is null ? 0 : player!.ServerId, activity is null ? null : player!.Name));
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
                rows.Children.Add(Actions(Text($"{item.Name}   ·   {item.Count} / {item.Target}" +
                    (item.ActivityId is null ? "" : Loc.T("   ·   авто")), 14, "Text", true),
                    Button("−", () => Change(item, -1)), Button("+", () => Change(item, 1))));
            if (rows.Children.Count == 0) rows.Children.Add(Hint(Loc.T("Нет целей в выбранном списке.")));
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
