using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;

namespace xyz.Client.Presentation.Controls.ThreeD;

/// <summary>
/// 两态升降气缸外观：IsRaised=true 升起，false 收回。
/// 原点在底座底面中心，沿 +Y 伸出；尺寸、行程和过渡时间均为内部示意参数。
/// </summary>
public sealed class LiftVisual3D : HardwareVisual3D
{
    private const double BodyRadius = 0.235;
    private const double BodyHeight = 0.85;
    private const double RodRadius = 0.075;
    private const double Stroke = 0.55;
    private const double BaseHeight = BodyRadius * 0.4;
    private const double MountThickness = BodyRadius * 0.18;
    private const double TransitionMilliseconds = 450;

    private readonly ScaleTransform3D _rodScale = new();
    private readonly TranslateTransform3D _mountTranslation = new();
    private int _transitionVersion;

    public LiftVisual3D()
    {
        BuildGeometry();
        UpdatePosition();
    }

    /// <summary>绑定外部气缸状态：true 为上位，false 为下位。变化时自动播放升降过渡。</summary>
    public bool IsRaised
    {
        get => (bool)GetValue(IsRaisedProperty);
        set => SetValue(IsRaisedProperty, value);
    }

    public static readonly DependencyProperty IsRaisedProperty = DependencyProperty.Register(
        nameof(IsRaised), typeof(bool), typeof(LiftVisual3D),
        new PropertyMetadata(false, OnIsRaisedChanged));

    // 仅供内部动画使用，外部不需要提供连续位置或行程。
    private static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        "Progress", typeof(double), typeof(LiftVisual3D),
        new PropertyMetadata(0d, OnProgressChanged));

    private static readonly DependencyPropertyKey MountHeightPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(MountHeight), typeof(double), typeof(LiftVisual3D), new PropertyMetadata(0d));

    public static readonly DependencyProperty MountHeightProperty = MountHeightPropertyKey.DependencyProperty;

    /// <summary>安装面当前的局部 Y 坐标（含动画），用于组装；不包含外部 Transform。</summary>
    public double MountHeight => (double)GetValue(MountHeightProperty);

    private static void OnIsRaisedChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((LiftVisual3D)sender).TransitionTo((bool)args.NewValue ? 1 : 0);

    private static void OnProgressChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((LiftVisual3D)sender).UpdatePosition();

    private void TransitionTo(double target)
    {
        int version = ++_transitionVersion;
        double current = (double)GetValue(ProgressProperty);
        if (VisualTreeHelper.GetParent(this) is null || Math.Abs(target - current) < 0.000001)
        {
            // 装入场景前直接呈现初始状态，避免初始 true 先显示为下位。
            SetValue(ProgressProperty, target);
            BeginAnimation(ProgressProperty, null);
            SetVisualActive(false);
            return;
        }

        var animation = new DoubleAnimation(current, target,
            TimeSpan.FromMilliseconds(TransitionMilliseconds * Math.Abs(target - current)))
        {
            FillBehavior = FillBehavior.HoldEnd
        };
        animation.Completed += (_, _) =>
        {
            // 快速反向时，旧动画不能终止新动画或提前取消高亮。
            if (version != _transitionVersion)
            {
                return;
            }

            SetValue(ProgressProperty, target);
            BeginAnimation(ProgressProperty, null);
            SetVisualActive(false);
        };
        SetVisualActive(true);
        BeginAnimation(ProgressProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private void BuildGeometry()
    {
        double capHeight = BodyHeight * 0.08;
        AddPart(HardwareMesh3D.ChamferedBox(
            new Point3D(-BodyRadius * 1.4, 0, -BodyRadius * 1.4),
            BodyRadius * 2.8, BaseHeight, BodyRadius * 2.8), 0.55);
        AddPart(HardwareMesh3D.Cylinder(BodyRadius * 1.06, capHeight, BaseHeight), 0.8);
        AddPart(HardwareMesh3D.Cylinder(BodyRadius, BodyHeight - capHeight * 2, BaseHeight + capHeight), 1);
        AddPart(HardwareMesh3D.Cylinder(BodyRadius * 1.06, capHeight,
            BaseHeight + BodyHeight - capHeight), 1.22);

        // 固定网格只构建一次；伸缩只改变杆的 Y 缩放和安装面的平移。
        var rodTransform = new Transform3DGroup();
        rodTransform.Children.Add(_rodScale);
        rodTransform.Children.Add(new TranslateTransform3D(0, BaseHeight + BodyHeight, 0));
        AddPart(HardwareMesh3D.Cylinder(RodRadius, 1, 0), 1.35).Transform = rodTransform;
        AddPart(HardwareMesh3D.Cylinder(BodyRadius * 0.78, MountThickness, 0), 1.12)
            .Transform = _mountTranslation;
    }

    private void UpdatePosition()
    {
        double rodLength = RodRadius * 0.8 + (double)GetValue(ProgressProperty) * Stroke;
        _rodScale.ScaleY = rodLength;
        _mountTranslation.OffsetY = BaseHeight + BodyHeight + rodLength;
        SetValue(MountHeightPropertyKey, _mountTranslation.OffsetY + MountThickness);
    }
}
