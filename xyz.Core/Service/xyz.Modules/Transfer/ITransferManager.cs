using System.Diagnostics.CodeAnalysis;
using xyz.Components.Interfaces;
using xyz.Shared.Dtos;

namespace xyz.Modules;

/// <summary>
/// 搬运管理
/// </summary>
public interface ITransferManager
{
    /// <summary>
    /// 搬运管理开着没有
    /// </summary>
    bool IsEnable { get; }

    /// <summary>
    /// 自动派单开着没有
    /// </summary>
    bool IsAutoDispatch { get; }

    /// <summary>
    /// 开自动派单（Auto）
    /// </summary>
    void StartAutoDispatch();

    /// <summary>
    /// 停自动派单（Manual）
    /// </summary>
    void StopAutoDispatch();

    /// <summary>
    /// 搬运能到的机械手
    /// </summary>
    IReadOnlyList<ITransferRobot> Robots { get; }

    /// <summary>
    /// 按名字找站点
    /// </summary>
    bool TryGetStation(string name, [MaybeNullWhen(false)] out ITransferStation station);

    /// <summary>
    /// 按名字找机械手
    /// </summary>
    bool TryGetRobot(string name, [MaybeNullWhen(false)] out ITransferRobot robot);

    /// <summary>
    /// 这个槽被哪一趟搬运占着
    /// </summary>
    bool IsSlotLocked(string station, int slot);

    /// <summary>
    /// 这台机械手在不在搬片
    /// </summary>
    bool IsRobotInTransfer(string robot);

    /// <summary>
    /// 运动已开始或站点占用未解除时失败、等人工确认的搬运（一条记录只列一次）
    /// </summary>
    IReadOnlyList<TransferExecution> HeldOperations { get; }

    /// <summary>
    /// 这个 Job 还有没收尾的搬运（在跑的、挂着等人工的记录里有它的片）
    /// </summary>
    bool HasUnfinishedTransfer(string owner);

    /// <summary>
    /// 撤掉跟某个归属（Job）有关的全部搬运，返回撤了几趟
    /// </summary>
    int CancelOwner(string owner, string reason);

    /// <summary>
    /// 中止全部搬运
    /// </summary>
    object? Abort();

    /// <summary>
    /// 受理一趟搬运
    /// </summary>
    HandleResult<TransferRoutine> Start(TransferRequest request);

    /// <summary>
    /// 受理一次同腔交换（同一槽先取走旧片、再放进新片）
    /// </summary>
    HandleResult<SwapRoutine> StartSwap(SwapRequest request);

    /// <summary>
    /// 机械手能来取放片的站点
    /// </summary>
    IEnumerable<ITransferStation> Stations { get; }

    /// <summary>
    /// 手动搬运等结果最多等多久
    /// </summary>
    int ManualWaitTimeoutMs { get; }

    /// <summary>
    /// 人工确认片位及站点状态后，释放失败搬运保留的资源
    /// </summary>
    bool ReleaseHold(Guid waferId);
}
