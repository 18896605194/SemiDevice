using System.Globalization;
using xyz.Elements.Elements;
using xyz.Elements.Interfaces;
using xyz.Elements.Models;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;

namespace xyz.Modules;

/// <summary>
/// 腔体按工艺配方做一次工艺（平台默认做法，照 machine.xml 里挂的设备走），建的时候按配方快照把步骤排好：
/// 先把喷嘴全关、Lift 升（摆臂要越过 Bowl 壁）、Bowl 升（围住盘面挡液）；然后配方一步一步做——
/// 先关掉这一步不用的喷嘴（卡盘加减速时不让上一步的药液接着喷），转速给卡盘；
/// 选了摆臂的，别的摆臂回 Home、这条摆到"位置"，开它上面药液对得上的喷嘴（有流量设定 AO 的先给流量）；
/// Time 停在位置上喷，Scan 在"位置"和"到"之间按"速度"来回扫；这一步时间到就进下一步，扫到一半到点了当场停轴。
/// 没选摆臂的一步不喷，摆臂都回 Home。做完喷嘴全关、摆臂回 Home、卡盘停转、Bowl 降。
/// 这一步跟上一步用同一个喷嘴就接着喷，不关了再开；转速跟上一步一样不重发。
/// 字段表里没配、这一步没填的字段就跳过（没填转速就不动卡盘，没填位置摆臂就不动）。位置是晶圆坐标，按摆臂的示教位换成轴位置；
/// 速度就是轴的设计速度，原样给轴，不按示教位换算。整个动作的上限是配方头的超时时间
/// </summary>
public sealed class ChamberProcessRoutine : ModuleRoutine
{
    // 配方字段名（machine.xml ProcessRecipe → Fields 下的节点名）：执行认这几个名字。时间是 RecipeField.SecondsKey。
    private const string RpmKey = "Rpm";
    private const string ArmKey = "Arm";
    private const string ChemicalKey = "Chemical";
    private const string FlowKey = "Flow";
    private const string ModeKey = "Mode";
    private const string PositionKey = "Position";
    private const string ScanToKey = "ScanTo";
    private const string ScanSpeedKey = "ScanSpeed";

    /// <summary>
    /// 方式字段里"来回扫"的写法（另一个是 Time）
    /// </summary>
    private const string ScanMode = "Scan";

    /// <summary>
    /// 摆臂的 Home 位：回零后轴在 0 位
    /// </summary>
    private const double HomePosition = 0;

    /// <summary>
    /// 进度满格
    /// </summary>
    private const int FullPercent = 100;

    /// <summary>
    /// 步骤都做完、还在收尾时的进度：动作真做完才算满（做完以后站点的进度就变空了）
    /// </summary>
    private const int FinishingPercent = 99;

    /// <summary>
    /// 准备段的步骤在 _owners 里记成它
    /// </summary>
    private const int PrepareOwner = -1;

    private readonly BaseChamberModule _chamber;
    private readonly string _recipeName;

    /// <summary>
    /// 每一步要喷几秒（按配方快照算好）
    /// </summary>
    private readonly double[] _stepSeconds;

    /// <summary>
    /// 所有步骤加起来一共几秒
    /// </summary>
    private readonly double _totalSeconds;

    /// <summary>
    /// 动作的每一步属于配方第几步（从 0 数）：准备段 -1，收尾段 = 配方步数；算进度用
    /// </summary>
    private readonly List<int> _owners = [];

    /// <summary>
    /// 配方每一步的"喷"；那一步认不出设备为 null
    /// </summary>
    private readonly ChamberDwellStep?[] _dwells;

    /// <summary>
    /// 排步骤时记着上一次给卡盘的转速（一样就不重发）；还没给过为 null
    /// </summary>
    private double? _lastRpm;

    /// <summary>
    /// 按配方快照排好步骤；配方选的摆臂、药液这个腔体没有（起工艺前已经查过，正常不会）就在那一步失败
    /// </summary>
    public ChamberProcessRoutine(BaseChamberModule chamber, string recipeName, RecipeData recipe)
        : base(chamber, $"Process {recipeName.Trim()}")
    {
        _chamber = chamber;
        _recipeName = recipeName.Trim();
        _stepSeconds = [.. recipe.Steps.Select(RecipeData.SecondsOf)];
        _totalSeconds = _stepSeconds.Sum();
        _dwells = new ChamberDwellStep?[recipe.Steps.Count];
        int timeout = recipe.TimeoutMilliseconds;
        LimitTo(() => timeout, ErrorCodes.ChamberActionTimeout);

        AddPrepareSteps();
        for (int index = 0; index < recipe.Steps.Count; index++)
        {
            AddRecipeStep(recipe.Steps[index], index);
        }

        AddFinishSteps(recipe.Steps.Count);
    }

    /// <summary>
    /// 工艺进度 0~100：按配方每一步的时间算，准备段 0、收尾段 99；做完的步骤时间加上这一步已经喷了多久，除以总时间。
    /// 每一步都没填时间就按做完几步算
    /// </summary>
    public int Percent
    {
        get
        {
            int number = StepNumber;
            if (number == 0)
            {
                return 0;
            }

            int owner = _owners[number - 1];
            if (owner < 0)
            {
                return 0;
            }

            if (owner >= _stepSeconds.Length)
            {
                return FinishingPercent;
            }

            if (_totalSeconds <= 0)
            {
                return Math.Min(FinishingPercent, owner * FullPercent / _stepSeconds.Length);
            }

            double done = 0;
            for (int step = 0; step < owner; step++)
            {
                done += _stepSeconds[step];
            }

            var dwell = _dwells[owner];
            if (dwell is not null)
            {
                done += dwell.DwelledSeconds;
            }

            return Math.Min(FinishingPercent, (int)(done * FullPercent / _totalSeconds));
        }
    }

    /// <summary>
    /// 这一步选的摆臂、药液照名字在腔体里找：没选摆臂 arm 为 null（这一步不喷）；选了的找不到，回是哪个字段（字段名）、什么值。
    /// 建工艺时按它认设备；起工艺前、流程配方保存、Job 挑腔体也按它把每一步先查一遍（<see cref="BaseChamberModule.FindRecipeMismatch"/>）
    /// </summary>
    public static (string Key, string Value)? FindDevices(BaseChamberModule chamber, RecipeStep step, out ISwingArm? arm, out INozzle? nozzle)
    {
        arm = null;
        nozzle = null;
        string armName = step.Get(ArmKey).Trim();
        if (armName.Length == 0)
        {
            return null;
        }

        arm = chamber.FindArm(armName);
        if (arm is null)
        {
            return (ArmKey, armName);
        }

        string chemical = step.Get(ChemicalKey).Trim();
        if (chemical.Length == 0)
        {
            return null;
        }

        nozzle = arm.FindNozzle(chemical);
        if (nozzle is null)
        {
            return (ChemicalKey, chemical);
        }

        return null;
    }

    /// <summary>
    /// 准备：喷嘴全关、Lift 升同时做，再 Bowl 升
    /// </summary>
    private void AddPrepareSteps()
    {
        var orders = new List<DeviceOrder>();
        foreach (var nozzle in _chamber.Nozzles)
        {
            orders.Add(new DeviceOrder(nozzle, ChamberDeviceAction.ValveOff, nozzle.Stop));
        }

        foreach (var lift in _chamber.Lifts)
        {
            orders.Add(new DeviceOrder(lift, ChamberDeviceAction.Up, lift.Up));
        }

        Devices("关喷嘴、Lift 升", orders);
        Devices("Bowl 升", _chamber.Bowls, ChamberDeviceAction.Up);
        Mark(PrepareOwner);
    }

    /// <summary>
    /// 配方一步：关别的喷嘴 → 卡盘转速 → 摆臂就位 → 开喷嘴 → 喷
    /// </summary>
    private void AddRecipeStep(RecipeStep step, int index)
    {
        string prefix = $"配方第 {(index + 1).ToString(CultureInfo.InvariantCulture)} 步";
        var missing = FindDevices(_chamber, step, out var arm, out var nozzle);
        if (missing is not null)
        {
            var found = missing.Value;
            Do($"{prefix} 认设备", () => HandleResult.Fail(ErrorCodes.ChamberRecipeOptionMissing, _chamber.Name, _recipeName, found.Key, found.Value));
            Mark(index);
            return;
        }

        double seconds = RecipeData.SecondsOf(step);
        double? rpm = Number(step, RpmKey);
        double? flow = Number(step, FlowKey);
        double? position = Number(step, PositionKey);
        double? scanTo = Number(step, ScanToKey);
        double? scanSpeed = Number(step, ScanSpeedKey);

        // 换步先关掉这一步不用的喷嘴，再改转速、摆臂：不然卡盘加减速那段上一步的药液还在喷
        var others = new List<DeviceOrder>();
        foreach (var other in _chamber.Nozzles)
        {
            if (other != nozzle)
            {
                others.Add(new DeviceOrder(other, ChamberDeviceAction.ValveOff, other.Stop));
            }
        }

        Devices($"{prefix} 关别的喷嘴", others);
        AddSpinStep(prefix, rpm);
        Devices($"{prefix} 摆臂就位", ArmOrders(arm, nozzle, position));
        AddNozzleStep(prefix, nozzle, flow);

        // 来回扫要有两头；没填"到"就当 Time 停在位置上喷
        bool isScan = arm is not null
            && position is not null
            && scanTo is not null
            && string.Equals(step.Get(ModeKey).Trim(), ScanMode, StringComparison.OrdinalIgnoreCase);
        ChamberDwellStep dwell;
        if (isScan)
        {
            dwell = new ChamberDwellStep($"{prefix} 来回扫 {seconds.ToString(CultureInfo.InvariantCulture)} s", seconds, arm, nozzle,
                position, scanTo, scanSpeed);
        }
        else
        {
            dwell = new ChamberDwellStep($"{prefix} 喷 {seconds.ToString(CultureInfo.InvariantCulture)} s", seconds, null, null, null, null, null);
        }

        Add(dwell);
        _dwells[index] = dwell;
        Mark(index);
    }

    /// <summary>
    /// 转速给卡盘：0 停转，别的按转速转；跟上一步一样不重发；没填转速、没配卡盘不动
    /// </summary>
    private void AddSpinStep(string prefix, double? rpm)
    {
        var spin = _chamber.SpinMotor;
        if (spin is null || rpm is null || rpm == _lastRpm)
        {
            return;
        }

        _lastRpm = rpm;
        double speed = rpm.Value;
        if (speed == 0)
        {
            Devices($"{prefix} 卡盘停转", spin, ChamberDeviceAction.Stop);
            return;
        }

        Devices($"{prefix} 卡盘 {speed.ToString(CultureInfo.InvariantCulture)} rpm",
            [new DeviceOrder(spin, ChamberDeviceAction.Spin, () => spin.Spin(speed))]);
    }

    /// <summary>
    /// 别的摆臂回 Home；这一步的摆臂填了位置就摆过去（选了摆臂才有 arm）
    /// </summary>
    private List<DeviceOrder> ArmOrders(ISwingArm? selected, INozzle? nozzle, double? position)
    {
        var orders = new List<DeviceOrder>();
        foreach (var arm in _chamber.Arms)
        {
            if (arm != selected)
            {
                orders.Add(new DeviceOrder(arm, ChamberDeviceAction.Move, () => arm.MoveTo(HomePosition)));
                continue;
            }

            if (position is not null)
            {
                double target = position.Value;
                orders.Add(new DeviceOrder(arm, ChamberDeviceAction.Move, () => arm.MoveTo(arm.ToAxisPosition(target, nozzle))));
            }
        }

        return orders;
    }

    /// <summary>
    /// 开这一步的喷嘴：填了流量的先给流量（接了 AO 的下发，没接的只给流量计当监控目标），再开阀
    /// </summary>
    private void AddNozzleStep(string prefix, INozzle? nozzle, double? flow)
    {
        if (nozzle is null)
        {
            return;
        }

        var orders = new List<DeviceOrder>();
        if (flow is not null)
        {
            double value = flow.Value;
            orders.Add(new DeviceOrder(nozzle, ChamberDeviceAction.Flow, () => nozzle.SetFlow(value)));
        }

        orders.Add(new DeviceOrder(nozzle, ChamberDeviceAction.ValveOn, nozzle.Open));
        Devices($"{prefix} 开 {nozzle.Chemical}", orders);
    }

    /// <summary>
    /// 收尾：喷嘴全关 → 摆臂都回 Home → 卡盘停转 → Bowl 降
    /// </summary>
    private void AddFinishSteps(int stepCount)
    {
        Devices("关喷嘴", _chamber.Nozzles, ChamberDeviceAction.ValveOff);
        Devices("摆臂回 Home", ArmOrders(null, null, null));
        Devices("卡盘停转", _chamber.SpinMotor, ChamberDeviceAction.Stop);
        Devices("Bowl 降", _chamber.Bowls, ChamberDeviceAction.Down);
        Mark(stepCount);
    }

    /// <summary>
    /// 刚加的这几步属于配方第几步
    /// </summary>
    private void Mark(int owner)
    {
        while (_owners.Count < StepCount)
        {
            _owners.Add(owner);
        }
    }

    /// <summary>
    /// 这一步某个数字字段的值（不变区域性）；没填、不是有限数为 null
    /// </summary>
    private static double? Number(RecipeStep step, string key)
    {
        if (double.TryParse(step.Get(key).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && double.IsFinite(value))
        {
            return value;
        }

        return null;
    }
}
