using xyz.Components.Components;
using xyz.Components.Enums;
using xyz.Components.Interfaces;
using xyz.Modules.Enums;

namespace xyz.Modules;

/// <summary>
/// CDS基类
/// </summary>
public abstract class BaseCdsModule : BaseModule, ISupplyModule, ISupplyAccess
{
    /// <summary>
    /// 状态：开机是未初始化
    /// </summary>
    public override int State { get; protected set; } = ModuleState.NotInit;

    /// <summary>
    /// 下面挂的槽，按 machine.xml 的先后
    /// </summary>
    public IReadOnlyList<ITank> Tanks => FindChildren<ITank>();

    /// <summary>
    /// 能不能供液：扫描线程每拍算一次，别的线程只读
    /// </summary>
    private volatile bool _isReady;

    /// <summary>
    /// 能不能供液（ISupplyAccess）：腔体起工艺前看它
    /// </summary>
    public bool IsReady => _isReady;

    /// <summary>
    /// 初始化：功能还没做，直接算做完、进空闲（不动阀），全部初始化不会卡在这儿
    /// </summary>
    public override ModuleRoutine? InitModule()
    {
        State = ModuleState.Idle;
        return ModuleRoutine.Done(this, "Init");
    }

    /// <summary>
    /// 扫描：下面的槽跟着扫（算液位、走补液排液），算能不能供液，再推状态
    /// </summary>
    protected override void OnScan()
    {
        base.OnScan();
        _isReady = CheckReady();
        PublishState();
    }

    /// <summary>
    /// 能不能供液的规则（在扫描线程里算）：初始化过、在空闲，并且有一个槽就绪或正在供液；机型的规则不一样就重写
    /// </summary>
    protected virtual bool CheckReady()
    {
        if (State != ModuleState.Idle)
        {
            return false;
        }

        foreach (var tank in Tanks)
        {
            var state = tank.State;
            if (state == TankState.Ready || state == TankState.Supplying)
            {
                return true;
            }
        }

        return false;
    }
}
