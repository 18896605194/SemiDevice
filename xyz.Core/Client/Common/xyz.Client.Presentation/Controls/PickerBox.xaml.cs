using System.Collections;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using xyz.Client.Presentation.Dialogs;

namespace xyz.Client.Presentation.Controls;

/// <summary>
/// 公共选择框：从一个库里选一项（如流程配方里选工艺配方）不用下拉框，用它——左边显示选中的值，右边"…"点开公共选择弹窗。
/// 弹窗里显示什么由用的地方给：标题（PickerTitle）、列（Columns）、数据（ItemsSource）、选中后回填哪个属性（ValuePath）。
/// 用法跟 InputTextBox 一样绑 Value；宽高直接设 Width / Height，或者用 DefaultPickerBoxStyle / ToolbarPickerBoxStyle / CompactPickerBoxStyle。
/// </summary>
public partial class PickerBox : UserControl
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(string), typeof(PickerBox),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(
        nameof(Placeholder), typeof(string), typeof(PickerBox), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty PickerTitleProperty = DependencyProperty.Register(
        nameof(PickerTitle), typeof(string), typeof(PickerBox), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IEnumerable), typeof(PickerBox), new PropertyMetadata(null));

    public static readonly DependencyProperty ValuePathProperty = DependencyProperty.Register(
        nameof(ValuePath), typeof(string), typeof(PickerBox), new PropertyMetadata("Name"));

    public static readonly DependencyProperty IsEditableProperty = DependencyProperty.Register(
        nameof(IsEditable), typeof(bool), typeof(PickerBox),
        new PropertyMetadata(false, (d, _) => ((PickerBox)d).ApplyEditable()));

    public static readonly DependencyProperty HasErrorProperty = DependencyProperty.Register(
        nameof(HasError), typeof(bool), typeof(PickerBox), new PropertyMetadata(false));

    public PickerBox()
    {
        InitializeComponent();

        // 没指定样式时用默认那一档（高 40）；XAML 里写了 Style 或直接写 Height 会覆盖它。
        SetResourceReference(StyleProperty, "DefaultPickerBoxStyle");
        ApplyEditable();
    }

    /// <summary>
    /// 选中的值（双向绑定）：弹窗里选中那一项的 ValuePath 属性；能手输时也是输入的字。
    /// </summary>
    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>
    /// 没有值时框里的提示字。
    /// </summary>
    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    /// <summary>
    /// 弹窗标题。
    /// </summary>
    public string PickerTitle
    {
        get => (string)GetValue(PickerTitleProperty);
        set => SetValue(PickerTitleProperty, value);
    }

    /// <summary>
    /// 弹窗里列出来的数据。
    /// </summary>
    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <summary>
    /// 选中后把数据项的哪个属性填回 Value（默认 Name）。
    /// </summary>
    public string ValuePath
    {
        get => (string)GetValue(ValuePathProperty);
        set => SetValue(ValuePathProperty, value);
    }

    /// <summary>
    /// 能不能手输：默认不能（只能从弹窗里选，免得打错名字）。
    /// </summary>
    public bool IsEditable
    {
        get => (bool)GetValue(IsEditableProperty);
        set => SetValue(IsEditableProperty, value);
    }

    /// <summary>
    /// 值不对（没选、选的不存在……）：框变红。
    /// </summary>
    public bool HasError
    {
        get => (bool)GetValue(HasErrorProperty);
        set => SetValue(HasErrorProperty, value);
    }

    /// <summary>
    /// 弹窗里显示哪几列，在 XAML 里写。
    /// </summary>
    public ObservableCollection<PickerColumn> Columns { get; } = [];

    private void ApplyEditable()
    {
        ValueBox.IsReadOnly = !IsEditable;
        ValueBox.Cursor = IsEditable ? Cursors.IBeam : Cursors.Hand;
    }

    private void OnPickClick(object sender, RoutedEventArgs args)
    {
        Pick();
    }

    /// <summary>
    /// 只能选的时候，点文字也弹窗。
    /// </summary>
    private void OnValueBoxClick(object sender, MouseButtonEventArgs args)
    {
        if (!IsEditable)
        {
            Pick();
        }
    }

    private void Pick()
    {
        var picked = DialogService.ShowPicker(PickerTitle, Columns, ItemsSource ?? Array.Empty<object>(), Value, ValuePath);
        if (picked is not null)
        {
            Value = PickerDialog.ValueOf(picked, ValuePath);
        }
    }
}
