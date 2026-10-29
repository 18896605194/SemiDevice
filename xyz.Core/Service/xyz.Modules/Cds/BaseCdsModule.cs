using xyz.Common.Log;
using xyz.Elements.Attributes;
using xyz.Elements.Elements;
using xyz.Elements.Enums;
using xyz.Elements.Interfaces;
using xyz.Shared.Dtos;

namespace xyz.Modules;

/// <summary>
/// CDS基类
/// </summary>
public abstract class BaseCdsModule : BaseModule, ISupplyModule, ISupplyAccess, IMode
{
    /// <summary>
    /// 状态：开机是未初始化
    /// </summary>
    public override int State { get; protected set; } = ModuleState.NotInit;

    #region 生产 / 维护模式

    /// <summary>
    /// 生产 / 维护：供液没有手动页、不用人确认，开机就是生产，腔体才能用它
    /// </summary>
    [VariableMark(VariableType.SV, ValueFormat.Enum, description: "模块模式（生产 / 维护）")]
    public ModuleMode Mode { get; private set; } = ModuleMode.Production;

    /// <summary>
    /// 切生产模式：腔体能用它起工艺；只改 Mode、不动阀，机型要多做事就重写、记得调 base
    /// </summary>
    public virtual void SetProduction()
    {
        SetMode(ModuleMode.Production);
    }

    /// <summary>
    /// 切维护模式：腔体不能再用它起新工艺，正在做的照常做完；重写同 <see cref="SetProduction"/>
    /// </summary>
    public virtual void SetMaintenance()
    {
        SetMode(ModuleMode.Maintenance);
    }

    private void SetMode(ModuleMode mode)
    {
        if (Mode == mode)
        {
            return;
        }

        Mode = mode;
        LogHelper.Info(this, $"{mode}");
    }

    #endregion

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
    public override HandleResult<ModuleRoutine> InitModule()
    {
        State = ModuleState.Idle;
        return HandleResult<ModuleRoutine>.Success(ModuleRoutine.Done(this, "Init"));
    }

    /// <summary>
    /// 扫描：下面的槽跟着扫（算液位、走补液排液），算能不能供液，再推状态
    /// </summary>
    protected override void OnScan()
    {
        _isReady = CheckReady();
        PublishState();
    }

    /// <summary>
    /// 能不能供液的规则（在扫描线程里算）：在生产、初始化过、在空闲，并且有一个槽就绪或正在供液；机型的规则不一样就重写
    /// </summary>
    protected virtual bool CheckReady()
    {
        if (Mode != ModuleMode.Production || State != ModuleState.Idle)
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
