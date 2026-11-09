using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace ArxBeta;

internal sealed class AtlasCanvas : FrameworkElement
{
    private const double W = AtlasCatalog.Width, H = AtlasCatalog.Height;
    private static readonly Brush Water = Swatch("#0D2029");
    private static readonly Brush GridLine = Swatch("#3B5260");
    private static readonly Brush Plate = Swatch("#132831");
    private static readonly Brush Teleport = Swatch("#88EBCC");
    private static readonly Brush Trace = Swatch("#C6B9FF");
    private static readonly Brush Gather = Swatch("#F4C580");
    private static readonly Brush Cube = Swatch("#FF9EAB");
    private static readonly Brush Pet = Swatch("#B4D2FE");
    private static readonly HashSet<string> Landmarks = new(StringComparer.Ordinal)
    {
        "Деревня беглецов", "Водопад Васфельта", "Озеро Идунн", "Утес Уртумхейма", "Побережье Дранактуса",
        "Деревня Джумион", "Лощина крепости Бертрон", "Окраина леса Тольбас", "Лагерь запада долины Кантаса",
        "Восточные корни Ратерана", "Западные корни Ратерана"
    };

    private Point _down;
    private Vector _atDown;
    private Vector _pan;
    private double _zoom = 1;
    private bool _drag;
    private AtlasCatalog.AtlasMap? _map;
    private ImageSource? _backdrop;
    private IReadOnlyList<MapPin> _pins = [];
    private IReadOnlyList<MapPin> _route = [];
    private MapPin? _hover;

    public bool Placing { get; set; }
    public Guid? Selected { get; set; }
    public event Action<MapPin>? PinSelected;
    public event Action<Point>? PinRequested;

    public AtlasCanvas()
    {
        ClipToBounds = true;
        Cursor = Cursors.Hand;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.Linear);
    }

    public void SetLayer(AtlasCatalog.AtlasMap? map, ImageSource? backdrop)
    {
        _map = map;
        _backdrop = backdrop;
        Selected = null;
        Placing = false;
        Fit();
    }

    public void SetPins(IReadOnlyList<MapPin> pins, IReadOnlyList<MapPin> route)
    {
        _pins = pins;
        _route = route;
        _hover = null;
        InvalidateVisual();
    }

    public void Fit()
    {
        _zoom = 1;
        _pan = default;
        InvalidateVisual();
    }

    public void FocusPin(MapPin pin)
    {
        _zoom = Math.Max(2.8, _zoom);
        _pan = new Vector((W / 2 - pin.X) * Scale, (H / 2 - pin.Y) * Scale);
        KeepInView();
        InvalidateVisual();
    }

    private double Scale => Math.Min(ActualWidth / W, ActualHeight / H) * _zoom;
    private Point Origin => new((ActualWidth - W * Scale) / 2 + _pan.X, (ActualHeight - H * Scale) / 2 + _pan.Y);

    public void Zoom(double multiplier, Point? around = null)
    {
        if (Scale <= 0) return;
        var cursor = around ?? new Point(ActualWidth / 2, ActualHeight / 2);
        var world = ToWorld(cursor);
        _zoom = Math.Clamp(_zoom * multiplier, 1, 12);
        _pan += cursor - ToScreen(world);
        KeepInView();
        _hover = null;
        InvalidateVisual();
    }

    private void KeepInView()
    {
        var x = Math.Max(0, (W * Scale - ActualWidth) / 2);
        var y = Math.Max(0, (H * Scale - ActualHeight) / 2);
        _pan = new Vector(Math.Clamp(_pan.X, -x, x), Math.Clamp(_pan.Y, -y, y));
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        KeepInView();
    }

    private Point ToWorld(Point point)
    {
        var origin = Origin;
        return new Point((point.X - origin.X) / Scale, (point.Y - origin.Y) / Scale);
    }

    private Point ToScreen(Point point)
    {
        var origin = Origin;
        return new Point(origin.X + point.X * Scale, origin.Y + point.Y * Scale);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var view = new Rect(0, 0, ActualWidth, ActualHeight);
        dc.DrawRectangle(Water, null, view);
        if (Scale <= 0) return;
        var matrix = new Matrix(Scale, 0, 0, Scale, Origin.X, Origin.Y);
        dc.PushTransform(new MatrixTransform(matrix));
        var bounds = new Rect(0, 0, W, H);
        if (_backdrop is not null) dc.DrawImage(_backdrop, bounds);
        else if (_map is not null) dc.DrawImage(_map.Terrain, bounds);
        DrawGrid(dc);
        dc.Pop();

        var track = new Pen(Teleport, 1.4) { DashStyle = DashStyles.Dash };
        for (var i = 1; i < _route.Count; i++)
            dc.DrawLine(track, Position(_route[i - 1]), Position(_route[i]));
        foreach (var pin in _pins)
        {
            var point = Position(pin);
            if (!view.Contains(point)) continue;
            DrawMarker(dc, pin, point, _zoom);
        }
        DrawLandmarks(dc, view);
        if (_pins.FirstOrDefault(pin => pin.Id == Selected) is { } selected)
            DrawCallout(dc, view, selected);
        if (_hover is { } hover && hover.Id != Selected)
            DrawCallout(dc, view, hover);
        DrawScale(dc);
    }

    private Point Position(MapPin pin) => ToScreen(new Point(pin.X, pin.Y));

    private void DrawGrid(DrawingContext dc)
    {
        var line = new Pen(GridLine, .55 / Scale);
        for (var x = 0; x <= W; x += 100) dc.DrawLine(line, new Point(x, 0), new Point(x, H));
        for (var y = 0; y <= H; y += 100) dc.DrawLine(line, new Point(0, y), new Point(W, y));
    }

    private static void DrawMarker(DrawingContext dc, MapPin pin, Point at, double zoom)
    {
        var color = pin.Found ? GridLine : pin.Category switch
        {
            "Телепорт" => Teleport,
            "След" => Trace,
            "Сбор" => Gather,
            "Куб" => Cube,
            "Питомец" => Pet,
            _ => Ui.Brush("Text")
        };
        var radius = pin.Category == "Телепорт" ? 5.1 : zoom >= 2 ? 4.0 : 2.7;
        if (pin.Category == "Телепорт")
        {
            var diamond = new StreamGeometry();
            using (var shape = diamond.Open())
            {
                shape.BeginFigure(new Point(at.X, at.Y - radius), true, true);
                shape.PolyLineTo([new Point(at.X + radius, at.Y), new Point(at.X, at.Y + radius),
                    new Point(at.X - radius, at.Y)], true, false);
            }
            dc.DrawGeometry(color, new Pen(Water, 1), diamond);
        }
        else dc.DrawEllipse(color, new Pen(Water, 1), at, radius, radius);
    }

    private void DrawLandmarks(DrawingContext dc, Rect view)
    {
        var occupied = new List<Rect>();
        foreach (var pin in _pins.Where(p => p.Category == "Телепорт" && Landmarks.Contains(p.Name)))
        {
            if (pin.Id == Selected || pin.Id == _hover?.Id) continue;
            var at = Position(pin) + new Vector(8, -9);
            var label = Caption(pin.Name, 11);
            var box = new Rect(at, new Size(label.Width + 10, label.Height + 4));
            if (!view.Contains(box) || occupied.Any(other => other.IntersectsWith(box))) continue;
            occupied.Add(box);
            dc.DrawRoundedRectangle(Plate, null, box, 3, 3);
            dc.DrawText(label, at + new Vector(5, 2));
        }
    }

    private void DrawCallout(DrawingContext dc, Rect view, MapPin pin)
    {
        var point = Position(pin);
        if (!view.Contains(point)) return;
        dc.DrawEllipse(null, new Pen(Ui.Brush("Text"), 1.6), point, 9, 9);
        var label = Caption(pin.Name, 12);
        var x = Math.Clamp(point.X + 12, 5, Math.Max(5, ActualWidth - label.Width - 21));
        var y = Math.Clamp(point.Y - 12, 5, Math.Max(5, ActualHeight - label.Height - 14));
        dc.DrawRoundedRectangle(Plate, new Pen(GridLine, 1), new Rect(x, y, label.Width + 14, label.Height + 8), 5, 5);
        dc.DrawText(label, new Point(x + 7, y + 4));
    }

    private void DrawScale(DrawingContext dc)
    {
        if (_map is not { } map) return;
        const double x = 18;
        var y = ActualHeight - 20;
        var target = map.Metres(95 / Scale);
        var unit = Math.Pow(10, Math.Floor(Math.Log10(target)));
        var distance = target < 2 * unit ? unit : target < 5 * unit ? 2 * unit : 5 * unit;
        var length = distance / map.Metres(1) * Scale;
        var pen = new Pen(Ui.Brush("Text"), 1);
        dc.DrawRoundedRectangle(Plate, null, new Rect(x - 7, y - 24, Math.Max(77, length + 14), 33), 5, 5);
        dc.DrawLine(pen, new Point(x, y), new Point(x + length, y));
        dc.DrawLine(pen, new Point(x, y - 3), new Point(x, y + 3));
        dc.DrawLine(pen, new Point(x + length, y - 3), new Point(x + length, y + 3));
        dc.DrawText(Caption(distance >= 1000 ? $"{distance / 1000:g} км" : $"{distance:g} м", 11), new Point(x, y - 19));
        dc.DrawText(Caption("↑ N", 14), new Point(Math.Max(0, ActualWidth - 43), 12));
    }

    private FormattedText Caption(string value, double size) =>
        new(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI Semibold"),
            size, Ui.Brush("Text"), VisualTreeHelper.GetDpi(this).PixelsPerDip)
        { MaxTextWidth = 244, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };

    private static Brush Swatch(string value)
    {
        var brush = (Brush)new BrushConverter().ConvertFromString(value)!;
        brush.Freeze();
        return brush;
    }

    private MapPin? Hit(Point at)
    {
        var closest = _pins.MinBy(pin => (Position(pin) - at).LengthSquared);
        return closest is not null && (Position(closest) - at).LengthSquared <= 100 ? closest : null;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        Zoom(e.Delta > 0 ? 1.22 : 1 / 1.22, e.GetPosition(this));
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
        var at = e.GetPosition(this);
        if (!IsMouseCaptured)
        {
            var hover = Placing || Scale <= 0 ? null : Hit(at);
            if (hover?.Id == _hover?.Id) return;
            _hover = hover;
            InvalidateVisual();
            return;
        }
        var delta = at - _down;
        if (delta.Length < 4 && !_drag) return;
        _drag = true;
        _hover = null;
        _pan = _atDown + delta;
        KeepInView();
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        _hover = null;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!IsMouseCaptured) return;
        ReleaseMouseCapture();
        if (_drag || Scale <= 0) return;
        var at = e.GetPosition(this);
        var point = ToWorld(at);
        if (point.X is < 0 or > W || point.Y is < 0 or > H) return;
        if (Placing) PinRequested?.Invoke(point);
        else if (Hit(at) is { } marker) PinSelected?.Invoke(marker);
    }
}
