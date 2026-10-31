using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using xyz.Client.Presentation.Controls.ThreeD;

namespace xyz.Client.Presentation.Controls.Fluid;

/// <summary>与平台硬件共用基类和材质反馈的泵预览。原点在底座中心，Y向上。</summary>
public sealed class PumpPreviewVisual3D : HardwareVisual3D
{
    private readonly AxisAngleRotation3D _rotor = new(new Vector3D(1, 0, 0), 0);
    private GeometryModel3D _cover = null!;
    public bool IsRunning { get => (bool)GetValue(IsRunningProperty); set => SetValue(IsRunningProperty, value); }
    public static readonly DependencyProperty IsRunningProperty = DependencyProperty.Register(nameof(IsRunning), typeof(bool), typeof(PumpPreviewVisual3D), new PropertyMetadata(false, Changed));
    public double Speed { get => (double)GetValue(SpeedProperty); set => SetValue(SpeedProperty, value); }
    public static readonly DependencyProperty SpeedProperty = DependencyProperty.Register(nameof(Speed), typeof(double), typeof(PumpPreviewVisual3D), new PropertyMetadata(1450d), value => value is double speed && double.IsFinite(speed) && speed is >= 0 and <= 10000);
    public bool ShowInternals { get => (bool)GetValue(ShowInternalsProperty); set => SetValue(ShowInternalsProperty, value); }
    public static readonly DependencyProperty ShowInternalsProperty = DependencyProperty.Register(nameof(ShowInternals), typeof(bool), typeof(PumpPreviewVisual3D), new PropertyMetadata(true, Changed));
    internal double RotorAngle => _rotor.Angle;

    public PumpPreviewVisual3D()
    {
        AddMesh(PreviewMesh3D.Box(new Point3D(-1.85, 0, -0.7), 3.5, 0.16, 1.4), 0.7);
        foreach (double x in new[] { -1.2, 0.8 }) AddMesh(PreviewMesh3D.Box(new Point3D(x - 0.18, 0.16, -0.38), 0.36, 0.42, 0.76), 0.9);
        AddMesh(PreviewMesh3D.Tube(new Point3D(-0.15, 0.99, 0), new Point3D(1.2, 0.99, 0), 0.46), 1.05);
        AddMesh(PreviewMesh3D.Tube(new Point3D(1.2, 0.99, 0), new Point3D(1.39, 0.99, 0), 0.51), 0.85);
        for (int i = 0; i < 12; i++)
        {
            var fin = AddMesh(PreviewMesh3D.Box(new Point3D(-0.08, 0.96, 0.43), 1.23, 0.055, 0.13, 0.015), 1.12);
            fin.Transform = new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), i * 30), 0, 0.99, 0);
        }
        AddMesh(PreviewMesh3D.Box(new Point3D(0.2, 1.43, -0.28), 0.53, 0.29, 0.56), 1.15);
        AddMesh(PreviewMesh3D.Tube(new Point3D(-0.94, 0.99, 0), new Point3D(-0.15, 0.99, 0), 0.15), 1.35);
        AddMesh(PreviewMesh3D.Tube(new Point3D(-1.48, 0.99, 0), new Point3D(-0.96, 0.99, 0), 0.63, 0.48), 1.0);
        AddMesh(PreviewMesh3D.Tube(new Point3D(-0.99, 0.99, 0), new Point3D(-0.93, 0.99, 0), 0.62), 0.82);
        for (int i = 0; i < 3; i++)
        {
            var blade = AddMesh(PreviewMesh3D.Box(new Point3D(-1.28, 1.08, -0.09), 0.10, 0.34, 0.18, 0.025), 1.45);
            var transform = new Transform3DGroup(); transform.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), i * 120), -1.23, 0.99, 0));
            transform.Children.Add(new RotateTransform3D(_rotor, -1.23, 0.99, 0)); blade.Transform = transform;
        }
        AddMesh(PreviewMesh3D.Tube(new Point3D(-1.32, 0.99, 0), new Point3D(-1.15, 0.99, 0), 0.12), 1.3);
        AddMesh(PreviewMesh3D.Tube(new Point3D(-2.02, 0.99, 0), new Point3D(-1.48, 0.99, 0), 0.21, 0.145), 1.15);
        AddMesh(PreviewMesh3D.Tube(new Point3D(-2.04, 0.99, 0), new Point3D(-1.91, 0.99, 0), 0.32, 0.145), 1.25);
        AddMesh(PreviewMesh3D.Tube(new Point3D(-1.21, 1.48, 0), new Point3D(-1.21, 2.0, 0), 0.20, 0.14), 1.1);
        AddMesh(PreviewMesh3D.Tube(new Point3D(-1.21, 1.91, 0), new Point3D(-1.21, 2.04, 0), 0.31, 0.14), 1.25);
        for (int i = 0; i < 8; i++)
        {
            double a = i * Math.Tau / 8;
            AddMesh(PreviewMesh3D.Tube(new Point3D(-1.54, 0.99 + Math.Cos(a) * 0.55, Math.Sin(a) * 0.55), new Point3D(-1.46, 0.99 + Math.Cos(a) * 0.55, Math.Sin(a) * 0.55), 0.045, 0, 6), 1.35);
        }
        _cover = new GeometryModel3D { Geometry = PreviewMesh3D.Tube(new Point3D(-1.50, 0.99, 0), new Point3D(-1.45, 0.99, 0), 0.58, 0.21) };
        Model.Children.Add(_cover); RefreshFeedback();
    }
    private static void Changed(DependencyObject d, DependencyPropertyChangedEventArgs _) => ((PumpPreviewVisual3D)d).RefreshFeedback();
    protected override void OnIsErrorChanged() => RefreshFeedback();
    internal void RefreshFeedback()
    {
        SetVisualActive(IsRunning && !IsError);
        if (_cover is null) return;
        Color c = IsError ? Color.FromRgb(239, 119, 114) : BodyColor;
        c.A = ShowInternals ? (byte)42 : (byte)255;
        var material = new DiffuseMaterial(new SolidColorBrush(c)); material.Freeze(); _cover.Material = material; _cover.BackMaterial = material;
    }
    internal void Advance(double seconds)
    {
        if (IsRunning && !IsError && Speed > 0) _rotor.Angle = (_rotor.Angle + Math.Clamp(Speed / 1450, 0.15, 3) * 170 * seconds) % 360;
    }
}
