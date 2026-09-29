using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using xyz.Client.Presentation.Models;

namespace xyz.Client.Presentation.Controls;

/// <summary>
/// 可等比缩放的腔体俯视图：腔体外壳 + 腔杯 + 片位上的圆片。
/// 片位数由使用方绑定（后端 sc.xml 的 SlotCount，腔体一般 1 片），0 表示还不知道、不画片位；
/// 片按 WaferModel.Slot 摆，颜色按片的状态，中间写片号（WaferModel.LpSlot）；空片位画成空盘。
/// </summary>
public partial class Chamber : UserControl
{
    public Chamber()
    {
        InitializeComponent();
        RebuildSlots();
    }

    public ObservableCollection<ChamberSlot> Slots { get; } = [];

    public int SlotCount
    {
        get => (int)GetValue(SlotCountProperty);
        set => SetValue(SlotCountProperty, value);
    }

    public static readonly DependencyProperty SlotCountProperty =
        DependencyProperty.Register(
            nameof(SlotCount), typeof(int), typeof(Chamber),
            new FrameworkPropertyMetadata(1, OnSlotCountChanged, CoerceSlotCount));

    public IEnumerable? Wafers
    {
        get => (IEnumerable?)GetValue(WafersProperty);
        set => SetValue(WafersProperty, value);
    }

    public static readonly DependencyProperty WafersProperty =
        DependencyProperty.Register(
            nameof(Wafers), typeof(IEnumerable), typeof(Chamber),
            new PropertyMetadata(null, OnWafersChanged));

    /// <summary>
    /// 片位横排在腔杯里，最多画 4 个，再多圆片就小得看不清了。
    /// </summary>
    private static object CoerceSlotCount(DependencyObject dependencyObject, object baseValue)
    {
        return Math.Clamp((int)baseValue, 0, 4);
    }

    private static void OnSlotCountChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is Chamber chamber)
        {
            chamber.RebuildSlots();
        }
    }

    private static void OnWafersChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not Chamber chamber)
        {
            return;
        }

        if (args.OldValue is INotifyCollectionChanged oldCollection)
        {
            oldCollection.CollectionChanged -= chamber.OnWafersCollectionChanged;
        }

        if (args.NewValue is INotifyCollectionChanged newCollection)
        {
            newCollection.CollectionChanged += chamber.OnWafersCollectionChanged;
        }

        chamber.RebuildSlots();
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
            Slots.Add(new ChamberSlot());
        }

        while (Slots.Count > count)
        {
            Slots.RemoveAt(Slots.Count - 1);
        }

        for (int index = 0; index < count; index++)
        {
            int slot = index + 1;
            Slots[index].Slot = slot;
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

        foreach (object? item in Wafers)
        {
            if (item is WaferModel wafer && wafer.Slot >= 1 && wafer.Slot <= slotCount && !result.ContainsKey(wafer.Slot))
            {
                result.Add(wafer.Slot, wafer);
            }
        }

        return result;
    }
}

/// <summary>
/// 腔体里单个片位的显示数据。
/// </summary>
public sealed class ChamberSlot : INotifyPropertyChanged
{
    private int _slot;
    public int Slot
    {
        get => _slot;
        set => SetProperty(ref _slot, value);
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
