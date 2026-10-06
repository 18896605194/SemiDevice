using xyz.Components.Enums;
using xyz.Components.Models;
using xyz.Shared.Dtos;

namespace xyz.Components.Interfaces;

/// <summary>
/// Job 的命令接口（按对象起名，跟 ILoadPort 一个写法）：本地界面的服务和 EAP（Host）调的是同样几个方法、过同一套检查——
/// 建 Job 都是先建 PJ、再建 CJ 把 PJ 收进来（照 SEMI E40 / E94）。命令当场执行、当场回结果，之后的进展看 <see cref="Snapshot"/> 和上报口。
/// </summary>
public interface IJobManager
{
    /// <summary>建一个 PJ（E40）：Host 给载具号，本地给 LoadPort。建好排着，等 CJ 来收。</summary>
    Task<HandleResult> CreateProcessJobAsync(ProcessJobSpec spec);

    /// <summary>建一个 CJ（E94），把已经建好、还没归 CJ 的 PJ 按顺序收进来。</summary>
    Task<HandleResult> CreateControlJobAsync(ControlJobSpec spec);

    /// <summary>CJ 命令（E94 CJStart / CJPause / CJResume / CJCancel / CJDeselect / CJStop / CJAbort / CJHOQ）。</summary>
    Task<HandleResult> CommandControlJobAsync(string id, CtrlJobCommand command, CtrlJobAction action);

    /// <summary>PJ 命令（E40 Start / Pause / Resume / Stop / Abort / Cancel）。</summary>
    Task<HandleResult> CommandProcessJobAsync(string id, PrJobCommand command);

    /// <summary>当前 Job 全貌（拿到就不变；每次变化换一份新的）。</summary>
    JobListDto Snapshot { get; }

    /// <summary>按载具号找 CJ（没删的，一个载具同时只有一个；不分大小写）；没有为 null。</summary>
    ControlJobDto? FindControlJobByCarrier(string carrierId);

    /// <summary>按载具号找 PJ（全貌里有的：没结束的，加上没删的 CJ 下面已经结束的；按建的先后）。</summary>
    IReadOnlyList<ProcessJobDto> FindProcessJobsByCarrier(string carrierId);

    /// <summary>E40（PJ）上报口；null 表示没接 EAP。EAP 侧接上时挂。</summary>
    IE40Callback? E40Callback { get; set; }

    /// <summary>E94（CJ）上报口；null 表示没接 EAP。跟 E40 共用一条派发线程。</summary>
    IE94Callback? E94Callback { get; set; }
}
