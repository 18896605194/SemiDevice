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
    IReadOnlyList<IRobot> Robots { get; }

    /// <summary>
    /// 按名字找站点
    /// </summary>
    bool TryGetStation(string name, [MaybeNullWhen(false)] out ITransferStation station);

    /// <summary>
    /// 按名字找机械手
    /// </summary>
    bool TryGetRobot(string name, [MaybeNullWhen(false)] out IRobot robot);

    /// <summary>
    /// 这个槽被哪一趟搬运占着
    /// </summary>
    bool IsSlotLocked(string station, int slot);

    /// <summary>
    /// 这台机械手在不在搬片
    /// </summary>
    bool IsRobotInTransfer(string robot);

    /// <summary>
    /// 动过手才失败、等人工确认的搬运
    /// </summary>
    IReadOnlyList<TransferRoutine> HeldOperations { get; }

    /// <summary>
    /// 撤掉某个归属（Job）的全部搬运，返回撤了几趟
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
    /// 机械手能来取放片的站点
    /// </summary>
    IEnumerable<ITransferStation> Stations { get; }

    /// <summary>
    /// 手动搬运等结果最多等多久
    /// </summary>
    int ManualWaitTimeoutMs { get; }

    /// <summary>
    /// 人工确认后放掉等确认的那一趟
    /// </summary>
    bool ReleaseHold(Guid waferId);
}
