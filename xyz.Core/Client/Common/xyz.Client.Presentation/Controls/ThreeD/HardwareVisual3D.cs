using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace xyz.Client.Presentation.Controls.ThreeD;

/// <summary>
/// 硬件三维外观的公共基类。动作和选中共用主题高亮，不执行设备指令。
/// </summary>
public abstract class HardwareVisual3D : ModelVisual3D
{
    /// <summary>两态件（门、Bowl、Lift）位置未知（命令发了、到位信号还没亮）时画在行程的哪儿：正中间，一眼看出没到位。</summary>
    protected const double UnknownProgress = 0.5;

    private readonly List<(GeometryModel3D Model, double Brightness)> _parts = [];
    private bool _isVisualActive;

    protected Model3DGroup Model { get; } = new();

    protected HardwareVisual3D()
    {
        Content = Model;
    }

    protected override void OnVisualParentChanged(DependencyObject oldParent)
    {
        base.OnVisualParentChanged(oldParent);
        NotifySceneConnection(this);
    }

    /// <summary>自身或所属硬件从装配树挂载/移除时刷新动画宿主。</summary>
    protected virtual void OnSceneConnectionChanged() { }

    private static void NotifySceneConnection(DependencyObject node)
    {
        if (node is HardwareVisual3D hardware)
        {
            hardware.OnSceneConnectionChanged();
        }

        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            NotifySceneConnection(VisualTreeHelper.GetChild(node, i));
        }
    }

    /// <summary>绑定设备动作反馈；为 true 时自动高亮，不依赖鼠标选中。</summary>
    public bool IsMoving
    {
        get => (bool)GetValue(IsMovingProperty);
        set => SetValue(IsMovingProperty, value);
    }

    public static readonly DependencyProperty IsMovingProperty = DependencyProperty.Register(
        nameof(IsMoving), typeof(bool), typeof(HardwareVisual3D),
        new PropertyMetadata(false, OnAppearanceChanged));

    /// <summary>由外部选择逻辑设置，与 IsMoving 相互独立。</summary>
    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    public static readonly DependencyProperty IsSelectedProperty = DependencyProperty.Register(
        nameof(IsSelected), typeof(bool), typeof(HardwareVisual3D),
        new PropertyMetadata(false, OnAppearanceChanged));

    public Color BodyColor
    {
        get => (Color)GetValue(BodyColorProperty);
        set => SetValue(BodyColorProperty, value);
    }

    public static readonly DependencyProperty BodyColorProperty = DependencyProperty.Register(
        nameof(BodyColor), typeof(Color), typeof(HardwareVisual3D),
        new PropertyMetadata(Color.FromRgb(104, 104, 104), OnAppearanceChanged));

    public Color HighlightColor
    {
        get => (Color)GetValue(HighlightColorProperty);
        set => SetValue(HighlightColorProperty, value);
    }

    public static readonly DependencyProperty HighlightColorProperty = DependencyProperty.Register(
        nameof(HighlightColor), typeof(Color), typeof(HardwareVisual3D),
        new PropertyMetadata(Color.FromRgb(66, 165, 245), OnAppearanceChanged));

    /// <summary>主题色混入比例（0～1），保留金属灰的明暗层次。</summary>
    public double HighlightStrength
    {
        get => (double)GetValue(HighlightStrengthProperty);
        set => SetValue(HighlightStrengthProperty, value);
    }

    public static readonly DependencyProperty HighlightStrengthProperty = DependencyProperty.Register(
        nameof(HighlightStrength), typeof(double), typeof(HardwareVisual3D),
        new PropertyMetadata(0.42, OnAppearanceChanged),
        value => value is double strength && double.IsFinite(strength) && strength is >= 0 and <= 1);

    protected void ClearParts()
    {
        _parts.Clear();
        Model.Children.Clear();
    }

    protected GeometryModel3D AddPart(MeshGeometry3D mesh, double brightness = 1)
    {
        var part = new GeometryModel3D(mesh, CreateMaterial(brightness));
        _parts.Add((part, brightness));
        Model.Children.Add(part);
        return part;
    }

    /// <summary>内部动作（升降过渡、持续出液等）的高亮，不修改外部绑定的 IsMoving。</summary>
    protected void SetVisualActive(bool active)
    {
        if (_isVisualActive == active)
        {
            return;
        }

        _isVisualActive = active;
        RefreshMaterials();
    }

    private static void OnAppearanceChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        ((HardwareVisual3D)sender).RefreshMaterials();
    }

    private void RefreshMaterials()
    {
        foreach (var (part, brightness) in _parts)
        {
            part.Material = CreateMaterial(brightness);
        }
    }

    private Material CreateMaterial(double brightness)
    {
        double blend = IsMoving || IsSelected || _isVisualActive ? HighlightStrength : 0;
        Color body = BodyColor;
        Color accent = HighlightColor;
        byte Channel(byte normal, byte highlight) => (byte)Math.Clamp(
            Math.Round((normal * (1 - blend) + highlight * blend) * brightness), 0, 255);

        // 高亮仍然使用受灯光影响的漫反射材质，避免发光效果破坏暗色界面的层次。
        var color = Color.FromArgb(body.A, Channel(body.R, accent.R),
            Channel(body.G, accent.G), Channel(body.B, accent.B));
        var material = new DiffuseMaterial(new SolidColorBrush(color));
        material.Freeze();
        return material;
    }
}
