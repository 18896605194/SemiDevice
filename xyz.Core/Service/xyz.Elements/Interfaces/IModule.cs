using xyz.Elements.Elements;
using xyz.Shared.Dtos;

namespace xyz.Elements.Interfaces;

/// <summary>
/// 模块（带状态和动作的元件）都有的
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

    bool IsEnabled { get; }

    /// <summary>
    /// 站点类型
    /// </summary>
    StationKind Kind { get; }

    /// <summary>
    /// 模块正在做的动作
    /// </summary>
    ModuleRoutine? CurrentRoutine { get; }

    /// <summary>
    /// 模块初始化（动硬件），被拒带原因
    /// </summary>
    HandleResult<ModuleRoutine> InitModule();

    /// <summary>
    /// 复位，被拒带原因
    /// </summary>
    HandleResult<ModuleRoutine> Reset();

    /// <summary>
    /// 中止，被拒带原因
    /// </summary>
    HandleResult<ModuleRoutine> Abort();

    /// <summary>
    /// 元件全路径（machine.xml 的分组节点也算上，如 Chamber.Chamber1）
    /// </summary>
    string FullPath { get; }
}
