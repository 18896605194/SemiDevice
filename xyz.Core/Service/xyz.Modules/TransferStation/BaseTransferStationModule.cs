using xyz.Modules.Enums;

namespace xyz.Modules;

/// <summary>
/// 机械手可服务工位基类
/// </summary>
public abstract class BaseTransferStationModule : BaseModule, ITransferStation
{
    #region 锚点（环的起终点定义）

    /// <summary>
    /// 工位就绪、可被机械手服务的状态 ,有的是idle，有的是loaded类似这种
    /// </summary>
    protected virtual int AnchorState => ModuleState.Idle;

    #endregion

    #region ITransferStation 实现

    public virtual bool CanPrepare => State == AnchorState;

    /// <summary>
    /// 准备一
    /// </summary>
    public virtual ModuleOperation? PrepareTransfer()
    {
        return TransferStep(AnchorState, TransferModuleState.PreTransfer)
            ? new NoOpOperation("PrepareTransfer")
            : null;
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
        // 只落到 TransferComplete 就交给钩子。收尾期间不能是锚点态——
        // 锚点态的意思是"我空闲、可以被服务"，门还在关就说这句话，调度器会把机械手再派过来。
        if (!TransferStep(TransferModuleState.Transferring, TransferModuleState.TransferComplete))
        {
            return false;
        }

        OnTransferFinished(); //钩子
        return true;
    }

    #endregion

    #region 钩子（子类扩展点）

    /// <summary>
    /// 一轮传片完成后的收尾钩子。默认没有收尾动作，直接回锚点态。
    /// 腔体重写：关门、起工艺，收尾真做完了再自己落状态——在那之前一直停在 TransferComplete。
    /// </summary>
    protected virtual void OnTransferFinished()
    {
        TransferStep(TransferModuleState.TransferComplete, AnchorState);
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
