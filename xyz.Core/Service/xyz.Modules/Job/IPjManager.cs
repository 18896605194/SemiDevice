using xyz.Components.Enums;
using xyz.Shared.Dtos;

namespace xyz.Modules;

/// <summary>PJ 登记、晶圆归属和独立状态机；在 JobManager 锁内使用，OwnerOf 可跨线程查询。</summary>
public interface IPjManager
{
    event Action<ProcessJob, ProcessJobState, int>? StateChanged;

    IReadOnlyList<ProcessJob> ProcessJobs { get; }

    void Add(ProcessJob processJob);
    void Remove(ProcessJob processJob);
    ProcessJob? Get(string id);
    string? OwnerOf(Guid wafer);

    /// <summary>料到了才定片的 PJ：任务行生成以后登记这些片归它（Queue 的时候还没有片）。</summary>
    void RegisterWafers(ProcessJob processJob);

    HandleResult Queue(ProcessJob processJob);
    HandleResult Setup(ProcessJob processJob);
    HandleResult WaitForStart(ProcessJob processJob);
    HandleResult Activate(ProcessJob processJob);
    HandleResult Start(ProcessJob processJob);
    HandleResult Complete(ProcessJob processJob);
    HandleResult Finish(ProcessJob processJob);
    HandleResult Pause(ProcessJob processJob);
    HandleResult FinishPause(ProcessJob processJob);
    HandleResult Resume(ProcessJob processJob);
    HandleResult Stop(ProcessJob processJob);
    HandleResult FinishStop(ProcessJob processJob);
    HandleResult Abort(ProcessJob processJob);
    HandleResult FinishAbort(ProcessJob processJob);
    HandleResult Dequeue(ProcessJob processJob);
}
