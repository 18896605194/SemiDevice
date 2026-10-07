using xyz.Components.Enums;
using xyz.Shared.Dtos;

namespace xyz.Modules;

public interface ICjManager
{
    event Action<ControlJob, ControlJobState, int>? StateChanged;

    IReadOnlyList<ControlJob> ControlJobs { get; }

    void Add(ControlJob controlJob);
    void Remove(ControlJob controlJob);
    ControlJob? Get(string id);
    ControlJob? FindByCarrier(string carrierId);
    ControlJob? FindByLoadPort(string loadPort);

    HandleResult Queue(ControlJob controlJob);
    HandleResult Select(ControlJob controlJob);
    HandleResult Activate(ControlJob controlJob);
    HandleResult Deselect(ControlJob controlJob);
    HandleResult Pause(ControlJob controlJob);
    HandleResult Resume(ControlJob controlJob);
    HandleResult Complete(ControlJob controlJob);
    HandleResult Abort(ControlJob controlJob);
    HandleResult FinishAbort(ControlJob controlJob);
    HandleResult Rollback(ControlJob controlJob);
    HandleResult WaitForStart(ControlJob controlJob);
    HandleResult FinishStop(ControlJob controlJob);
    HandleResult Dequeue(ControlJob controlJob);
    HandleResult Delete(ControlJob controlJob);
}
