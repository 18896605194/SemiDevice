using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;

namespace xyz.Client.Presentation.Controls.ThreeD;

/// <summary>
/// 薄壁 Bowl 外观。HeightLevel 选择三级侧壁高度，IsRaised 独立控制整体升降。
/// 原点为下位底面中心，Y 向上；不包含晶圆、主轴和设备指令。
/// </summary>
public sealed class BowlVisual3D : HardwareVisual3D
{
    private const double LevelOneHeight = 0.275;
    private const double Stroke = 0.55;
    private readonly TranslateTransform3D _translation = new();
    private int _transitionVersion;

    public BowlVisual3D()
    {
        Model.Transform = _translation;
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

    /// <summary>整体升降反馈，true 上位、false 下位；不改变 HeightLevel。</summary>
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
        ((BowlVisual3D)sender)._translation.OffsetY = (double)args.NewValue * Stroke;

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
        while (parent is not null && parent is not Viewport3D) parent = VisualTreeHelper.GetParent(parent);
        BindingOperations.ClearBinding(this, IsHostVisibleProperty);
        if (parent is Viewport3D viewport)
            BindingOperations.SetBinding(this, IsHostVisibleProperty,
                new Binding(nameof(UIElement.IsVisible)) { Source = viewport, Mode = BindingMode.OneWay });
        else
            SetValue(IsHostVisibleProperty, false);
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
        var animation = new DoubleAnimation(current, target, TimeSpan.FromMilliseconds(450 * Math.Abs(target - current)));
        animation.Completed += (_, _) =>
        {
            if (version == _transitionVersion) FinishTransition(target);
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
            AddPart(HardwareMesh3D.RevolvedBand(a.Radius * scale, a.Y, b.Radius * scale, b.Y), a.Brightness);
        }
    }
}
