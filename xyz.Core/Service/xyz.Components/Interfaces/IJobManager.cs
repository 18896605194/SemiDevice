using xyz.Components.Enums;
using xyz.Components.Models;
using xyz.Shared.Dtos;

namespace xyz.Components.Interfaces;

/// <summary>
/// job 管理
/// </summary>
public interface IJobManager
{
    /// <summary>
    /// 创建独立 PJ
    /// </summary>
    /// <param name="loadPort"></param>
    /// <param name="pjName"></param>
    /// <param name="slots"></param>
    /// <param name="sequence"></param>
    /// <param name="lotId"></param>
    /// <param name="carrierId"></param>
    /// <returns></returns>
    Task<HandleResult> CreateProcessJobAsync(string? loadPort, string pjName, IReadOnlyList<int> slots, string sequence, string? lotId, string? carrierId = null);

    /// <summary>
    /// 创建 CJ
    /// </summary>
    /// <param name="loadPort"></param>
    /// <param name="processJobs"></param>
    /// <param name="cjName"></param>
    /// <param name="lotId"></param>
    /// <returns></returns>
    Task<HandleResult> CreateControlJobAsync(string? loadPort, IReadOnlyList<string> processJobs, string? cjName = null, string? lotId = null);

    /// <summary>
    /// 使用已经创建的 PJ 创建 Job；内部创建 CJ 并按名称列表关联，不再创建 PJ
    /// </summary>
    /// <param name="loadPort"></param>
    /// <param name="processJobs"></param>
    /// <param name="cjName"></param>
    /// <param name="lotId"></param>
    /// <returns></returns>
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

    /// <summary>
    /// EAP 命令适配入口，内部调用对应的 CJ 方法。
    /// </summary>
    /// <param name="id"></param>
    /// <param name="command"></param>
    /// <param name="action"></param>
    /// <returns></returns>
    Task<HandleResult> ExecuteControlJobCommandAsync(string id, ControlJobCommand command, ControlJobAction action);

    /// <summary>
    /// EAP 命令适配入口，内部调用对应的 PJ 方法。
    /// </summary>
    /// <param name="id"></param>
    /// <param name="command"></param>
    /// <returns></returns>
    Task<HandleResult> ExecuteProcessJobCommandAsync(string id, ProcessJobCommand command);

    ControlJobDto? FindControlJobByCarrier(string carrierId);
    IReadOnlyList<ProcessJobDto> FindProcessJobsByCarrier(string carrierId);

    /// <summary>
    /// cj pj 的快照
    /// </summary>
    JobListDto Snapshot { get; }

    IE40Callback? E40Callback { get; set; }

    IE94Callback? E94Callback { get; set; }


    /// <summary>
    /// 这片归哪个 Job（片号），不归 Job 是 null
    /// </summary>
    string? OwnerOf(Guid waferId);

    /// <summary>
    /// 全部 Job 走中止（整机停止用）
    /// </summary>
    Task<HandleResult> AbortAllAsync();

    /// <summary>
    /// 有没有在跑的 CJ（执行中、暂停、中止中）
    /// </summary>
    bool HasRunningJob { get; }

    /// <summary>
    /// Job 记录落的库（machine.xml 的 Database 节点名），历史页查它
    /// </summary>
    string Database { get; }

    /// <summary>
    /// Job 历史页一次最多返回多少个 CJ
    /// </summary>
    int HistoryQueryMaxCount { get; }

    /// <summary>
    /// 工艺没做成的那一片重做这个任务
    /// </summary>
    Task<HandleResult> RetryTaskAsync(string processJob, int slot, int task);

    /// <summary>
    /// 工艺没做成的那一片这个任务标记完成
    /// </summary>
    Task<HandleResult> CompleteTaskAsync(string processJob, int slot, int task);
}
