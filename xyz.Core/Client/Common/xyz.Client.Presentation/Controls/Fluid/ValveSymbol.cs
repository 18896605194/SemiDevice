using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace xyz.Client.Presentation.Controls.Fluid;

/// <summary>独立阀门显示控件。开关动画仅表示模拟状态，不持有 DO 或设备命令。</summary>
public sealed class ValveSymbol : Viewbox
{
    private readonly System.Windows.Shapes.Path _body;
    private readonly Line _gate;
    private readonly Ellipse _indicator;
    private readonly RotateTransform _gateRotation = new(90, 18, 18);
    private readonly Line _channel;

    public bool? IsOpen { get => (bool?)GetValue(IsOpenProperty); set => SetValue(IsOpenProperty, value); }
    public static readonly DependencyProperty IsOpenProperty = DependencyProperty.Register(nameof(IsOpen), typeof(bool?), typeof(ValveSymbol),
        new PropertyMetadata(null, (d, e) => ((ValveSymbol)d).Apply(e.OldValue)));
    private static readonly DependencyProperty OpeningProperty = DependencyProperty.Register("Opening", typeof(double), typeof(ValveSymbol),
        new PropertyMetadata(0d, (d, e) => ((ValveSymbol)d)._gateRotation.Angle = 90 * (1 - (double)e.NewValue)));
    internal double DisplayedOpening => (double)GetValue(OpeningProperty);

    public ValveSymbol()
    {
        var grid = new Grid { Width = 36, Height = 36, Background = Brushes.Transparent };
        _channel = new Line { X1 = 0, X2 = 36, Y1 = 18, Y2 = 18, StrokeThickness = 3, Opacity = 0 };
        _body = new System.Windows.Shapes.Path { Data = Geometry.Parse("M 1,8 L 18,18 L 1,28 Z M 35,8 L 18,18 L 35,28 Z"), StrokeThickness = 2 };
        _body.SetResourceReference(Shape.FillProperty, "DarkControlBackground");
        _gate = new Line { X1 = 9, X2 = 27, Y1 = 18, Y2 = 18, StrokeThickness = 3, RenderTransform = _gateRotation };
        var stem = new Line { X1 = 18, X2 = 18, Y1 = 18, Y2 = 4, StrokeThickness = 2 };
        stem.SetResourceReference(Shape.StrokeProperty, "DarkDisabledText");
        _indicator = new Ellipse { Width = 7, Height = 7, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
        grid.Children.Add(_body); grid.Children.Add(_channel); grid.Children.Add(stem); grid.Children.Add(_gate); grid.Children.Add(_indicator);
        Child = grid;
        IsVisibleChanged += (_, _) => { if (!IsVisible) BeginAnimation(OpeningProperty, null); };
        Apply(null);
    }

    private void Apply(object? before)
    {
        string key = IsOpen switch { true => "DarkSignalLampOn", false => "DarkDisabledText", _ => "DarkSignalLampUnknown" };
        foreach (var shape in new Shape[] { _body, _gate, _channel }) shape.SetResourceReference(Shape.StrokeProperty, key);
        _indicator.SetResourceReference(Shape.FillProperty, key);
        _channel.Opacity = IsOpen == true ? 0.7 : 0;
        _gate.StrokeDashArray = IsOpen is null ? new DoubleCollection { 1, 1 } : null;
        double target = IsOpen switch { true => 1, false => 0, _ => 0.5 };
        double current = DisplayedOpening;
        BeginAnimation(OpeningProperty, null); SetValue(OpeningProperty, target);
        if (IsVisible && !Equals(before, IsOpen))
            BeginAnimation(OpeningProperty, new DoubleAnimation(current, target, TimeSpan.FromMilliseconds(180)) { FillBehavior = FillBehavior.Stop });
    }
}
