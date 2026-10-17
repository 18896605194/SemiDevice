using xyz.Components.Components;
using xyz.Shared.Dtos;

namespace xyz.Components.Interfaces;

/// <summary>
/// 模块（带状态和动作的组件）都有的
/// </summary>
public interface IModule
{
    /// <summary>
    /// 模块名（如 LoadPort1）
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 状态码
    /// </summary>
    int State { get; }

    /// <summary>
    /// 在线 / 离线
    /// </summary>
    ModuleMode Mode { get; }

    /// <summary>
    /// sc 里启用了没有（停用的当没装）
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// 手上在做的动作，没有是 null
    /// </summary>
    ModuleOperation? CurrentOperation { get; }

    /// <summary>
    /// 模块初始化（动硬件），被拒是 null
    /// </summary>
    ModuleOperation? InitModule();

    /// <summary>
    /// 复位
    /// </summary>
    ModuleOperation? Reset();

    /// <summary>
    /// 中止
    /// </summary>
    ModuleOperation? Abort();

    /// <summary>
    /// 组件全路径（sc 的分组节点也算上，如 Chamber.Chamber1）
    /// </summary>
    string FullPath { get; }

    /// <summary>
    /// 上线：参与自动调度（只改 Mode、不经设备）
    /// </summary>
    void Online();

    /// <summary>
    /// 下线：退出自动调度
    /// </summary>
    void Offline();
}
