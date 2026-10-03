using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;

namespace xyz.Client.Presentation.Controls.ThreeD;

/// <summary>
/// 气缸驱动的上下升降门示意。上升打开、下降关闭，外部只绑定 IsOpen。
/// 原点为门框下方安装面中心，Y 向上，门宽沿 X；打开时门板完全高于门框。
/// </summary>
public sealed class DoorVisual3D : HardwareVisual3D
{
    private readonly TranslateTransform3D _panelTranslation = new();
    private int _transitionVersion;

    public DoorVisual3D()
    {
        RebuildGeometry();
    }

    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    public static readonly DependencyProperty IsOpenProperty = DependencyProperty.Register(
        nameof(IsOpen), typeof(bool), typeof(DoorVisual3D), new PropertyMetadata(false, OnOpenChanged));

    /// <summary>门板宽度，使用装配的统一长度单位。</summary>
    public double Width
    {
        get => (double)GetValue(WidthProperty);
        set => SetValue(WidthProperty, value);
    }

    public static readonly DependencyProperty WidthProperty = DependencyProperty.Register(
        nameof(Width), typeof(double), typeof(DoorVisual3D), new PropertyMetadata(2.2d, OnGeometryChanged), IsPositiveFinite);

    /// <summary>门板高度；动画行程由内部示意结构决定。</summary>
    public double Height
    {
        get => (double)GetValue(HeightProperty);
        set => SetValue(HeightProperty, value);
    }

    public static readonly DependencyProperty HeightProperty = DependencyProperty.Register(
        nameof(Height), typeof(double), typeof(DoorVisual3D), new PropertyMetadata(0.58d, OnGeometryChanged), IsPositiveFinite);

    private static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        "Progress", typeof(double), typeof(DoorVisual3D), new PropertyMetadata(0d, OnProgressChanged));
    private static readonly DependencyProperty IsHostVisibleProperty = DependencyProperty.Register(
        "IsHostVisible", typeof(bool), typeof(DoorVisual3D), new PropertyMetadata(false, OnHostVisibleChanged));

    private static bool IsPositiveFinite(object value) => value is double number && double.IsFinite(number) && number > 0;
    private static void OnGeometryChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) => ((DoorVisual3D)sender).RebuildGeometry();
    private static void OnProgressChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) => ((DoorVisual3D)sender).UpdatePosition();
    private static void OnOpenChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) => ((DoorVisual3D)sender).TransitionTo((bool)args.NewValue ? 1 : 0);

    private static void OnHostVisibleChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (!(bool)args.NewValue)
        {
            var door = (DoorVisual3D)sender;
            door.FinishTransition(door.IsOpen ? 1 : 0);
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
        else SetValue(IsHostVisibleProperty, false);
    }

    private void TransitionTo(double target)
    {
        double current = (double)GetValue(ProgressProperty);
        if (!(bool)GetValue(IsHostVisibleProperty) || Math.Abs(current - target) < 0.000001)
        {
            FinishTransition(target);
            return;
        }
        int version = ++_transitionVersion;
        var animation = new DoubleAnimation(current, target, TimeSpan.FromMilliseconds(450 * Math.Abs(target - current)));
        animation.Completed += (_, _) => { if (version == _transitionVersion) FinishTransition(target); };
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
        // 仅显示门框与升降门板；气缸、活塞杆和外侧连接件不参与外观。
        // 门板沿门框前侧滑动，与横梁保留间隙，避免开门时穿过横梁。
        AddPart(HardwareMesh3D.ChamferedBox(new Point3D(-Width / 2, 0.18, 0.1), Width, Height, 0.1), 1.05)
            .Transform = _panelTranslation;
        foreach (double x in new[] { -Width / 2 - 0.12, Width / 2 })
            AddPart(HardwareMesh3D.ChamferedBox(new Point3D(x, 0, -0.08), 0.12, Height + 0.28, 0.16), 0.65);
        AddPart(HardwareMesh3D.ChamferedBox(new Point3D(-Width / 2 - 0.12, Height + 0.18, -0.08), Width + 0.24, 0.1, 0.16), 0.8);
        UpdatePosition();
    }

    private void UpdatePosition()
    {
        // 打开后门板底边高于门框最高点 0.04，整个门洞完全空出。
        double travel = (Height + 0.14) * (double)GetValue(ProgressProperty);
        _panelTranslation.OffsetY = travel;
    }
}
