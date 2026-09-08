namespace xyz.Modules;

/// <summary>
/// LoadPort 模块对外公开的统一操作契约。
/// 动作为下发即返回（返回操作实例，null=被拒）；结果两条路：
/// 手动/同步调用方用 WaitReply 等终态后读 IsSuccess；事件驱动调用方看模块状态（经事件通道回馈）。
/// </summary>
public interface ILoadPort
{
    int State { get; }

    ModuleOperation? Load();

    ModuleOperation? Unload();

    ModuleOperation? Home();

    ModuleOperation? Reset();

    ModuleOperation? Abort();

    /// <summary>
    /// E84 上线（自动模式）；成功后 IsAutoMode=true。
    /// </summary>
    ModuleOperation? Online();

    /// <summary>
    /// E84 下线（手动模式）；成功后 IsAutoMode=false。
    /// </summary>
    ModuleOperation? Offline();

    string? ReadCarrierId();
}
