using xyz.Components.Components;
using xyz.Shared.Dtos;

namespace xyz.Modules;


public interface ITransferStation
{
    /// <summary>
    /// 工位模块名（如 LoadPort1）。机械手按这个名字查自己的站点表。
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 槽位数（sc.xml 本模块的 SlotCount：LoadPort 花篮 25、腔体 1）；机械手手动取放按它限定可选槽位。
    /// </summary>
    int SlotCount { get; }

    /// <summary>
    /// 当前状态是否允许发起准备；调度器选工位时先过滤，避免盲目发起被拒。
    /// </summary>
    bool CanPrepare { get; }

    /// <summary>
    /// 准备阶段一：粗准备（腔体排气/预热/机构到位等）；无需准备的工位为立即成功的空操作。
    /// 返回操作实例；null 表示状态不允许/被拒。
    /// </summary>
    ModuleOperation? PrepareTransfer();

    /// <summary>
    /// 准备阶段二：最终准备（开门/放行），成功后工位进入 TransferReady，机械手可开始取放片。
    /// 返回操作实例；null 表示状态不允许/被拒。
    /// </summary>
    ModuleOperation? PrepareTransfer2();

    /// <summary>
    /// 标记本轮取放片进行中（机械手开始交互时调用）；状态不符返回 false。
    /// </summary>
    bool Transferring();

    /// <summary>
    /// 标记本轮完成：先落 TransferComplete 并发布，随即回落锚点态，并回调 OnTransferFinished。
    /// 状态不符返回 false。
    /// </summary>
    bool TransferComplete();

    /// <summary>
    /// 撤回本轮：准备做了（PreTransfer / TransferReady）但机械手还没伸手，这一趟不搬了，站点回锚点态、可以再被服务。
    /// 已经在交互（Transferring）或收尾的不撤——片可能动过，留着等人工确认；本来就在锚点态（没占着）返回 true。
    /// </summary>
    bool CancelTransfer();

    /// <summary>
    /// 这个站点支持的任务（<see cref="StationTaskAction"/>）：取片、放片，再加上站内任务（腔体的工艺）。
    /// Job 建任务表时按它查，要用到的这个站点不支持就不建。
    /// </summary>
    IReadOnlyList<string> SupportedTasks { get; }

    /// <summary>站内任务现在能不能起（不动设备，调度每拍都会问）：能起返回成功，不能返回原因（错误码 + 参数）。</summary>
    HandleResult CheckTask(StationTaskRequest request);

    /// <summary>起站内任务：查过了才发；被拒返回 null（原因用 <see cref="CheckTask"/> 查）。</summary>
    ModuleOperation? StartTask(StationTaskRequest request);
}
