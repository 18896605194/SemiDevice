using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using xyz.Client.Common.Ec;
using xyz.Client.Presentation.Localization;
using xyz.Client.Presentation.Models;
using xyz.Shared.Dtos;

namespace xyz.Client.Presentation.Controls;

/// <summary>
/// 通用输入框：框架里的文本、数值输入都用它。界面绑 Value（提交后的合法值），不要绑 Text（正在输入的原文）。
///
/// 校验分两步，免得"范围 2~20 想输 10，敲下 1 就报错"：
/// ① 边输入边查格式——只拦不可能再变合法的输入（整数里敲了字母、小数里两个小数点），"-"、"1." 这种没输完的不算错；
/// ② 回车或离开输入框时提交——查完整格式和范围，合法才写进 Value，不合法红框提示、Value 保留上一次的合法值。
///
/// 类型和范围：界面上写的（DataType / Minimum / Maximum / Unit）优先；没写的按 EcKey 从 EC 取（格式、上下限、单位）；
/// 都没配就是普通输入框，不校验。文本类型没有上下限的概念，只有整数、小数才查范围。
/// </summary>
public class InputTextBox : TextBox
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(string), typeof(InputTextBox),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (d, _) => ((InputTextBox)d).SyncTextFromValue()));

    public static readonly DependencyProperty DataTypeProperty = DependencyProperty.Register(
        nameof(DataType), typeof(InputDataType), typeof(InputTextBox),
        new PropertyMetadata(InputDataType.Text, OnSettingChanged));

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(double?), typeof(InputTextBox), new PropertyMetadata(null, OnSettingChanged));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double?), typeof(InputTextBox), new PropertyMetadata(null, OnSettingChanged));

    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
        nameof(Unit), typeof(string), typeof(InputTextBox), new PropertyMetadata(string.Empty, OnSettingChanged));

    public static readonly DependencyProperty EcKeyProperty = DependencyProperty.Register(
        nameof(EcKey), typeof(string), typeof(InputTextBox), new PropertyMetadata(string.Empty, OnSettingChanged));

    public static readonly DependencyProperty ErrorMessageProperty = DependencyProperty.Register(
        nameof(ErrorMessage), typeof(string), typeof(InputTextBox), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty HasErrorProperty = DependencyProperty.Register(
        nameof(HasError), typeof(bool), typeof(InputTextBox), new PropertyMetadata(false));

    /// <summary>
    /// 校验错误的挂载点：Text 绑到它（Explicit，永不回写），只为让 WPF 校验有个绑定可挂错误——
    /// MaterialDesign 的红框和错误文字按 Validation.HasError 画，挂上去就是框架统一的错误样子。
    /// </summary>
    private static readonly DependencyProperty CarrierProperty = DependencyProperty.Register(
        "Carrier", typeof(string), typeof(InputTextBox), new PropertyMetadata(string.Empty));

    private static readonly Regex IntegerDraft = new(@"^[+-]?\d*$", RegexOptions.Compiled);

    private static readonly Regex DecimalDraft = new(@"^[+-]?\d*\.?\d*$", RegexOptions.Compiled);

    private const NumberStyles DecimalStyle = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;

    private InputDataType _type;
    private double? _min;
    private double? _max;
    private string _unit = string.Empty;

    /// <summary>程序在改 Text（从 Value 同步、提交时规整写法），不算用户输入。</summary>
    private bool _syncing;

    /// <summary>从上次同步或提交以来用户改过没有；没改过就不提交——点进来又点出去不该报"请输入数字"。</summary>
    private bool _dirty;

    public InputTextBox()
    {
        // 没指定样式时用框架默认的输入框样式；XAML 里写了 Style 会覆盖它。
        SetResourceReference(StyleProperty, "DefaultTextBoxStyle");

        SetBinding(TextProperty, new Binding
        {
            Path = new PropertyPath(CarrierProperty),
            RelativeSource = RelativeSource.Self,
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.Explicit,
        });

        // EC 目录可能比界面晚到（连上后端才拉），到了再取一次范围；卸载时退订，免得静态事件拖住控件。
        Loaded += (_, _) =>
        {
            // Loaded 可能连着来两次（中间没有 Unloaded），先退后订保证只订一份。
            ClientEc.Changed -= Resolve;
            ClientEc.Changed += Resolve;
            Resolve();
        };
        Unloaded += (_, _) => ClientEc.Changed -= Resolve;

        Resolve();
    }

    /// <summary>提交后的合法值（双向绑定）。整数、小数按不变区域性写（小数点是点号），绑到数值属性由绑定自动转换。</summary>
    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>数据类型；不写就按 EC 的格式（Int → 整数，Double → 小数），再没有就是文本。</summary>
    public InputDataType DataType
    {
        get => (InputDataType)GetValue(DataTypeProperty);
        set => SetValue(DataTypeProperty, value);
    }

    /// <summary>最小值（含）；不写就用 EC 的下限，再没有就不限。文本类型不生效。</summary>
    public double? Minimum
    {
        get => (double?)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    /// <summary>最大值（含）；不写就用 EC 的上限，再没有就不限。文本类型不生效。</summary>
    public double? Maximum
    {
        get => (double?)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    /// <summary>单位，只用在范围提示里（如"范围 2 ~ 20 L/min"）；不写就用 EC 的单位。</summary>
    public string Unit
    {
        get => (string)GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    /// <summary>
    /// 从哪项 EC 取类型、范围、单位："组件全路径.参数名"，如 LoadPort1.LoadTimeout、Chamber1.Door.ActionTimeoutMs。
    /// 可以绑定（按模块拼出来的键）；EC 里没有这项就当没配。
    /// </summary>
    public string EcKey
    {
        get => (string)GetValue(EcKeyProperty);
        set => SetValue(EcKeyProperty, value);
    }

    /// <summary>自定义错误提示：给了就一律显示它，可用 {0} {1} 占最小、最大值；不给用语言包里的默认提示。</summary>
    public string ErrorMessage
    {
        get => (string)GetValue(ErrorMessageProperty);
        set => SetValue(ErrorMessageProperty, value);
    }

    /// <summary>当前输入有没有错（格式不对，或提交时范围不对）。外面可以 OneWayToSource 绑出去，比如错着的时候不让下发。</summary>
    public bool HasError
    {
        get => (bool)GetValue(HasErrorProperty);
        set => SetValue(HasErrorProperty, value);
    }

    /// <summary>
    /// 按当前文本做完整校验并提交：合法就规整写法、写进 Value，返回 true；不合法红框提示、Value 不动，返回 false。
    /// 回车、离开输入框时自动调；焦点不会离开输入框的场合（比如不抢焦点的按钮）可以手动调。
    /// </summary>
    public bool Commit()
    {
        if (!TryValidate(Text ?? string.Empty, out string normalized, out string error))
        {
            ShowError(error);
            return false;
        }

        _syncing = true;
        try
        {
            if (!string.Equals(Text, normalized, StringComparison.Ordinal))
            {
                SetCurrentValue(TextProperty, normalized);
                CaretIndex = normalized.Length;
            }

            SetCurrentValue(ValueProperty, normalized);
            PushToSource(ValueProperty);
        }
        finally
        {
            _syncing = false;
        }

        _dirty = false;
        ClearError();
        return true;
    }

    protected override void OnTextChanged(TextChangedEventArgs e)
    {
        base.OnTextChanged(e);
        if (_syncing)
        {
            return;
        }

        _dirty = true;
        CheckDraft();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _dirty)
        {
            Commit();
        }

        base.OnKeyDown(e);
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        if (_dirty)
        {
            Commit();
        }
    }

    #region 取类型和范围

    private static void OnSettingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((InputTextBox)d).Resolve();
    }

    /// <summary>
    /// 算出实际生效的类型、上下限、单位：界面上写的优先，没写的按 EcKey 从 EC 取。
    /// 规则真变了（EC 晚到、界面改了限值）才按新规则把框里的内容再查一次；没变（比如切页签重新加载）什么都不动，
    /// 不然刚报出来的范围错会被"只查格式"清掉，框里留着不合法的数却不红。
    /// </summary>
    private void Resolve()
    {
        EcItemDto? ec = null;
        if (!string.IsNullOrWhiteSpace(EcKey) && ClientEc.TryGet(EcKey.Trim(), out var item))
        {
            ec = item;
        }

        var type = IsSetByUi(DataTypeProperty) ? DataType : TypeOf(ec?.Format);
        var min = Minimum ?? ParseLimit(ec?.Min);
        var max = Maximum ?? ParseLimit(ec?.Max);
        var unit = !string.IsNullOrEmpty(Unit) ? Unit : ec?.Unit ?? string.Empty;
        if (type == _type && min == _min && max == _max && unit == _unit)
        {
            return;
        }

        _type = type;
        _min = min;
        _max = max;
        _unit = unit;
        Revalidate();
    }

    /// <summary>
    /// 按新规则再查框里的内容：还在输入（没提交）的只查格式；已提交的（框里就是 Value）按格式 + 范围查，
    /// 不合法只标红，Value 不动——提交与否仍由用户决定。
    /// </summary>
    private void Revalidate()
    {
        if (_dirty)
        {
            CheckDraft();
            return;
        }

        string text = Text ?? string.Empty;
        if (text.Length == 0 || TryValidate(text, out _, out string error))
        {
            ClearError();
        }
        else
        {
            ShowError(error);
        }
    }

    /// <summary>界面上（本地值、绑定、样式）设过这个属性没有；只剩默认值就算没设。</summary>
    private bool IsSetByUi(DependencyProperty property)
    {
        return DependencyPropertyHelper.GetValueSource(this, property).BaseValueSource != BaseValueSource.Default;
    }

    private static InputDataType TypeOf(string? format)
    {
        if (string.Equals(format, "Int", StringComparison.OrdinalIgnoreCase))
        {
            return InputDataType.Integer;
        }

        if (string.Equals(format, "Double", StringComparison.OrdinalIgnoreCase))
        {
            return InputDataType.Decimal;
        }

        return InputDataType.Text;
    }

    private static double? ParseLimit(string? text)
    {
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : null;
    }

    #endregion

    #region 校验

    /// <summary>边输入边查：只拦不可能再变合法的格式，范围等提交时再查。</summary>
    private void CheckDraft()
    {
        if (IsDraftValid(Text ?? string.Empty))
        {
            ClearError();
        }
        else
        {
            ShowError(FormatError());
        }
    }

    private bool IsDraftValid(string text)
    {
        return _type switch
        {
            InputDataType.Integer => IntegerDraft.IsMatch(text.Trim()),
            InputDataType.Decimal => DecimalDraft.IsMatch(text.Trim()),
            _ => true,
        };
    }

    /// <summary>提交时查：完整格式 + 范围；合法时给出规整后的写法（去空格、去多余的零和小数点）。</summary>
    private bool TryValidate(string text, out string normalized, out string error)
    {
        normalized = text;
        error = string.Empty;

        double number;
        switch (_type)
        {
            case InputDataType.Integer:
                if (!long.TryParse(text.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long integer))
                {
                    error = FormatError();
                    return false;
                }

                number = integer;
                normalized = integer.ToString(CultureInfo.InvariantCulture);
                break;

            case InputDataType.Decimal:
                if (!double.TryParse(text.Trim(), DecimalStyle, CultureInfo.InvariantCulture, out number) || !double.IsFinite(number))
                {
                    error = FormatError();
                    return false;
                }

                normalized = number.ToString(CultureInfo.InvariantCulture);
                break;

            default:
                return true;
        }

        if ((_min is double min && number < min) || (_max is double max && number > max))
        {
            error = RangeError();
            return false;
        }

        return true;
    }

    private string FormatError()
    {
        return CustomError() ?? L10n.Get(_type == InputDataType.Integer ? "input.integer_required" : "input.number_required");
    }

    private string RangeError()
    {
        var custom = CustomError();
        if (custom is not null)
        {
            return custom;
        }

        string unit = _unit.Length == 0 ? string.Empty : " " + _unit;
        if (_min is double min && _max is double max)
        {
            return L10n.Get("input.range", FormatNumber(min), FormatNumber(max), unit);
        }

        return _min is double low
            ? L10n.Get("input.min", FormatNumber(low), unit)
            : L10n.Get("input.max", FormatNumber(_max ?? 0), unit);
    }

    /// <summary>界面给了 ErrorMessage 就一律用它，{0} {1} 换成最小、最大值；写错了占位就原样显示。</summary>
    private string? CustomError()
    {
        if (string.IsNullOrEmpty(ErrorMessage))
        {
            return null;
        }

        try
        {
            return string.Format(ErrorMessage,
                _min is double min ? FormatNumber(min) : string.Empty,
                _max is double max ? FormatNumber(max) : string.Empty);
        }
        catch (FormatException)
        {
            return ErrorMessage;
        }
    }

    private static string FormatNumber(double value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    #endregion

    #region 同步与错误显示

    /// <summary>
    /// Value 从外面变了（绑定的属性改了）：同步到输入框。正在输入时不冲掉用户敲的，等他提交。
    /// </summary>
    private void SyncTextFromValue()
    {
        if (_syncing || IsKeyboardFocusWithin)
        {
            return;
        }

        _syncing = true;
        try
        {
            SetCurrentValue(TextProperty, Value ?? string.Empty);
        }
        finally
        {
            _syncing = false;
        }

        _dirty = false;
        ClearError();
    }

    /// <summary>
    /// 把自己改的值推回绑定源。表格（DataGrid）会给每行挂一个 BindingGroup，行里的绑定自动入组，
    /// 回写被扣着等"行编辑提交"——只读表格永远不提交，不推一下 ViewModel 就一直拿不到值。
    /// </summary>
    private void PushToSource(DependencyProperty property)
    {
        GetBindingExpression(property)?.UpdateSource();
    }

    private void ShowError(string message)
    {
        SetCurrentValue(HasErrorProperty, true);
        PushToSource(HasErrorProperty);
        var expression = GetBindingExpression(TextProperty);
        if (expression is not null)
        {
            Validation.ClearInvalid(expression);
            Validation.MarkInvalid(expression, new ValidationError(ManualRule.Instance, expression, message, null));
        }
    }

    private void ClearError()
    {
        SetCurrentValue(HasErrorProperty, false);
        PushToSource(HasErrorProperty);
        var expression = GetBindingExpression(TextProperty);
        if (expression is not null)
        {
            Validation.ClearInvalid(expression);
        }
    }

    /// <summary>挂错误要给一条规则；这条规则本身不校验，错误全由上面手动挂。</summary>
    private sealed class ManualRule : ValidationRule
    {
        public static readonly ManualRule Instance = new();

        public override ValidationResult Validate(object value, CultureInfo cultureInfo)
        {
            return ValidationResult.ValidResult;
        }
    }

    #endregion
}
