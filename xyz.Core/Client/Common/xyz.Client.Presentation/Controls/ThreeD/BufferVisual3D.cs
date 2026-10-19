using System.Globalization;
using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using xyz.Client.Presentation.Models;

namespace xyz.Client.Presentation.Controls.ThreeD;

/// <summary>
/// 可放进共享 Viewport3D 的贯通缓存架。前后对称，仅显示槽位和晶圆，不包含相机、灯光或模块状态。
/// 三维对象不继承 DataContext，绑定数据时显式指定 Source 或 ElementName。
/// </summary>
public sealed class BufferVisual3D : HardwareVisual3D
{
    private const double SlotPitch = 0.17;
    private const double FirstHeight = 0.36;
    private readonly SortedDictionary<int, DiskVisual3D> _disks = [];
    private readonly SortedDictionary<int, WaferModel> _slotWafers = [];
    private readonly HashSet<WaferModel> _observedWafers = [];

    public BufferVisual3D()
    {
        RebuildGeometry();
    }

    /// <summary>最底层的物理槽号，默认 1。</summary>
    public int FirstSlot
    {
        get => (int)GetValue(FirstSlotProperty);
        set => SetValue(FirstSlotProperty, value);
    }

    public static readonly DependencyProperty FirstSlotProperty = DependencyProperty.Register(
        nameof(FirstSlot), typeof(int), typeof(BufferVisual3D), new PropertyMetadata(1, OnSlotsChanged),
        value => value is int first && first >= 1 && first <= int.MaxValue - 100);

    /// <summary>实际槽数，默认 10；0 清空结构和晶圆。固定紧凑槽距。</summary>
    public int SlotCount
    {
        get => (int)GetValue(SlotCountProperty);
        set => SetValue(SlotCountProperty, value);
    }

    public static readonly DependencyProperty SlotCountProperty = DependencyProperty.Register(
        nameof(SlotCount), typeof(int), typeof(BufferVisual3D), new PropertyMetadata(10, OnSlotsChanged),
        value => value is int count && count is >= 0 and <= 100);

    /// <summary>按 WaferModel.Slot 摆片、State 上色。跟踪集合和单片槽位变化；在 UI 线程更新。</summary>
    public IEnumerable? Wafers
    {
        get => (IEnumerable?)GetValue(WafersProperty);
        set => SetValue(WafersProperty, value);
    }

    public static readonly DependencyProperty WafersProperty = DependencyProperty.Register(
        nameof(Wafers), typeof(IEnumerable), typeof(BufferVisual3D), new PropertyMetadata(null, OnWafersChanged));

    private static readonly DependencyPropertyKey WaferCountPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(WaferCount), typeof(int), typeof(BufferVisual3D), new PropertyMetadata(0));

    public static readonly DependencyProperty WaferCountProperty = WaferCountPropertyKey.DependencyProperty;

    /// <summary>当前有效占用槽数，只读。</summary>
    public int WaferCount => (int)GetValue(WaferCountProperty);

    internal event EventHandler? GeometryChanged;
    internal event EventHandler? WaferCountChanged;

    private static void OnSlotsChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        // 普通页面控件通过 AddOwner 复用这些属性，只有三维件本身负责建模。
        if (sender is BufferVisual3D rack)
        {
            rack.RebuildGeometry();
        }
    }

    private static void OnWafersChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not BufferVisual3D rack)
        {
            return;
        }

        if (args.OldValue is INotifyCollectionChanged previous)
        {
            CollectionChangedEventManager.RemoveHandler(previous, rack.OnCollectionChanged);
        }

        if (args.NewValue is INotifyCollectionChanged current)
        {
            CollectionChangedEventManager.AddHandler(current, rack.OnCollectionChanged);
        }

        rack.RefreshWafers();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args) => RefreshWafers();

    private void OnWaferSlotChanged(object? sender, PropertyChangedEventArgs args) => RefreshWafers();

    private void RefreshWafers()
    {
        var wafers = Wafers?.OfType<WaferModel>().ToList() ?? [];
        var current = wafers.ToHashSet();
        // 弱事件避免外部的长寿命集合持有已移除的缓存架。
        foreach (var wafer in _observedWafers.Except(current).ToArray())
        {
            PropertyChangedEventManager.RemoveHandler(wafer, OnWaferSlotChanged, nameof(WaferModel.Slot));
            _observedWafers.Remove(wafer);
        }

        foreach (var wafer in current.Except(_observedWafers))
        {
            PropertyChangedEventManager.AddHandler(wafer, OnWaferSlotChanged, nameof(WaferModel.Slot));
            _observedWafers.Add(wafer);
        }

        _slotWafers.Clear();
        foreach (var wafer in wafers)
        {
            if (wafer.Slot >= FirstSlot && wafer.Slot - FirstSlot < SlotCount)
            {
                _slotWafers.TryAdd(wafer.Slot, wafer);
            }
        }

        UpdateDisks(_slotWafers);
        if (WaferCount != _slotWafers.Count)
        {
            SetValue(WaferCountPropertyKey, _slotWafers.Count);
            WaferCountChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void RebuildGeometry()
    {
        int firstSlot = FirstSlot;
        int slotCount = SlotCount;
        ClearMeshes();
        foreach (var disk in _disks.Values)
        {
            Children.Remove(disk);
        }

        _disks.Clear();
        if (slotCount == 0)
        {
            RefreshWafers();
            GeometryChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        double top = SlotHeight(firstSlot + slotCount - 1) + 0.2;
        AddBox(0, 0.1, 0, 2.24, 0.16, 1.99, 0.7);
        AddBox(0, 0.2, 0, 2.08, 0.06, 1.83, 0.9);
        foreach (double x in new[] { -0.98, 0.98 })
        {
            foreach (double z in new[] { -0.8, 0.8 })
            {
                AddBox(x, (top + 0.18) / 2, z, 0.075, top - 0.18, 0.075);
                AddBox(x, 0.235, z, 0.19, 0.08, 0.17, 0.8);
            }

            AddBox(x, top, 0, 0.085, 0.07, 1.67);
            for (int index = 0; index < slotCount; index++)
            {
                AddBox(Math.Sign(x) * 0.815, SlotHeight(firstSlot + index) - 0.025, 0, 0.23, 0.035, 1.57, 1.15);
            }
        }

        foreach (double z in new[] { -0.8, 0.8 })
        {
            AddBox(0, top, z, 2.04, 0.07, 0.075);
        }

        // 前后各自可读的槽号；几何、支撑和开口完全对称。
        for (int index = 0; index < slotCount; index++)
        {
            int slot = firstSlot + index;
            if (slotCount > 20 && index != 0 && index != slotCount - 1 && slot % 5 != 0)
            {
                continue;
            }

            AddLabel(slot, 1);
            AddLabel(slot, -1);
        }

        RefreshWafers();
        GeometryChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateDisks(IReadOnlyDictionary<int, WaferModel> wafers)
    {
        bool slotsChanged = !_disks.Keys.SequenceEqual(wafers.Keys.Order());
        if (slotsChanged)
        {
            // 从下往上装，避免二维盘面四角的透明区挡住后面的片；相同槽位复用已有盘面。
            var previous = new Dictionary<int, DiskVisual3D>(_disks);
            foreach (var disk in _disks.Values)
            {
                Children.Remove(disk);
            }

            _disks.Clear();
            foreach (int slot in wafers.Keys.Order())
            {
                var disk = previous.GetValueOrDefault(slot) ?? CreateDisk(slot);
                _disks.Add(slot, disk);
                Children.Add(disk);
            }
        }

        foreach (var (slot, disk) in _disks)
        {
            disk.Data = wafers[slot];
        }
    }

    private double SlotHeight(int slot) => FirstHeight + (slot - FirstSlot) * SlotPitch;

    private DiskVisual3D CreateDisk(int slot)
    {
        var disk = new DiskVisual3D
        {
            Radius = 0.755,
            Thickness = 0.017,
            Transform = new TranslateTransform3D(0, SlotHeight(slot) - 0.0085, 0),
        };
        if (Application.Current?.TryFindResource("DarkHardwareDiskBorder") is Brush border)
        {
            disk.BorderBrush = border;
        }

        BindingOperations.SetBinding(disk, HighlightColorProperty, new Binding(nameof(HighlightColor)) { Source = this });
        return disk;
    }

    private void AddBox(double x, double y, double z, double width, double height, double depth, double brightness = 1)
    {
        AddMesh(HardwareMesh3D.ChamferedBox(new Point3D(x - width / 2, y - height / 2, z - depth / 2),
            width, height, depth, 0.012), brightness);
    }

    private void AddLabel(int slot, int side)
    {
        var brush = new SolidColorBrush(HardwareTheme.ColorOf("DarkDisabledText", Colors.DarkGray));
        var text = new FormattedText(slot.ToString("00", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, new Typeface("Segoe UI"), 100, brush, 1);
        var drawing = new GeometryDrawing(brush, null, text.BuildGeometry(new Point()));
        var material = new EmissiveMaterial(new DrawingBrush(drawing) { Stretch = Stretch.Uniform });
        material.Freeze();
        var mesh = new MeshGeometry3D
        {
            Positions = new Point3DCollection { new(-0.07, -0.05, 0), new(0.07, -0.05, 0), new(0.07, 0.05, 0), new(-0.07, 0.05, 0) },
            TextureCoordinates = new PointCollection { new(0, 1), new(1, 1), new(1, 0), new(0, 0) },
            TriangleIndices = new Int32Collection { 0, 1, 2, 0, 2, 3 },
        };
        mesh.Freeze();
        var transform = new Transform3DGroup();
        if (side < 0)
        {
            transform.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), 180)));
        }

        transform.Children.Add(new TranslateTransform3D(-side * 1.27, SlotHeight(slot), side * 0.845));
        transform.Freeze();
        Model.Children.Add(new GeometryModel3D(mesh, material) { Transform = transform });
    }
}
