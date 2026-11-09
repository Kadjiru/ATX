using System.Windows;
using System.Windows.Media;

namespace ArxBeta;

internal sealed class ItemIllustration(string type) : FrameworkElement
{
    protected override void OnRender(DrawingContext dc)
    {
        var (color, shape) = type switch
        {
            "Sword" or "Greatsword" or "Dagger" =>
                ("Coral", "M10,38 L18,30 15,26 19,23 24,27 36,9 46,5 42,16 25,31 29,35 26,39 21,35 14,44 Z"),
            "Mace" or "Staff" =>
                ("Coral", "M10,43 L30,19 30,12 37,6 45,10 45,18 38,24 34,22 15,46 Z"),
            "Guarder" =>
                ("Coral", "M25,5 L44,12 41,30 Q37,40 25,46 Q13,40 9,30 L6,12 Z M25,13 L25,37 M15,23 L35,23"),
            "Bow" =>
                ("Coral", "M10,9 Q39,13 41,41 Q35,27 10,9 M10,9 L41,41 M9,42 L38,12 M30,13 L38,12 37,20"),
            "Torso" or "Shoulder" or "Cape" =>
                ("Mint", "M8,14 L17,8 25,14 33,8 42,14 46,24 37,27 35,42 15,42 13,27 4,24 Z M17,20 L25,26 33,20 M17,32 L33,32"),
            "Helmet" =>
                ("Mint", "M9,36 L8,24 Q9,7 25,7 Q41,7 42,24 L41,36 32,42 29,25 21,25 18,42 Z M11,22 L39,22"),
            "Boots" or "Pants" or "Gloves" =>
                ("Mint", "M11,8 L39,8 41,39 31,44 25,26 19,44 9,39 Z M12,15 L38,15"),
            "Ring" or "Earring" or "Necklace" or "Bracelet" =>
                ("Gold", "M17,14 L21,6 29,6 33,14 25,22 Z M16,18 C4,24 9,44 25,44 C41,44 46,24 34,18 M19,24 C12,29 16,38 25,38 C34,38 38,29 31,24"),
            "Potion" or "Drink" =>
                ("Gold", "M18,6 L32,6 32,11 29,14 29,23 Q41,28 39,40 Q38,45 25,45 Q12,45 11,40 Q9,28 21,23 L21,14 18,11 Z M14,33 Q24,30 36,34"),
            "Food" =>
                ("Coral", "M5,33 Q25,43 45,33 Q41,44 25,44 Q9,44 5,33 Z M10,31 Q12,17 25,17 Q38,17 40,31 Z M21,16 Q20,12 25,10 M28,15 Q35,11 33,7"),
            "Magicbook" or "Scroll" =>
                ("Gold", "M7,10 Q18,5 25,12 Q32,5 43,10 L43,39 Q32,34 25,41 Q18,34 7,39 Z M25,12 L25,40 M12,19 L20,19 M30,19 L38,19 M12,26 L20,26 M30,26 L38,26"),
            _ => ("Mint", "M25,5 L41,18 35,39 25,46 15,39 9,18 Z M9,18 L41,18 M25,5 L18,18 25,46 32,18 Z")
        };
        var ink = (SolidColorBrush)Ui.Brush(color);
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;
        dc.PushTransform(new ScaleTransform(size / 50, size / 50));
        var tint = ink.Color;
        var background = new LinearGradientBrush(Color.FromArgb(70, tint.R, tint.G, tint.B), Color.FromRgb(15, 30, 34), 65);
        dc.DrawRoundedRectangle(background, new Pen(Ui.Brush("Line"), .8), new Rect(.5, .5, 49, 49), 8, 8);
        var pen = new Pen(ink, 1.35) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round };
        dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(50, tint.R, tint.G, tint.B)), pen, Geometry.Parse(shape));
        dc.Pop();
    }
}
