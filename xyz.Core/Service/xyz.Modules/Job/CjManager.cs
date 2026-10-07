using xyz.Common.Log;
using xyz.Components.Enums;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;

namespace xyz.Modules;

/// <summary>保存 CJ 及各自状态机；PJ 协调由 JobManager 处理。</summary>
public sealed class CjManager : ICjManager
{
    #region CJ 字典

    private readonly Dictionary<string, CjEntity> _jobs = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<ControlJob> ControlJobs => _jobs.Values.Select(entity => entity.ControlJob).ToList();

    /// <summary>状态变化及本次 E94 转换编号；内部转换的编号为 0，不上报 E94。</summary>
    public event Action<ControlJob, ControlJobState, int>? StateChanged;

    public void Add(ControlJob controlJob)
    {
        ArgumentNullException.ThrowIfNull(controlJob);
        if (string.IsNullOrWhiteSpace(controlJob.Id))
        {
            throw new ArgumentException("CJ ID 不能为空。", nameof(controlJob));
        }

        if (_jobs.TryGetValue(controlJob.Id, out var existing))
        {
            if (!ReferenceEquals(existing.ControlJob, controlJob))
            {
                throw new InvalidOperationException($"CJ {controlJob.Id} 已存在。");
            }

            return;
        }

        var stateMachine = new CjStateMachine();
        stateMachine.CurrentState = controlJob.State;
        var entity = new CjEntity(controlJob, stateMachine);
        stateMachine.OnStateChanged += (previous, current) => OnStateChanged(entity, previous, current);
        _jobs.Add(controlJob.Id, entity);
    }

    public void Remove(ControlJob controlJob)
    {
        if (_jobs.TryGetValue(controlJob.Id, out var entity)&& ReferenceEquals(entity.ControlJob, controlJob))
        {
            _jobs.Remove(controlJob.Id);
        }
    }

    public ControlJob? Get(string id)
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

    #endregion

    #region CJ 状态动作

    public HandleResult Queue(ControlJob controlJob)
    {
        return Fire(controlJob, ControlStateAction.Queue);
    }

    public HandleResult Select(ControlJob controlJob)
    {
        return Fire(controlJob, ControlStateAction.Select);
    }

    public HandleResult Activate(ControlJob controlJob)
    {
        return Fire(controlJob, ControlStateAction.Activate);
    }

    public HandleResult Deselect(ControlJob controlJob)
    {
        return Fire(controlJob, ControlStateAction.Deselect);
    }

    public HandleResult Pause(ControlJob controlJob)
    {
        return Fire(controlJob, ControlStateAction.Pause);
    }

    public HandleResult Resume(ControlJob controlJob)
    {
        return Fire(controlJob, ControlStateAction.Resume);
    }

    public HandleResult Complete(ControlJob controlJob)
    {
        return Fire(controlJob, ControlStateAction.Complete);
    }

    public HandleResult Abort(ControlJob controlJob)
    {
        return Fire(controlJob, ControlStateAction.Abort);
    }

    public HandleResult FinishAbort(ControlJob controlJob)
    {
        return Fire(controlJob, ControlStateAction.FinishAbort);
    }

    public HandleResult Rollback(ControlJob controlJob)
    {
        return Fire(controlJob, ControlStateAction.Rollback);
    }

    public HandleResult WaitForStart(ControlJob controlJob)
    {
        return Fire(controlJob, ControlStateAction.WaitForStart);
    }

    public HandleResult FinishStop(ControlJob controlJob)
    {
        return Fire(controlJob, ControlStateAction.FinishStop);
    }

    public HandleResult Dequeue(ControlJob controlJob)
    {
        return Fire(controlJob, ControlStateAction.Dequeue);
    }

    public HandleResult Delete(ControlJob controlJob)
    {
        return Fire(controlJob, ControlStateAction.Delete);
    }

    private HandleResult Fire(ControlJob controlJob, ControlStateAction action)
    {
        if (controlJob is null)
        {
            return HandleResult.Fail(ErrorCodes.JobNotFound, string.Empty);
        }

        if (!_jobs.TryGetValue(controlJob.Id, out var entity)
            || !ReferenceEquals(entity.ControlJob, controlJob) || controlJob.IsEnded)
        {
            return HandleResult.Fail(ErrorCodes.JobNotFound, controlJob.Id);
        }

        entity.Action = action;
        var result = entity.StateMachine.StateChange(action);
        if (!result.IsSuccess)
        {
            return HandleResult.Fail(result.ErrorMessage,
                controlJob.Id, action.ToString(), JobNames.Of(controlJob.State));
        }

        return HandleResult.Success(controlJob.Id);
    }

    #endregion

    #region CJ 状态通知

    private void OnStateChanged(CjEntity entity, ControlJobState previous, ControlJobState current)
    {
        var job = entity.ControlJob;
        job.State = current;
        int e94TransitionNumber = 0;
        switch (entity.Action)
        {
            case ControlStateAction.Queue:
                e94TransitionNumber = 1;
                break;
            case ControlStateAction.Dequeue:
                e94TransitionNumber = 2;
                break;
            case ControlStateAction.Select:
                e94TransitionNumber = 3;
                break;
            case ControlStateAction.Deselect:
                e94TransitionNumber = 4;
                break;
            case ControlStateAction.Activate:
                e94TransitionNumber = previous == ControlJobState.WaitingForStart ? 7 : 5;
                break;
            case ControlStateAction.WaitForStart:
                e94TransitionNumber = 6;
                break;
            case ControlStateAction.Pause:
                e94TransitionNumber = 8;
                break;
            case ControlStateAction.Resume:
                e94TransitionNumber = 9;
                break;
            case ControlStateAction.Complete:
                e94TransitionNumber = 10;
                break;
            case ControlStateAction.FinishStop:
                e94TransitionNumber = 11;
                break;
            case ControlStateAction.FinishAbort:
                e94TransitionNumber = 12;
                break;
            case ControlStateAction.Delete:
                e94TransitionNumber = 13;
                break;
        }

        var now = DateTime.Now;
        if (current == ControlJobState.Executing && job.StartedAt is null)
        {
            job.StartedAt = now;
        }

        if (current == ControlJobState.Aborting)
        {
            job.StateBeforeAbort = previous;
            job.Ending = ControlJobEnding.Abort;
        }

        if (current is ControlJobState.Completed or ControlJobState.Aborted && job.CompletedAt is null)
        {
            job.CompletedAt = now;
            job.CompletedBy = e94TransitionNumber;
        }

        if (entity.Action is ControlStateAction.Dequeue or ControlStateAction.Delete)
        {
            job.EndedBy = e94TransitionNumber;
            job.EndedAt = now;
        }

        LogHelper.Info("Job", $"CJ {job.Id}：{JobNames.Of(previous)} → {JobNames.Of(current)}");
        StateChanged?.Invoke(job, current, e94TransitionNumber);
    }

    #endregion

    private sealed class CjEntity(ControlJob controlJob, CjStateMachine stateMachine)
    {
        public ControlJob ControlJob { get; } = controlJob;
        public CjStateMachine StateMachine { get; } = stateMachine;
        public ControlStateAction Action { get; set; }
    }
}
