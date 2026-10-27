using xyz.Elements.Enums;
using xyz.Elements.Interfaces;
using xyz.Secs.Gem;
using xyz.Secs.Gem.E40;
using xyz.Secs.Gem.E94;
using xyz.Shared.Dtos;

namespace xyz.Elements.Elements;

/// <summary>
/// xyz.Secs 的 E40 / E94 要的 Job 管理：PJ / CJ 只在 Job 管理里有一份（状态机、队列、存盘），这里现取快照翻成 E40 / E94 的样子（用完就扔），
/// Host 的建、命令翻成 Job 管理的命令（来源 Host），被拒的错误码经 JobErrors 翻成 E5
/// </summary>
internal sealed class EapJobs : IGemJobs
{
    private readonly IJobManager _jobs;

    public EapJobs(IJobManager jobs)
    {
        _jobs = jobs;
    }

    public IReadOnlyList<E40ProcessJob> ProcessJobs => _jobs.Snapshot.ProcessJobs.Select(ProcessJobOf).ToList();

    public IReadOnlyList<E94ControlJob> ControlJobs => _jobs.Snapshot.ControlJobs.Select(ControlJobOf).ToList();

    public async Task<E5Error?> CreateProcessJobAsync(string id, string carrierId, IReadOnlyList<int> slots, string recipe)
    {
        var result = await _jobs.CreateProcessJobAsync(loadPort: null, pjName: id, slots: slots, sequence: recipe, lotId: null, carrierId: carrierId)
            .ConfigureAwait(false);
        return ErrorOf(result);
    }

    public async Task<E5Error?> ProcessJobCommandAsync(string id, E40Command command)
    {
        var result = await _jobs.ExecuteProcessJobCommandAsync(id, CommandOf(command)).ConfigureAwait(false);
        return ErrorOf(result);
    }

    public async Task<E5Error?> CreateControlJobAsync(string id, IReadOnlyList<string> processJobs)
    {
        var result = await _jobs.CreateControlJobAsync(loadPort: null, processJobs: processJobs, cjName: id).ConfigureAwait(false);
        return ErrorOf(result);
    }

    public async Task<E5Error?> ControlJobCommandAsync(string id, E94Command command, E94Action action)
    {
        var result = await _jobs.ExecuteControlJobCommandAsync(id, (ControlJobCommand)(int)command, (ControlJobAction)(int)action).ConfigureAwait(false);
        return ErrorOf(result);
    }

    /// <summary>
    /// PJ 的快照：定了片报实际的片在来源载具上的槽号，没定报建的时候要的槽号
    /// </summary>
    public static E40ProcessJob ProcessJobOf(ProcessJobDto job)
    {
        return new E40ProcessJob(job.Id, (byte)job.State, job.Sequence, job.ControlJob, job.CarrierId,
            job.Wafers.Select(wafer => wafer.SourceSlot).ToList(), job.Slots, job.AutoStart);
    }

    /// <summary>
    /// CJ 的快照：状态报 E94 的状态（Job 管理另记的，没有就用自己的）
    /// </summary>
    public static E94ControlJob ControlJobOf(ControlJobDto job)
    {
        return new E94ControlJob(job.Id, (byte)(job.E94State ?? job.State), job.CarrierId, job.ProcessJobs, job.AutoStart);
    }

    private static ProcessJobCommand CommandOf(E40Command command)
    {
        switch (command)
        {
            case E40Command.Start:
                return ProcessJobCommand.Start;
            case E40Command.Pause:
                return ProcessJobCommand.Pause;
            case E40Command.Resume:
                return ProcessJobCommand.Resume;
            case E40Command.Stop:
                return ProcessJobCommand.Stop;
            case E40Command.Abort:
                return ProcessJobCommand.Abort;
            default:
                return ProcessJobCommand.Cancel;
        }
    }

    private static E5Error? ErrorOf(HandleResult result)
    {
        if (result.IsSuccess)
        {
            return null;
        }

        return JobErrors.Of(result);
    }
}
