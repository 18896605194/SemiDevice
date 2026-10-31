using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace xyz.Client.Presentation.Controls.Fluid;

/// <summary>原创的简化硬件外观；几何和动画均为显示用途。</summary>
internal sealed class FluidHardwareDrawing : FrameworkElement
{
    private readonly DispatcherTimer _timer;
    private double _phase;
    public FluidHardwareKind Kind { get; set; }
    public HardwareState State { get; set; }
    public double? Reading { get; set; }
    public double? Target { get; set; }
    public string Unit { get; set; } = "";
    internal bool AnimationRunning => _timer.IsEnabled;
    private Brush _ink = Brushes.Gray;
    private Brush _signal = Brushes.Gray;
    private readonly Brush _metal = new LinearGradientBrush(new GradientStopCollection
    { new(Color.FromRgb(151, 162, 170), 0), new(Color.FromRgb(76, 88, 99), 0.28), new(Color.FromRgb(42, 53, 63), 0.7), new(Color.FromRgb(106, 119, 129), 1) }, new Point(0, 0), new Point(1, 1));
    private readonly Brush _panel = new SolidColorBrush(Color.FromRgb(22, 31, 39));

    public FluidHardwareDrawing()
    {
        IsHitTestVisible = false;
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(60) };
        _timer.Tick += (_, _) => { _phase = (_phase + 14) % 360; InvalidateVisual(); };
        IsVisibleChanged += (_, _) => Refresh();
    }
    public void Refresh()
    {
        bool animated = Kind is FluidHardwareKind.CentrifugalPump or FluidHardwareKind.DiaphragmPump or FluidHardwareKind.MeteringPump
            or FluidHardwareKind.Mixer or FluidHardwareKind.InlineHeater or FluidHardwareKind.ImmersionHeater;
        if (IsVisible && State == HardwareState.Active && animated) _timer.Start(); else _timer.Stop();
        InvalidateVisual();
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        double scale = Math.Min(ActualWidth / 260, ActualHeight / 175);
        dc.PushTransform(new TranslateTransform((ActualWidth - 260 * scale) / 2, (ActualHeight - 175 * scale) / 2));
        dc.PushTransform(new ScaleTransform(scale, scale));
        _ink = Resource("DarkDisabledText", Brushes.Gray);
        _signal = Resource(State switch { HardwareState.Fault => "DarkAlarmStatus", HardwareState.Active => "DarkSignalLampOn", _ => "DarkDisabledText" }, Brushes.LightSkyBlue);
        if (State == HardwareState.Unknown) dc.PushOpacity(0.55);
        Ports(dc);
        switch (Kind)
        {
            case FluidHardwareKind.CentrifugalPump: Pump(dc, 0); break;
            case FluidHardwareKind.DiaphragmPump: Pump(dc, 1); break;
            case FluidHardwareKind.MeteringPump: Pump(dc, 2); break;
            case FluidHardwareKind.Mixer: Mixer(dc); break;
            case FluidHardwareKind.InlineHeater: Heater(dc, false); break;
            case FluidHardwareKind.ImmersionHeater: Heater(dc, true); break;
            case FluidHardwareKind.HeatExchanger: Exchanger(dc); break;
            case FluidHardwareKind.ManualValve:
            case FluidHardwareKind.ProportionalValve:
            case FluidHardwareKind.ThreeWayValve: Valve(dc); break;
            case FluidHardwareKind.CheckValve: CheckValve(dc); break;
            case FluidHardwareKind.PressureRegulator:
            case FluidHardwareKind.ReliefValve: Regulator(dc); break;
            case FluidHardwareKind.Filter: Filter(dc); break;
            case FluidHardwareKind.PulseDamper: Damper(dc); break;
            case FluidHardwareKind.FlowMeter: FlowMeter(dc); break;
            case FluidHardwareKind.PressureGauge: Gauge(dc); break;
            case FluidHardwareKind.TemperatureSensor: Probe(dc, "T"); break;
            case FluidHardwareKind.LevelSensor: Level(dc); break;
            case FluidHardwareKind.ConductivitySensor: Probe(dc, "EC"); break;
            case FluidHardwareKind.PhSensor: Probe(dc, "pH"); break;
        }
        if (Unit.Length > 0 && Kind != FluidHardwareKind.FlowMeter)
            Text(dc, HardwareVisual.FormatReading(Reading, Unit, State), 130, 145, 18, Resource("DarkPrimaryText", Brushes.White));
        if (Target.HasValue) Text(dc, "SP " + Target.Value.ToString("0.##", CultureInfo.InvariantCulture) + " " + Unit, 130, 164, 11, _ink);
        if (State == HardwareState.Fault) { dc.DrawEllipse(_signal, null, new Point(231, 30), 9, 9); Text(dc, "!", 231, 22, 14, Brushes.White); }
        if (State == HardwareState.Unknown) dc.Pop(); dc.Pop(); dc.Pop();
    }
    private Brush Resource(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;
    private static Pen Pen(Brush brush, double width = 2) => new(brush, width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
    private void Line(DrawingContext dc, double x1, double y1, double x2, double y2, Brush? brush = null, double width = 2) => dc.DrawLine(Pen(brush ?? _ink, width), new Point(x1, y1), new Point(x2, y2));
    private void Shape(DrawingContext dc, string path, Brush? fill = null, Brush? stroke = null, double width = 2) => dc.DrawGeometry(fill, Pen(stroke ?? _ink, width), Geometry.Parse(path));
    private void Body(DrawingContext dc, double x, double y, double w, double h, double r = 5) => dc.DrawRoundedRectangle(_metal, Pen(_ink), new Rect(x, y, w, h), r, r);
    private void Text(DrawingContext dc, string value, double x, double y, double size, Brush brush)
    {
        var text = new FormattedText(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(text, new Point(x - text.Width / 2, y));
    }
    private void Ports(DrawingContext dc)
    {
        if (Kind is FluidHardwareKind.Mixer or FluidHardwareKind.ImmersionHeater or FluidHardwareKind.LevelSensor or FluidHardwareKind.LeakSensor) return;
        Line(dc, 20, 95, 240, 95, _metal, 11);
        Line(dc, 20, 95, 240, 95, State == HardwareState.Active ? _signal : _panel, 3);
        foreach (double x in new[] { 24d, 236d }) { Line(dc, x, 87, x, 103, _ink, 4); }
    }
    private void Rotor(DrawingContext dc, double x, double y, double radius)
    {
        dc.PushTransform(new RotateTransform(State == HardwareState.Active ? _phase : 0, x, y));
        for (int i = 0; i < 3; i++)
        {
            dc.PushTransform(new RotateTransform(i * 120, x, y));
            dc.DrawEllipse(_signal, null, new Point(x + radius * 0.5, y), radius * 0.48, radius * 0.19); dc.Pop();
        }
        dc.DrawEllipse(_panel, Pen(_ink), new Point(x, y), 5, 5); dc.Pop();
    }
    private void Pump(DrawingContext dc, int type)
    {
        if (type == 0)
        {
            Body(dc, 146, 74, 55, 43); for (int x = 156; x < 196; x += 8) Line(dc, x, 79, x, 112);
            dc.DrawEllipse(_metal, Pen(_ink, 3), new Point(111, 95), 37, 37);
            dc.DrawEllipse(_panel, Pen(_ink), new Point(111, 95), 27, 27); Rotor(dc, 111, 95, 22);
            Shape(dc, "M 90,130 L 82,139 L 190,139 L 180,117", null, _ink, 3);
        }
        else if (type == 1)
        {
            Body(dc, 116, 52, 28, 76); Body(dc, 104, 35, 52, 20);
            foreach (double x in new[] { 91d, 169d })
            {
                dc.DrawEllipse(_metal, Pen(_ink, 3), new Point(x, 94), 26, 32);
                double push = State == HardwareState.Active ? Math.Sin(_phase * Math.PI / 180) * 5 : 0;
                Line(dc, x + (x < 130 ? push : -push), 70, x + (x < 130 ? push : -push), 118, _signal, 3);
            }
            Line(dc, 91, 132, 169, 132, _ink, 5);
        }
        else
        {
            Body(dc, 72, 70, 85, 60); Body(dc, 166, 83, 28, 34); Line(dc, 157, 100, 166, 100, _signal, 5);
            dc.DrawEllipse(_panel, Pen(_ink), new Point(113, 96), 20, 20); Rotor(dc, 113, 96, 17);
            Body(dc, 86, 39, 56, 25); Text(dc, "P", 114, 40, 15, _signal);
            Shape(dc, "M 178,83 L 178,68 L 198,68 M 178,117 L 178,132 L 198,132", null, _ink, 3);
        }
    }
    private void Mixer(DrawingContext dc)
    {
        Body(dc, 109, 18, 42, 28); Line(dc, 130, 46, 130, 122, _ink, 5);
        Shape(dc, "M 75,65 L 75,133 Q 130,150 185,133 L 185,65", _panel, _ink, 3);
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(90, 96, 188, 222)), null, new Rect(79, 98, 102, 31));
        Line(dc, 130, 46, 130, 123, _ink, 4); dc.PushTransform(new ScaleTransform(1, 0.35, 130, 119)); Rotor(dc, 130, 119, 37); dc.Pop();
    }
    private void Heater(DrawingContext dc, bool immersion)
    {
        Brush heat = State == HardwareState.Active ? Brushes.Orange : _signal;
        if (State == HardwareState.Fault) heat = _signal;
        if (!immersion)
        {
            Body(dc, 67, 65, 126, 62, 12); Body(dc, 102, 41, 56, 21);
            Shape(dc, "M 78,96 L 88,79 L 101,112 L 115,79 L 129,112 L 143,79 L 157,112 L 171,79 L 182,96", null, heat, 3);
        }
        else
        {
            Shape(dc, "M 70,76 L 70,133 Q 130,145 190,133 L 190,76", _panel, _ink, 2);
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(75, 96, 188, 222)), null, new Rect(74, 99, 112, 31));
            Body(dc, 101, 21, 58, 25); Shape(dc, "M 113,46 L 113,116 Q 130,140 147,116 L 147,46", null, heat, 5);
        }
        if (State == HardwareState.Active)
        {
            dc.PushOpacity(0.45 + 0.4 * Math.Abs(Math.Sin(_phase * Math.PI / 180)));
            foreach (double x in new[] { 88d, 174d }) Shape(dc, $"M {x},58 Q {x - 7},48 {x},40 Q {x + 7},30 {x},24", null, heat, 2);
            dc.Pop();
        }
    }
    private void Exchanger(DrawingContext dc)
    {
        Body(dc, 69, 55, 122, 78, 10);
        for (int x = 86; x < 178; x += 14) Line(dc, x, 63, x, 125, _ink);
        Shape(dc, "M 35,73 L 80,73 L 96,115 L 112,73 L 128,115 L 144,73 L 160,115 L 180,73 L 225,73", null, Brushes.Orange, 3);
        Shape(dc, "M 35,116 L 80,116 L 96,77 L 112,116 L 128,77 L 144,116 L 160,77 L 180,116 L 225,116", null, Brushes.LightSkyBlue, 3);
    }
    private void Valve(DrawingContext dc)
    {
        Shape(dc, "M 94,74 L 130,95 L 94,116 Z M 166,74 L 130,95 L 166,116 Z", _panel, _signal, 3);
        Line(dc, 130, 95, 130, 47, _signal, 3);
        if (Kind == FluidHardwareKind.ManualValve) { dc.DrawEllipse(_metal, Pen(_ink), new Point(130, 40), 26, 8); Line(dc, 106, 40, 154, 40); }
        else if (Kind == FluidHardwareKind.ProportionalValve) { Body(dc, 107, 25, 46, 26); Text(dc, "~", 130, 21, 24, _signal); }
        else { Shape(dc, "M 109,130 L 130,95 L 151,130 Z", _panel, _signal, 3); Line(dc, 130, 130, 130, 140, _metal, 9); Body(dc, 110, 26, 40, 23); }
        if (State == HardwareState.Stopped) Line(dc, 130, 81, 130, 109, _signal, 4);
    }
    private void CheckValve(DrawingContext dc)
    {
        Body(dc, 81, 68, 98, 54); Shape(dc, "M 108,78 L 145,95 L 108,112 Z", _panel, _signal, 3); Line(dc, 149, 75, 149, 115, _signal, 3);
    }
    private void Regulator(DrawingContext dc)
    {
        Shape(dc, "M 94,75 L 130,95 L 94,115 Z M 166,75 L 130,95 L 166,115 Z", _metal, _ink, 2);
        Body(dc, 107, 57, 46, 17); Shape(dc, "M 130,57 L 120,51 L 140,45 L 120,39 L 140,33 L 130,27", null, _signal, 2);
        Line(dc, 107, 23, 153, 23, _ink, 4);
        if (Kind == FluidHardwareKind.ReliefValve) Shape(dc, "M 130,95 L 130,133 L 187,133 M 179,127 L 187,133 L 179,139", null, _ink, 3);
        else { Line(dc, 130, 95, 130, 65, _signal, 3); dc.DrawEllipse(_panel, Pen(_signal), new Point(130, 95), 7, 7); }
    }
    private void Filter(DrawingContext dc)
    {
        Body(dc, 97, 51, 66, 80, 13); Body(dc, 91, 44, 78, 16, 4);
        dc.PushClip(new RectangleGeometry(new Rect(106, 64, 48, 58)));
        for (int y = 30; y < 160; y += 13) { Line(dc, 106, y, 154, y + 38, _signal); Line(dc, 154, y, 106, y + 38, _ink, 1); }
        dc.Pop(); Text(dc, "ΔP", 130, 18, 14, _ink);
    }
    private void Damper(DrawingContext dc)
    {
        Body(dc, 102, 35, 56, 94, 26); Shape(dc, "M 105,82 Q 130,67 155,82", null, _signal, 3);
        Line(dc, 130, 129, 130, 139, _metal, 7); Line(dc, 120, 31, 140, 31, _ink, 3);
    }
    private void FlowMeter(DrawingContext dc)
    {
        Body(dc, 76, 73, 108, 49, 9); Body(dc, 94, 19, 72, 46, 5); Line(dc, 130, 65, 130, 73, _ink, 5);
        dc.DrawRoundedRectangle(_panel, Pen(_ink, 1), new Rect(100, 25, 60, 33), 3, 3);
        Text(dc, State == HardwareState.Unknown || Reading is null ? "—" : Reading.Value.ToString("0.##", CultureInfo.InvariantCulture), 130, 27, 18, _signal);
        Text(dc, Unit, 130, 48, 9, _ink); Shape(dc, "M 107,96 L 151,96 M 142,87 L 151,96 L 142,105", null, _signal, 3);
        Text(dc, HardwareVisual.FormatReading(Reading, Unit, State), 130, 145, 18, Resource("DarkPrimaryText", Brushes.White));
    }
    private void Gauge(DrawingContext dc)
    {
        Line(dc, 130, 94, 130, 130, _metal, 8); dc.DrawEllipse(_metal, Pen(_ink, 3), new Point(130, 64), 35, 35);
        dc.DrawEllipse(_panel, Pen(_ink), new Point(130, 64), 28, 28);
        for (int i = 0; i < 7; i++) { double a = (145 + i * 40) * Math.PI / 180; Line(dc, 130 + Math.Cos(a) * 21, 64 + Math.Sin(a) * 21, 130 + Math.Cos(a) * 25, 64 + Math.Sin(a) * 25, _ink, 1); }
        double angle = (145 + Math.Clamp((Reading ?? 0) / 2, 0, 1) * 240) * Math.PI / 180;
        Line(dc, 130, 64, 130 + Math.Cos(angle) * 21, 64 + Math.Sin(angle) * 21, _signal, 3); dc.DrawEllipse(_signal, null, new Point(130, 64), 3, 3);
    }
    private void Probe(DrawingContext dc, string tag)
    {
        Body(dc, 108, 23, 44, 34); Text(dc, tag, 130, 27, 17, _signal);
        Line(dc, 130, 57, 130, 121, _metal, 9); Line(dc, 118, 69, 142, 69, _ink, 4);
        if (Kind == FluidHardwareKind.PhSensor) dc.DrawEllipse(_signal, Pen(_ink), new Point(130, 123), 6, 9);
        else if (Kind == FluidHardwareKind.ConductivitySensor) { Line(dc, 123, 109, 123, 132, _signal, 3); Line(dc, 137, 109, 137, 132, _signal, 3); }
        else { dc.DrawEllipse(_signal, null, new Point(130, 120), 6, 6); }
    }
    private void Level(DrawingContext dc)
    {
        Shape(dc, "M 75,47 L 75,131 Q 130,143 185,131 L 185,47", _panel, _ink, 2);
        double fill = State == HardwareState.Unknown ? 0 : Math.Clamp((Reading ?? 0) / 100, 0, 1);
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(95, 96, 188, 222)), null, new Rect(79, 130 - fill * 76, 102, fill * 76));
        Body(dc, 115, 25, 30, 20); Line(dc, 130, 45, 130, 127, _ink, 4);
        for (int y = 58; y <= 126; y += 17) Line(dc, 152, y, 164, y, _ink, 1);
        dc.DrawEllipse(_signal, Pen(_ink), new Point(130, 130 - fill * 76), 13, 5);
    }
}
