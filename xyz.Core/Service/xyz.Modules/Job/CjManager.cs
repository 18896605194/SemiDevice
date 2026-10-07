using xyz.Common.Log;
using xyz.Components.Enums;
using xyz.Components.Models;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;

namespace xyz.Modules;

internal sealed class CjManager : ICjManager
{
    private const string LogModule = "Job";

    #region CJ 字典

    private readonly Dictionary<string, CjEntity> _jobs = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, CjEntity> Jobs => _jobs;

    public event Action<ControlJob, ControlJobState>? StateChanged;

    public void Add(ControlJob controlJob)
    {
        var entity = new CjEntity(controlJob);
        _jobs.Add(controlJob.Id, entity);
        entity.StateMachine.StateChanged += OnStateChanged;
        entity.StateMachine.Fire(ControlStateAction.Create);
    }

    public void Remove(ControlJob controlJob)
    {
        if (_jobs.Remove(controlJob.Id, out var entity))
        {
            entity.StateMachine.StateChanged -= OnStateChanged;
        }
    }

    #endregion

    public ControlJob? Find(string id)
    {
        if (_jobs.TryGetValue(id, out var entity))
        {
            return entity.ControlJob;
        }

        return null;
    }

    public ControlJob? FindByLoadPort(string loadPort)
    {
        foreach (var entity in _jobs.Values)
        {
            if (string.Equals(entity.ControlJob.LoadPort, loadPort, StringComparison.OrdinalIgnoreCase))
            {
                return entity.ControlJob;
            }
        }

        return null;
    }

    public ControlJob? FindByCarrier(string carrierId)
    {
        foreach (var entity in _jobs.Values)
        {
            if (string.Equals(entity.ControlJob.CarrierId, carrierId, StringComparison.OrdinalIgnoreCase))
            {
                return entity.ControlJob;
            }
        }

        return null;
    }

    #region CJ 命令（E94）

    public HandleResult Start(string id)
    {
        id = id.Trim();
        if (!_jobs.TryGetValue(id, out var entity) || entity.ControlJob.IsEnded)
        {
            return HandleResult.Fail(ErrorCodes.JobNotFound, id);
        }

        var job = entity.ControlJob;
        string name = JobNames.Of(ControlJobCommand.Start);
        if (job.Ending != ControlJobEnding.None)
        {
            return HandleResult.Fail(ErrorCodes.JobEnding, job.Id, name);
        }

        if (!entity.StateMachine.Fire(ControlStateAction.Start))
        {
            return HandleResult.Fail(ErrorCodes.JobCommandNotAllowed, job.Id, name, JobNames.Of(job.State));
        }

        return HandleResult.Success(job.Id);
    }

    public HandleResult Pause(string id)
    {
        id = id.Trim();
        if (!_jobs.TryGetValue(id, out var entity) || entity.ControlJob.IsEnded)
        {
            return HandleResult.Fail(ErrorCodes.JobNotFound, id);
        }

        var job = entity.ControlJob;
        string name = JobNames.Of(ControlJobCommand.Pause);
        if (job.Ending != ControlJobEnding.None)
        {
            return HandleResult.Fail(ErrorCodes.JobEnding, job.Id, name);
        }

        if (!entity.StateMachine.Fire(ControlStateAction.Pause))
        {
            return HandleResult.Fail(ErrorCodes.JobCommandNotAllowed, job.Id, name, JobNames.Of(job.State));
        }

        return HandleResult.Success(job.Id);
    }

    public HandleResult Resume(string id)
    {
        id = id.Trim();
        if (!_jobs.TryGetValue(id, out var entity) || entity.ControlJob.IsEnded)
        {
            return HandleResult.Fail(ErrorCodes.JobNotFound, id);
        }

        var job = entity.ControlJob;
        string name = JobNames.Of(ControlJobCommand.Resume);
        if (job.Ending != ControlJobEnding.None)
        {
            return HandleResult.Fail(ErrorCodes.JobEnding, job.Id, name);
        }

        if (!entity.StateMachine.Fire(ControlStateAction.Resume))
        {
            return HandleResult.Fail(ErrorCodes.JobCommandNotAllowed, job.Id, name, JobNames.Of(job.State));
        }

        return HandleResult.Success(job.Id);
    }

    /// <summary>Stop：排队时删除；已激活则记录请求，等待执行结束。</summary>
    public HandleResult Stop(string id)
    {
        id = id.Trim();
        if (!_jobs.TryGetValue(id, out var entity) || entity.ControlJob.IsEnded)
        {
            return HandleResult.Fail(ErrorCodes.JobNotFound, id);
        }

        var job = entity.ControlJob;
        string name = JobNames.Of(ControlJobCommand.Stop);
        if (job.State == ControlJobState.Queued)
        {
            if (!entity.StateMachine.Fire(ControlStateAction.Dequeue))
            {
                return HandleResult.Fail(ErrorCodes.JobCommandNotAllowed, job.Id, name, JobNames.Of(job.State));
            }

            return HandleResult.Success(job.Id);
        }

        if (job.State == ControlJobState.Completed)
        {
            return HandleResult.Fail(ErrorCodes.JobCommandNotAllowed, job.Id, name, JobNames.Of(job.State));
        }

        if (job.Ending == ControlJobEnding.Abort || job.Ending == ControlJobEnding.Stop)
        {
            return HandleResult.Success(job.Id);
        }

        job.Ending = ControlJobEnding.Stop;
        LogHelper.Info(LogModule, $"CJ {job.Id} 收下 {name}，等待执行结束");
        return HandleResult.Success(job.Id);
    }

    /// <summary>Abort：排队时删除；已激活则记录请求，等待执行结束。</summary>
    public HandleResult Abort(string id)
    {
        id = id.Trim();
        if (!_jobs.TryGetValue(id, out var entity) || entity.ControlJob.IsEnded)
        {
            return HandleResult.Fail(ErrorCodes.JobNotFound, id);
        }

        var job = entity.ControlJob;
        string name = JobNames.Of(ControlJobCommand.Abort);
        if (job.State == ControlJobState.Queued)
        {
            if (!entity.StateMachine.Fire(ControlStateAction.Dequeue))
            {
                return HandleResult.Fail(ErrorCodes.JobCommandNotAllowed, job.Id, name, JobNames.Of(job.State));
            }

            return HandleResult.Success(job.Id);
        }

        if (job.State == ControlJobState.Completed)
        {
            return HandleResult.Fail(ErrorCodes.JobCommandNotAllowed, job.Id, name, JobNames.Of(job.State));
        }

        if (job.Ending == ControlJobEnding.Abort)
        {
            return HandleResult.Success(job.Id);
        }

        job.Ending = ControlJobEnding.Abort;
        LogHelper.Info(LogModule, $"CJ {job.Id} 收下 {name}，等待执行结束");
        return HandleResult.Success(job.Id);
    }

    public HandleResult Cancel(string id)
    {
        id = id.Trim();
        if (!_jobs.TryGetValue(id, out var entity) || entity.ControlJob.IsEnded)
        {
            return HandleResult.Fail(ErrorCodes.JobNotFound, id);
        }

        var job = entity.ControlJob;
        string name = JobNames.Of(ControlJobCommand.Cancel);
        if (job.Ending != ControlJobEnding.None)
        {
            return HandleResult.Fail(ErrorCodes.JobEnding, job.Id, name);
        }

        if (!entity.StateMachine.Fire(ControlStateAction.Dequeue))
        {
            return HandleResult.Fail(ErrorCodes.JobCommandNotAllowed, job.Id, name, JobNames.Of(job.State));
        }

        return HandleResult.Success(job.Id);
    }

    public HandleResult Deselect(string id)
    {
        id = id.Trim();
        if (!_jobs.TryGetValue(id, out var entity) || entity.ControlJob.IsEnded)
        {
            return HandleResult.Fail(ErrorCodes.JobNotFound, id);
        }

        var job = entity.ControlJob;
        string name = JobNames.Of(ControlJobCommand.Deselect);
        if (job.Ending != ControlJobEnding.None)
        {
            return HandleResult.Fail(ErrorCodes.JobEnding, job.Id, name);
        }

        if (!entity.StateMachine.Fire(ControlStateAction.Deselect))
        {
            return HandleResult.Fail(ErrorCodes.JobCommandNotAllowed, job.Id, name, JobNames.Of(job.State));
        }

        return HandleResult.Success(job.Id);
    }

    public HandleResult HeadOfQueue(string id)
    {
        id = id.Trim();
        if (!_jobs.TryGetValue(id, out var entity) || entity.ControlJob.IsEnded)
        {
            return HandleResult.Fail(ErrorCodes.JobNotFound, id);
        }

        var job = entity.ControlJob;
        string name = JobNames.Of(ControlJobCommand.HeadOfQueue);
        if (job.Ending != ControlJobEnding.None)
        {
            return HandleResult.Fail(ErrorCodes.JobEnding, job.Id, name);
        }

        if (job.State != ControlJobState.Queued)
        {
            return HandleResult.Fail(ErrorCodes.JobCommandNotAllowed, job.Id, name, JobNames.Of(job.State));
        }

        var ordered = _jobs.Values.ToList();
        ordered.Remove(entity);
        int head = ordered.FindIndex(current => current.ControlJob.State == ControlJobState.Queued);
        ordered.Insert(head < 0 ? ordered.Count : head, entity);
        _jobs.Clear();
        foreach (var current in ordered)
        {
            _jobs.Add(current.ControlJob.Id, current);
        }

        return HandleResult.Success(job.Id);
    }

    #endregion

    private void OnStateChanged(ControlJob job, ControlJobState from, ControlJobState to)
    {
        if (job.IsEnded)
        {
            Remove(job);
        }

        Report(job, job.TransitionNumber, from, job.IsEnded ? null : to);
    }

    /// <summary>CJ 转了（含刚建好的 #1）：记一行日志，告诉 Job 组件。</summary>
    private void Report(ControlJob job, int number, ControlJobState? from, ControlJobState? to)
    {
        LogHelper.Info(LogModule, $"CJ {job.Id} E94 #{number}：{JobNames.Of(from)} → {JobNames.Of(to)}");
        StateChanged?.Invoke(job, job.State);
    }
}
