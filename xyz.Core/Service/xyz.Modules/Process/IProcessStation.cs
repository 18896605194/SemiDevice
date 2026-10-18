using xyz.Components.Components;
using xyz.Components.Interfaces;

namespace xyz.Modules;

/// <summary>
/// 能做工艺的站点：工艺配方、流程配方、Job 按它认（腔体、以后的热板冷板都实现它）
/// </summary>
public interface IProcessStation : IModule
{
    /// <summary>
    /// 站点类型：工艺配方按它挑能跑的站点
    /// </summary>
    string Type { get; }

    /// <summary>正在跑的工艺请求；没在跑为 null。</summary>
    ProcessRequest? CurrentProcess { get; }

    /// <summary>这个配方在这个站点上跑不跑得了（配方里选的设备这里有没有）：对得上返回 null，对不上回字段和值。不看状态，不动设备。</summary>
    (string Field, string Value)? FindRecipeMismatch(RecipeData recipe);

    /// <summary>现在能不能起这个工艺（状态、片、配方对不对得上这个站点）：能起返回 null，不能返回原因。不动设备。</summary>
    ProcessRejection? CheckProcess(ProcessRequest request);

    /// <summary>起工艺：检查过了才发；被拒返回 null（原因用 <see cref="CheckProcess"/> 查）。</summary>
    ModuleOperation? StartProcess(ProcessRequest request);
}
