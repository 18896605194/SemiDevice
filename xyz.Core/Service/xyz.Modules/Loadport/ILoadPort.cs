using xyz.Drivers.Loadport;

namespace xyz.Modules;

/// <summary>
/// LoadPort 模块对外公开的统一操作契约。
/// 动作为下发即返回（返回操作实例，null=被拒）；结果两条路：
/// 手动/同步调用方用 WaitReply 等终态后读 IsSuccess；事件驱动调用方看模块状态（经事件通道回馈）。
/// EAP 也只依赖本接口（对应 CTC 的 ILoadPort）：读载具状态、下发动作；载具节点经 <see cref="EapCallback"/> 回调给 EAP。
/// </summary>
public interface ILoadPort
{
    #region 状态

    /// <summary>
    /// 模块名（如 LoadPort1），EAP 据此区分端口。
    /// </summary>
    string Name { get; }

    int State { get; }

    /// <summary>
    /// FOUP 是否在位。
    /// </summary>
    bool IsPodPlaced { get; }

    /// <summary>
    /// 自动/手动模式（true=自动）。
    /// </summary>
    bool IsAutoMode { get; }

    /// <summary>
    /// 当前载具 ID：读卡成功或 Host 改写后有值；未读或载具已移走为 null。
    /// </summary>
    string? CarrierId { get; }

    /// <summary>
    /// 最近一次 Mapping 结果，下标 0 对应第 1 槽；未 Mapping 或载具已移走为空列表。
    /// </summary>
    IReadOnlyList<SlotState> SlotMap { get; }

    #endregion

    #region 动作

    ModuleOperation? Load();

    ModuleOperation? Unload();

    ModuleOperation? Home();

    ModuleOperation? Reset();

    ModuleOperation? Abort();

    /// <summary>
    /// 夹紧 FOUP（仅空闲时允许）。
    /// </summary>
    ModuleOperation? Clamp();

    /// <summary>
    /// 松开 FOUP（仅空闲时允许，已装载/门开时拒绝）。
    /// </summary>
    ModuleOperation? Unclamp();

    /// <summary>
    /// 设置自动/手动模式（内部模式位，不经设备协议）。
    /// </summary>
    void SetAutoMode(bool autoMode);

    /// <summary>
    /// 读取载具 ID（同步）；未挂读头返回 null。
    /// </summary>
    string? ReadCarrierId();

    /// <summary>
    /// 改写载具 ID：Host 确认的 ID 与读到的不一致时以 Host 为准（对应 CTC 的 ProceedSetCarrierID）。
    /// </summary>
    void SetCarrierId(string carrierId);

    #endregion

    #region EAP 口子

    /// <summary>
    /// EAP 回调；null 表示未接 EAP，模块照常运行。
    /// </summary>
    ILoadPortEapCallback? EapCallback { get; set; }

    #endregion
}
