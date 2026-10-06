using xyz.Components.Enums;
using xyz.Components.Models;
using xyz.Shared.Dtos;

namespace xyz.Modules;


public interface ICjManager
{
    /// <summary>CJ 转了（含建好的 #1）：CJ、转换号、从哪个状态、到哪个状态（删掉了为 null）。Job 组件据此报 EAP、做牵扯别处的事。</summary>
    event Action<ControlJob, int, CtrlJobState?, CtrlJobState?>? Transitioned;

    IReadOnlyList<ControlJob> Jobs { get; }

    IReadOnlyList<ControlJob> History { get; }

    IReadOnlyList<ControlJobDto> Restored { get; }

    ControlJob? Find(string id);

    ControlJob? On(string loadPort);

    /// <summary>建 CJ（本地、Host 一样）：引用的 PJ 都要已经建好、还没归别的 CJ、在同一个 LoadPort 上，这个 LoadPort 上还没有 CJ。造出来还没进队列。</summary>
    HandleResult? TryBuild(ControlJobSpec spec, JobCommandSource source, out ControlJob job, out List<ProcessJob> processes);

    /// <summary>造好的 CJ 进队列（排在队尾），收下它的 PJ，报 E94 #1。</summary>
    void Add(ControlJob job, IReadOnlyList<ProcessJob> processes);

    /// <summary>CJ 命令（E94 CJStart / CJPause / CJResume / CJCancel / CJDeselect / CJStop / CJAbort / CJHOQ）。</summary>
    HandleResult Command(string id, CtrlJobCommand command, CtrlJobAction action);

    /// <summary>整机停止：没结束的 CJ 都走中止（排队的 PJ 一起删）。</summary>
    void AbortAll();

    /// <summary>把每个 CJ 该自动转的转一轮；转了返回 true。LoadPort 上的载具好没好、拿没拿走由 Job 组件查设备给。</summary>
    bool Advance(Func<string, bool> isCarrierReady, Func<ControlJob, bool> isCarrierGone);

    /// <summary>历史连上次开机留下的一起最多留 keep 个，先扔最老的。</summary>
    void TrimHistory(int keep);

    /// <summary>重启收场：上次没删的 CJ 记成中止结束（标着重启），跟上次的历史一起放进历史。</summary>
    void CloseOutLastRun(JobListDto? last);
}
