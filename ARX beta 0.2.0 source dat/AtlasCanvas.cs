using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

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
    private MapPin? _focusBeforeLayout;
    private readonly DispatcherTimer _detailTimer = new() { Interval = TimeSpan.FromMilliseconds(95) };
    private DetailRequest? _requestedDetail;
    private DetailRequest? _renderedDetail;
    private BitmapSource? _detail;
    private bool _renderingDetail;

    private sealed record DetailRequest(AtlasCatalog.AtlasMap Map, Rect Area, int Width, int Height);

    public bool Placing { get; set; }
    public bool Compact { get; init; }
    public Guid? Selected { get; set; }
    public event Action<MapPin>? PinSelected;
    public event Action<Point>? PinRequested;

    public AtlasCanvas()
    {
        ClipToBounds = true;
        Cursor = Cursors.Hand;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        _detailTimer.Tick += (_, _) => RenderDetail();
        Loaded += (_, _) => QueueDetail();
        Unloaded += (_, _) => ClearDetail();
    }

    public void SetLayer(AtlasCatalog.AtlasMap? map, ImageSource? backdrop)
    {
        _map = map;
        _backdrop = backdrop;
        ClearDetail();
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
        _focusBeforeLayout = null;
        _zoom = 1;
        _pan = default;
        QueueDetail();
        InvalidateVisual();
    }

    public void FocusPin(MapPin pin)
    {
        if (Scale <= 0)
        {
            _focusBeforeLayout = pin;
            return;
        }
        _focusBeforeLayout = null;
        _zoom = Math.Max(2.8, _zoom);
        _pan = new Vector((W / 2 - pin.X) * Scale, (H / 2 - pin.Y) * Scale);
        KeepInView();
        QueueDetail();
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
        QueueDetail();
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
        if (_focusBeforeLayout is { } pin && Scale > 0)
        {
            FocusPin(pin);
            return;
        }
        KeepInView();
        QueueDetail();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        QueueDetail();
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
        else if (_map is { } map) DrawTerrain(dc, map, bounds);
        DrawGrid(dc);
        dc.Pop();

        var track = new Pen(Teleport, 1.4) { DashStyle = DashStyles.Dash };
        for (var i = 1; i < _route.Count; i++)
            dc.DrawLine(track, Position(_route[i - 1]), Position(_route[i]));
        foreach (var pin in ScreenPins(view))
        {
            var point = Position(pin);
            DrawMarker(dc, pin, point, _zoom, Compact);
        }
        if (!Compact || _zoom >= 2.8) DrawLandmarks(dc, view);
        if (_pins.FirstOrDefault(pin => pin.Id == Selected) is { } selected)
            DrawCallout(dc, view, selected);
        if (_hover is { } hover && hover.Id != Selected)
            DrawCallout(dc, view, hover);
        DrawScale(dc);
    }

    private void DrawTerrain(DrawingContext dc, AtlasCatalog.AtlasMap map, Rect bounds)
    {
        if (_renderedDetail is { } current && _detail is { } detail)
        {
            dc.PushClip(new CombinedGeometry(GeometryCombineMode.Exclude,
                new RectangleGeometry(bounds), new RectangleGeometry(current.Area)));
            dc.DrawImage(map.Terrain, bounds);
            dc.Pop();
            dc.DrawImage(detail, current.Area);
        }
        else dc.DrawImage(map.Terrain, bounds);
    }

    private void QueueDetail()
    {
        if (_map is not { } map || _backdrop is not null || !IsLoaded || Scale <= 0)
        {
            ClearDetail();
            return;
        }
        var bounds = new Rect(0, 0, W, H);
        var dpi = VisualTreeHelper.GetDpi(this);
        var visible = new Rect(ToWorld(new Point()), ToWorld(new Point(ActualWidth, ActualHeight)));
        visible.Intersect(bounds);
        if (visible.IsEmpty || visible.Width <= 0 || visible.Height <= 0)
        {
            ClearDetail();
            return;
        }

        // Keep a little extra area in the cache so short drags can reuse the same sharp image.
        // Supersample the overview too. The small-scale contours must survive reduction to the display.
        const double samplesPerPixel = 1.5;
        var scaleX = Scale * dpi.DpiScaleX * samplesPerPixel;
        var scaleY = Scale * dpi.DpiScaleY * samplesPerPixel;
        if (_renderedDetail is { } rendered && rendered.Area.Contains(visible) &&
            Math.Abs(rendered.Width / rendered.Area.Width - scaleX) <= scaleX * .04 &&
            Math.Abs(rendered.Height / rendered.Area.Height - scaleY) <= scaleY * .04)
        {
            _requestedDetail = rendered;
            _detailTimer.Stop();
            return;
        }
        visible.Inflate(visible.Width * .10, visible.Height * .10);
        visible.Intersect(bounds);
        var request = new DetailRequest(map, visible, Math.Clamp((int)Math.Ceiling(visible.Width * scaleX), 1, 4096),
            Math.Clamp((int)Math.Ceiling(visible.Height * scaleY), 1, 3072));
        if (request == _requestedDetail) return;
        _requestedDetail = request;
        _detailTimer.Stop();
        _detailTimer.Start();
    }

    private async void RenderDetail()
    {
        _detailTimer.Stop();
        if (_renderingDetail || _requestedDetail is not { } request || request == _renderedDetail) return;
        _renderingDetail = true;
        try
        {
            var area = request.Area;
            var fraction = new Rect(area.X / W, area.Y / H, area.Width / W, area.Height / H);
            var map = request.Map;
            var bitmap = await Task.Run(() => AtlasTerrain.Render(map.Relief, map.Columns, map.Rows, fraction,
                request.Width, request.Height));
            if (_requestedDetail != request || !IsLoaded) return;
            _detail = bitmap;
            _renderedDetail = request;
            InvalidateVisual();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
        finally
        {
            _renderingDetail = false;
            if (_requestedDetail is { } next && next != request && next != _renderedDetail && IsLoaded)
                _detailTimer.Start();
        }
    }

    private void ClearDetail()
    {
        _detailTimer.Stop();
        _requestedDetail = null;
        _renderedDetail = null;
        _detail = null;
    }

    private Point Position(MapPin pin) => ToScreen(new Point(pin.X, pin.Y));

    private void DrawGrid(DrawingContext dc)
    {
        var line = new Pen(GridLine, .55 / Scale);
        for (var x = 0; x <= W; x += 100) dc.DrawLine(line, new Point(x, 0), new Point(x, H));
        for (var y = 0; y <= H; y += 100) dc.DrawLine(line, new Point(0, y), new Point(W, y));
    }

    private static void DrawMarker(DrawingContext dc, MapPin pin, Point at, double zoom, bool compact)
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
        if (compact) radius *= .75;
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
        { MaxTextWidth = Compact ? Math.Clamp(ActualWidth - 30, 40, 180) : 244,
            MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };

    private static Brush Swatch(string value)
    {
        var brush = (Brush)new BrushConverter().ConvertFromString(value)!;
        brush.Freeze();
        return brush;
    }

    private MapPin? Hit(Point at)
    {
        var closest = ScreenPins(new Rect(0, 0, ActualWidth, ActualHeight))
            .MinBy(pin => (Position(pin) - at).LengthSquared);
        return closest is not null && (Position(closest) - at).LengthSquared <= 100 ? closest : null;
    }

    private IEnumerable<MapPin> ScreenPins(Rect view)
    {
        if (!Compact) return _pins.Where(pin => view.Contains(Position(pin)));

        // Keep crowded zones legible on the small canvas. The search still lists every matching pin.
        const double spacing = 11;
        var cells = new Dictionary<(int X, int Y), MapPin>();
        foreach (var pin in _pins)
        {
            var point = Position(pin);
            if (!view.Contains(point)) continue;
            var cell = ((int)(point.X / spacing), (int)(point.Y / spacing));
            if (!cells.TryGetValue(cell, out var other) || Priority(pin) > Priority(other))
                cells[cell] = pin;
        }
        return cells.Values;
    }

    private int Priority(MapPin pin) => pin.Id == Selected ? 4 : pin.Category switch
    {
        "Заметка" => 3,
        "Телепорт" => 2,
        _ => pin.Found ? 0 : 1
    };

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
        QueueDetail();
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
