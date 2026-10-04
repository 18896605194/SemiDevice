using System.Globalization;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;
using xyz.Shared.Dtos;

namespace xyz.Client.Recipe.Models;

/// <summary>
/// 工艺步骤的一行：时间、转速，然后摆臂 → 这条摆臂上的药液 → 流量 → 方式（Time / Scan）→ 位置。
/// 摆臂选"不出液"（空）时后面几格不用填，摆臂在 Home。数字格绑的是输入框提交后的文字（InputTextBox 的 Value），
/// 输入框自己查格式和范围、把有没有错报回来（HasXxxError）；改了任何一格都告诉页面（标记"有没保存的修改"、重新检查）。
/// </summary>
public sealed class ProcessRecipeStepModel : ObservableObject
{
    /// <summary>
    /// 数字写成文字时的样子：最多三位小数，不带多余的 0。
    /// </summary>
    private const string NumberFormat = "0.###";

    private readonly IReadOnlyList<ProcessArmDto> _arms;
    private readonly Action<ProcessRecipeStepModel> _changed;

    public ProcessRecipeStepModel(ProcessRecipeStepDto dto, ProcessRecipeLimitsModel limits, IReadOnlyList<ProcessArmDto> arms,
        Action<ProcessRecipeStepModel> changed)
    {
        Limits = limits;
        _arms = arms;
        _changed = changed;
        _secondsText = Format(dto.Seconds);
        _rpmText = dto.Rpm.ToString(CultureInfo.InvariantCulture);
        _arm = dto.Arm ?? string.Empty;
        _chemical = dto.Chemical ?? string.Empty;
        _mode = dto.Mode;

        bool dispensing = _arm.Length > 0;
        bool scan = dispensing && dto.Mode == ProcessArmMode.Scan;
        _flowText = dispensing ? Format(dto.Flow) : string.Empty;

        // 没出液的先把位置放在晶圆中心、Scan 的另一头放在晶圆边缘（中心扫到边缘最常见）：选了摆臂马上有个起点，人再改
        _positionText = Format(dispensing ? dto.Position : limits.MaxPosition);
        _scanToText = Format(scan ? dto.ScanTo : limits.MinPosition);
        _scanSpeedText = scan ? Format(dto.ScanSpeed) : string.Empty;
    }

    /// <summary>
    /// 各项范围：输入框的上下限绑这里。
    /// </summary>
    public ProcessRecipeLimitsModel Limits { get; }

    private int _number;

    /// <summary>
    /// 步号（从 1 起，跟后端提示里的步号一样）。
    /// </summary>
    public int Number
    {
        get => _number;
        set => SetProperty(ref _number, value);
    }

    private string _secondsText;

    public string SecondsText
    {
        get => _secondsText;
        set => SetAndReport(ref _secondsText, value ?? string.Empty);
    }

    private bool _hasSecondsError;

    public bool HasSecondsError
    {
        get => _hasSecondsError;
        set => SetAndReport(ref _hasSecondsError, value);
    }

    private string _rpmText;

    public string RpmText
    {
        get => _rpmText;
        set => SetAndReport(ref _rpmText, value ?? string.Empty);
    }

    private bool _hasRpmError;

    public bool HasRpmError
    {
        get => _hasRpmError;
        set => SetAndReport(ref _hasRpmError, value);
    }

    private string _arm;

    /// <summary>
    /// 摆臂名（sc.xml 原样）；空 = 不出液。换了摆臂，药液选项跟着换，原来的药液不在新摆臂上就清掉。
    /// 下拉框换数据源时会先推一个 null 过来，不当真。
    /// </summary>
    public string? Arm
    {
        get => _arm;
        set
        {
            if (value is null || value == _arm)
            {
                return;
            }

            _arm = value;
            if (!ChemicalOptions.Contains(_chemical, StringComparer.OrdinalIgnoreCase))
            {
                _chemical = string.Empty;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(ChemicalOptions));
            OnPropertyChanged(nameof(Chemical));
            NotifyShape();
            _changed(this);
        }
    }

    /// <summary>
    /// 这条摆臂上的药液（下拉框的选项）；不出液或摆臂没了为空。
    /// </summary>
    public IReadOnlyList<string> ChemicalOptions =>
        _arms.FirstOrDefault(arm => string.Equals(arm.Name, _arm, StringComparison.OrdinalIgnoreCase))?.Chemicals ?? [];

    private string _chemical;

    /// <summary>
    /// 药液（这条摆臂上喷嘴的 Chemical，原样）。下拉框换数据源时推过来的 null 不当真。
    /// </summary>
    public string? Chemical
    {
        get => _chemical;
        set
        {
            if (value is null || value == _chemical)
            {
                return;
            }

            _chemical = value;
            OnPropertyChanged();
            NotifyShape();
            _changed(this);
        }
    }

    private string _flowText;

    public string FlowText
    {
        get => _flowText;
        set => SetAndReport(ref _flowText, value ?? string.Empty);
    }

    private bool _hasFlowError;

    public bool HasFlowError
    {
        get => _hasFlowError;
        set => SetAndReport(ref _hasFlowError, value);
    }

    private ProcessArmMode _mode;

    public ProcessArmMode Mode
    {
        get => _mode;
        set
        {
            if (SetProperty(ref _mode, value))
            {
                NotifyShape();
                _changed(this);
            }
        }
    }

    private string _positionText;

    /// <summary>
    /// 位置（晶圆坐标）：Time 停在这里喷，Scan 从这里扫到另一头。
    /// </summary>
    public string PositionText
    {
        get => _positionText;
        set => SetAndReport(ref _positionText, value ?? string.Empty);
    }

    private bool _hasPositionError;

    public bool HasPositionError
    {
        get => _hasPositionError;
        set => SetAndReport(ref _hasPositionError, value);
    }

    private string _scanToText;

    public string ScanToText
    {
        get => _scanToText;
        set => SetAndReport(ref _scanToText, value ?? string.Empty);
    }

    private bool _hasScanToError;

    public bool HasScanToError
    {
        get => _hasScanToError;
        set => SetAndReport(ref _hasScanToError, value);
    }

    private string _scanSpeedText;

    public string ScanSpeedText
    {
        get => _scanSpeedText;
        set => SetAndReport(ref _scanSpeedText, value ?? string.Empty);
    }

    private bool _hasScanSpeedError;

    public bool HasScanSpeedError
    {
        get => _hasScanSpeedError;
        set => SetAndReport(ref _hasScanSpeedError, value);
    }

    /// <summary>
    /// 这一步出液（选了摆臂）：药液、方式、位置那几格才显示。
    /// </summary>
    public bool IsDispensing => _arm.Length > 0;

    /// <summary>
    /// 这一步不出液：药液、方式那几格显示"—"，位置显示 Home。
    /// </summary>
    public bool IsIdle => !IsDispensing;

    /// <summary>
    /// 选了药液：流量那一格才显示。
    /// </summary>
    public bool HasChemical => IsDispensing && _chemical.Length > 0;

    public bool NoChemical => !HasChemical;

    /// <summary>
    /// Scan：位置那一格多出"到 … 速度 … mm/s"。
    /// </summary>
    public bool IsScan => IsDispensing && _mode == ProcessArmMode.Scan;

    private bool _hasChemicalError;

    /// <summary>
    /// 选了摆臂没选药液（或药液不在这条摆臂上）：药液下拉框标红。由页面检查后填。
    /// </summary>
    public bool HasChemicalError
    {
        get => _hasChemicalError;
        set => SetProperty(ref _hasChemicalError, value);
    }

    private bool _hasError;

    /// <summary>
    /// 这一行有问题：整行淡红底。由页面检查后填。
    /// </summary>
    public bool HasError
    {
        get => _hasError;
        set => SetProperty(ref _hasError, value);
    }

    /// <summary>
    /// 数字格的文字按不变区域性读（输入框提交的就是这个写法）；空的、不是数的读不出来。
    /// </summary>
    public static bool TryNumber(string text, out double value)
    {
        return double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
    }

    /// <summary>
    /// 转成保存请求里的一步（检查通过之后调）：不出液的只带时间、转速；Time 不带 Scan 的另一头和速度。
    /// </summary>
    public ProcessRecipeStepDto ToDto()
    {
        var dto = new ProcessRecipeStepDto
        {
            Seconds = NumberOf(_secondsText),
            Rpm = int.TryParse(_rpmText.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int rpm) ? rpm : 0,
        };
        if (!IsDispensing)
        {
            return dto;
        }

        dto.Arm = _arm;
        dto.Chemical = _chemical;
        dto.Flow = NumberOf(_flowText);
        dto.Mode = _mode;
        dto.Position = NumberOf(_positionText);
        if (IsScan)
        {
            dto.ScanTo = NumberOf(_scanToText);
            dto.ScanSpeed = NumberOf(_scanSpeedText);
        }

        return dto;
    }

    /// <summary>
    /// 改了才通知界面、再告诉页面；属性名透传下去（不然 SetProperty 拿到的是这个方法的名字）。
    /// </summary>
    private void SetAndReport<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (SetProperty(ref field, value, propertyName))
        {
            _changed(this);
        }
    }

    private void NotifyShape()
    {
        OnPropertyChanged(nameof(IsDispensing));
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(HasChemical));
        OnPropertyChanged(nameof(NoChemical));
        OnPropertyChanged(nameof(IsScan));
    }

    private static double NumberOf(string text)
    {
        return TryNumber(text, out double value) ? value : 0;
    }

    private static string Format(double value)
    {
        return value.ToString(NumberFormat, CultureInfo.InvariantCulture);
    }
}
