using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;

namespace xyz.Client.Presentation.Controls.ThreeD;

/// <summary>
/// LoadPort 三维外观：背板（门洞、信号灯面板）、背板门、下面的驱动柜、承载台和前后走的托板。
/// 载具（<see cref="CarrierVisual3D"/>）放进 <see cref="Attachments"/>，坐在托板的定位销上、跟着对接前后走。
/// 外部只绑结果：<see cref="IsDocked"/>（托板推进去贴着门洞）、<see cref="IsDoorOpen"/> / <see cref="IsDoorUnknown"/>（门开到位 / 在半路）、面板上的灯。
/// 动作有先后：要开门先推进去再开，要退出先关门再退；每个动作全行程 0.5 s（用户定的，写死），中途换目标从当前位置接着走。
/// 只有在动的那一块（门或托板）高亮；门在半路停在行程中间、一直高亮。原点在承载台面中心，Y 向上，+Z 朝操作员，背板在 -Z。
/// </summary>
public sealed class LoadPortVisual3D : HardwareVisual3D
{
    #region 尺寸和行程（场景示意单位）

    /// <summary>一个动作（对接 / 退出、开门 / 关门）全行程的时间：用户定的 0.5 s，写死。</summary>
    private const double TransitionMilliseconds = 500;

    /// <summary>退出时托板往前（+Z）退出去的距离。</summary>
    private const double DockTravel = 0.72;

    /// <summary>开门先往后（-Z）让开门框，再往下收进背板后面的门腔。</summary>
    private const double DoorRetract = 0.65;

    private const double DoorDrop = 3.5;

    /// <summary>开门行程里往后让开占的比例，剩下的是往下收。</summary>
    private const double DoorRetractShare = 0.22;

    /// <summary>载具底面离承载台面的高度：坐在定位销上。</summary>
    private const double CarrierSeatHeight = 0.185;

    /// <summary>机加件的小倒角。</summary>
    private const double Bevel = 0.02;

    /// <summary>进度差小于它算到了。</summary>
    private const double Arrived = 0.000001;

    #endregion

    #region 各档金属的亮度（相对 BodyColor 的机身灰）

    private const double Coat = 1.15;
    private const double Edge = 1.45;
    private const double Dark = 0.66;
    private const double Black = 0.45;
    private const double Steel = 1.8;
    private const double Rubber = 0.32;

    #endregion

    private static readonly Color FallbackLampOn = Color.FromRgb(67, 160, 71);
    private static readonly Color FallbackLampManual = Color.FromRgb(251, 192, 45);
    private static readonly Color FallbackLampOff = Color.FromRgb(97, 97, 97);

    /// <summary>亮着的灯自发光的强度（相对灯色）。</summary>
    private const double LampGlow = 0.45;

    /// <summary>灭着的灯比灰色再暗一点，像没通电的灯罩。</summary>
    private const double LampOffBrightness = 0.7;

    private readonly MovingPart _door = new();
    private readonly MovingPart _carriage = new();
    private readonly ModelVisual3D _seat = new() { Transform = new TranslateTransform3D(0, CarrierSeatHeight, 0) };
    private readonly TranslateTransform3D _doorTranslation = new();
    private readonly TranslateTransform3D _carriageTranslation = new();
    private readonly GeometryModel3D _presentLamp;
    private readonly GeometryModel3D _placedLamp;
    private readonly GeometryModel3D _autoLamp;
    private readonly GeometryModel3D _manualLamp;
    private int _transitionVersion;

    public LoadPortVisual3D()
    {
        _door.Transform = _doorTranslation;
        _carriage.Transform = _carriageTranslation;
        _carriage.Children.Add(_seat);
        Children.Add(_door);
        Children.Add(_carriage);
        foreach (var part in new[] { _door, _carriage })
        {
            foreach (var property in new[] { BodyColorProperty, HighlightColorProperty, HighlightStrengthProperty, IsSelectedProperty, IsMovingProperty })
            {
                BindingOperations.SetBinding(part, property, new Binding(property.Name) { Source = this, Mode = BindingMode.OneWay });
            }
        }

        BuildBody();
        BuildDoor();
        BuildCarriage();
        _presentLamp = AddLamp(-1.17, 4.64);
        _placedLamp = AddLamp(0.16, 4.64);
        _autoLamp = AddLamp(-1.17, 4.10);
        _manualLamp = AddLamp(0.16, 4.10);
        UpdateLamps();
        UpdatePositions();
    }

    #region 依赖属性

    /// <summary>装在托板上的东西（载具）；原点在托板定位销上，跟着对接前后走。</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
    public Visual3DCollection Attachments => _seat.Children;

    /// <summary>托板推进去了（对接）：载具贴着门洞。false = 退出来，人可以放取载具。</summary>
    public bool IsDocked
    {
        get => (bool)GetValue(IsDockedProperty);
        set => SetValue(IsDockedProperty, value);
    }

    public static readonly DependencyProperty IsDockedProperty = DependencyProperty.Register(
        nameof(IsDocked), typeof(bool), typeof(LoadPortVisual3D), new PropertyMetadata(false, OnTargetChanged));

    /// <summary>门开到位（只在对接时才画得开：没推进去门一定是关着的）。</summary>
    public bool IsDoorOpen
    {
        get => (bool)GetValue(IsDoorOpenProperty);
        set => SetValue(IsDoorOpenProperty, value);
    }

    public static readonly DependencyProperty IsDoorOpenProperty = DependencyProperty.Register(
        nameof(IsDoorOpen), typeof(bool), typeof(LoadPortVisual3D), new PropertyMetadata(false, OnTargetChanged));

    /// <summary>门在半路（开到位、关到位都没亮）：门画在行程中间并一直高亮，到位了再走到那一头。</summary>
    public bool IsDoorUnknown
    {
        get => (bool)GetValue(IsDoorUnknownProperty);
        set => SetValue(IsDoorUnknownProperty, value);
    }

    public static readonly DependencyProperty IsDoorUnknownProperty = DependencyProperty.Register(
        nameof(IsDoorUnknown), typeof(bool), typeof(LoadPortVisual3D), new PropertyMetadata(false, OnTargetChanged));

    /// <summary>面板上"在位"灯。</summary>
    public bool IsPresent
    {
        get => (bool)GetValue(IsPresentProperty);
        set => SetValue(IsPresentProperty, value);
    }

    public static readonly DependencyProperty IsPresentProperty = DependencyProperty.Register(
        nameof(IsPresent), typeof(bool), typeof(LoadPortVisual3D), new PropertyMetadata(false, OnLampChanged));

    /// <summary>面板上"放置到位"灯。</summary>
    public bool IsPlaced
    {
        get => (bool)GetValue(IsPlacedProperty);
        set => SetValue(IsPlacedProperty, value);
    }

    public static readonly DependencyProperty IsPlacedProperty = DependencyProperty.Register(
        nameof(IsPlaced), typeof(bool), typeof(LoadPortVisual3D), new PropertyMetadata(false, OnLampChanged));

    /// <summary>存取方式：true 亮 AUTO（绿），false 亮 MANUAL（黄）。</summary>
    public bool IsAutoMode
    {
        get => (bool)GetValue(IsAutoModeProperty);
        set => SetValue(IsAutoModeProperty, value);
    }

    public static readonly DependencyProperty IsAutoModeProperty = DependencyProperty.Register(
        nameof(IsAutoMode), typeof(bool), typeof(LoadPortVisual3D), new PropertyMetadata(false, OnLampChanged));

    /// <summary>对接进度：0 = 退出，1 = 推进去。</summary>
    private static readonly DependencyProperty DockProgressProperty = DependencyProperty.Register(
        "DockProgress", typeof(double), typeof(LoadPortVisual3D), new PropertyMetadata(0d, OnProgressChanged));

    /// <summary>开门进度：0 = 关，1 = 开（先往后让、再往下收）。</summary>
    private static readonly DependencyProperty DoorProgressProperty = DependencyProperty.Register(
        "DoorProgress", typeof(double), typeof(LoadPortVisual3D), new PropertyMetadata(0d, OnProgressChanged));

    private static readonly DependencyProperty IsHostVisibleProperty = DependencyProperty.Register(
        "IsHostVisible", typeof(bool), typeof(LoadPortVisual3D), new PropertyMetadata(false, OnTargetChanged));

    private static void OnTargetChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        ((LoadPortVisual3D)sender).Advance();
    }

    private static void OnProgressChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        ((LoadPortVisual3D)sender).UpdatePositions();
    }

    private static void OnLampChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        ((LoadPortVisual3D)sender).UpdateLamps();
    }

    #endregion

    #region 动作

    /// <summary>门该画到哪：没推进去一定关着；推进去了在半路画中间，否则开 1、关 0。</summary>
    private double DoorTarget
    {
        get
        {
            if (!IsDocked)
            {
                return 0;
            }

            if (IsDoorUnknown)
            {
                return UnknownProgress;
            }

            return IsDoorOpen ? 1 : 0;
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

    /// <summary>
    /// 从现在画到的位置往目标走一步：托板要动而门没关好，先关门；门关好了再动托板；托板到了再动门。
    /// 每一步 0.5 s 全行程（按要走的距离折算），走完接着下一步。没显示（页面藏着、还没挂进视口）时直接落到目标。
    /// </summary>
    private void Advance()
    {
        int version = ++_transitionVersion;
        double dock = Settle(DockProgressProperty);
        double door = Settle(DoorProgressProperty);
        double dockTarget = IsDocked ? 1 : 0;
        double doorTarget = DoorTarget;
        if (!(bool)GetValue(IsHostVisibleProperty))
        {
            SetValue(DockProgressProperty, dockTarget);
            SetValue(DoorProgressProperty, doorTarget);
            _carriage.SetActive(false);
            _door.SetActive(IsDocked && IsDoorUnknown);
            return;
        }

        if (Math.Abs(dock - dockTarget) > Arrived)
        {
            if (door > Arrived)
            {
                _carriage.SetActive(false);
                _door.SetActive(true);
                Animate(DoorProgressProperty, door, 0, version);
                return;
            }

            _door.SetActive(false);
            _carriage.SetActive(true);
            Animate(DockProgressProperty, dock, dockTarget, version);
            return;
        }

        _carriage.SetActive(false);
        if (Math.Abs(door - doorTarget) > Arrived)
        {
            _door.SetActive(true);
            Animate(DoorProgressProperty, door, doorTarget, version);
            return;
        }

        _door.SetActive(IsDocked && IsDoorUnknown);
    }

    /// <summary>停掉这个进度上的动画，留在现在画到的位置。</summary>
    private double Settle(DependencyProperty property)
    {
        double current = (double)GetValue(property);
        BeginAnimation(property, null);
        SetValue(property, current);
        return current;
    }

    private void Animate(DependencyProperty property, double from, double to, int version)
    {
        var animation = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(TransitionMilliseconds * Math.Abs(to - from)));
        animation.Completed += (_, _) =>
        {
            if (version == _transitionVersion)
            {
                Advance();
            }
        };
        BeginAnimation(property, animation);
    }

    private void UpdatePositions()
    {
        double dock = (double)GetValue(DockProgressProperty);
        double door = (double)GetValue(DoorProgressProperty);
        _carriageTranslation.OffsetZ = (1 - dock) * DockTravel;
        _doorTranslation.OffsetZ = -DoorRetract * Math.Min(1, door / DoorRetractShare);
        _doorTranslation.OffsetY = -DoorDrop * Math.Max(0, (door - DoorRetractShare) / (1 - DoorRetractShare));
    }

    private void UpdateLamps()
    {
        Color on = HardwareTheme.ColorOf("DarkRunningStatus", FallbackLampOn);
        Color manual = HardwareTheme.ColorOf("DarkWarningStatus", FallbackLampManual);
        Color off = HardwareTheme.ColorOf("DarkInactiveStatus", FallbackLampOff);
        _presentLamp.Material = LampMaterial(IsPresent, on, off);
        _placedLamp.Material = LampMaterial(IsPlaced, on, off);
        _autoLamp.Material = LampMaterial(IsAutoMode, on, off);
        _manualLamp.Material = LampMaterial(!IsAutoMode, manual, off);
    }

    private static Material LampMaterial(bool isOn, Color on, Color off)
    {
        if (!isOn)
        {
            return HardwareTheme.Diffuse(off, LampOffBrightness);
        }

        var glow = new EmissiveMaterial(new SolidColorBrush(Color.FromRgb(
            (byte)(on.R * LampGlow), (byte)(on.G * LampGlow), (byte)(on.B * LampGlow))));
        var material = new MaterialGroup { Children = { HardwareTheme.Diffuse(on, 1), glow } };
        material.Freeze();
        return material;
    }

    #endregion

    #region 外形（照 Codex 的 LoadPort 预览）

    /// <summary>背板（带门洞）、信号灯面板、门框、驱动柜、承载台：这些不动。</summary>
    private void BuildBody()
    {
        // 背板两侧立框：底层、面板、包边。
        foreach (double side in new[] { -1d, 1d })
        {
            AddMesh(Box(side * 1.475, 0.665, -1.60, 0.35, 8.53, 0.19), Dark);
            AddMesh(Box(side * 1.475, 0.665, -1.475, 0.25, 8.38, 0.065), Coat);
            AddMesh(Box(side * 1.42, 0.665, -1.42, 0.035, 8.30, 0.05), Edge);
        }

        // 背板上段（灯面板后面）、下段（门腔前面）。
        AddMesh(Box(0, 4.37, -1.60, 2.61, 1.15, 0.19), Dark);
        AddMesh(Box(0, 4.37, -1.475, 2.61, 1.06, 0.065), Coat);
        AddMesh(Box(0, -1.71, -1.60, 2.61, 3.78, 0.19), Dark);
        AddMesh(Box(0, -1.71, -1.475, 2.61, 3.68, 0.065), Coat);

        // 信号灯面板：黑底、胶面、两排灯中间一道分隔线。
        AddMesh(Box(0, 4.35, -1.405, 2.92, 0.96, 0.075), Black);
        AddMesh(Box(0, 4.35, -1.36, 2.78, 0.84, 0.022), Rubber);
        AddMesh(Box(0, 4.30, -1.307, 2.44, 0.012, 0.008), Black);

        // 背板螺钉。
        foreach (double side in new[] { -1d, 1d })
        {
            foreach (double y in new[] { -1.43, 3.72, 4.78 })
            {
                AddMesh(HardwareMesh3D.Cylinder(0.044, 0.018, -0.009), Steel).Transform = AlongZ(side * 1.4, y, -1.415);
            }
        }

        // 门框：左右胶条 + 包边，上下胶条；中间是通的，开门后从背面能看到晶圆。
        foreach (double side in new[] { -1d, 1d })
        {
            AddMesh(Box(side * 1.30, 1.97, -1.315, 0.09, 3.59, 0.20), Rubber);
            AddMesh(Box(side * 1.30, 1.97, -1.245, 0.045, 3.59, 0.05), Edge);
        }

        foreach (double y in new[] { 0.19, 3.76 })
        {
            AddMesh(Box(0, y, -1.315, 2.60, 0.08, 0.20), Rubber);
        }

        // 驱动柜：前罩、面板、底脚，背后收门的门腔；面板上的通风槽和螺钉。
        AddMesh(Box(0, -1.77, -0.60, 2.78, 3.43, 1.30), Coat);
        AddMesh(Box(0, -1.74, 0.074, 2.60, 3.18, 0.046), Dark);
        AddMesh(Box(0, -3.50, -0.60, 2.80, 0.09, 1.36), Black);
        AddMesh(Box(0, -1.75, -1.97, 2.85, 3.61, 0.67), Dark);
        for (int i = 0; i < 6; i++)
        {
            AddMesh(Box(0, -2.58 - i * 0.085, 0.111, 1.76, 0.025, 0.015), Rubber);
        }

        foreach (double side in new[] { -1d, 1d })
        {
            foreach (double y in new[] { -0.29, -3.25 })
            {
                AddMesh(HardwareMesh3D.Cylinder(0.044, 0.018, -0.009), Steel).Transform = AlongZ(side * 1.18, y, 0.111);
            }
        }

        // 承载台：厚底座、不锈钢台面、两边压条和螺栓、托板导轨。
        AddMesh(Box(0, -0.06, 0.05, 3.35, 0.22, 3.02), Dark);
        AddMesh(Box(0, 0.067, 0.05, 3.20, 0.045, 2.88), Steel);
        foreach (double side in new[] { -1d, 1d })
        {
            AddMesh(Box(side * 1.48, 0.122, 0.04, 0.035, 0.016, 2.54), Black);
            foreach (double z in new[] { -1.14, 1.25 })
            {
                AddMesh(HardwareMesh3D.Cylinder(0.038, 0.019, 0.1055), Steel).Transform = At(side * 1.48, 0, z);
            }

            AddMesh(Box(side * 0.99, 0.13, -0.04, 0.065, 0.026, 1.92), Edge);
        }
    }

    /// <summary>背板门：外框、门板、两个锁销座和锁销。</summary>
    private void BuildDoor()
    {
        _door.Add(Box(0, 1.96, -1.36, 2.52, 3.48, 0.11), Edge);
        _door.Add(Box(0, 1.96, -1.29, 2.38, 3.33, 0.055), Dark);
        foreach (double side in new[] { -1d, 1d })
        {
            _door.Add(Box(side * 0.71, 1.57, -1.23, 0.18, 0.39, 0.06), Black);
            _door.Add(HardwareMesh3D.Cylinder(0.055, 0.04, -0.02), Steel).Transform = AlongZ(side * 0.71, 1.57, -1.19);
        }
    }

    /// <summary>托板：板、三个定位销（黑座 + 钢销）、前面的挡块。载具坐在销上。</summary>
    private void BuildCarriage()
    {
        _carriage.Add(Box(0, 0.105, 0.16, 2.71, 0.036, 2.31), Coat);
        foreach (var (x, z) in new[] { (-0.82, 0.78), (0.82, 0.78), (0, -0.76) })
        {
            _carriage.Add(HardwareMesh3D.Cylinder(0.10, 0.055, 0.1175), Black).Transform = At(x, 0, z);
            _carriage.Add(HardwareMesh3D.Cylinder(0.05, 0.095, 0.1575), Steel).Transform = At(x, 0, z);
        }

        _carriage.Add(Box(0, 0.23, 1.36, 0.50, 0.23, 0.16), Dark);
        _carriage.Add(Box(0, 0.28, 1.455, 0.27, 0.055, 0.018), Steel);
    }

    /// <summary>面板上一盏灯：钢圈 + 灯罩（灯罩的材质跟状态换，不参与高亮）。</summary>
    private GeometryModel3D AddLamp(double x, double y)
    {
        AddMesh(HardwareMesh3D.Cylinder(0.046, 0.014, -0.007), Steel).Transform = AlongZ(x, y, -1.315);
        var lamp = new GeometryModel3D { Geometry = HardwareMesh3D.Cylinder(0.030, 0.020, -0.010), Transform = AlongZ(x, y, -1.30) };
        Model.Children.Add(lamp);
        return lamp;
    }

    private static MeshGeometry3D Box(double x, double y, double z, double width, double height, double depth)
    {
        return HardwareMesh3D.ChamferedBox(new Point3D(x - width / 2, y - height / 2, z - depth / 2), width, height, depth, Bevel);
    }

    /// <summary>沿 Y 建的圆柱放到 (x, y, z)。</summary>
    private static Transform3D At(double x, double y, double z)
    {
        var transform = new TranslateTransform3D(x, y, z);
        transform.Freeze();
        return transform;
    }

    /// <summary>沿 Y 建的圆柱转成沿 Z（朝操作员）再放到 (x, y, z)：灯、螺钉、锁销。</summary>
    private static Transform3D AlongZ(double x, double y, double z)
    {
        var transform = new Transform3DGroup
        {
            Children =
            {
                new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), 90)),
                new TranslateTransform3D(x, y, z),
            },
        };
        transform.Freeze();
        return transform;
    }

    #endregion

    /// <summary>LoadPort 上会动的一块（门、托板）：自己一套网格和高亮，动的时候只亮它。</summary>
    private sealed class MovingPart : HardwareVisual3D
    {
        public GeometryModel3D Add(MeshGeometry3D mesh, double brightness)
        {
            return AddMesh(mesh, brightness);
        }

        public void SetActive(bool active)
        {
            SetVisualActive(active);
        }
    }
}
