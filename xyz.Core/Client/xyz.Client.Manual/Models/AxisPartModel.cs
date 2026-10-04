using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using xyz.Client.Common.Ec;
using xyz.Client.Presentation.Localization;
using xyz.Client.Presentation.Models;
using xyz.Shared.Dtos;

namespace xyz.Client.Manual.Models;

/// <summary>
/// 腔体手动页上的一根轴（一个页签）：位置、速度和五盏灯来自部件推送；目标位置、移动速度、点动速度、步距是页面上的输入。
/// 输入的默认值取这根轴的 EC（MoveSpeed、JogSpeed、JogStep；EC 还没拉到就先空着，拉到了再补），目标位置第一次收到数据时取当前位置；
/// 页面上改了只管这次，不写回 EC。
/// </summary>
public class AxisPartModel : ObservableObject
{
    /// <summary>位置、速度显示几位小数（跟后端推的位数一样）。</summary>
    private const string NumberFormat = "F3";

    private const string MoveSpeedEc = "MoveSpeed";
    private const string JogSpeedEc = "JogSpeed";
    private const string JogStepEc = "JogStep";

    /// <param name="module">腔体模块名，页签上的名字去掉它（"Chamber1.Arm1" → "Arm1"）。</param>
    /// <param name="dto">这根轴的推送。</param>
    public AxisPartModel(string module, PartDto dto)
    {
        Path = dto.Path;
        string prefix = module + ".";
        Name = Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? Path[prefix.Length..] : Path;
        FillDefaults();
        Update(dto);
    }

    /// <summary>组件全路径，如 "Chamber1.Arm1"；动作按它找部件。</summary>
    public string Path { get; }

    /// <summary>页签上的名字：sc 路径去掉腔体名，照 sc 原样显示。</summary>
    public string Name { get; }

    /// <summary>移动速度输入框按它从 EC 取格式、上下限。</summary>
    public string MoveSpeedEcKey => $"{Path}.{MoveSpeedEc}";

    /// <summary>点动速度输入框按它从 EC 取格式、上下限。</summary>
    public string JogSpeedEcKey => $"{Path}.{JogSpeedEc}";

    /// <summary>步距输入框按它从 EC 取格式、上下限。</summary>
    public string JogStepEcKey => $"{Path}.{JogStepEc}";

    private string _positionText = string.Empty;

    /// <summary>当前位置；PLC 没数据显示"—"。</summary>
    public string PositionText
    {
        get => _positionText;
        private set => SetProperty(ref _positionText, value);
    }

    private string _speedText = string.Empty;

    /// <summary>当前速度；PLC 没数据显示"—"。</summary>
    public string SpeedText
    {
        get => _speedText;
        private set => SetProperty(ref _speedText, value);
    }

    private bool _isServoOn;

    /// <summary>伺服就绪（使能）。</summary>
    public bool IsServoOn
    {
        get => _isServoOn;
        private set => SetProperty(ref _isServoOn, value);
    }

    private bool _isHomed;

    /// <summary>已回零。</summary>
    public bool IsHomed
    {
        get => _isHomed;
        private set => SetProperty(ref _isHomed, value);
    }

    private bool _isBusy;

    /// <summary>运动中。</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    private bool _isInPosition;

    /// <summary>已到位。</summary>
    public bool IsInPosition
    {
        get => _isInPosition;
        private set => SetProperty(ref _isInPosition, value);
    }

    private bool _isError;

    /// <summary>故障（驱动器报错）。</summary>
    public bool IsError
    {
        get => _isError;
        private set => SetProperty(ref _isError, value);
    }

    private string _targetPosition = string.Empty;

    /// <summary>目标位置（"移动"走到这儿），InputTextBox 的 Value。</summary>
    public string TargetPosition
    {
        get => _targetPosition;
        set => SetProperty(ref _targetPosition, value);
    }

    private string _moveSpeed = string.Empty;

    /// <summary>移动速度（"移动"用），空着就按后端的 EC MoveSpeed。</summary>
    public string MoveSpeed
    {
        get => _moveSpeed;
        set => SetProperty(ref _moveSpeed, value);
    }

    private string _jogSpeed = string.Empty;

    /// <summary>点动速度（点动、步进都用，取绝对值，方向看按的是 + 还是 -）。</summary>
    public string JogSpeed
    {
        get => _jogSpeed;
        set => SetProperty(ref _jogSpeed, value);
    }

    private string _jogStep = string.Empty;

    /// <summary>步距（步进一下走多远，取绝对值）。</summary>
    public string JogStep
    {
        get => _jogStep;
        set => SetProperty(ref _jogStep, value);
    }

    /// <summary>用推送就地刷新（界面线程调用）；目标位置还空着时取第一次拿到的当前位置。</summary>
    public void Update(PartDto dto)
    {
        bool hasData = dto.GetBool(PartValueNames.HasPlcData);
        double? position = dto.GetDouble(PartValueNames.CurrentPosition);
        double? speed = dto.GetDouble(PartValueNames.CurrentSpeed);
        PositionText = Format(hasData, position);
        SpeedText = Format(hasData, speed);
        IsServoOn = dto.GetBool(PartValueNames.IsServoOn);
        IsHomed = dto.GetBool(PartValueNames.IsHomed);
        IsBusy = dto.GetBool(PartValueNames.IsBusy);
        IsInPosition = dto.GetBool(PartValueNames.IsInPosition);
        IsError = dto.GetBool(PartValueNames.IsError);
        if (hasData && position is not null && string.IsNullOrEmpty(TargetPosition))
        {
            TargetPosition = position.Value.ToString(NumberFormat, CultureInfo.InvariantCulture);
        }
    }

    /// <summary>还空着的速度、步距按 EC 补上（连上后端拉到 EC 时也调一次）；已经填了的不动。</summary>
    public void FillDefaults()
    {
        if (string.IsNullOrEmpty(MoveSpeed))
        {
            MoveSpeed = EcValue(MoveSpeedEcKey);
        }

        if (string.IsNullOrEmpty(JogSpeed))
        {
            JogSpeed = EcValue(JogSpeedEcKey);
        }

        if (string.IsNullOrEmpty(JogStep))
        {
            JogStep = EcValue(JogStepEcKey);
        }
    }

    private static string EcValue(string key)
    {
        return ClientEc.TryGet(key, out var item) ? item.Value ?? string.Empty : string.Empty;
    }

    private static string Format(bool hasData, double? value)
    {
        if (!hasData || value is null)
        {
            return L10n.Get("chambermanual.na");
        }

        return value.Value.ToString(NumberFormat, CultureInfo.InvariantCulture);
    }
}
