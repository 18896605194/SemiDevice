using xyz.Components.Enums;
using xyz.Components.Models;
using xyz.Shared.Dtos;

namespace xyz.Components.Interfaces;

public interface IJobManager
{
    /// <summary>创建独立 PJ：来源口、名字、槽位、流程配方和批次号；建好后等待 CJ 关联。Host 未指定来源口时按 carrierId 找载具。</summary>
    Task<HandleResult> CreateProcessJobAsync(string? loadPort, string pjName, IReadOnlyList<int> slots, string sequence, string? lotId, string? carrierId = null);

    /// <summary>创建 CJ，按名称集合关联已有 PJ。</summary>
    Task<HandleResult> CreateControlJobAsync(string? loadPort, IReadOnlyList<string> processJobs, string? cjName = null, string? lotId = null);

    /// <summary>使用已经创建的 PJ 创建 Job；内部创建 CJ 并按名称列表关联，不再创建 PJ。</summary>
    Task<HandleResult> CreateJobAsync(string? loadPort, IReadOnlyList<string> processJobs, string? cjName = null, string? lotId = null);

    Task<HandleResult> StartProcessJobAsync(string id);
    Task<HandleResult> PauseProcessJobAsync(string id);
    Task<HandleResult> ResumeProcessJobAsync(string id);
    Task<HandleResult> StopProcessJobAsync(string id);
    Task<HandleResult> AbortProcessJobAsync(string id);
    Task<HandleResult> CancelProcessJobAsync(string id);

    Task<HandleResult> StartControlJobAsync(string id);
    Task<HandleResult> PauseControlJobAsync(string id);
    Task<HandleResult> ResumeControlJobAsync(string id);
    Task<HandleResult> StopControlJobAsync(string id, ControlJobAction action = ControlJobAction.RemoveJobs);
    Task<HandleResult> AbortControlJobAsync(string id, ControlJobAction action = ControlJobAction.RemoveJobs);
    Task<HandleResult> CancelControlJobAsync(string id, ControlJobAction action = ControlJobAction.RemoveJobs);

    /// <summary>EAP 命令适配入口，内部调用对应的 CJ 方法。</summary>
    Task<HandleResult> ExecuteControlJobCommandAsync(string id, ControlJobCommand command, ControlJobAction action);

    /// <summary>EAP 命令适配入口，内部调用对应的 PJ 方法。</summary>
    Task<HandleResult> ExecuteProcessJobCommandAsync(string id, ProcessJobCommand command);

    ControlJobDto? FindControlJobByCarrier(string carrierId);
    IReadOnlyList<ProcessJobDto> FindProcessJobsByCarrier(string carrierId);

    /// <summary>根据当前运行对象生成独立的 CJ、PJ 查询结果。</summary>
    JobListDto Snapshot { get; }

    IE40Callback? E40Callback { get; set; }

    IE94Callback? E94Callback { get; set; }

}
