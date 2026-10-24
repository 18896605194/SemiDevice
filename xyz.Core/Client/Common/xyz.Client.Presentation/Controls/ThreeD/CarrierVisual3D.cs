using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using xyz.Client.Presentation.Models;

namespace xyz.Client.Presentation.Controls.ThreeD;

/// <summary>
/// 载具（花篮）三维外观：底板、两侧带槽齿的侧壁、前后立柱、顶梁和提手，前立柱上标槽号（1、5、10……）；
/// 有片的槽各放一片 <see cref="DiskVisual3D"/>（跟腔体的旋转盘同一个组件，盘面状态色、片号都沿用）。
/// 槽数照 machine.xml 配（<see cref="SlotCount"/>），槽距不变、槽数多花篮就高。
/// 开口朝 +Z（操作员那面）露出晶圆，背面朝 -Z 对着 LoadPort 背板。原点在底面中心，Y 向上；
/// 放进 <see cref="LoadPortVisual3D.Attachments"/> 就坐在托板的定位销上、跟着对接前后走。只显示，点了哪片由装配层用 <see cref="SlotOf"/> 认。
/// </summary>
public sealed class CarrierVisual3D : HardwareVisual3D
{
    /// <summary>槽距（场景示意单位）。</summary>
    private const double SlotPitch = 0.124;

    /// <summary>第 1 槽晶圆中心离底面的高度。</summary>
    private const double FirstSlotHeight = 0.265;

    private const double WaferRadius = 1.015;
    private const double WaferThickness = 0.03;

    /// <summary>侧壁底边离底面的高度、侧壁比"第 1 槽到最后一槽"多出的高度（上下留边）。</summary>
    private const double WallBottom = 0.075;

    private const double WallMargin = 0.404;

    /// <summary>侧壁外面四道加强筋在侧壁高度上的位置（比例）。</summary>
    private static readonly double[] RibLevels = [0.115, 0.367, 0.615, 0.870];

    /// <summary>倒角：塑料件圆一点。</summary>
    private const double Bevel = 0.03;

    /// <summary>槽号：每 5 槽标一个（加上第 1 槽和最后一槽），贴在前左立柱上（立柱宽 0.17）。</summary>
    private const int LabelEvery = 5;

    private const double LabelWidth = 0.16;
    private const double LabelHeight = 0.11;

    private static readonly Color FallbackLabelColor = Color.FromRgb(158, 158, 158);

    /// <summary>每槽的片（槽号在 1～槽数之内的，同一槽只认第一片）。</summary>
    private readonly SortedDictionary<int, WaferModel> _slotWafers = [];

    private readonly Dictionary<int, DiskVisual3D> _disks = [];

    static CarrierVisual3D()
    {
        // 深灰工程塑料，比 LoadPort 机身暗一档。
        BodyColorProperty.OverrideMetadata(typeof(CarrierVisual3D), new PropertyMetadata(Color.FromRgb(72, 72, 72)));
    }

    public CarrierVisual3D()
    {
        RebuildGeometry();
    }

    /// <summary>槽数（machine.xml 里 LoadPort 的 SlotCount），花篮高度、槽齿、槽号都跟着它；0 = 还不知道，什么都不画。</summary>
    public int SlotCount
    {
        get => (int)GetValue(SlotCountProperty);
        set => SetValue(SlotCountProperty, value);
    }

    public static readonly DependencyProperty SlotCountProperty = DependencyProperty.Register(
        nameof(SlotCount), typeof(int), typeof(CarrierVisual3D),
        new PropertyMetadata(0, OnGeometryChanged, CoerceSlotCount));

    /// <summary>花篮里的片（<see cref="WaferModel"/>，按 Slot 放进槽里，交给 DiskVisual3D 按 State 上色）；整个换掉才刷新。</summary>
    public IEnumerable? Wafers
    {
        get => (IEnumerable?)GetValue(WafersProperty);
        set => SetValue(WafersProperty, value);
    }

    public static readonly DependencyProperty WafersProperty = DependencyProperty.Register(
        nameof(Wafers), typeof(IEnumerable), typeof(CarrierVisual3D), new PropertyMetadata(null, OnWafersChanged));

    /// <summary>选中的槽（点了哪片），0 = 没选；那一片高亮。</summary>
    public int SelectedSlot
    {
        get => (int)GetValue(SelectedSlotProperty);
        set => SetValue(SelectedSlotProperty, value);
    }

    public static readonly DependencyProperty SelectedSlotProperty = DependencyProperty.Register(
        nameof(SelectedSlot), typeof(int), typeof(CarrierVisual3D), new PropertyMetadata(0, OnSelectedSlotChanged));

    /// <summary>有片的槽号（从小到大）。</summary>
    public IReadOnlyList<int> OccupiedSlots => _slotWafers.Keys.ToList();

    /// <summary>点到的东西（三维件或盘面上的二维元素）是哪一槽的片；不是片返回 0。</summary>
    public int SlotOf(DependencyObject? hit)
    {
        var node = hit;
        while (node is not null && !ReferenceEquals(node, this))
        {
            if (node is DiskVisual3D disk)
            {
                foreach (var (slot, candidate) in _disks)
                {
                    if (ReferenceEquals(candidate, disk))
                    {
                        return slot;
                    }
                }

                return 0;
            }

            node = VisualTreeHelper.GetParent(node);
        }

        return 0;
    }

    private static object CoerceSlotCount(DependencyObject sender, object value)
    {
        return Math.Max(0, (int)value);
    }

    private static void OnGeometryChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        ((CarrierVisual3D)sender).RebuildGeometry();
    }

    private static void OnWafersChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        ((CarrierVisual3D)sender).UpdateWafers();
    }

    private static void OnSelectedSlotChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var carrier = (CarrierVisual3D)sender;
        foreach (var (slot, disk) in carrier._disks)
        {
            disk.IsSelected = slot == carrier.SelectedSlot;
        }
    }

    private double WallHeight => (SlotCount - 1) * SlotPitch + WallMargin;

    private static double SlotHeight(int slot) => FirstSlotHeight + (slot - 1) * SlotPitch;

    /// <summary>花篮框架、槽齿、槽号按槽数重搭；片的槽位高度跟着变，所以片也重摆。</summary>
    private void RebuildGeometry()
    {
        ClearMeshes();
        foreach (var disk in _disks.Values)
        {
            Children.Remove(disk);
        }

        _disks.Clear();
        if (SlotCount > 0)
        {
            double wallHeight = WallHeight;
            double wallMiddle = WallBottom + wallHeight / 2;
            double wallTop = WallBottom + wallHeight;

            // 底板、顶梁（靠背面）、提手。
            AddMesh(Box(0, 0.07, 0, 2.45, 0.14, 2.32));
            AddMesh(Box(0, wallTop + 0.03, -0.87, 2.45, 0.13, 0.43));
            AddMesh(Box(0, wallTop + 0.11, -0.72, 0.74, 0.09, 0.19), 1.33);
            foreach (double side in new[] { -1d, 1d })
            {
                // 侧壁、前立柱、后立柱、外侧加强筋。
                AddMesh(Box(side * 1.14, wallMiddle, -0.15, 0.16, wallHeight, 2.12));
                AddMesh(Box(side * 1.14, wallMiddle, 0.98, 0.17, wallHeight, 0.17), 1.33);
                AddMesh(Box(side * 1.14, wallMiddle, -1.10, 0.19, wallHeight, 0.16), 1.33);
                foreach (double level in RibLevels)
                {
                    AddMesh(Box(side * 1.214, WallBottom + level * wallHeight, -0.24, 0.10, 0.055, 1.71), 1.33);
                }

                // 每槽一对槽齿，片的边搭在上面。
                for (int slot = 1; slot <= SlotCount; slot++)
                {
                    AddMesh(Box(side * WaferRadius, SlotHeight(slot) - 0.035, -0.06, 0.17, 0.026, 1.74), 1.33);
                }
            }

            AddLabels();
        }

        UpdateWafers();
    }

    /// <summary>前左立柱上的槽号：第 1 槽、每 5 槽、最后一槽。字是几何图形贴的，放大不糊；自发光，背面看不见。</summary>
    private void AddLabels()
    {
        Color color = HardwareTheme.ColorOf("DarkDisabledText", FallbackLabelColor);
        for (int slot = 1; slot <= SlotCount; slot++)
        {
            if (slot != 1 && slot % LabelEvery != 0 && slot != SlotCount)
            {
                continue;
            }

            var quad = new MeshGeometry3D
            {
                Positions = new Point3DCollection
                {
                    new(-LabelWidth / 2, -LabelHeight / 2, 0), new(LabelWidth / 2, -LabelHeight / 2, 0),
                    new(LabelWidth / 2, LabelHeight / 2, 0), new(-LabelWidth / 2, LabelHeight / 2, 0),
                },
                TextureCoordinates = new PointCollection { new(0, 1), new(1, 1), new(1, 0), new(0, 0) },
                TriangleIndices = new Int32Collection { 0, 1, 2, 0, 2, 3 },
            };
            quad.Freeze();
            var label = new GeometryModel3D(quad, LabelMaterial(slot.ToString("00", CultureInfo.InvariantCulture), color))
            {
                Transform = Frozen(new TranslateTransform3D(-1.145, SlotHeight(slot), 1.08)),
            };
            Model.Children.Add(label);
        }
    }

    private static Material LabelMaterial(string text, Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 100, brush, 1);
        var drawing = new GeometryDrawing(brush, null, formatted.BuildGeometry(new Point(0, 0)));
        var fill = new DrawingBrush(drawing) { Stretch = Stretch.Uniform };
        fill.Freeze();
        var material = new EmissiveMaterial(fill);
        material.Freeze();
        return material;
    }

    /// <summary>
    /// 按 Wafers 摆片：有片的槽还是那几个就只换每片的数据（推送一来就换，不重建）；哪几槽有片变了才按槽号从下往上重摆
    /// （盘面四角是透明的，下面的先画、上面的后画，从斜上方看不会被挡出缺口）。选中的槽没片了就取消选中。
    /// </summary>
    private void UpdateWafers()
    {
        _slotWafers.Clear();
        var wafers = Wafers;
        if (wafers is not null)
        {
            foreach (object? item in wafers)
            {
                if (item is WaferModel wafer && wafer.Slot >= 1 && wafer.Slot <= SlotCount && !_slotWafers.ContainsKey(wafer.Slot))
                {
                    _slotWafers.Add(wafer.Slot, wafer);
                }
            }
        }

        if (!_disks.Keys.Order().SequenceEqual(_slotWafers.Keys))
        {
            foreach (var disk in _disks.Values)
            {
                Children.Remove(disk);
            }

            var reused = new Dictionary<int, DiskVisual3D>(_disks);
            _disks.Clear();
            foreach (int slot in _slotWafers.Keys)
            {
                var disk = reused.GetValueOrDefault(slot) ?? CreateDisk(slot);
                _disks.Add(slot, disk);
                Children.Add(disk);
            }
        }

        foreach (var (slot, disk) in _disks)
        {
            disk.Data = _slotWafers[slot];
            disk.IsSelected = slot == SelectedSlot;
        }

        if (SelectedSlot != 0 && !_slotWafers.ContainsKey(SelectedSlot))
        {
            SelectedSlot = 0;
        }
    }

    /// <summary>一槽一片：跟腔体的旋转盘同一个组件、同一套盘面颜色，盘底坐在槽齿上。</summary>
    private DiskVisual3D CreateDisk(int slot)
    {
        var disk = new DiskVisual3D
        {
            Radius = WaferRadius,
            Thickness = WaferThickness,
            Transform = Frozen(new TranslateTransform3D(0, SlotHeight(slot) - WaferThickness / 2, 0)),
        };
        if (Application.Current?.TryFindResource("DarkHardwareDiskFill") is Brush fill)
        {
            disk.FillColor = fill;
        }

        if (Application.Current?.TryFindResource("DarkHardwareDiskBorder") is Brush border)
        {
            disk.BorderBrush = border;
        }

        BindingOperations.SetBinding(disk, HighlightColorProperty, new Binding(nameof(HighlightColor)) { Source = this, Mode = BindingMode.OneWay });
        return disk;
    }

    private static MeshGeometry3D Box(double x, double y, double z, double width, double height, double depth)
    {
        return HardwareMesh3D.ChamferedBox(new Point3D(x - width / 2, y - height / 2, z - depth / 2), width, height, depth, Bevel);
    }

    private static Transform3D Frozen(Transform3D transform)
    {
        transform.Freeze();
        return transform;
    }
}
