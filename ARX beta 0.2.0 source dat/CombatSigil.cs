using System.Windows;
using System.Windows.Media;

namespace ArxBeta;

internal sealed class CombatSigil : FrameworkElement
{
    private static readonly Geometry Wings = Geometry.Parse(
        "M208,107 C148,111 91,91 42,43 C64,109 115,145 177,154 L139,137 L204,145 " +
        "M232,107 C292,111 349,91 398,43 C376,109 325,145 263,154 L301,137 L236,145");
    private static readonly Geometry Crest = Geometry.Parse(
        "M220,38 L267,106 L220,181 L173,106 Z M220,62 L250,106 L220,157 L190,106 Z " +
        "M220,80 L234,106 L220,136 L206,106 Z");

    protected override void OnRender(DrawingContext dc)
    {
        dc.PushTransform(new ScaleTransform(ActualWidth / 440, ActualHeight / 218));
        var center = new Point(220, 109);
        var glow = new RadialGradientBrush(Color.FromArgb(70, 126, 245, 206), Colors.Transparent);
        dc.DrawEllipse(glow, null, center, 210, 107);
        var outline = new Pen(new SolidColorBrush(Color.FromArgb(88, 140, 229, 210)), 1);
        var warm = new Pen(new SolidColorBrush(Color.FromArgb(195, 255, 213, 151)), 1);
        foreach (var radius in new[] { 68, 86, 95 })
            dc.DrawEllipse(null, outline, center, radius, radius);
        for (var i = 0; i < 36; i++)
        {
            var angle = i * Math.Tau / 36;
            Point At(double r) => center + new Vector(r * Math.Cos(angle), r * Math.Sin(angle));
            dc.DrawLine(i % 3 == 0 ? warm : outline, At(90), At(i % 3 == 0 ? 98 : 94));
        }
        dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(45, 124, 240, 203)), outline, Wings);
        dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(30, 255, 213, 151)), warm, Crest);
        dc.DrawEllipse(Ui.Brush("Coral"), null, new Point(220, 106), 3, 3);
        dc.Pop();
    }
}
