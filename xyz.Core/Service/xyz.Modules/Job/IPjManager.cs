using xyz.Components.Enums;
using xyz.Shared.Dtos;

namespace xyz.Modules;

/// <summary>
/// PJ 管理（SEMI E40）：PJ 队列、片归属、E40 状态机（转换表、转、按任务进度自动往下转）、PJ 命令。
/// Job 组件建它、管它，只在 Job 组件的锁里用；片归属任意线程可查。
/// </summary>
public interface IPjManager
{
    /// <summary>PJ 转了（含建好的 #1）：PJ、转换号、从哪个状态、到哪个状态（结束了为 null）。Job 组件据此报 EAP、做牵扯别处的事。</summary>
    event Action<ProcessJob, int, PrJobState?, PrJobState?>? Transitioned;

    /// <summary>PJ 队列：没结束的 PJ，按建的先后（含还不归任何 CJ 的）。</summary>
    IReadOnlyList<ProcessJob> Jobs { get; }

    /// <summary>按名字找没结束的 PJ（不分大小写）；没有为 null。</summary>
    ProcessJob? Find(string id);

    /// <summary>这一片现在归哪个 PJ；不归任何没结束的 PJ 返回 null。任意线程可调。</summary>
    string? OwnerOf(Guid wafer);

    /// <summary>建好的 PJ 进队列：片记到它名下，报 #1。</summary>
    void Add(ProcessJob job);

    /// <summary>按 E40 转换表转（表里没有的不转）。转成了返回 true。</summary>
    bool Fire(ProcessJob job, ProcessStateAction trigger);

    /// <summary>PJ 命令（E40 Start / Pause / Resume / Stop / Abort / Cancel）。</summary>
    HandleResult Command(string id, PrJobCommand command);

    /// <summary>整机停止：不归 CJ 的 PJ 都中止（排队的撤掉）。归 CJ 的由 CJ 管理按 CJ 中止。</summary>
    void AbortLoose();

    /// <summary>按任务进度把每个 PJ 该自动转的转一轮；转了返回 true。</summary>
    bool Advance();

    /// <summary>按状态给每个 PJ 的行定调度许可（暂停、停止就体现在这上面）。</summary>
    void UpdatePermissions();
}
