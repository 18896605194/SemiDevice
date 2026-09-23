using System.Diagnostics.CodeAnalysis;
using xyz.Common.Log;
using xyz.Components;
using xyz.Components.Attributes;
using xyz.Components.Enums;

namespace xyz.Modules;

/// <summary>
/// 搬运管理：全系统唯一一个"把片从 A 搬到 B"的执行口。
/// 手动传片和自动调度走的是同一条路——都是往这儿下一张搬运单，区别只在单子是人下的还是系统生成的；
/// 两者都完整走站点交互环（准备→取放→收尾），门、夹紧、晶圆账一样不落。
/// 设备点动（直接调 robot.Pick / loadPort.Load）不走这儿，那是维修手段，只在模块 Offline 时允许。
/// </summary>
[Component(description: "搬运管理：执行搬运单，驱动站点交互环与机械手取放")]
public class TransferManager : ComponentBase
{
    /// <summary>
    /// 当前搬运管理；sc.xml 里装出来即生效。冒烟与测试可以直接换成自己的实例。
    /// </summary>
    public static TransferManager? Current { get; set; }

    public TransferManager()
    {
        Current = this;
    }

    #region SC

    [SCEditor("True", "Transfer", "是否启用搬运（False=不执行任何搬运单，设备照常点动）")]
    public bool IsEnable { get; set; } = true;

    #endregion

    #region SV

    /// <summary>
    /// 自动派单是否开启（SV）：只管"要不要自己生成搬运单"，不管"要不要执行搬运单"。
    /// 关掉之后手动下的单照跑——执行搬运单是本组件的本职，自动派单只是其中一路输入。
    /// </summary>
    [VariableMark(VariableType.SV, ValueFormat.Bool, description: "自动派单是否开启")]
    public bool IsAutoDispatch { get; private set; }

    /// <summary>
    /// 开自动派单：系统开始按工艺自己生成搬运单。
    /// </summary>
    public void StartAutoDispatch()
    {
        SetAutoDispatch(true);
    }

    /// <summary>
    /// 关自动派单：不再生成新单；已经在跑的单跑完自己那一趟。
    /// </summary>
    public void StopAutoDispatch()
    {
        SetAutoDispatch(false);
    }

    private void SetAutoDispatch(bool enabled)
    {
        if (IsAutoDispatch == enabled)
        {
            return;
        }

        IsAutoDispatch = enabled;
        LogHelper.Info(Name, enabled ? "自动派单已开启" : "自动派单已关闭");
    }

    #endregion

    #region 模块表（装配后灌）

    private IReadOnlyDictionary<string, BaseModule> _modules =
        new Dictionary<string, BaseModule>(StringComparer.OrdinalIgnoreCase);

    private IReadOnlyList<IRobot> _robots = [];

    public IReadOnlyList<IRobot> Robots => _robots;

    /// <summary>
    /// 绑定模块表（装配完、模块起扫描之后调一次）。
    /// 搬运单里的站点名就是模块名，靠这张表把名字解析成站点和机械手。
    /// </summary>
    public void Bind(IEnumerable<BaseModule> modules)
    {
        var table = new Dictionary<string, BaseModule>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in modules)
        {
            table[module.Name] = module;
        }

        _modules = table;
        _robots = table.Values.OfType<IRobot>().ToList();

        LogHelper.Info(Name,
            $"搬运模块表 {table.Count} 个，机械手 {_robots.Count} 台：{string.Join(", ", _robots.Select(r => r.Name))}");
    }

    /// <summary>
    /// 按模块名取可服务工位；名字不在表里、或那个模块不是可服务工位，返回 false。
    /// </summary>
    public bool TryGetStation(string name, [MaybeNullWhen(false)] out ITransferStation station)
    {
        station = _modules.TryGetValue(name, out var module) ? module as ITransferStation : null;
        return station is not null;
    }

    /// <summary>
    /// 按模块名取机械手；名字不在表里、或那个模块不是机械手，返回 false。
    /// </summary>
    public bool TryGetRobot(string name, [MaybeNullWhen(false)] out IRobot robot)
    {
        robot = _modules.TryGetValue(name, out var module) ? module as IRobot : null;
        return robot is not null;
    }

    #endregion

    #region EC 在线参数（扫描）

    [VariableMark(VariableType.EC, ValueFormat.Int, unit: "ms", min: "10", max: "5000",
        @default: "200", description: "单周期慢扫描警告阈值")]
    public int SlowScanWarnMs
    {
        get { return GetEcInt(nameof(SlowScanWarnMs)); }
        set { SetEcInt(nameof(SlowScanWarnMs), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, unit: "ms", min: "10", max: "5000",
        @default: "300", description: "单周期慢扫描报警阈值")]
    public int SlowScanAlarmMs
    {
        get { return GetEcInt(nameof(SlowScanAlarmMs)); }
        set { SetEcInt(nameof(SlowScanAlarmMs), value); }
    }

    protected override int SlowScanWarnMilliseconds => SlowScanWarnMs;

    protected override int SlowScanAlarmMilliseconds => SlowScanAlarmMs;

    #endregion

    #region 扫描

    /// <summary>
    /// 扫描周期：推进在跑的搬运单，再看要不要派新单。
    /// 本组件有自己的扫描线程，在所有模块起来之后由装配显式 Start。
    /// </summary>
    protected override void OnScan()
    {
        base.OnScan();

        if (!IsEnable)
        {
            return;
        }

        // 待接：推进在跑的搬运单（手动、自动下的单都在这儿跑）。
        // 待接：IsAutoDispatch 开着时按工艺生成新单。
    }

    #endregion

    #region 中止

    /// <summary>
    /// 中止：先关自动派单，急停之后不能再派出新的一趟。
    /// 在跑的单怎么收尾，等搬运单接进来再处理——设备侧的急停由各模块自己的 Abort 管。
    /// </summary>
    public override object? Abort()
    {
        base.Abort();
        StopAutoDispatch();
        return null;
    }

    #endregion
}
