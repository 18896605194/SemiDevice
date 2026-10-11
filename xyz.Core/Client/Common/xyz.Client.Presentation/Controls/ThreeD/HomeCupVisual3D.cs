using System.Windows;

namespace xyz.Client.Presentation.Controls.ThreeD;

/// <summary>
/// Home 位置的固定接液杯：敞口、收液锥面及贯通的下排液口。
/// 原点在排液口底面中心，Y 向上。放在场景中，不随 Arm 或 Lift 移动。
/// </summary>
public sealed class HomeCupVisual3D : HardwareVisual3D
{
    public HomeCupVisual3D()
    {
        RebuildGeometry();
    }

    /// <summary>杯口外半径，与装配使用相同长度单位。</summary>
    public double Radius
    {
        get => (double)GetValue(RadiusProperty);
        set => SetValue(RadiusProperty, value);
    }

    public static readonly DependencyProperty RadiusProperty = DependencyProperty.Register(
        nameof(Radius), typeof(double), typeof(HomeCupVisual3D), new PropertyMetadata(0.32d, OnGeometryChanged), IsPositiveFinite);

    /// <summary>排液口底面到杯口的总高度；杯口局部坐标为 (0, Height, 0)。</summary>
    public double Height
    {
        get => (double)GetValue(HeightProperty);
        set => SetValue(HeightProperty, value);
    }

    public static readonly DependencyProperty HeightProperty = DependencyProperty.Register(
        nameof(Height), typeof(double), typeof(HomeCupVisual3D), new PropertyMetadata(0.655d, OnGeometryChanged), IsPositiveFinite);

    /// <summary>可选的接液/排液状态反馈，true 自动高亮；液流由管子组件显示。</summary>
    public bool IsDraining
    {
        get => (bool)GetValue(IsDrainingProperty);
        set => SetValue(IsDrainingProperty, value);
    }

    public static readonly DependencyProperty IsDrainingProperty = DependencyProperty.Register(
        nameof(IsDraining), typeof(bool), typeof(HomeCupVisual3D), new PropertyMetadata(false, OnDrainingChanged));

    private static bool IsPositiveFinite(object value) => value is double number && double.IsFinite(number) && number > 0;

    private static void OnGeometryChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((HomeCupVisual3D)sender).RebuildGeometry();

    private static void OnDrainingChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((HomeCupVisual3D)sender).SetVisualActive((bool)args.NewValue);

    private void RebuildGeometry()
    {
        ClearMeshes();
        // 沿实体截面闭合：外排液口→外壁→杯沿→内壁→收液锥面→内排液口。
        // 底部也只封闭环形管壁，保留贯通孔，避免把排液管画成实心柱。
        (double Radius, double Y, double Brightness)[] profile =
        [
            (0.095, 0, 0.8), (0.095, 0.205, 0.9), (0.32, 0.355, 1),
            (0.32, 0.63, 1.18), (0.295, 0.655, 1.3), (0.265, 0.655, 0.7),
            (0.265, 0.395, 0.78), (0.08, 0.255, 0.6), (0.08, 0, 0.65)
        ];
        double radialScale = Radius / 0.32;
        double heightScale = Height / 0.655;
        for (int i = 0; i < profile.Length; i++)
        {
            var a = profile[i];
            var b = profile[(i + 1) % profile.Length];
            AddMesh(HardwareMesh3D.RevolvedBand(a.Radius * radialScale, a.Y * heightScale,
                b.Radius * radialScale, b.Y * heightScale), a.Brightness);
        }
    }
}
