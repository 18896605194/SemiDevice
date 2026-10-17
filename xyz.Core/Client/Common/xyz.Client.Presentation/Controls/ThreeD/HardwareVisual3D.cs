using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace xyz.Client.Presentation.Controls.ThreeD;

/// <summary>
/// 硬件三维外观的公共基类。动作和选中共用主题高亮，故障整块画成报警色，不执行设备指令。
/// </summary>
public abstract class HardwareVisual3D : ModelVisual3D
{
    /// <summary>两态件（门、Bowl、Lift）位置未知（命令发了、到位信号还没亮）时画在行程的哪儿：正中间，一眼看出没到位。</summary>
    protected const double UnknownProgress = 0.5;

    private static readonly Color FallbackFault = Color.FromRgb(239, 83, 80);

    private readonly List<(GeometryModel3D Model, double Brightness)> _meshes = [];
    private bool _isVisualActive;
    private bool _isVisualFault;

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

    /// <summary>
    /// IsError 变了：网格之外自己画的部分（如盘面）在这里跟着变。
    /// </summary>
    protected virtual void OnIsErrorChanged() { }

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

    /// <summary>
    /// 绑定设备故障反馈（如轴驱动器报错）；为 true 时整块画成报警色，盖过高亮。
    /// </summary>
    public bool IsError
    {
        get => (bool)GetValue(IsErrorProperty);
        set => SetValue(IsErrorProperty, value);
    }

    public static readonly DependencyProperty IsErrorProperty = DependencyProperty.Register(
        nameof(IsError), typeof(bool), typeof(HardwareVisual3D),
        new PropertyMetadata(false, OnErrorChanged));

    /// <summary>
    /// 报警红，取主题的 DarkAlarmStatus。
    /// </summary>
    protected static Color FaultColor => HardwareTheme.ColorOf("DarkAlarmStatus", FallbackFault);

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

    protected void ClearMeshes()
    {
        _meshes.Clear();
        Model.Children.Clear();
    }

    protected GeometryModel3D AddMesh(MeshGeometry3D mesh, double brightness = 1)
    {
        var model = new GeometryModel3D(mesh, CreateMaterial(brightness));
        _meshes.Add((model, brightness));
        Model.Children.Add(model);
        return model;
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

    /// <summary>
    /// 内部判出的故障（两态件位置未知），效果同 IsError，不修改外部绑定的 IsError。
    /// </summary>
    protected void SetVisualFault(bool fault)
    {
        if (_isVisualFault == fault)
        {
            return;
        }

        _isVisualFault = fault;
        RefreshMaterials();
    }

    private static void OnAppearanceChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        ((HardwareVisual3D)sender).RefreshMaterials();
    }

    private static void OnErrorChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var hardware = (HardwareVisual3D)sender;
        hardware.RefreshMaterials();
        hardware.OnIsErrorChanged();
    }

    private void RefreshMaterials()
    {
        foreach (var (model, brightness) in _meshes)
        {
            model.Material = CreateMaterial(brightness);
        }
    }

    private Material CreateMaterial(double brightness)
    {
        double blend = IsMoving || IsSelected || _isVisualActive ? HighlightStrength : 0;
        Color body = BodyColor;
        Color accent = HighlightColor;
        // 故障不混金属灰，整块换成报警色，只留各面的深浅
        if (IsError || _isVisualFault)
        {
            blend = 1;
            accent = FaultColor;
        }

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
