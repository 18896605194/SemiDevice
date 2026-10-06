using xyz.Common.Log;
using xyz.Components.Components;
using xyz.Components.Enums;
using xyz.Components.Interfaces;

namespace xyz.Modules;

/// <summary>
/// Job 状态转换往外报：每条转换记一行日志，接了 EAP 的再经上报口（IE40Callback / IE94Callback）报出去。
/// 快照在扫描线程上当场转好，回调丢给 EAP 派发线程按顺序发：PJ、CJ 共用一条线程，先后不乱。
/// </summary>
internal sealed class JobEvents
{
    private const string LogModule = "Job";

    private readonly EapNotifier _notifier;
    private readonly Func<IE40Callback?> _e40;
    private readonly Func<IE94Callback?> _e94;

    public JobEvents(EapNotifier notifier, Func<IE40Callback?> e40, Func<IE94Callback?> e94)
    {
        _notifier = notifier;
        _e40 = e40;
        _e94 = e94;
    }

    public void ProcessJobTransitioned(ProcessJob job, int transition, PrJobState? from, PrJobState? to)
    {
        LogHelper.Info(LogModule, $"PJ {job.Id} E40 #{transition}：{JobNames.Of(from)} → {JobNames.Of(to)}");
        var callback = _e40();
        if (callback is null)
        {
            return;
        }

        var dto = JobDtos.Of(job);
        _notifier.Post(() => callback.ProcessJobTransitioned(dto, transition, from, to));
    }

    public void ControlJobTransitioned(ControlJob job, int transition, CtrlJobState? from, CtrlJobState? to)
    {
        LogHelper.Info(LogModule, $"CJ {job.Id} E94 #{transition}：{JobNames.Of(from)} → {JobNames.Of(to)}");
        var callback = _e94();
        if (callback is null)
        {
            return;
        }

        var dto = JobDtos.Of(job);
        _notifier.Post(() => callback.ControlJobTransitioned(dto, transition, from, to));
    }

    public void WaferProcessStarted(ProcessJob job, JobWafer wafer, string station)
    {
        LogHelper.Info(LogModule, $"PJ {job.Id} {wafer.Name} 在 {station} 开始加工（第 {wafer.Step + 1} 站）");
        var callback = _e40();
        if (callback is null)
        {
            return;
        }

        var jobDto = JobDtos.Of(job);
        var waferDto = JobDtos.Of(wafer);
        _notifier.Post(() => callback.WaferProcessStarted(jobDto, waferDto, station));
    }

    public void WaferProcessEnded(ProcessJob job, JobWafer wafer, string station, bool success)
    {
        if (success)
        {
            LogHelper.Info(LogModule, $"PJ {job.Id} {wafer.Name} 在 {station} 加工完成");
        }
        else
        {
            LogHelper.Warn(LogModule, $"PJ {job.Id} {wafer.Name} 在 {station} 加工没做成");
        }

        var callback = _e40();
        if (callback is null)
        {
            return;
        }

        var jobDto = JobDtos.Of(job);
        var waferDto = JobDtos.Of(wafer);
        _notifier.Post(() => callback.WaferProcessEnded(jobDto, waferDto, station, success));
    }
}
