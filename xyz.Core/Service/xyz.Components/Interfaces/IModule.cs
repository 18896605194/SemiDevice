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
    /// sc 里启用了没有（停用的当没装）
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// 模块自己报是哪一类（LoadPort、腔体、buffer、机械手……）：界面、账单这些照抄，别处不用判断具体类型
    /// </summary>
    StationKind Kind { get; }

    /// <summary>
    /// 手上在做的动作，没有是 null
    /// </summary>
    ModuleRoutine? CurrentRoutine { get; }

    /// <summary>
    /// 模块初始化（动硬件），被拒是 null
    /// </summary>
    ModuleRoutine? InitModule();

    /// <summary>
    /// 复位
    /// </summary>
    ModuleRoutine? Reset();

    /// <summary>
    /// 中止
    /// </summary>
    ModuleRoutine? Abort();

    /// <summary>
    /// 组件全路径（sc 的分组节点也算上，如 Chamber.Chamber1）
    /// </summary>
    string FullPath { get; }
}
