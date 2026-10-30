using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using xyz.Client.Presentation.Controls.ThreeD;

namespace CdsAssemblyPreview;

/// <summary>管路加热器预览，与平台硬件共用金属灰、动作高亮和故障反馈。</summary>
public sealed class HeaterPreviewVisual3D : HardwareVisual3D
{
    private GeometryModel3D _shell = null!;
    private GeometryModel3D _coil = null!;
    private double _phase;
    public double? Temperature { get => (double?)GetValue(TemperatureProperty); set => SetValue(TemperatureProperty, value); }
    public static readonly DependencyProperty TemperatureProperty = DependencyProperty.Register(nameof(Temperature), typeof(double?), typeof(HeaterPreviewVisual3D), new PropertyMetadata(null), IsFinite);
    public double? TargetTemperature { get => (double?)GetValue(TargetTemperatureProperty); set => SetValue(TargetTemperatureProperty, value); }
    public static readonly DependencyProperty TargetTemperatureProperty = DependencyProperty.Register(nameof(TargetTemperature), typeof(double?), typeof(HeaterPreviewVisual3D), new PropertyMetadata(null), IsFinite);
    private static bool IsFinite(object value) => value is null || value is double number && double.IsFinite(number);
    public bool IsHeating { get => (bool)GetValue(IsHeatingProperty); set => SetValue(IsHeatingProperty, value); }
    public static readonly DependencyProperty IsHeatingProperty = DependencyProperty.Register(nameof(IsHeating), typeof(bool), typeof(HeaterPreviewVisual3D), new PropertyMetadata(false, Changed));
    public double PowerFraction { get => (double)GetValue(PowerFractionProperty); set => SetValue(PowerFractionProperty, value); }
    public static readonly DependencyProperty PowerFractionProperty = DependencyProperty.Register(nameof(PowerFraction), typeof(double), typeof(HeaterPreviewVisual3D), new PropertyMetadata(0.65d, Changed), value => value is double number && double.IsFinite(number) && number is >= 0 and <= 1);
    public bool ShowInternals { get => (bool)GetValue(ShowInternalsProperty); set => SetValue(ShowInternalsProperty, value); }
    public static readonly DependencyProperty ShowInternalsProperty = DependencyProperty.Register(nameof(ShowInternals), typeof(bool), typeof(HeaterPreviewVisual3D), new PropertyMetadata(true, Changed));
    internal double HeatPhase => _phase;
    public HeaterPreviewVisual3D()
    {
        AddMesh(PreviewMesh3D.Box(new Point3D(-1.6, 0, -0.58), 3.2, 0.13, 1.16), 0.7);
        foreach (double x in new[] { -1.0, 1.0 })
        {
            AddMesh(PreviewMesh3D.Box(new Point3D(x - 0.14, 0.13, -0.3), 0.28, 0.42, 0.6), 0.85);
            AddMesh(PreviewMesh3D.Tube(new Point3D(x - 0.07, 1.0, 0), new Point3D(x + 0.07, 1.0, 0), 0.55, 0.48), 1.2);
        }
        foreach (int side in new[] { -1, 1 })
        {
            double x = side * 1.4;
            AddMesh(PreviewMesh3D.Tube(new Point3D(x - 0.07, 1.0, 0), new Point3D(x + 0.07, 1.0, 0), 0.58, 0.16), 1.1);
            AddMesh(PreviewMesh3D.Tube(new Point3D(side * 1.46, 1.0, 0), new Point3D(side * 2.0, 1.0, 0), 0.21, 0.145), 1.1);
            AddMesh(PreviewMesh3D.Tube(new Point3D(side * 1.91, 1.0, 0), new Point3D(side * 2.04, 1.0, 0), 0.31, 0.145), 1.3);
            for (int i = 0; i < 6; i++)
            {
                double a = i * Math.Tau / 6;
                AddMesh(PreviewMesh3D.Tube(new Point3D(x - 0.1, 1.0 + Math.Cos(a) * 0.49, Math.Sin(a) * 0.49), new Point3D(x + 0.1, 1.0 + Math.Cos(a) * 0.49, Math.Sin(a) * 0.49), 0.04, 0, 6), 1.35);
            }
        }
        AddMesh(PreviewMesh3D.Box(new Point3D(-0.30, 1.45, -0.29), 0.6, 0.36, 0.58), 1.12);
        AddMesh(PreviewMesh3D.Tube(new Point3D(0, 1.4, 0), new Point3D(0, 1.48, 0), 0.16), 1.2);
        _coil = new GeometryModel3D { Geometry = PreviewMesh3D.Coil(-1.22, 1.22, 1.0, 0.30, 0.035, 8) }; Model.Children.Add(_coil);
        _shell = new GeometryModel3D { Geometry = PreviewMesh3D.Tube(new Point3D(-1.38, 1.0, 0), new Point3D(1.38, 1.0, 0), 0.50, 0.45) }; Model.Children.Add(_shell);
        RefreshFeedback();
    }
    private static void Changed(DependencyObject d, DependencyPropertyChangedEventArgs _) => ((HeaterPreviewVisual3D)d).RefreshFeedback();
    protected override void OnIsErrorChanged() => RefreshFeedback();
    internal void RefreshFeedback()
    {
        SetVisualActive(IsHeating && !IsError);
        if (_shell is null || _coil is null) return;
        Color shell = IsError ? Color.FromRgb(239, 119, 114) : BodyColor; shell.A = ShowInternals ? (byte)36 : (byte)255;
        var outer = new DiffuseMaterial(new SolidColorBrush(shell)); outer.Freeze(); _shell.Material = outer; _shell.BackMaterial = outer;
        double glow = IsHeating && !IsError ? PowerFraction * (0.8 + 0.15 * Math.Sin(_phase)) : 0;
        Color c = IsError ? Color.FromRgb(239, 119, 114) : Color.FromRgb((byte)(110 + 130 * glow), (byte)(112 + 50 * glow), (byte)(115 - 60 * glow));
        var material = new MaterialGroup(); material.Children.Add(new DiffuseMaterial(new SolidColorBrush(c)));
        if (glow > 0) material.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromArgb((byte)(90 * glow), c.R, c.G, c.B))));
        material.Freeze(); _coil.Material = material; _coil.BackMaterial = material;
    }
    internal void Advance(double seconds) { if (IsHeating && !IsError && PowerFraction > 0) { _phase += seconds * 3; RefreshFeedback(); } }
}
