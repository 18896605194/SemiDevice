using xyz.Components.Enums;
using xyz.Components.Models;
using xyz.Shared.Dtos;

namespace xyz.Modules;


public interface ICjManager
{
    event Action<ControlJob, ControlJobState>? StateChanged;

    /// <summary>按 CJ ID 保存对应的运行实体。</summary>
    IReadOnlyDictionary<string, CjEntity> Jobs { get; }

    void Add(ControlJob controlJob);

    void Remove(ControlJob controlJob);

    ControlJob? Find(string id);

    ControlJob? FindByCarrier(string carrierId);

    ControlJob? FindByLoadPort(string loadPort);

    HandleResult Start(string id);
    HandleResult Pause(string id);
    HandleResult Resume(string id);
    HandleResult Stop(string id);
    HandleResult Abort(string id);
    HandleResult Cancel(string id);
    HandleResult Deselect(string id);
    HandleResult HeadOfQueue(string id);

}
