using System.Windows;
using System.Windows.Controls;
using AionMeter.Core.Combat;
using static ArxBeta.Ui;

namespace ArxBeta;

internal sealed class BossTimeline
{
    private readonly (CombatantDetail Member, long[] Cumulative)[] _rows;
    public int Seconds { get; }
    public int? BestStart { get; }
    public bool Partial { get; }

    public BossTimeline(FightAnalysis analysis)
    {
        var details = analysis.Fight.Captured?.Details ?? [];
        Seconds = Math.Max(1, (int)Math.Ceiling(analysis.Fight.Duration));
        _rows = details.Where(row => row.PerSecond.Count > 0).Select(row =>
        {
            var cumulative = new long[Math.Min(Seconds, row.PerSecond.Count) + 1];
            for (var i = 0; i < row.PerSecond.Count; i++) cumulative[Math.Min(i + 1, Seconds)] += row.PerSecond[i];
            for (var i = 1; i < cumulative.Length; i++) cumulative[i] += cumulative[i - 1];
            return (row, cumulative);
        }).ToArray();
        Partial = _rows.Sum(row => row.Cumulative[^1]) != analysis.Damage ||
            analysis.Fight.Captured?.Summary.MostDamageUnsent == true;
        long best = 0;
        var fullSeconds = (int)Math.Floor(analysis.Fight.Duration);
        for (var start = 0; start + 5 <= fullSeconds; start++)
        {
            var total = _rows.Sum(row => DamageIn(row.Cumulative, start, start + 5));
            if (total <= best) continue;
            BestStart = start;
            best = total;
        }
    }

    public bool HasSamples => _rows.Length > 0;

    public IEnumerable<(CombatantDetail Member, long Damage)> InRange(int from, int to) =>
        _rows.Select(row => (row.Member, DamageIn(row.Cumulative, from, to)))
            .OrderByDescending(row => row.Item2);

    private static long DamageIn(long[] values, int from, int to) =>
        values[Math.Clamp(to, 0, values.Length - 1)] - values[Math.Clamp(from, 0, values.Length - 1)];
}

public sealed partial class Dashboard
{
    private FrameworkElement BossTimelinePanel(FightAnalysis analysis)
    {
        var timeline = new BossTimeline(analysis);
        if (!timeline.HasSamples) return Hint(Loc.T("Посекундные данные для этой записи отсутствуют."));
        var chart = new BattleChart(analysis) { Height = 170, SelectionSeconds = timeline.Seconds };
        var summary = Text("", 14, "Mint", true);
        var details = new ContentControl();
        var peak = Button(Loc.T("Пик за 5 секунд"), () =>
        {
            if (timeline.BestStart is { } start) Show(start, start + 5);
        });
        peak.IsEnabled = timeline.BestStart.HasValue;
        chart.RangeSelected += Show;
        Show(0, timeline.Seconds);
        return Stack(Space(chart, 6),
            Actions(Button(Loc.T("Весь бой"), () => Show(0, timeline.Seconds)), peak),
            Space(Hint(timeline.Partial ? Loc.T("Данные таймлайна неполные. Сводка боя ниже сохраняет все доступные итоги.") :
                Loc.T("Перетащите по графику, чтобы выбрать интервал. Данные сгруппированы по секундам.")), 6),
            Space(summary, 4), details);

        void Show(int from, int to)
        {
            chart.ShowRange(from, to);
            var rows = timeline.InRange(from, to).Where(row => row.Damage > 0).ToArray();
            var duration = Math.Max(0.001, Math.Min(analysis.Fight.Duration, to) - from);
            var selectedDamage = rows.Sum(row => row.Damage);
            summary.Text = $"{Clock(TimeSpan.FromSeconds(from))} – {Clock(TimeSpan.FromSeconds(Math.Min(to, analysis.Fight.Duration)))}" +
                Loc.F($"  ·  {Compact(selectedDamage)} урона");
            details.Content = rows.Length == 0 ? Hint(Loc.T("За этот интервал урон не зарегистрирован.")) :
                MakeTable([Loc.T("Участник"), Loc.T("Урон за интервал"), Loc.T("DPS за интервал")], rows, row =>
                    [row.Member.IsSelf ? Loc.F($"ВЫ · {row.Member.Name}") : row.Member.Name, Compact(row.Damage), Compact(row.Damage / duration)]);
        }
    }
}
