using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace xyz.Client.Presentation.Controls.ThreeD;

/// <summary>
/// 深灰色切角安装底板。原点在上表面中心，Y 向上，板厚向下延伸。
/// Attachments 内的硬件共享底座的外部变换，设备状态互相独立。
/// </summary>
public sealed class ChamberBaseVisual3D : HardwareVisual3D
{
    static ChamberBaseVisual3D()
    {
        BodyColorProperty.OverrideMetadata(typeof(ChamberBaseVisual3D),
            new PropertyMetadata(Color.FromRgb(51, 51, 51)));
    }

    public ChamberBaseVisual3D()
    {
        RebuildGeometry();
    }

    /// <summary>安装硬件的集合；各子项 Transform 相对于底板上表面中心。</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
    public Visual3DCollection Attachments => Children;

    /// <summary>底板沿 X 轴的长度。</summary>
    public double Length
    {
        get => (double)GetValue(LengthProperty);
        set => SetValue(LengthProperty, value);
    }

    public static readonly DependencyProperty LengthProperty = DependencyProperty.Register(
        nameof(Length), typeof(double), typeof(ChamberBaseVisual3D), new PropertyMetadata(5.6d, OnGeometryChanged), IsPositiveFinite);

    /// <summary>底板沿 Z 轴的宽度。</summary>
    public double Width
    {
        get => (double)GetValue(WidthProperty);
        set => SetValue(WidthProperty, value);
    }

    public static readonly DependencyProperty WidthProperty = DependencyProperty.Register(
        nameof(Width), typeof(double), typeof(ChamberBaseVisual3D), new PropertyMetadata(4.4d, OnGeometryChanged), IsPositiveFinite);

    /// <summary>板厚向 -Y 延伸；调整厚度不会顶起或移动上方硬件。</summary>
    public double Thickness
    {
        get => (double)GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
        nameof(Thickness), typeof(double), typeof(ChamberBaseVisual3D), new PropertyMetadata(0.12d, OnGeometryChanged), IsPositiveFinite);

    private static bool IsPositiveFinite(object value) => value is double number && double.IsFinite(number) && number > 0;

    private static void OnGeometryChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((ChamberBaseVisual3D)sender).RebuildGeometry();

    private void RebuildGeometry()
    {
        // 仅更新底板 Content，保留已挂载的硬件、变换和所有外部绑定。
        ClearParts();
        AddPart(HardwareMesh3D.ChamferedBox(new Point3D(-Length / 2, -Thickness, -Width / 2),
            Length, Thickness, Width));
    }
}
