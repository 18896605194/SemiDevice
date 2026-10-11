using xyz.Components.Components;
using xyz.Shared.Dtos;

namespace xyz.Modules;


public interface ITransferStation
{
    /// <summary>
    /// 工位模块名（如 LoadPort1）
    /// </summary>
    string Name { get; }

    int SlotCount { get; }

    bool CanPrepare { get; }

    /// <summary>
    /// 准备阶段一：粗准备（腔体排气/预热/机构到位等）
    /// </summary>
    /// <returns></returns>
    ModuleOperation? PrepareTransfer();

    /// <summary>
    /// 准备阶段二
    /// </summary>
    /// <returns></returns>
    ModuleOperation? PrepareTransfer2();

    /// <summary>
    /// 标记本轮取放片进行中
    /// </summary>
    bool Transferring();

    /// <summary>
    /// 标记本轮完成：先落 TransferComplete 并发布，随即回落待命态，并回调 OnTransferFinished。
    /// 状态不符返回 false。
    /// </summary>
    bool TransferComplete();

    /// <summary>
    /// 撤回本轮：准备做了（PreTransfer / TransferReady）但机械手还没伸手
    /// </summary>
    /// <returns></returns>
    bool CancelTransfer();

    /// <summary>
    /// 这个站点支持的任务 Job 建任务表时按它查，要用到的这个站点不支持就不建。
    /// </summary>
    IReadOnlyList<string> SupportedTasks { get; }

    /// <summary>
    /// 单独询问站内任务能不能起
    /// </summary>
    /// <param name="request"></param>
    /// <returns></returns>
    HandleResult CheckTask(StationTaskRequest request);

    /// <summary>
    /// 校验并启动站内任务
    /// </summary>
    /// <param name="request"></param>
    /// <returns></returns>
    ModuleOperation? StartTask(StationTaskRequest request);
}
