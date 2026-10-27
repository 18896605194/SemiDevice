using xyz.Components.Components;
using xyz.Components.Models;
using xyz.Shared.Dtos;

namespace xyz.Components.Interfaces;

/// <summary>
/// 工位任务的声明和执行，与机械手的搬运交互分开。
/// </summary>
public interface IStationTaskExecutor
{
    /// <summary>
    /// 这个站点支持的任务；Job 建任务表时按它检查。
    /// </summary>
    IReadOnlyList<string> SupportedTasks { get; }

    /// <summary>
    /// 单独询问站内任务能不能起。
    /// </summary>
    HandleResult CheckTask(StationTaskRequest request);

    /// <summary>
    /// 校验并启动站内任务。
    /// </summary>
    ModuleRoutine? StartTask(StationTaskRequest request);
}
