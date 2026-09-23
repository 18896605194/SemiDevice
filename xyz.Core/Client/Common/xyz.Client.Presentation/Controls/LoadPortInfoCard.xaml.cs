using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using xyz.Client.Presentation.Models;

namespace xyz.Client.Presentation.Controls;

/// <summary>
/// 标题栏位于底部的 LoadPort 信息卡片：左花篮槽位（槽数跟 sc 的 SlotCount）右 disk 圆片，底栏标题 + 状态灯。
/// 槽位重建逻辑与手动页的 <see cref="LoadPort"/> 同源；调度图下排两颗 LP 用它。
/// </summary>
public partial class LoadPortInfoCard : UserControl
{
    public LoadPortInfoCard()
    {
        InitializeComponent();
        RebuildSlots();
    }

    public ObservableCollection<LoadPortSlot> Slots { get; } = [];

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(LoadPortInfoCard),
            new PropertyMetadata(string.Empty));

    /// <summary>底栏 Online 文案。</summary>
    public string IsOnline
    {
        get => (string)GetValue(IsOnlineProperty);
        set => SetValue(IsOnlineProperty, value);
    }

    public static readonly DependencyProperty IsOnlineProperty =
        DependencyProperty.Register(nameof(IsOnline), typeof(string), typeof(LoadPortInfoCard),
            new PropertyMetadata(string.Empty));

    /// <summary>通讯灯：驱动串口是否连上。</summary>
    public bool IsConnected
    {
        get => (bool)GetValue(IsConnectedProperty);
        set => SetValue(IsConnectedProperty, value);
    }

    public static readonly DependencyProperty IsConnectedProperty =
        DependencyProperty.Register(nameof(IsConnected), typeof(bool), typeof(LoadPortInfoCard),
            new PropertyMetadata(false));

    /// <summary>在位灯：FOUP 是否放上。</summary>
    public bool Present
    {
        get => (bool)GetValue(PresentProperty);
        set => SetValue(PresentProperty, value);
    }

    public static readonly DependencyProperty PresentProperty =
        DependencyProperty.Register(nameof(Present), typeof(bool), typeof(LoadPortInfoCard),
            new PropertyMetadata(false));

    /// <summary>到位灯：FOUP 是否夹紧放平。</summary>
    public bool Placed
    {
        get => (bool)GetValue(PlacedProperty);
        set => SetValue(PlacedProperty, value);
    }

    public static readonly DependencyProperty PlacedProperty =
        DependencyProperty.Register(nameof(Placed), typeof(bool), typeof(LoadPortInfoCard),
            new PropertyMetadata(false));

    /// <summary>报警灯：设备硬件报警。</summary>
    public bool Alarm
    {
        get => (bool)GetValue(AlarmProperty);
        set => SetValue(AlarmProperty, value);
    }

    public static readonly DependencyProperty AlarmProperty =
        DependencyProperty.Register(nameof(Alarm), typeof(bool), typeof(LoadPortInfoCard),
            new PropertyMetadata(false));

    /// <summary>AUTO / MANUAL 灯：true=Auto，false=Manual。</summary>
    public bool AutoMode
    {
        get => (bool)GetValue(AutoModeProperty);
        set => SetValue(AutoModeProperty, value);
    }

    public static readonly DependencyProperty AutoModeProperty =
        DependencyProperty.Register(nameof(AutoMode), typeof(bool), typeof(LoadPortInfoCard),
            new PropertyMetadata(true));

    /// <summary>花篮里的片（有片的槽位）；槽数由 <see cref="SlotCount"/> 决定。</summary>
    public IEnumerable? Wafers
    {
        get => (IEnumerable?)GetValue(WafersProperty);
        set => SetValue(WafersProperty, value);
    }

    public static readonly DependencyProperty WafersProperty =
        DependencyProperty.Register(nameof(Wafers), typeof(IEnumerable), typeof(LoadPortInfoCard),
            new PropertyMetadata(null, OnWafersChanged));

    /// <summary>花篮槽数（sc.xml 的 LoadPort.SlotCount）；0 表示还不知道、不画槽。</summary>
    public int SlotCount
    {
        get => (int)GetValue(SlotCountProperty);
        set => SetValue(SlotCountProperty, value);
    }

    public static readonly DependencyProperty SlotCountProperty =
        DependencyProperty.Register(nameof(SlotCount), typeof(int), typeof(LoadPortInfoCard),
            new FrameworkPropertyMetadata(0, OnSlotCountChanged, CoerceSlotCount));

    /// <summary>右侧 disk 圆片上的片（与 ChamberCard 同字段）。</summary>
    public WaferModel? Wafer
    {
        get => (WaferModel?)GetValue(WaferProperty);
        set => SetValue(WaferProperty, value);
    }

    public static readonly DependencyProperty WaferProperty =
        DependencyProperty.Register(nameof(Wafer), typeof(WaferModel), typeof(LoadPortInfoCard),
            new PropertyMetadata(null));

    public ICommand? CreateCommand
    {
        get => (ICommand?)GetValue(CreateCommandProperty);
        set => SetValue(CreateCommandProperty, value);
    }

    public static readonly DependencyProperty CreateCommandProperty =
        DependencyProperty.Register(nameof(CreateCommand), typeof(ICommand), typeof(LoadPortInfoCard),
            new PropertyMetadata(null));

    public ICommand? DeleteCommand
    {
        get => (ICommand?)GetValue(DeleteCommandProperty);
        set => SetValue(DeleteCommandProperty, value);
    }

    public static readonly DependencyProperty DeleteCommandProperty =
        DependencyProperty.Register(nameof(DeleteCommand), typeof(ICommand), typeof(LoadPortInfoCard),
            new PropertyMetadata(null));

    public bool CreateEnable
    {
        get => (bool)GetValue(CreateEnableProperty);
        set => SetValue(CreateEnableProperty, value);
    }

    public static readonly DependencyProperty CreateEnableProperty =
        DependencyProperty.Register(nameof(CreateEnable), typeof(bool), typeof(LoadPortInfoCard),
            new PropertyMetadata(true));

    public bool DeleteEnable
    {
        get => (bool)GetValue(DeleteEnableProperty);
        set => SetValue(DeleteEnableProperty, value);
    }

    public static readonly DependencyProperty DeleteEnableProperty =
        DependencyProperty.Register(nameof(DeleteEnable), typeof(bool), typeof(LoadPortInfoCard),
            new PropertyMetadata(true));

    public double RotationSpeed
    {
        get => (double)GetValue(RotationSpeedProperty);
        set => SetValue(RotationSpeedProperty, value);
    }

    public static readonly DependencyProperty RotationSpeedProperty =
        DependencyProperty.Register(nameof(RotationSpeed), typeof(double), typeof(LoadPortInfoCard),
            new PropertyMetadata(0.0));

    private static object CoerceSlotCount(DependencyObject dependencyObject, object baseValue)
    {
        return Math.Clamp((int)baseValue, 0, 50);
    }

    private static void OnSlotCountChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is LoadPortInfoCard card)
        {
            card.RebuildSlots();
        }
    }

    private static void OnWafersChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not LoadPortInfoCard card)
        {
            return;
        }

        if (args.OldValue is INotifyCollectionChanged oldCollection)
        {
            oldCollection.CollectionChanged -= card.OnWafersCollectionChanged;
        }

        if (args.NewValue is INotifyCollectionChanged newCollection)
        {
            newCollection.CollectionChanged += card.OnWafersCollectionChanged;
        }

        card.RebuildSlots();
    }

    private void OnWafersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        RebuildSlots();
    }

    private void RebuildSlots()
    {
        int count = SlotCount;
        var wafersBySlot = BuildWaferSlotMap(count);

        while (Slots.Count < count)
        {
            Slots.Add(new LoadPortSlot());
        }

        while (Slots.Count > count)
        {
            Slots.RemoveAt(Slots.Count - 1);
        }

        for (int index = 0; index < count; index++)
        {
            // 自顶向下编号：大号在上（25 顶、01 底），与 UniformGrid 的填充顺序一致
            int slot = count - index;
            Slots[index].Slot = slot;
            Slots[index].SlotText = slot.ToString("00");
            Slots[index].Wafer = wafersBySlot.GetValueOrDefault(slot);
        }
    }

    private Dictionary<int, WaferModel> BuildWaferSlotMap(int slotCount)
    {
        var result = new Dictionary<int, WaferModel>();
        if (Wafers is null)
        {
            return result;
        }

        int index = 0;
        foreach (object? item in Wafers)
        {
            if (item is WaferModel wafer)
            {
                int slot = ResolveSlot(wafer, index + 1, slotCount);
                if (slot > 0 && !result.ContainsKey(slot))
                {
                    result.Add(slot, wafer);
                }
            }

            index++;
        }

        return result;
    }

    private static int ResolveSlot(WaferModel wafer, int fallbackSlot, int slotCount)
    {
        if (wafer.Slot >= 1 && wafer.Slot <= slotCount)
        {
            return wafer.Slot;
        }

        if (int.TryParse(wafer.LpSlot, out int lpSlot) && lpSlot >= 1 && lpSlot <= slotCount)
        {
            return lpSlot;
        }

        return fallbackSlot <= slotCount ? fallbackSlot : 0;
    }
}
