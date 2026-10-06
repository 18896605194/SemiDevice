using xyz.Components.Attributes;
using xyz.Components.Components;
using xyz.Components.Interfaces;
using xyz.Drivers.Loadport;
using xyz.Modules;
using xyz.Modules.Enums;
using xyz._35021.Module.Loadport.Operation;

namespace xyz._35021.Module.Loadport;

/// <summary>
/// 35021 机台 LoadPort 模块：各动作的操作（操作类在 Operation 文件夹）。
/// 设备状态查询、在位判断、断线重连都在平台（BaseLoadPortModule、驱动组件），机型不用管。
/// 品牌驱动是 sc.xml 挂在本模块下的 _driver 子组件，换 Type 即换品牌。
/// </summary>
[Component(description: "35021 LoadPort 模块")]
public class LoadPortModule : BaseLoadPortModule, ILoadPort
{
    #region Mapping

    /// <summary>
    /// Load 操作收到 Mapping 数据后调用：转交基类更新 SlotMap 并回调 EAP。
    /// </summary>
    internal void NoteSlotMap(IReadOnlyList<SlotState> slotMap)
    {
        UpdateSlotMap(slotMap);
    }

    #endregion

    #region 扫描

    /// <summary>
    /// 扫描：基类做完一拍（动作、状态查询、在位、E84、读码）后推送状态。
    /// </summary>
    protected override void OnScan()
    {
        base.OnScan();
        PublishState();
    }

    #endregion

    #region Action

    public override ModuleOperation? Load()
    {
        return Begin(LoadPortAction.Load, new LoadOperation(this));
    }

    public override ModuleOperation? Unload()
    {
        return Begin(LoadPortAction.Unload, new UnloadOperation(this));
    }

    public override ModuleOperation? Home()
    {
        return Begin(LoadPortAction.Home, new HomeOperation(this));
    }

    protected override ModuleOperation? ResetDevice()
    {
        return Begin(LoadPortAction.Reset, new ResetOperation(this));
    }

    protected override ModuleOperation? AbortDevice()
    {
        return Begin(LoadPortAction.Abort, new AbortOperation(this));
    }

    public override ModuleOperation? Clamp()
    {
        return Begin(LoadPortAction.Clamp, new ClampOperation(this));
    }

    public override ModuleOperation? Unclamp()
    {
        return Begin(LoadPortAction.Unclamp, new UnclampOperation(this));
    }

    #endregion
}
