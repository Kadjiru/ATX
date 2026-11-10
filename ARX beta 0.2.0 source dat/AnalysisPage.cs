using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using static ArxBeta.Ui;

namespace ArxBeta;

public sealed partial class Dashboard
{
    private UIElement AnalysisPage()
    {
        var result = _store.Analysis;
        var page = Heading(Loc.T("Разбор и история"), Loc.T("Локальные отчёты группы с боевым графиком, вкладом группы и навыками"));
        page.Children.Add(Actions(Button(Loc.T("Снимок живого боя"), () =>
        {
            if (_store.LiveAnalysis is { } live) _store.SetFight(live.Fight, false);
        }), Button(Loc.T("Экспорт отчёта"), () => ExportFight(result.Fight))));
        var input = new Expander { Header = Loc.T("Добавить отчёт"), Foreground = Ui.Brush("Mint"), IsExpanded = result.Players.Length == 0 };
        var title = Input("", 218, 80);
        var self = Input(_store.Profile.Character, 180, 80);
        var duration = Input("60", 96, 8);
        var totals = Input("", 440, 16000);
        totals.AcceptsReturn = true;
        totals.Height = 76;
        totals.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        var error = Text("", 12, "Coral");
        var form = Stack(Space(Actions(Field(Loc.T("Название боя"), title), Field(Loc.T("Ваш персонаж"), self),
                Field(Loc.T("Длительность, с"), duration)), 5),
            Space(Hint(Loc.T("Для ручного ввода укажите по одной строке на участника: имя; урон; лечение.")), 5),
            Space(Field(Loc.T("Итоги участников"), totals), 8),
            Actions(Button(Loc.T("Сохранить итоги"), SaveTotals, true), Button(Loc.T("Импорт CSV с попаданиями"), Import),
                Button(Loc.T("Сохранить шаблон CSV"), ExportTemplate)),
            error);
        input.Content = form;
        page.Children.Add(Space(Card(input), 12));

        if (result.Players.Length > 0)
        {
            var current = result.Fight;
            var currentTitle = Text(current.Name, 22, bold: true);
            var header = Stack(Space(currentTitle, 4), Space(Hint(
                $"{(current.Demo ? Loc.T("Демонстрация") : current.Captured is null ? Loc.T("Локальный отчёт") : Loc.T("Данные Npcap"))}  ·  {Clock(TimeSpan.FromSeconds(current.Duration))}  ·  " +
                Loc.F($"{Number(result.Damage)} урона  ·  {Number(result.Healing)} лечения")), 10));
            if (current.Captured?.Summary.Boss is { } boss)
            {
                header.Children.Add(Space(Hint($"{boss.Name}  ·  {OutcomeLabel(current.Captured.Summary.Reason)}"), 8));
                header.Children.Add(Space(BossTimelinePanel(result), 10));
            }
            else if (result.GroupCurve.Length > 0) header.Children.Add(Space(new BattleChart(result) { Height = 170 }, 10));
            if (current.Build is { } build) header.Children.Add(Space(Hint(Loc.F($"Заявленный билд: {build}")), 8));
            if (current.Captured?.Summary.MostDamageUnsent == true) header.Children.Add(Space(Hint(Loc.T("Сервер прислал неполные данные об уроне.")), 8));
            string[] labels = [Loc.T("Участник"), Loc.T("Урон"), "DPS", Loc.T("Лечение"), "HPS", Loc.T("Крит"), Loc.T("Бурст"), Loc.T("Сила")];
            var memberTable = MakeTable(labels, result.Players, player => StatCells(result, player), player =>
            {
                _player = player.Player;
                _actor = player.ActorId;
                RenderPage();
            });
            header.Children.Add(memberTable);
            page.Children.Add(Space(Card(header), 12));

            var selected = result.Players.FirstOrDefault(p => _actor is { } id ? p.ActorId == id : string.Equals(p.Player, _player, StringComparison.OrdinalIgnoreCase))
                ?? result.Players.FirstOrDefault(p => p.IsSelf)
                ?? result.Players[0];
            var damageShare = result.Damage == 0 ? 0 : (double)selected.Damage / result.Damage;
            var detail = Stack(Space(Text(Loc.F($"Навыки · {selected.Player}"), 19, bold: true), 6),
                Space(Hint(Loc.F($"Доля урона {damageShare:P1}  ·  Попадания {selected.Hits?.ToString() ?? "—"}  ·  Криты {selected.Crits?.ToString() ?? "—"}")), 9));
            var captured = current.Captured?.Details.FirstOrDefault(p => p.ActorId == selected.ActorId);
            if (captured is not null)
            {
                string Percent(double? n) => n is { } value ? value.ToString("P1") : "—";
                detail.Children.Add(Space(Hint(Loc.F($"Макс. удар {Compact(captured.Quality.MaxHit)} · Полученный урон {Compact(captured.DamageTaken)} · ") +
                    Loc.F($"Атака со спины {Percent(captured.Quality.BackRate)} · Идеальный удар {Percent(captured.Quality.PerfectRate)} · ") +
                    Loc.F($"Тяжёлый удар {Percent(captured.Quality.HeavyRate)}")), 9));
            }
            var skills = result.Skills(selected.Player, selected.ActorId);
            if (skills.Length == 0) detail.Children.Add(Hint(Loc.T("Разбивка по навыкам появляется при импорте событий.")));
            else detail.Children.Add(MakeTable([Loc.T("Навык"), Loc.T("Урон"), "DPS", Loc.T("Лечение"), "HPS", Loc.T("Крит"), Loc.T("Бурст"), Loc.T("Макс.")],
                skills, skill => StatCells(result, skill, true)));
            page.Children.Add(Space(Card(detail), 12));
        }

        var history = Stack(Space(Text(Loc.T("Сохранённые отчёты"), 19, bold: true), 7));
        if (_store.Profile.History.Count == 0) history.Children.Add(Hint(Loc.T("Здесь появятся последние 30 импортированных или введённых отчётов.")));
        foreach (var record in _store.Profile.History)
            history.Children.Add(Space(Button($"{record.Date.ToLocalTime():dd.MM.yyyy HH:mm}   ·   {record.Name}",
                () => _store.SetFight(record, false)), 5));
        page.Children.Add(Card(history));
        return page;

        void SaveTotals()
        {
            if (!double.TryParse(duration.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var seconds) ||
                !double.IsFinite(seconds) || seconds is <= 0 or > 43200)
            {
                error.Text = Loc.T("Укажите длительность от 1 до 43 200 секунд.");
                return;
            }
            var entries = new List<Totals>();
            var players = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in totals.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var fields = row.Split(';', StringSplitOptions.TrimEntries);
                if (fields.Length != 3 || fields[0].Length is 0 or > 80 || !players.Add(fields[0]) ||
                    !long.TryParse(fields[1], out var damage) || damage is < 0 or > 100_000_000_000 ||
                    !long.TryParse(fields[2], out var healing) || healing is < 0 or > 100_000_000_000)
                {
                    error.Text = Loc.T("Проверьте формат, суммы и уникальность имён участников.");
                    return;
                }
                entries.Add(new Totals(fields[0], damage, healing));
            }
            if (entries.Count == 0)
            {
                error.Text = Loc.T("Добавьте хотя бы одного участника.");
                return;
            }
            var record = new Fight(Guid.NewGuid(), ReportName(Loc.T("Ручной отчёт")), DateTimeOffset.Now,
                seconds, self.Text.Trim(), [], entries.ToArray());
            TryAction(() => _store.SetFight(record, true));
        }

        void Import()
        {
            var dialog = new OpenFileDialog { Filter = Loc.T("CSV с событиями ARX (*.csv)|*.csv") };
            if (dialog.ShowDialog(this) != true) return;
            TryAction(() =>
            {
                var record = FightImport.Csv(dialog.FileName, ReportName(Path.GetFileNameWithoutExtension(dialog.FileName)), self.Text.Trim());
                _store.SetFight(record, true);
            });
        }

        void ExportTemplate()
        {
            var dialog = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "arx-combat-template.csv" };
            if (dialog.ShowDialog(this) != true) return;
            TryAction(() => File.WriteAllText(dialog.FileName,
                "seconds,player,skill,damage,healing,critical\n0,Aster,Strike,12000,0,false\n6,Aster,Strike,16000,0,true\n"));
        }

        void TryAction(Action action)
        {
            try { action(); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or Microsoft.VisualBasic.FileIO.MalformedLineException)
            {
                error.Text = ex.Message;
            }
        }

        string ReportName(string fallback) => string.IsNullOrWhiteSpace(title.Text) ? fallback : title.Text.Trim();
    }

    private static string[] StatCells(FightAnalysis result, PlayerStats player, bool skill = false) =>
    [
        player.IsSelf ? Loc.F($"ВЫ · {player.Player}") : player.Player, Compact(player.Damage), Compact(result.Rate(player.Damage)),
        Compact(player.Healing), Compact(result.Rate(player.Healing)),
        player.CritRate is { } crit ? $"{crit:P0}" : "—", player.Burst is { } peak ? Compact(peak) : "—",
        (skill ? player.MaxHit : player.CombatPower) is > 0 and var value ? Compact(value) : "—"
    ];

    private void ExportFight(Fight fight)
    {
        if (fight.Id == Guid.Empty || fight.Demo) return;
        ExportJson(fight, "arx-fight.json");
    }

    private void ExportJson<T>(T value, string name)
    {
        var dialog = new SaveFileDialog { Filter = "JSON (*.json)|*.json", FileName = name };
        if (dialog.ShowDialog(this) != true) return;
        try { File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true })); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, Loc.T("Экспорт ARX"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static StackPanel MakeTable<T>(string[] headers, IEnumerable<T> rows, Func<T, string[]> values,
        Action<T>? choose = null)
    {
        var result = Stack(TableRow(headers, true));
        var alternate = false;
        foreach (var row in rows)
        {
            var cells = TableRow(values(row), false);
            if (choose is null)
                result.Children.Add(new Border { Background = Ui.Brush(alternate ? "Raised" : "Surface"), Child = cells });
            else
            {
                var button = Button("", () => choose(row));
                button.Content = cells;
                button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                button.Padding = new Thickness(0);
                button.Background = Ui.Brush(alternate ? "Raised" : "Surface");
                button.BorderThickness = new Thickness(0);
                result.Children.Add(button);
            }
            alternate = !alternate;
        }
        return result;
    }

    private static Grid TableRow(string[] values, bool header)
    {
        var grid = new Grid { MinHeight = 32 };
        for (var i = 0; i < values.Length; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(i == 0 ? 2 : 1, GridUnitType.Star) });
            var cell = Text(values[i], header ? 11 : 13, header ? "Muted" : "Text", header);
            cell.TextWrapping = TextWrapping.NoWrap;
            cell.TextTrimming = TextTrimming.CharacterEllipsis;
            cell.VerticalAlignment = VerticalAlignment.Center;
            cell.Margin = new Thickness(8, 1, 5, 1);
            Place(grid, cell, 0, i);
        }
        return grid;
    }
}
