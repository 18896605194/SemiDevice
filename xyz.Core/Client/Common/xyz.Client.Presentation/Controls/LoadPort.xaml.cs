using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using xyz.Client.Presentation.Models;

namespace xyz.Client.Presentation.Controls;

/// <summary>
/// 可等比缩放的垂直 Load Port 槽位控件，默认显示 25 个槽位。
/// </summary>
public partial class LoadPort : UserControl
{
    public LoadPort()
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
        DependencyProperty.Register(
            nameof(Title), typeof(string), typeof(LoadPort),
            new PropertyMetadata("LOAD PORT"));

    /// <summary>
    /// 标题行显示的当前状态文字（如“空闲”“装载中”），由页面绑定，空串不占位。
    /// </summary>
    public string StatusText
    {
        get => (string)GetValue(StatusTextProperty);
        set => SetValue(StatusTextProperty, value);
    }

    public static readonly DependencyProperty StatusTextProperty =
        DependencyProperty.Register(
            nameof(StatusText), typeof(string), typeof(LoadPort),
            new PropertyMetadata(string.Empty));

    public int SlotCount
    {
        get => (int)GetValue(SlotCountProperty);
        set => SetValue(SlotCountProperty, value);
    }

    public static readonly DependencyProperty SlotCountProperty =
        DependencyProperty.Register(
            nameof(SlotCount), typeof(int), typeof(LoadPort),
            new FrameworkPropertyMetadata(25, OnSlotCountChanged, CoerceSlotCount));

    public IEnumerable? Wafers
    {
        get => (IEnumerable?)GetValue(WafersProperty);
        set => SetValue(WafersProperty, value);
    }

    public static readonly DependencyProperty WafersProperty =
        DependencyProperty.Register(
            nameof(Wafers), typeof(IEnumerable), typeof(LoadPort),
            new PropertyMetadata(null, OnWafersChanged));

    public ICommand? CreateCommand
    {
        get => (ICommand?)GetValue(CreateCommandProperty);
        set => SetValue(CreateCommandProperty, value);
    }

    public static readonly DependencyProperty CreateCommandProperty =
        DependencyProperty.Register(
            nameof(CreateCommand), typeof(ICommand), typeof(LoadPort),
            new PropertyMetadata(null));

    public ICommand? DeleteCommand
    {
        get => (ICommand?)GetValue(DeleteCommandProperty);
        set => SetValue(DeleteCommandProperty, value);
    }

    public static readonly DependencyProperty DeleteCommandProperty =
        DependencyProperty.Register(
            nameof(DeleteCommand), typeof(ICommand), typeof(LoadPort),
            new PropertyMetadata(null));

    public bool CreateEnable
    {
        get => (bool)GetValue(CreateEnableProperty);
        set => SetValue(CreateEnableProperty, value);
    }

    public static readonly DependencyProperty CreateEnableProperty =
        DependencyProperty.Register(
            nameof(CreateEnable), typeof(bool), typeof(LoadPort),
            new PropertyMetadata(true));

    public bool DeleteEnable
    {
        get => (bool)GetValue(DeleteEnableProperty);
        set => SetValue(DeleteEnableProperty, value);
    }

    public static readonly DependencyProperty DeleteEnableProperty =
        DependencyProperty.Register(
            nameof(DeleteEnable), typeof(bool), typeof(LoadPort),
            new PropertyMetadata(true));

    private static object CoerceSlotCount(DependencyObject dependencyObject, object baseValue)
    {
        return Math.Clamp((int)baseValue, 1, 50);
    }

    private static void OnSlotCountChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is LoadPort loadPort)
        {
            loadPort.RebuildSlots();
        }
    }

    private static void OnWafersChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not LoadPort loadPort)
        {
            return;
        }

        if (args.OldValue is INotifyCollectionChanged oldCollection)
        {
            oldCollection.CollectionChanged -= loadPort.OnWafersCollectionChanged;
        }

        if (args.NewValue is INotifyCollectionChanged newCollection)
        {
            newCollection.CollectionChanged += loadPort.OnWafersCollectionChanged;
        }

        loadPort.RebuildSlots();
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

/// <summary>
/// Load Port 中单个物理槽位的显示数据。
/// </summary>
public sealed class LoadPortSlot : INotifyPropertyChanged
{
    private int _slot;
    public int Slot
    {
        get => _slot;
        set => SetProperty(ref _slot, value);
    }

    private string _slotText = string.Empty;
    public string SlotText
    {
        get => _slotText;
        set => SetProperty(ref _slotText, value);
    }

    private WaferModel? _wafer;
    public WaferModel? Wafer
    {
        get => _wafer;
        set => SetProperty(ref _wafer, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
