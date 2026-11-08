using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace ArxBeta;

public sealed class BattleChart(FightAnalysis analysis) : FrameworkElement
{
    public void Update(FightAnalysis value)
    {
        if (ReferenceEquals(analysis, value)) return;
        analysis = value;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth - 44;
        var height = ActualHeight - 29;
        if (width <= 0 || height <= 0) return;
        const double left = 39, top = 7;
        var line = new Pen(Ui.Brush("Line"), 1);
        var data = analysis.GroupCurve;
        var max = Math.Max(1, data.Length == 0 ? 1 : data.Max());
        for (var i = 0; i <= 4; i++)
        {
            var y = top + height * i / 4;
            dc.DrawLine(line, new Point(left, y), new Point(left + width, y));
            Caption(dc, data.Length == 0 ? (i == 4 ? "0" : "") : Ui.Compact(max * (4 - i) / 4),
                new Point(0, y - 6));
        }
        if (data.Length == 0)
        {
            Caption(dc, "Ожидание событий боя", new Point(left + 10, top + height / 2));
            return;
        }
        DrawCurve(dc, data, "Mint", left, top, width, height, max);
        if (analysis.Fight.Self.Length > 0) DrawCurve(dc, analysis.SelfCurve, "Coral", left, top, width, height, max);
        for (var i = 0; i <= 4; i++)
            Caption(dc, Ui.Clock(TimeSpan.FromSeconds(analysis.Fight.Duration * i / 4)),
                new Point(left + width * i / 4 - (i == 4 ? 30 : 0), top + height + 6));
    }

    private static void DrawCurve(DrawingContext dc, double[] values, string tint,
        double left, double top, double width, double height, double max)
    {
        if (values.Length == 0) return;
        Point Coordinate(int i) => new(left + width * i / Math.Max(1, values.Length - 1),
            top + height * (1 - values[i] / max));
        if (values.Length == 1)
        {
            dc.DrawEllipse(Ui.Brush(tint), null, Coordinate(0), 3, 3);
            return;
        }
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(Coordinate(0), false, false);
            for (var i = 1; i < values.Length; i++) ctx.LineTo(Coordinate(i), true, false);
        }
        dc.DrawGeometry(null, new Pen(Ui.Brush(tint), 2), geometry);
    }

    private void Caption(DrawingContext dc, string value, Point at) =>
        dc.DrawText(new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 10, Ui.Brush("Muted"), VisualTreeHelper.GetDpi(this).PixelsPerDip), at);
}
