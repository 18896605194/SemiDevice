using xyz.Components.Attributes;
using xyz.Drivers.Communication;
using xyz.Drivers.Loadport;
using xyz.Drivers.Loadport.FCD;
using xyz.Modules;
using xyz.Modules.Enums;
using xyz._35021.Module.Loadport.Operation;

namespace xyz._35021.Module.Loadport;

/// <summary>
/// 35021 机台 LoadPort 模块：FCD 驱动 + 各动作操作（操作类在 Operation 文件夹）。
/// </summary>
[Component(description: "35021 LoadPort 模块")]
public class LoadPortModule : LoadPortBase, ILoadPort
{
    /// <summary>
    /// 最近一次 Load 的 Mapping 槽位数据（如 25 个 P）。
    /// </summary>
    public string SlotMap { get; internal set; } = string.Empty;

    protected override LoadPortDriverBase CreateDriver()
    {
        return new FcdLoadPortDriver(new FrameCommunication(CreateTransport(), new FcdFrameCodec()));
    }

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

    public override ModuleOperation? Reset()
    {
        return Begin(LoadPortAction.Reset, new ResetOperation(this));
    }

    public override ModuleOperation? Abort()
    {
        return Begin(LoadPortAction.Abort, new AbortOperation(this));
    }
}
