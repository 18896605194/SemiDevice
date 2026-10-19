using xyz.Components.Components;
using xyz.Components.Interfaces;
using xyz.Modules.Enums;

namespace xyz.Modules;

/// <summary>
/// CDS基类
/// </summary>
public abstract class BaseCdsModule : BaseModule, ISupplyModule
{
    /// <summary>
    /// 状态：开机是未初始化
    /// </summary>
    public override int State { get; protected set; } = ModuleState.NotInit;

    /// <summary>
    /// 下面挂的槽，按 sc 的先后
    /// </summary>
    public IReadOnlyList<ITank> Tanks => FindChildren<ITank>();

    /// <summary>
    /// 初始化：功能还没做，直接算做完、进空闲（不动阀），全部初始化不会卡在这儿
    /// </summary>
    public override ModuleOperation? InitModule()
    {
        State = ModuleState.Idle;
        return new NoOpOperation("Init");
    }

    /// <summary>
    /// 扫描：下面的槽跟着扫（算液位、走补液排液），再推状态
    /// </summary>
    protected override void OnScan()
    {
        base.OnScan();
        PublishState();
    }
}
