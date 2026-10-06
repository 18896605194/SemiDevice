using xyz.Components.Enums;
using xyz.Components.Models;
using xyz.Shared.Dtos;

namespace xyz.Components.Interfaces;

/// <summary>
/// Job 的命令接口（按对象起名，跟 ILoadPort 一个写法）：本地界面的服务和 EAP（Host）调的是同一个接口、过同一套检查。
/// 每个命令进 JobManager 的命令队列，由它的扫描线程统一执行（只有它改 Job 数据），当场回受理结果；
/// 请求号相同的重发回同一个结果。状态照 SEMI E94（CJ）/ E40（PJ），之后的进展看 <see cref="Snapshot"/> 和上报口。
/// </summary>
public interface IJobManager
{
    /// <summary>本地建 Job：一篮一个 CJ，相同流程配方的槽分成一个 PJ。</summary>
    Task<JobCommandResult> CreateLocalJobAsync(LocalJobRequest request);

    /// <summary>建一个 PJ（E40，Host 先建 PJ 再建 CJ）。</summary>
    Task<JobCommandResult> CreateProcessJobAsync(ProcessJobSpec spec, JobCommandSource source);

    /// <summary>建一个 CJ，把已经建好、还没归 CJ 的 PJ 收进来（E94）。</summary>
    Task<JobCommandResult> CreateControlJobAsync(ControlJobSpec spec, JobCommandSource source);

    /// <summary>CJ 命令（E94 CJStart / CJPause / CJResume / CJCancel / CJDeselect / CJStop / CJAbort / CJHOQ）。</summary>
    Task<JobCommandResult> CommandControlJobAsync(string id, CtrlJobCommand command, CtrlJobAction action, JobCommandSource source,
        string? requestId = null);

    /// <summary>PJ 命令（E40 Start / Pause / Resume / Stop / Abort / Cancel）。</summary>
    Task<JobCommandResult> CommandProcessJobAsync(string id, PrJobCommand command, JobCommandSource source, string? requestId = null);

    /// <summary>
    /// 恢复派单：出过执行故障、自动派单暂停后，人到现场确认过片位、对好了账再调。还有出错的搬运单没放锁时不收。
    /// </summary>
    Task<JobCommandResult> RecoverAsync(JobCommandSource source);

    /// <summary>当前 Job 全貌（拿到就不变；每次变化换一份新的）。</summary>
    JobListDto Snapshot { get; }

    /// <summary>还能建几个 PJ（PJ 上限减去没结束的；Host 用 S16F21 问）。</summary>
    int ProcessJobSpace { get; }

    /// <summary>E40（PJ）上报口；null 表示没接 EAP。EAP 侧接上时挂。</summary>
    IE40Callback? E40Callback { get; set; }

    /// <summary>E94（CJ）上报口；null 表示没接 EAP。跟 E40 共用一条派发线程。</summary>
    IE94Callback? E94Callback { get; set; }
}
