using xyz.Components.Components;

namespace xyz.Modules;

/// <summary>
/// 能加工的站点
/// </summary>
public interface IProcessStation
{
    /// <summary>模块名（sc.xml 原样）。</summary>
    string Name { get; }

    /// <summary>正在跑的工艺请求；没在跑为 null。</summary>
    ProcessRequest? CurrentProcess { get; }

    /// <summary>现在能不能起这个工艺（状态、片、配方对不对得上这个腔体）：能起返回 null，不能返回原因。不动设备。</summary>
    ProcessRejection? CheckProcess(ProcessRequest request);

    /// <summary>起工艺：检查过了才发；被拒返回 null（原因用 <see cref="CheckProcess"/> 查）。</summary>
    ModuleOperation? StartProcess(ProcessRequest request);
}
