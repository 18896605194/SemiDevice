using xyz.Drivers.Loadport;

namespace xyz.Modules;

/// <summary>
/// E87 载具管理的设备侧上报口：端口上发生的物理事实由模块调这里，EAP 侧据此推进 E87 状态机并上报 Host。
/// 实现由 EAP 侧提供并挂到 ILoadPort.E87Callback；未接 EAP 时为 null，模块照常运行。
/// 全部回调在模块的 EAP 派发线程上按发生顺序串行调用（不占扫描线程），实现里可以慢，但不要死等。
/// </summary>
public interface IE87Callback
{
    void CarrierArrived(ILoadPort port);

    void CarrierRemoved(ILoadPort port, string? carrierId);

    void CarrierIdRead(ILoadPort port, string carrierId);

    void CarrierIdReadFailed(ILoadPort port);

    void SlotMapRead(ILoadPort port, IReadOnlyList<SlotState> slotMap);

    void LoadCompleted(ILoadPort port);

    void UnloadCompleted(ILoadPort port);

    void Homed(ILoadPort port);

    void ClampCompleted(ILoadPort port);

    void UnclampCompleted(ILoadPort port);

    void AutoModeChanged(ILoadPort port, bool autoMode);

    /// <summary>可以开始取放这个载具了（Load 完成后）。</summary>
    void AccessStarted(ILoadPort port);

    /// <summary>不再取放这个载具（Unload 完成或中断）。</summary>
    void AccessStopped(ILoadPort port);

    /// <summary>这个载具的活干完了，由上层作业判定后经 ILoadPort.NoteCarrierComplete 触发。</summary>
    void CarrierComplete(ILoadPort port);

    /// <summary>端口出错：动作失败或设备报错，error 为"错误码#内容"。</summary>
    void PortError(ILoadPort port, string error);
}
