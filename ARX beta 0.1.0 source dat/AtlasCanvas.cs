using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace ArxBeta;

internal sealed record AtlasZone(string Id, string Name, Point Center, string Region, int Seed)
{
    public static AtlasZone[] All { get; } =
    [
        new("ishalgen", "Ишальген", new Point(184, 157), "АСМОДЕЯ", 12),
        new("altgard", "Альтгард", new Point(309, 219), "АСМОДЕЯ", 42),
        new("upper", "Верхняя Решанта", new Point(538, 114), "БЕЗДНА", 26),
        new("lower", "Нижняя Решанта", new Point(462, 317), "БЕЗДНА", 74),
        new("outer", "Внешняя Решанта", new Point(658, 252), "БЕЗДНА", 45),
        new("poeta", "Поэта", new Point(690, 478), "ЭЛИЗЕЯ", 81),
        new("verteron", "Бертрон", new Point(808, 386), "ЭЛИЗЕЯ", 24)
    ];
}

internal sealed class AtlasCanvas : FrameworkElement
{
    private const double W = 1000, H = 600;
    private static readonly Geometry Asmodae = Geometry.Parse("M 31,211 L 84,159 105,99 192,75 217,112 296,105 339,143 385,149 406,199 358,232 344,292 273,321 214,287 173,294 146,254 83,261 Z");
    private static readonly Geometry Elysea = Geometry.Parse("M 610,525 L 638,460 698,417 725,348 793,315 838,322 873,292 925,324 958,382 912,402 909,475 845,489 814,528 758,528 693,560 Z");
    private static readonly Geometry Abyss = Geometry.Parse("M 431,110 L 479,68 572,62 608,116 578,166 500,183 438,151 Z M 397,308 L 438,269 505,263 532,306 500,356 450,370 416,348 Z M 595,245 L 639,200 707,219 735,249 696,289 631,298 Z");
    private static readonly string[] Palette = ["#10252A", "#203F43", "#456566", "#2C5150", "#508072", "#88B2A3"];
    private static readonly Brush[] AtlasBrushes = Palette.Select(value => (Brush)new BrushConverter().ConvertFromString(value)!).ToArray();
    private readonly Func<AtlasZone?, IEnumerable<MapPin>> _pins;
    private Point _down;
    private Vector _atDown;
    private Vector _pan;
    private double _zoom = 1;
    private bool _drag;

    public AtlasZone? Zone { get; private set; }
    public bool Placing { get; set; }
    public Guid? Selected { get; set; }
    public event Action<AtlasZone>? ZoneSelected;
    public event Action<MapPin>? PinSelected;
    public event Action<Point>? PinRequested;

    public AtlasCanvas(Func<AtlasZone?, IEnumerable<MapPin>> pins)
    {
        _pins = pins;
        ClipToBounds = true;
        Focusable = true;
        Cursor = Cursors.Hand;
    }

    public void ChangeZone(AtlasZone? zone)
    {
        Zone = zone;
        Selected = null;
        Placing = false;
        Fit();
    }

    public void Fit()
    {
        _zoom = 1;
        _pan = default;
        InvalidateVisual();
    }

    private double Scale => Math.Min(ActualWidth / W, ActualHeight / H) * _zoom;
    private Point Origin => new((ActualWidth - W * Scale) / 2 + _pan.X, (ActualHeight - H * Scale) / 2 + _pan.Y);

    public void Zoom(double multiplier, Point? around = null)
    {
        if (Scale <= 0) return;
        var cursor = around ?? new Point(ActualWidth / 2, ActualHeight / 2);
        var world = ToWorld(cursor);
        _zoom = Math.Clamp(_zoom * multiplier, 1, 4);
        var offset = Origin;
        _pan += cursor - new Point(offset.X + world.X * Scale, offset.Y + world.Y * Scale);
        KeepInView();
        InvalidateVisual();
    }

    private void KeepInView()
    {
        var x = Math.Max(0, (W * Scale - ActualWidth) / 2);
        var y = Math.Max(0, (H * Scale - ActualHeight) / 2);
        _pan = new Vector(Math.Clamp(_pan.X, -x, x), Math.Clamp(_pan.Y, -y, y));
    }

    private Point ToWorld(Point point)
    {
        var offset = Origin;
        return new Point((point.X - offset.X) / Scale, (point.Y - offset.Y) / Scale);
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(AtlasBrushes[0], null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (Scale <= 0) return;
        var matrix = new Matrix(Scale, 0, 0, Scale, Origin.X, Origin.Y);
        dc.PushTransform(new MatrixTransform(matrix));
        DrawGrid(dc);
        if (Zone is null) DrawWorld(dc);
        else DrawZone(dc, Zone);
        Compass(dc);
        dc.Pop();
    }

    private static void DrawGrid(DrawingContext dc)
    {
        var grid = new Pen(AtlasBrushes[1], .7);
        for (var x = 0; x <= W; x += 40) dc.DrawLine(grid, new Point(x, 0), new Point(x, H));
        for (var y = 0; y <= H; y += 40) dc.DrawLine(grid, new Point(0, y), new Point(W, y));
    }

    private void DrawWorld(DrawingContext dc)
    {
        DrawLand(dc, Asmodae, AtlasBrushes[1]);
        DrawLand(dc, Elysea, AtlasBrushes[3]);
        DrawLand(dc, Abyss, AtlasBrushes[2]);
        var route = new Pen(AtlasBrushes[5], 1.5) { DashStyle = DashStyles.Dash };
        for (var i = 1; i < AtlasZone.All.Length; i++)
            dc.DrawLine(route, AtlasZone.All[i - 1].Center, AtlasZone.All[i].Center);
        Caption(dc, "АСМОДЕЯ", new Point(116, 263), 21, AtlasBrushes[5]);
        Caption(dc, "БЕЗДНА", new Point(572, 333), 20, AtlasBrushes[5]);
        Caption(dc, "ЭЛИЗЕЯ", new Point(793, 462), 21, AtlasBrushes[5]);
        foreach (var zone in AtlasZone.All)
        {
            dc.DrawEllipse(Ui.Brush("Mint"), new Pen(AtlasBrushes[0], 2), zone.Center, 9, 9);
            Caption(dc, zone.Name, new Point(zone.Center.X + 13, zone.Center.Y - 10), 16, Ui.Brush("Text"));
        }
    }

    private void DrawZone(DrawingContext dc, AtlasZone zone)
    {
        var rng = new Random(zone.Seed);
        var coast = new List<Point>();
        for (var i = 0; i < 32; i++)
        {
            var angle = Math.PI * 2 * i / 32;
            var radial = 0.73 + rng.NextDouble() * .25;
            coast.Add(new Point(510 + Math.Cos(angle) * 445 * radial, 307 + Math.Sin(angle) * 272 * radial));
        }
        var shape = Polygon(coast);
        DrawLand(dc, shape, AtlasBrushes[3]);
        dc.PushClip(shape);
        for (var i = 0; i < 9; i++)
        {
            var scale = 0.86 - i * 0.074;
            var contour = Polygon(coast.Select(p => new Point(500 + (p.X - 500) * scale + i * 3,
                290 + (p.Y - 290) * scale - i * 1.7)));
            dc.DrawGeometry(null, new Pen(AtlasBrushes[4], 1), contour);
        }
        for (var i = 0; i < 70; i++)
        {
            var p = new Point(rng.Next(130, 900), rng.Next(100, 530));
            dc.DrawLine(new Pen(AtlasBrushes[5], .7), p, p + new Vector(rng.Next(3, 12), -4));
        }
        dc.DrawGeometry(null, new Pen(AtlasBrushes[0], 8), Geometry.Parse("M 82,294 C 268,158 375,433 558,350 S 746,174 918,255"));
        dc.DrawGeometry(null, new Pen(AtlasBrushes[5], 1.5) { DashStyle = DashStyles.Dash },
            Geometry.Parse("M 120,338 C 293,278 452,235 573,304 S 714,443 913,371"));
        dc.Pop();
        Caption(dc, zone.Region, new Point(38, 38), 14, Ui.Brush("Muted"));
        Caption(dc, zone.Name.ToUpperInvariant(), new Point(38, 59), 27, Ui.Brush("Text"));
        foreach (var pin in _pins(zone))
        {
            var point = new Point(pin.X, pin.Y);
            var color = Ui.Brush(pin.Found ? "Mint" : pin.Category == "Босс" ? "Coral" : "Gold");
            if (pin.Id == Selected) dc.DrawEllipse(null, new Pen(Ui.Brush("Text"), 2), point, 15, 15);
            dc.DrawEllipse(color, new Pen(AtlasBrushes[0], 2), point, 8, 8);
            Caption(dc, pin.Name, point + new Vector(12, -9), 13, Ui.Brush("Text"));
        }
    }

    private static void DrawLand(DrawingContext dc, Geometry shape, Brush fill)
    {
        dc.DrawGeometry(fill, new Pen(AtlasBrushes[2], 9), shape);
        dc.DrawGeometry(fill, new Pen(AtlasBrushes[5], 1.3), shape);
    }

    private static StreamGeometry Polygon(IEnumerable<Point> points)
    {
        var geometry = new StreamGeometry();
        using var writer = geometry.Open();
        using var iterator = points.GetEnumerator();
        if (iterator.MoveNext()) writer.BeginFigure(iterator.Current, true, true);
        while (iterator.MoveNext()) writer.LineTo(iterator.Current, true, false);
        return geometry;
    }

    private void Compass(DrawingContext dc)
    {
        var at = new Point(945, 65);
        dc.DrawEllipse(AtlasBrushes[0], new Pen(AtlasBrushes[4], 1), at, 26, 26);
        dc.DrawGeometry(Ui.Brush("Mint"), null, Polygon([new Point(945, 48), new Point(956, 78), new Point(945, 72), new Point(934, 78)]));
        Caption(dc, "N", new Point(941, 23), 11, Ui.Brush("Muted"));
    }

    private void Caption(DrawingContext dc, string text, Point at, double size, Brush color)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI Semibold"), size, color, VisualTreeHelper.GetDpi(this).PixelsPerDip) { MaxTextWidth = 220 };
        dc.DrawText(formatted, at);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        Zoom(e.Delta > 0 ? 1.18 : 1 / 1.18, e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        _down = e.GetPosition(this);
        _atDown = _pan;
        _drag = false;
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!IsMouseCaptured) return;
        var delta = e.GetPosition(this) - _down;
        if (delta.Length < 4 && !_drag) return;
        _drag = true;
        _pan = _atDown + delta;
        KeepInView();
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!IsMouseCaptured) return;
        ReleaseMouseCapture();
        if (_drag || Scale <= 0) return;
        var point = ToWorld(e.GetPosition(this));
        if (point.X is < 0 or > W || point.Y is < 0 or > H) return;
        if (Zone is null)
        {
            var nearest = AtlasZone.All.MinBy(zone => (zone.Center - point).Length);
            if (nearest is not null && (nearest.Center - point).Length < 60) ZoneSelected?.Invoke(nearest);
            return;
        }
        if (Placing)
        {
            PinRequested?.Invoke(point);
            return;
        }
        var marker = _pins(Zone).MinBy(pin => (new Point(pin.X, pin.Y) - point).Length);
        if (marker is not null && (new Point(marker.X, marker.Y) - point).Length <= 17) PinSelected?.Invoke(marker);
    }
}
