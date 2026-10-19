using System.Collections;
using System.Windows;
using System.Windows.Controls;
using xyz.Client.Presentation.Models;

namespace xyz.Client.Presentation.Controls;

/// <summary>Buffer 模块卡片：标题和状态围绕可独立使用的 BufferView，数据与交互由内层视图处理。</summary>
public partial class Buffer : UserControl
{
    public Buffer()
    {
        InitializeComponent();
        Scene.WaferCountChanged += (_, _) => UpdateCount();
        UpdateCount();
    }

    #region 依赖属性

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(Buffer), new PropertyMetadata("Buffer"));

    /// <summary>状态文案由调用方提供，空字符串不显示徽标。</summary>
    public string StateText
    {
        get => (string)GetValue(StateTextProperty);
        set => SetValue(StateTextProperty, value);
    }

    public static readonly DependencyProperty StateTextProperty = DependencyProperty.Register(
        nameof(StateText), typeof(string), typeof(Buffer), new PropertyMetadata(string.Empty));

    public ModuleStateTone StateTone
    {
        get => (ModuleStateTone)GetValue(StateToneProperty);
        set => SetValue(StateToneProperty, value);
    }

    public static readonly DependencyProperty StateToneProperty = DependencyProperty.Register(
        nameof(StateTone), typeof(ModuleStateTone), typeof(Buffer), new PropertyMetadata(ModuleStateTone.Inactive));

    /// <summary>参与调度的在线状态；无通讯驱动的被动 Buffer 也可使用。</summary>
    public bool IsOnline
    {
        get => (bool)GetValue(IsOnlineProperty);
        set => SetValue(IsOnlineProperty, value);
    }

    public static readonly DependencyProperty IsOnlineProperty = DependencyProperty.Register(
        nameof(IsOnline), typeof(bool), typeof(Buffer), new PropertyMetadata(false));

    /// <summary>最底层的物理槽号；用于显示同一个架子的某个槽段，默认 1。</summary>
    public int FirstSlot
    {
        get => (int)GetValue(FirstSlotProperty);
        set => SetValue(FirstSlotProperty, value);
    }

    public static readonly DependencyProperty FirstSlotProperty = BufferView.FirstSlotProperty.AddOwner(typeof(Buffer));

    /// <summary>实际槽数，默认 10；0 显示空视图。固定槽距，增减槽数后自动重新取景。</summary>
    public int SlotCount
    {
        get => (int)GetValue(SlotCountProperty);
        set => SetValue(SlotCountProperty, value);
    }

    public static readonly DependencyProperty SlotCountProperty = BufferView.SlotCountProperty.AddOwner(typeof(Buffer));

    /// <summary>
    /// WaferModel 集合，按物理 Slot 摆片、State 上色；支持集合增删、替换及单片 Slot/State 变化。
    /// 范围外的片不显示，同槽重复只显示第一片；不按 LoadPort 的来源槽号推断位置。
    /// 集合与模型应像其它 WPF 绑定数据一样在 UI 线程更新。
    /// </summary>
    public IEnumerable? Wafers
    {
        get => (IEnumerable?)GetValue(WafersProperty);
        set => SetValue(WafersProperty, value);
    }

    public static readonly DependencyProperty WafersProperty = BufferView.WafersProperty.AddOwner(typeof(Buffer));

    private static readonly DependencyPropertyKey WaferCountPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(WaferCount), typeof(int), typeof(Buffer), new PropertyMetadata(0));

    public static readonly DependencyProperty WaferCountProperty = WaferCountPropertyKey.DependencyProperty;

    /// <summary>当前显示的片数，只读，按实际有效占用槽统计。</summary>
    public int WaferCount => (int)GetValue(WaferCountProperty);

    #endregion

    private void UpdateCount() => SetValue(WaferCountPropertyKey, Scene.WaferCount);

    private void OnDefaultViewClick(object sender, RoutedEventArgs args) => Scene.ResetView();

    private void OnBackViewClick(object sender, RoutedEventArgs args) => Scene.ShowBackView();
}
