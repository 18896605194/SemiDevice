using xyz.Drivers.Loadport;

namespace xyz.Modules;

/// <summary>
/// LoadPort → EAP 的载具节点回调（对应 CTC 的 IE87CallBack）。
/// 模块在物理节点完成后回调，EAP 实现据此推进 E87 等状态并上报 Host；需要设备动作时反调 <see cref="ILoadPort"/>。
/// 回调在模块扫描线程上按发生顺序串行触发，且不持有模块锁：实现方要及时返回（耗时处理自行排队），
/// 可以在回调里直接调用 ILoadPort 的动作（被拒返回 null）。
/// </summary>
public interface ILoadPortEapCallback
{
    /// <summary>
    /// FOUP 放上（在位由无到有）。
    /// </summary>
    void CarrierArrived(ILoadPort port);

    /// <summary>
    /// FOUP 移走（在位由有到无）；carrierId 为移走前的载具 ID，未读过为 null。
    /// </summary>
    void CarrierRemoved(ILoadPort port, string? carrierId);

    /// <summary>
    /// 载具 ID 读取成功。
    /// </summary>
    void CarrierIdRead(ILoadPort port, string carrierId);

    /// <summary>
    /// 载具 ID 读取失败（读头返回空或抛异常）。
    /// </summary>
    void CarrierIdReadFailed(ILoadPort port);

    /// <summary>
    /// Mapping 结果可用，下标 0 对应第 1 槽。
    /// </summary>
    void SlotMapRead(ILoadPort port, IReadOnlyList<SlotState> slotMap);

    /// <summary>
    /// Load 成功完成（已开门，可取放片）。
    /// </summary>
    void LoadCompleted(ILoadPort port);

    /// <summary>
    /// Unload 成功完成（已关门，可搬走）。
    /// </summary>
    void UnloadCompleted(ILoadPort port);

    /// <summary>
    /// Home 成功完成。
    /// </summary>
    void Homed(ILoadPort port);

    /// <summary>
    /// Clamp 成功完成（FOUP 已夹紧）。
    /// </summary>
    void ClampCompleted(ILoadPort port);

    /// <summary>
    /// Unclamp 成功完成（FOUP 已松开）。
    /// </summary>
    void UnclampCompleted(ILoadPort port);

    /// <summary>
    /// 自动/手动模式切换（true=自动）。
    /// </summary>
    void AutoModeChanged(ILoadPort port, bool autoMode);
}
