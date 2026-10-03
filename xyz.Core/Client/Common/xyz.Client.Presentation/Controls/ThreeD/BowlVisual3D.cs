using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;

namespace xyz.Client.Presentation.Controls.ThreeD;

/// <summary>
/// 薄壁 Bowl 外观。HeightLevel 选择三级侧壁高度，IsRaised 控制升降：底边一直贴着安装面，
/// 升起时只把上沿抬高、直侧壁跟着拉长，侧壁始终连到底，不会悬空露缝。
/// 原点为底面中心，Y 向上；不包含晶圆、主轴和设备指令。
/// </summary>
public sealed class BowlVisual3D : HardwareVisual3D
{
    private const double LevelOneHeight = 0.275;
    private const double Stroke = 0.55;

    /// <summary>全行程升降过渡时间（示意，不代表实际气缸速度；三维里的动画一律 0.2 s）。</summary>
    private const double TransitionMilliseconds = 200;
    private readonly List<WallBand> _bands = [];
    private int _transitionVersion;

    public BowlVisual3D()
    {
        RebuildGeometry();
    }

    /// <summary>外观高度等级：1 为原有低矮造型，2、3 依次加高；不是升降挡位。</summary>
    public int HeightLevel
    {
        get => (int)GetValue(HeightLevelProperty);
        set => SetValue(HeightLevelProperty, value);
    }

    public static readonly DependencyProperty HeightLevelProperty = DependencyProperty.Register(
        nameof(HeightLevel), typeof(int), typeof(BowlVisual3D), new PropertyMetadata(1, OnGeometryChanged),
        value => value is int level && level is >= 1 and <= 3);

    /// <summary>外半径，与装配使用同一长度单位；内径随半径保持薄壁比例。</summary>
    public double Radius
    {
        get => (double)GetValue(RadiusProperty);
        set => SetValue(RadiusProperty, value);
    }

    public static readonly DependencyProperty RadiusProperty = DependencyProperty.Register(
        nameof(Radius), typeof(double), typeof(BowlVisual3D), new PropertyMetadata(1.59d, OnGeometryChanged),
        value => value is double radius && double.IsFinite(radius) && radius > 0);

    /// <summary>升降反馈，true 上位、false 下位；底边保持在安装面，不改变 HeightLevel。</summary>
    public bool IsRaised
    {
        get => (bool)GetValue(IsRaisedProperty);
        set => SetValue(IsRaisedProperty, value);
    }

    public static readonly DependencyProperty IsRaisedProperty = DependencyProperty.Register(
        nameof(IsRaised), typeof(bool), typeof(BowlVisual3D), new PropertyMetadata(false, OnRaisedChanged));

    private static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        "Progress", typeof(double), typeof(BowlVisual3D), new PropertyMetadata(0d, OnProgressChanged));

    private static readonly DependencyProperty IsHostVisibleProperty = DependencyProperty.Register(
        "IsHostVisible", typeof(bool), typeof(BowlVisual3D), new PropertyMetadata(false, OnHostVisibleChanged));

    private static void OnGeometryChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((BowlVisual3D)sender).RebuildGeometry();

    private static void OnRaisedChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((BowlVisual3D)sender).TransitionTo((bool)args.NewValue ? 1 : 0);

    private static void OnProgressChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((BowlVisual3D)sender).UpdateWall();

    private static void OnHostVisibleChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (!(bool)args.NewValue)
        {
            var bowl = (BowlVisual3D)sender;
            bowl.FinishTransition(bowl.IsRaised ? 1 : 0);
        }
    }

    protected override void OnSceneConnectionChanged()
    {
        DependencyObject? parent = VisualTreeHelper.GetParent(this);
        while (parent is not null && parent is not Viewport3D)
        {
            parent = VisualTreeHelper.GetParent(parent);
        }

        BindingOperations.ClearBinding(this, IsHostVisibleProperty);
        if (parent is Viewport3D viewport)
        {
            BindingOperations.SetBinding(this, IsHostVisibleProperty,
                new Binding(nameof(UIElement.IsVisible)) { Source = viewport, Mode = BindingMode.OneWay });
        }
        else
        {
            SetValue(IsHostVisibleProperty, false);
        }
    }

    private void TransitionTo(double target)
    {
        double current = (double)GetValue(ProgressProperty);
        if (!(bool)GetValue(IsHostVisibleProperty) || Math.Abs(target - current) < 0.000001)
        {
            FinishTransition(target);
            return;
        }

        int version = ++_transitionVersion;
        var animation = new DoubleAnimation(current, target, TimeSpan.FromMilliseconds(TransitionMilliseconds * Math.Abs(target - current)));
        animation.Completed += (_, _) =>
        {
            if (version == _transitionVersion)
            {
                FinishTransition(target);
            }
        };
        SetVisualActive(true);
        BeginAnimation(ProgressProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private void FinishTransition(double target)
    {
        ++_transitionVersion;
        SetValue(ProgressProperty, target);
        BeginAnimation(ProgressProperty, null);
        SetVisualActive(false);
    }

    private void RebuildGeometry()
    {
        ClearParts();
        _bands.Clear();
        double height = LevelOneHeight * HeightLevel;
        double scale = Radius / 1.59;
        // 相同薄壁截面；加高时只延长侧壁，保留细小倒角和敞开的中心。
        (double Radius, double Y, double Brightness)[] profile =
        [
            (1.575, 0, 0.85), (1.59, 0.015, 1), (1.59, height - 0.015, 1.18),
            (1.575, height, 1.3), (1.515, height, 0.9), (1.50, height - 0.015, 0.65),
            (1.50, 0.015, 0.6), (1.515, 0, 0.65)
        ];
        for (int i = 0; i < profile.Length; i++)
        {
            var a = profile[i];
            var b = profile[(i + 1) % profile.Length];
            var part = AddPart(HardwareMesh3D.RevolvedBand(a.Radius * scale, a.Y, b.Radius * scale, b.Y), a.Brightness);
            var stretch = new ScaleTransform3D(1, 1, 1);
            var offset = new TranslateTransform3D();
            var transform = new Transform3DGroup();
            transform.Children.Add(stretch);
            transform.Children.Add(offset);
            part.Transform = transform;
            // 上半截的截面点跟着上沿抬高，下半截（底边和底部倒角）不动：中间那两条直侧壁就被拉长。
            _bands.Add(new WallBand(stretch, offset, a.Y, b.Y, a.Y > height / 2, b.Y > height / 2));
        }

        UpdateWall();
    }

    /// <summary>
    /// 按升降进度摆每一段截面：两端都在上半截的整体抬高，一端在上半截的（直侧壁）按新长度拉伸，
    /// 都在下半截的不动。只改变换，不重建网格。
    /// </summary>
    private void UpdateWall()
    {
        double rise = (double)GetValue(ProgressProperty) * Stroke;
        foreach (var band in _bands)
        {
            double a = band.A + (band.MoveA ? rise : 0);
            double b = band.B + (band.MoveB ? rise : 0);
            double factor = Math.Abs(band.B - band.A) < 1e-9 ? 1 : (b - a) / (band.B - band.A);
            band.Scale.ScaleY = factor;
            band.Offset.OffsetY = a - factor * band.A;
        }
    }

    /// <summary>截面的一段：它的伸缩、平移变换，两端原来的高度，以及两端是否跟着上沿抬高。</summary>
    private sealed record WallBand(ScaleTransform3D Scale, TranslateTransform3D Offset,
        double A, double B, bool MoveA, bool MoveB);
}
