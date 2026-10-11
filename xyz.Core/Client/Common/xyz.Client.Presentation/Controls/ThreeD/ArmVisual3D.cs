using System.ComponentModel;
using System.Windows;
using System.Windows.Media.Media3D;

namespace xyz.Client.Presentation.Controls.ThreeD;

/// <summary>
/// 可组合的摆臂外观：局部 Y 轴向上，原点是回转中心，零角度沿 +X。
/// 只显示外部反馈；不包含 Lift、喷嘴、供液管路或任何设备控制逻辑。
/// </summary>
public sealed class ArmVisual3D : HardwareVisual3D
{
    private readonly AxisAngleRotation3D _rotation = new(new Vector3D(0, 1, 0), 0);
    private readonly ModelVisual3D _attachmentRoot = new();

    public ArmVisual3D()
    {
        Model.Transform = new RotateTransform3D(_rotation);
        _attachmentRoot.Transform = Model.Transform;
        Children.Add(_attachmentRoot);
        RebuildGeometry();
    }

    /// <summary>随摆角和外部安装变换一起运动的附件，如 DIW/SC1 管路；附件动作状态独立。</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
    public Visual3DCollection Attachments => _attachmentRoot.Children;

    /// <summary>当前摆角，单位为度。正角度使臂尖从 +X 转向 -Z。</summary>
    public double Angle
    {
        get => (double)GetValue(AngleProperty);
        set => SetValue(AngleProperty, value);
    }

    public static readonly DependencyProperty AngleProperty = DependencyProperty.Register(
        nameof(Angle), typeof(double), typeof(ArmVisual3D),
        new PropertyMetadata(0d, OnAngleChanged),
        value => value is double angle && double.IsFinite(angle));

    /// <summary>回转中心到末端安装块中心的距离，与场景使用同一长度单位。</summary>
    public double Length
    {
        get => (double)GetValue(LengthProperty);
        set => SetValue(LengthProperty, value);
    }

    public static readonly DependencyProperty LengthProperty = DependencyProperty.Register(
        nameof(Length), typeof(double), typeof(ArmVisual3D),
        new PropertyMetadata(2.2d, OnGeometryChanged), IsPositiveFinite);

    public double Width
    {
        get => (double)GetValue(WidthProperty);
        set => SetValue(WidthProperty, value);
    }

    public static readonly DependencyProperty WidthProperty = DependencyProperty.Register(
        nameof(Width), typeof(double), typeof(ArmVisual3D),
        new PropertyMetadata(0.23d, OnGeometryChanged), IsPositiveFinite);

    public double Thickness
    {
        get => (double)GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
        nameof(Thickness), typeof(double), typeof(ArmVisual3D),
        new PropertyMetadata(0.085d, OnGeometryChanged), IsPositiveFinite);

    public double PivotRadius
    {
        get => (double)GetValue(PivotRadiusProperty);
        set => SetValue(PivotRadiusProperty, value);
    }

    public static readonly DependencyProperty PivotRadiusProperty = DependencyProperty.Register(
        nameof(PivotRadius), typeof(double), typeof(ArmVisual3D),
        new PropertyMetadata(0.285d, OnGeometryChanged), IsPositiveFinite);

    private static bool IsPositiveFinite(object value) =>
        value is double number && double.IsFinite(number) && number > 0;

    private static void OnAngleChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        // 仅更新变换，不重建网格，也不覆盖调用方用于安装位置/升降的 Transform。
        ((ArmVisual3D)sender)._rotation.Angle = (double)args.NewValue % 360;
    }

    private static void OnGeometryChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        ((ArmVisual3D)sender).RebuildGeometry();
    }

    private void RebuildGeometry()
    {
        ClearMeshes();
        double length = Length;
        double width = Width;
        double thickness = Thickness;

        // 回转座和薄盖；气缸属于独立的 Lift 组件。
        AddMesh(HardwareMesh3D.Cylinder(PivotRadius, thickness, -thickness * 1.5), 0.64);
        AddMesh(HardwareMesh3D.Cylinder(PivotRadius, thickness * 0.5, -thickness * 0.5), 1.22);

        AddMesh(HardwareMesh3D.ChamferedBox(
            new Point3D(0, -thickness * 0.5, -width * 0.5), length, thickness, width), 1);

        // 两条笔直的上沿增强立体层次，不包含未确认的供液弯管。
        double inset = Math.Min(width * 0.3, length * 0.1);
        foreach (double side in new[] { -1d, 1d })
        {
            AddMesh(HardwareMesh3D.ChamferedBox(
                new Point3D(inset, thickness * 0.5, side * width * 0.34 - width * 0.06),
                length - inset * 2, thickness * 0.18, width * 0.12), 1.28);
        }

        double tipLength = Math.Min(width * 0.8, length * 0.3);
        AddMesh(HardwareMesh3D.ChamferedBox(
            new Point3D(length - tipLength * 0.5, -thickness, -width * 0.6),
            tipLength, thickness * 1.45, width * 1.2), 1.12);
    }
}
