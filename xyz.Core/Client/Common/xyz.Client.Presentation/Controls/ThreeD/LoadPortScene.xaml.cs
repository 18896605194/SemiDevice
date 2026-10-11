using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using xyz.Client.Presentation.Localization;

namespace xyz.Client.Presentation.Controls.ThreeD;

/// <summary>
/// LoadPort 三维图：LoadPort（<see cref="LoadPortVisual3D"/>）托板上装一个载具（<see cref="CarrierVisual3D"/>），有载具才装上。
/// 状态都从依赖属性进来、原样转给两个组件：对接、门开 / 门在半路、面板上的灯，槽数和片（晶圆账）。
/// 左键拖动旋转视角、滚轮缩放、点一下晶圆选中看槽位；图下面一条工具栏：默认视角、俯视、背面。
/// 右上角写片数，框外一行写选中的槽位和空槽。
/// </summary>
public partial class LoadPortScene : UserControl
{
    #region 视角

    private const double DefaultYaw = -30;
    private const double DefaultPitch = 12;
    private const double TopPitch = 56;
    private const double TopZoom = 0.92;

    /// <summary>背面：从设备里面那侧看门和晶圆。</summary>
    private const double BackYaw = 162;

    private const double MinPitch = -11;
    private const double MaxPitch = 72;
    private const double MinZoom = 0.75;
    private const double MaxZoom = 1.2;

    /// <summary>拖动一像素转多少度：横向转方位、纵向转俯仰。</summary>
    private const double DragYawPerPixel = 0.4;

    private const double DragPitchPerPixel = 0.34;

    /// <summary>滚轮一格的 Delta（Windows 约定 120）和一格缩放多少。</summary>
    private const double WheelNotch = 120;

    private const double ZoomPerNotch = 0.06;

    /// <summary>正交相机离观察点的距离：只影响裁剪，不影响大小。</summary>
    private const double CameraDistance = 12;

    /// <summary>默认缩放时视野比整台高出多少（上下各留一点边）。</summary>
    private const double FitMargin = 1.2;

    /// <summary>图比这个宽高比还窄时按宽度撑开视野，左右不切。</summary>
    private const double NarrowAspect = 0.62;

    /// <summary>鼠标按下到抬起挪不到这么多像素算点击（选片），超过算拖动转视角。</summary>
    private const double ClickTolerance = 5;

    #endregion

    private readonly CarrierVisual3D _carrier = new();

    /// <summary>取景：看整台的中心、默认缩放时视野的半高。槽数多花篮高了跟着变（见 <see cref="Refit"/>）。</summary>
    private Point3D _cameraTarget;

    private double _fitHalfHeight = 1;
    private double _yaw = DefaultYaw;
    private double _pitch = DefaultPitch;
    private double _zoom = 1;
    private Point? _dragFrom;
    private Point? _pressedAt;

    public LoadPortScene()
    {
        InitializeComponent();
        ApplyTheme(Port);
        ApplyTheme(_carrier);
        Viewport.SizeChanged += (_, _) => UpdateCamera();
        Refit();
        UpdateInfo();
    }

    #region 依赖属性

    /// <summary>端口上有载具：有才装上花篮。</summary>
    public bool IsCarrierPresent
    {
        get => (bool)GetValue(IsCarrierPresentProperty);
        set => SetValue(IsCarrierPresentProperty, value);
    }

    public static readonly DependencyProperty IsCarrierPresentProperty = DependencyProperty.Register(
        nameof(IsCarrierPresent), typeof(bool), typeof(LoadPortScene), new PropertyMetadata(false, OnCarrierPresentChanged));

    /// <summary>托板推进去了（对接）。</summary>
    public bool IsDocked
    {
        get => (bool)GetValue(IsDockedProperty);
        set => SetValue(IsDockedProperty, value);
    }

    public static readonly DependencyProperty IsDockedProperty = DependencyProperty.Register(
        nameof(IsDocked), typeof(bool), typeof(LoadPortScene), new PropertyMetadata(false, OnPortChanged));

    /// <summary>门开到位。</summary>
    public bool IsDoorOpen
    {
        get => (bool)GetValue(IsDoorOpenProperty);
        set => SetValue(IsDoorOpenProperty, value);
    }

    public static readonly DependencyProperty IsDoorOpenProperty = DependencyProperty.Register(
        nameof(IsDoorOpen), typeof(bool), typeof(LoadPortScene), new PropertyMetadata(false, OnPortChanged));

    /// <summary>门在半路（开到位、关到位都没亮）。</summary>
    public bool IsDoorUnknown
    {
        get => (bool)GetValue(IsDoorUnknownProperty);
        set => SetValue(IsDoorUnknownProperty, value);
    }

    public static readonly DependencyProperty IsDoorUnknownProperty = DependencyProperty.Register(
        nameof(IsDoorUnknown), typeof(bool), typeof(LoadPortScene), new PropertyMetadata(false, OnPortChanged));

    /// <summary>在位反馈；null = 反馈不可用，灯灭。</summary>
    public bool? IsPresent
    {
        get => (bool?)GetValue(IsPresentProperty);
        set => SetValue(IsPresentProperty, value);
    }

    public static readonly DependencyProperty IsPresentProperty = DependencyProperty.Register(
        nameof(IsPresent), typeof(bool?), typeof(LoadPortScene), new PropertyMetadata(null, OnPortChanged));

    /// <summary>放置到位反馈；null = 反馈不可用，灯灭。</summary>
    public bool? IsPlaced
    {
        get => (bool?)GetValue(IsPlacedProperty);
        set => SetValue(IsPlacedProperty, value);
    }

    public static readonly DependencyProperty IsPlacedProperty = DependencyProperty.Register(
        nameof(IsPlaced), typeof(bool?), typeof(LoadPortScene), new PropertyMetadata(null, OnPortChanged));

    /// <summary>存取方式 Auto（AUTO 灯亮）/ Manual（MANUAL 灯亮）。</summary>
    public bool IsAutoMode
    {
        get => (bool)GetValue(IsAutoModeProperty);
        set => SetValue(IsAutoModeProperty, value);
    }

    public static readonly DependencyProperty IsAutoModeProperty = DependencyProperty.Register(
        nameof(IsAutoMode), typeof(bool), typeof(LoadPortScene), new PropertyMetadata(false, OnPortChanged));

    /// <summary>槽数（sc.xml 的 SlotCount）：花篮高度、槽号、片数、空槽都按它；0 = 还不知道，不装花篮。</summary>
    public int SlotCount
    {
        get => (int)GetValue(SlotCountProperty);
        set => SetValue(SlotCountProperty, value);
    }

    public static readonly DependencyProperty SlotCountProperty = DependencyProperty.Register(
        nameof(SlotCount), typeof(int), typeof(LoadPortScene), new PropertyMetadata(0, OnCarrierChanged));

    /// <summary>花篮里的片（晶圆账，WaferModel 按 Slot 摆、按 State 上色）。</summary>
    public IEnumerable? Wafers
    {
        get => (IEnumerable?)GetValue(WafersProperty);
        set => SetValue(WafersProperty, value);
    }

    public static readonly DependencyProperty WafersProperty = DependencyProperty.Register(
        nameof(Wafers), typeof(IEnumerable), typeof(LoadPortScene), new PropertyMetadata(null, OnCarrierChanged));

    private static void OnPortChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var scene = (LoadPortScene)sender;
        var port = scene.Port;
        port.IsDocked = scene.IsDocked;
        port.IsDoorOpen = scene.IsDoorOpen;
        port.IsDoorUnknown = scene.IsDoorUnknown;
        port.IsPresent = scene.IsPresent == true;
        port.IsPlaced = scene.IsPlaced == true;
        port.IsAutoMode = scene.IsAutoMode;
    }

    private static void OnCarrierPresentChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var scene = (LoadPortScene)sender;
        scene.AttachCarrier();
        scene.UpdateInfo();
    }

    private static void OnCarrierChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var scene = (LoadPortScene)sender;
        bool slotCountChanged = scene._carrier.SlotCount != scene.SlotCount;
        scene._carrier.SlotCount = scene.SlotCount;
        scene._carrier.Wafers = scene.Wafers;
        scene.AttachCarrier();
        if (slotCountChanged)
        {
            scene.Refit();
        }

        scene.UpdateInfo();
    }

    #endregion

    /// <summary>有载具、槽数也知道了才把花篮装到托板上；拿走就卸下、取消选中。装卸都重新取景。</summary>
    private void AttachCarrier()
    {
        bool show = IsCarrierPresent && SlotCount > 0;
        var attachments = Port.Attachments;
        if (show == attachments.Contains(_carrier))
        {
            return;
        }

        if (show)
        {
            attachments.Add(_carrier);
        }
        else
        {
            attachments.Remove(_carrier);
            _carrier.SelectedSlot = 0;
        }

        Refit();
    }

    /// <summary>按整台（含花篮）的实际高低取景：看它的中心，视野比它高出一点；槽数多、花篮高出背板时也不会切掉。</summary>
    private void Refit()
    {
        Rect3D bounds = VisualTreeHelper.GetContentBounds(Port);
        bounds.Union(VisualTreeHelper.GetDescendantBounds(Port));
        if (bounds.IsEmpty)
        {
            return;
        }

        _cameraTarget = new Point3D(0, bounds.Y + bounds.SizeY / 2, 0);
        _fitHalfHeight = bounds.SizeY / 2 * FitMargin;
        UpdateCamera();
    }

    /// <summary>硬件用主题的金属灰和强调色（token 跟组件默认值一样，统一从主题取）。</summary>
    private void ApplyTheme(HardwareVisual3D hardware)
    {
        if (TryFindResource("DarkAccent") is SolidColorBrush accent)
        {
            hardware.HighlightColor = accent.Color;
        }

        if (hardware is LoadPortVisual3D && TryFindResource("DarkHardwareBodyColor") is Color body)
        {
            hardware.BodyColor = body;
        }
    }

    /// <summary>右上角片数、框外一行（选中的槽位 / 提示，空槽）；没载具时都不写。</summary>
    private void UpdateInfo()
    {
        bool present = Port.Attachments.Contains(_carrier);
        var occupied = _carrier.OccupiedSlots;
        int total = _carrier.SlotCount;
        Counter.Visibility = present ? Visibility.Visible : Visibility.Collapsed;
        InfoRow.Visibility = present ? Visibility.Visible : Visibility.Hidden;
        WaferCount.Text = occupied.Count.ToString(CultureInfo.InvariantCulture);
        SlotTotal.Text = L10n.Get("lpscene.slot_total", total);

        int selected = _carrier.SelectedSlot;
        ClickHint.Visibility = selected == 0 ? Visibility.Visible : Visibility.Collapsed;
        SelectedSlotText.Visibility = selected == 0 ? Visibility.Collapsed : Visibility.Visible;
        SelectedSlotText.Text = L10n.Get("lpscene.selected_slot", selected);

        if (occupied.Count == 0)
        {
            EmptySlotsText.Text = L10n.Get("lpscene.no_wafer");
        }
        else if (occupied.Count == total)
        {
            EmptySlotsText.Text = L10n.Get("lpscene.full", total);
        }
        else
        {
            var empty = Enumerable.Range(1, total).Where(slot => !occupied.Contains(slot)).Select(slot => slot.ToString("00", CultureInfo.InvariantCulture));
            EmptySlotsText.Text = L10n.Get("lpscene.empty_slots", string.Join(" / ", empty));
        }

        EmptySlotsText.ToolTip = EmptySlotsText.Text;
    }

    #region 视角和点选

    private void UpdateCamera()
    {
        double yaw = _yaw * Math.PI / 180;
        double pitch = _pitch * Math.PI / 180;
        var offset = new Vector3D(
            CameraDistance * Math.Cos(pitch) * Math.Sin(yaw),
            CameraDistance * Math.Sin(pitch),
            CameraDistance * Math.Cos(pitch) * Math.Cos(yaw));
        Camera.Position = _cameraTarget + offset;
        Camera.LookDirection = -offset;
        double aspect = Viewport.ActualHeight > 0 && Viewport.ActualWidth > 0 ? Viewport.ActualWidth / Viewport.ActualHeight : 1;
        double halfHeight = Math.Max(_fitHalfHeight, _fitHalfHeight * NarrowAspect / aspect) / _zoom;
        Camera.Width = 2 * halfHeight * aspect;
    }

    private void SetView(double yaw, double pitch, double zoom)
    {
        _yaw = yaw;
        _pitch = pitch;
        _zoom = zoom;
        UpdateCamera();
    }

    private void OnDefaultViewClick(object sender, RoutedEventArgs args)
    {
        SetView(DefaultYaw, DefaultPitch, 1);
    }

    private void OnTopViewClick(object sender, RoutedEventArgs args)
    {
        SetView(DefaultYaw, TopPitch, TopZoom);
    }

    private void OnBackViewClick(object sender, RoutedEventArgs args)
    {
        SetView(BackYaw, DefaultPitch, 1);
    }

    private void OnViewportMouseLeftButtonDown(object sender, MouseButtonEventArgs args)
    {
        _dragFrom = args.GetPosition(Viewport);
        _pressedAt = _dragFrom;
        Viewport.CaptureMouse();
    }

    private void OnViewportMouseLeftButtonUp(object sender, MouseButtonEventArgs args)
    {
        var pressedAt = _pressedAt;
        Point releasedAt = args.GetPosition(Viewport);
        _dragFrom = null;
        _pressedAt = null;
        Viewport.ReleaseMouseCapture();
        if (pressedAt is not null && (releasedAt - pressedAt.Value).Length < ClickTolerance)
        {
            SelectWaferAt(releasedAt);
        }
    }

    private void OnViewportLostMouseCapture(object sender, MouseEventArgs args)
    {
        _dragFrom = null;
    }

    private void OnViewportMouseMove(object sender, MouseEventArgs args)
    {
        var from = _dragFrom;
        if (from is null)
        {
            return;
        }

        Point current = args.GetPosition(Viewport);
        _yaw -= (current.X - from.Value.X) * DragYawPerPixel;
        _pitch = Math.Clamp(_pitch + (current.Y - from.Value.Y) * DragPitchPerPixel, MinPitch, MaxPitch);
        _dragFrom = current;
        UpdateCamera();
    }

    private void OnViewportMouseWheel(object sender, MouseWheelEventArgs args)
    {
        _zoom = Math.Clamp(_zoom + args.Delta / WheelNotch * ZoomPerNotch, MinZoom, MaxZoom);
        UpdateCamera();
        args.Handled = true;
    }

    /// <summary>点到的第一片晶圆选中（挡在前面的立柱、槽齿不算，点在盘面上也算）；没点到片不改选中。</summary>
    private void SelectWaferAt(Point point)
    {
        if (!Port.Attachments.Contains(_carrier))
        {
            return;
        }

        int slot = 0;
        VisualTreeHelper.HitTest(Viewport, null, result =>
        {
            slot = _carrier.SlotOf(result.VisualHit);
            return slot > 0 ? HitTestResultBehavior.Stop : HitTestResultBehavior.Continue;
        }, new PointHitTestParameters(point));
        if (slot > 0)
        {
            _carrier.SelectedSlot = slot;
            UpdateInfo();
        }
    }

    #endregion
}
