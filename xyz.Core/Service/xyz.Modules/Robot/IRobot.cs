using System.Diagnostics.CodeAnalysis;

namespace xyz.Modules;

/// <summary>
/// Robot 模块对外公开的统一操作契约（与 ILoadPort 同一写法）。
/// 动作为下发即返回（返回操作实例，null=被拒）；结果两条路：
/// 手动/同步调用方用 WaitReply 等终态后读 IsSuccess；事件驱动调用方看模块状态。
/// </summary>
public interface IRobot
{
    #region 状态

    /// <summary>
    /// 模块名（如 Robot1）。
    /// </summary>
    string Name { get; }

    int State { get; }

    /// <summary>
    /// 伺服是否上使能；尚未查到为 null。
    /// </summary>
    bool? IsServoOn { get; }

    /// <summary>
    /// 设备当前报错（错误码#内容）；无报错为 null。
    /// </summary>
    string? DeviceError { get; }

    /// <summary>
    /// 手指上是否有片（设备推送）；尚未收到该手指的推送为 null。
    /// </summary>
    bool? HasWafer(int arm);

    #endregion

    #region 站点

    /// <summary>
    /// 本机械手的站点表：模块名（如 LoadPort1）→ 站点配置（站点号 Number、伸出距离 Y、伸出方向 Direction），来自 sc.xml 本 Robot 节点下的 Stations。
    /// </summary>
    IReadOnlyDictionary<string, RobotStation> Stations { get; }

    /// <summary>
    /// 按模块名查站点配置（忽略大小写）；未配置返回 false。
    /// </summary>
    bool TryGetStation(string station, [MaybeNullWhen(false)] out RobotStation config);

    #endregion

    #region 动作

    ModuleOperation? Home();

    /// <summary>
    /// 初始化：子组件先初始化，再 Home。
    /// </summary>
    ModuleOperation? Init();

    /// <summary>
    /// 清除设备报错；报错状态下清错后回 NotInit，需重新 Home。
    /// </summary>
    ModuleOperation? Reset();

    /// <summary>
    /// 急停，可顶替在途动作；打断后回 NotInit，需重新 Home。
    /// </summary>
    ModuleOperation? Abort();

    /// <summary>
    /// 用指定手指从站点（模块名，如 LoadPort1）的槽位取片（仅空闲时允许）；站点号取本机械手站点表，未配置的站点被拒。
    /// </summary>
    ModuleOperation? Pick(int arm, string station, int slot);

    /// <summary>
    /// 用指定手指向站点（模块名）的槽位放片（仅空闲时允许）；站点号取本机械手站点表，未配置的站点被拒。
    /// </summary>
    ModuleOperation? Place(int arm, string station, int slot);

    ModuleOperation? PowerOn();

    ModuleOperation? PowerOff();

    #endregion
}
