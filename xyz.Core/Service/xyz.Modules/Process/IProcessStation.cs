using xyz.Components.Components;

namespace xyz.Modules;

/// <summary>
/// 能加工的站点（腔体这类）：Job 和手动起工艺走同一个口子。
/// 先 <see cref="CheckProcess"/> 问能不能起（不动设备，调度每拍都会问），再 <see cref="StartProcess"/> 真起；
/// 起工艺时把账上的片标成加工中，做完标成完成 / 失败 / 中止——Job 判"这一站做完了"看的是操作收尾完成，不靠猜。
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
