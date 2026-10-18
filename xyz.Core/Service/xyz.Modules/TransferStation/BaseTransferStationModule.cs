using System.Globalization;
using xyz.Components.Components;
using xyz.Components.Interfaces;
using xyz.Modules.Enums;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;

namespace xyz.Modules;

/// <summary>
/// 机械手可服务工位基类
/// </summary>
public abstract class BaseTransferStationModule : BaseModule, ITransferStation, IStationTaskExecutor
{
    #region 待命态（传片环的起点和终点）

    /// <summary>
    /// 待命态：站点空着、机械手可以来取放片的状态，传片环从这里出发、做完回到这里。
    /// 各站点不一样：腔体是 Idle，LoadPort 是 Loaded（载具装好了才能取放）。
    /// </summary>
    protected virtual int StandbyState => ModuleState.Idle;

    #endregion

    #region ITransferStation 实现

    /// <summary>
    /// 槽位数
    /// </summary>
    public abstract int SlotCount { get; set; }

    public bool CanPrepareTransfer => CheckPrepareTransfer() is null;

    /// <summary>
    /// 只检查传片环是否在待命态；模块补充自己的准备条件，不在这里增加自动调度限制。
    /// </summary>
    public virtual HandleResult? CheckPrepareTransfer()
    {
        int state = State;
        return state == StandbyState
            ? null
            : HandleResult.Fail(ErrorCodes.ModuleStateNotAllowed, Name, state.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// 准备一：锁内重查准入并占用本轮传片环。
    /// </summary>
    public virtual ModuleOperation? PrepareTransfer()
    {
        lock (OperationGate)
        {
            if (CheckPrepareTransfer() is not null)
            {
                return null;
            }

            return TransferStep(StandbyState, TransferModuleState.PreTransfer)
                ? new NoOpOperation("PrepareTransfer")
                : null;
        }
    }

    /// <summary>
    /// 准备二
    /// </summary>
    public virtual ModuleOperation? PrepareTransfer2()
    {
        return TransferStep(TransferModuleState.PreTransfer, TransferModuleState.TransferReady)
            ? new NoOpOperation("PrepareTransfer2")
            : null;
    }

    /// <summary>
    /// 机械手和工站正在交互
    /// </summary>
    public bool Transferring()
    {
        return TransferStep(TransferModuleState.TransferReady, TransferModuleState.Transferring);
    }

    /// <summary>
    /// 机械手和站点传输完成
    /// </summary>
    public bool TransferComplete()
    {    
        if (!TransferStep(TransferModuleState.Transferring, TransferModuleState.TransferComplete))
        {
            return false;
        }

        OnTransferFinished(); //钩子
        return true;
    }

    /// <summary>
    /// 撤回本轮
    /// </summary>
    /// <returns></returns>
    public bool CancelTransfer()
    {
        int state = State;
        if (state == StandbyState)
        {
            return true;
        }

        if (state != TransferModuleState.PreTransfer && state != TransferModuleState.TransferReady)
        {
            return false;
        }

        OnTransferCancelled(state);
        return true;
    }

    #endregion

    #region IStationTaskExecutor 实现

    /// <summary>
    /// 支持的任务：默认只能取放（LoadPort 这类）。有站内任务的站点重写，比如腔体加上工艺。
    /// </summary>
    public virtual IReadOnlyList<string> SupportedTasks => StationTaskAction.PickPlace;

    /// <summary>
    /// 站内任务能不能起：
    /// </summary>
    public virtual HandleResult CheckTask(StationTaskRequest request)
    {
        return HandleResult.Fail(ErrorCodes.StationTaskUnsupported, Name, request.Kind);
    }

    /// <summary>
    /// 起站内任务：默认没有站内任务，返回 null。
    /// </summary>
    public virtual ModuleOperation? StartTask(StationTaskRequest request)
    {
        return null;
    }

    #endregion

    #region 钩子（子类扩展点）

    /// <summary>
    /// 一轮传片完成后的收尾钩子。默认没有收尾动作，直接回待命态。
    /// 腔体重写：关门、起工艺，收尾真做完了再自己落状态——在那之前一直停在 TransferComplete。
    /// </summary>
    protected virtual void OnTransferFinished()
    {
        TransferStep(TransferModuleState.TransferComplete, StandbyState);
    }

    /// <summary>
    /// 撤回本轮的钩子（from 是撤之前的状态）。默认准备阶段什么都没动过，直接回待命态；
    /// 准备时开过门、抽过气的站点重写成先收回去，收好了再自己落待命态。
    /// </summary>
    protected virtual void OnTransferCancelled(int from)
    {
        TransferStep(from, StandbyState);
    }

    #endregion

    #region 环标记公共实现

    /// <summary>
    /// 状态环单步：锁内校验当前态 → 迁移 → 立即发布；状态不符返回 false。
    /// 子类收尾完毕落状态也走这个，别直接赋 State（会跳过校验和发布）。
    /// </summary>
    protected bool TransferStep(int expected, int next)
    {
        lock (OperationGate)
        {
            if (State != expected)
            {
                return false;
            }

            State = next;
            PublishState();
            return true;
        }
    }

    #endregion
}
