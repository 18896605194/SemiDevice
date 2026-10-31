using System.Collections.Concurrent;
using xyz.Common.Log;
using xyz.Elements.Enums;
using xyz.Elements.Interfaces;
using xyz.Elements.Models;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;

namespace xyz.Elements.Elements;

/// <summary>
/// 保存 PJ、各自状态机和晶圆归属；执行进度与设备协调由 JobManager 处理。logOwner 是日志记在谁名下（Job 管理）
/// </summary>
public sealed class PjManager(ILogSource logOwner) : IPjManager
{
    #region PJ 字典

    private readonly Dictionary<string, PjEntity> _jobs = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<Guid, string> _owners = new();

    public IReadOnlyList<ProcessJob> ProcessJobs => _jobs.Values.Select(entity => entity.ProcessJob).ToList();

    /// <summary>状态变化及本次 E40 转换编号。</summary>
    public event Action<ProcessJob, int, int>? StateChanged;

    public void Add(ProcessJob processJob)
    {
        ArgumentNullException.ThrowIfNull(processJob);
        if (string.IsNullOrWhiteSpace(processJob.Id))
        {
            throw new ArgumentException("PJ ID 不能为空。", nameof(processJob));
        }

        if (_jobs.TryGetValue(processJob.Id, out var existing))
        {
            if (!ReferenceEquals(existing.ProcessJob, processJob))
            {
                throw new InvalidOperationException($"PJ {processJob.Id} 已存在。");
            }

            return;
        }

        // 状态只存在 ProcessJob.State 上，这张表只管规则
        var states = new StateTable(logOwner);
        states.Register(PjStateTable.Transitions);
        _jobs.Add(processJob.Id, new PjEntity(processJob, states));
    }

    public void Remove(ProcessJob processJob)
    {
        if (_jobs.TryGetValue(processJob.Id, out var entity)
            && ReferenceEquals(entity.ProcessJob, processJob))
        {
            foreach (var row in processJob.Rows)
            {
                _owners.TryRemove(new KeyValuePair<Guid, string>(row.WaferId, processJob.Id));
            }

            _jobs.Remove(processJob.Id);
        }
    }

    public ProcessJob? Get(string id)
    {
        if (_jobs.TryGetValue(id, out var entity))
        {
            return entity.ProcessJob;
        }

        return null;
    }

    public string? OwnerOf(Guid wafer)
    {
        if (_owners.TryGetValue(wafer, out string? owner))
        {
            return owner;
        }

        return null;
    }

    public void RegisterWafers(ProcessJob processJob)
    {
        if (!_jobs.TryGetValue(processJob.Id, out var entity) || !ReferenceEquals(entity.ProcessJob, processJob))
        {
            return;
        }

        foreach (var row in processJob.Rows)
        {
            _owners[row.WaferId] = processJob.Id;
        }
    }

    #endregion

    #region PJ 状态动作

    public HandleResult Queue(ProcessJob processJob)
    {
        return Fire(processJob, ProcessStateAction.Queue);
    }

    public HandleResult Setup(ProcessJob processJob)
    {
        return Fire(processJob, ProcessStateAction.Setup);
    }

    public HandleResult WaitForStart(ProcessJob processJob)
    {
        return Fire(processJob, ProcessStateAction.WaitForStart);
    }

    public HandleResult Activate(ProcessJob processJob)
    {
        return Fire(processJob, ProcessStateAction.Activate);
    }

    public HandleResult Start(ProcessJob processJob)
    {
        return Fire(processJob, ProcessStateAction.Start);
    }

    public HandleResult Complete(ProcessJob processJob)
    {
        return Fire(processJob, ProcessStateAction.Complete);
    }

    public HandleResult Finish(ProcessJob processJob)
    {
        return Fire(processJob, ProcessStateAction.Finish);
    }

    public HandleResult Pause(ProcessJob processJob)
    {
        return Fire(processJob, ProcessStateAction.Pause);
    }

    public HandleResult FinishPause(ProcessJob processJob)
    {
        return Fire(processJob, ProcessStateAction.FinishPause);
    }

    public HandleResult Resume(ProcessJob processJob)
    {
        return Fire(processJob, ProcessStateAction.Resume);
    }

    public HandleResult Stop(ProcessJob processJob)
    {
        return Fire(processJob, ProcessStateAction.Stop);
    }

    public HandleResult FinishStop(ProcessJob processJob)
    {
        return Fire(processJob, ProcessStateAction.FinishStop);
    }

    public HandleResult Abort(ProcessJob processJob)
    {
        return Fire(processJob, ProcessStateAction.Abort);
    }

    public HandleResult FinishAbort(ProcessJob processJob)
    {
        return Fire(processJob, ProcessStateAction.FinishAbort);
    }

    public HandleResult Dequeue(ProcessJob processJob)
    {
        return Fire(processJob, ProcessStateAction.Dequeue);
    }

    private HandleResult Fire(ProcessJob processJob, string action)
    {
        if (processJob is null)
        {
            return HandleResult.Fail(ErrorCodes.JobNotFound, string.Empty);
        }

        if (!_jobs.TryGetValue(processJob.Id, out var entity)
            || !ReferenceEquals(entity.ProcessJob, processJob) || processJob.IsEnded)
        {
            return HandleResult.Fail(ErrorCodes.JobNotFound, processJob.Id);
        }

        if (!entity.States.TryGet(processJob.State, action, out var row))
        {
            return HandleResult.Fail(ErrorCodes.JobCommandNotAllowed,
                processJob.Id, action, JobNames.PjState(processJob.State));
        }

        entity.Action = action;
        int previous = processJob.State;

        // 暂停时记住从哪儿来，恢复就回哪儿去（SettingUp / WaitingForStart / Processing 三选一）
        if (action == ProcessStateAction.Pause)
        {
            entity.BeforePause = previous;
        }

        int next = row.SuccessState;
        if (action == ProcessStateAction.Resume)
        {
            next = entity.BeforePause;
        }

        processJob.State = next;
        OnStateChanged(entity, previous, processJob.State);
        return HandleResult.Success(processJob.Id);
    }

    #endregion

    #region PJ 状态通知

    private void OnStateChanged(PjEntity entity, int previous, int current)
    {
        var job = entity.ProcessJob;
        job.State = current;
        int e40TransitionNumber = 0;
        switch (entity.Action)
        {
            case ProcessStateAction.Queue:
                e40TransitionNumber = 1;
                foreach (var row in job.Rows)
                {
                    _owners[row.WaferId] = job.Id;
                }

                break;
            case ProcessStateAction.Setup:
                e40TransitionNumber = 2;
                break;
            case ProcessStateAction.WaitForStart:
                e40TransitionNumber = 3;
                break;
            case ProcessStateAction.Activate:
                e40TransitionNumber = 4;
                break;
            case ProcessStateAction.Start:
                e40TransitionNumber = 5;
                break;
            case ProcessStateAction.Complete:
                e40TransitionNumber = 6;
                break;
            case ProcessStateAction.Finish:
                e40TransitionNumber = 7;
                break;
            case ProcessStateAction.Pause:
                e40TransitionNumber = 8;
                break;
            case ProcessStateAction.FinishPause:
                e40TransitionNumber = 9;
                break;
            case ProcessStateAction.Resume:
                e40TransitionNumber = 10;
                break;
            case ProcessStateAction.Stop:
                if (previous is ProcessJobState.Pausing or ProcessJobState.Paused)
                {
                    e40TransitionNumber = 12;
                }
                else
                {
                    e40TransitionNumber = 11;
                }

                break;
            case ProcessStateAction.Abort:
                if (previous == ProcessJobState.Stopping)
                {
                    e40TransitionNumber = 14;
                }
                else if (previous is ProcessJobState.Pausing or ProcessJobState.Paused)
                {
                    e40TransitionNumber = 15;
                }
                else
                {
                    e40TransitionNumber = 13;
                }

                break;
            case ProcessStateAction.FinishAbort:
                e40TransitionNumber = 16;
                break;
            case ProcessStateAction.FinishStop:
                e40TransitionNumber = 17;
                break;
            case ProcessStateAction.Dequeue:
                e40TransitionNumber = 18;
                break;
        }

        var now = DateTime.Now;
        if (current == ProcessJobState.Processing && job.StartedAt is null)
        {
            job.StartedAt = now;
        }

        if (entity.Action is ProcessStateAction.Finish or ProcessStateAction.FinishAbort
            or ProcessStateAction.FinishStop or ProcessStateAction.Dequeue)
        {
            job.EndedBy = e40TransitionNumber;
            job.EndedAt = now;
        }

        LogHelper.Info(logOwner, $"PJ {job.Id} E40 #{e40TransitionNumber}：{JobNames.PjState(previous)} → {JobNames.PjState(current)}");
        StateChanged?.Invoke(job, current, e40TransitionNumber);
    }

    #endregion

    private sealed class PjEntity(ProcessJob processJob, StateTable states)
    {
        public ProcessJob ProcessJob { get; } = processJob;
        public StateTable States { get; } = states;
        public string Action { get; set; } = string.Empty;

        /// <summary>
        /// 暂停前在哪个状态，恢复时回到它
        /// </summary>
        public int BeforePause { get; set; } = ProcessJobState.Processing;
    }
}
