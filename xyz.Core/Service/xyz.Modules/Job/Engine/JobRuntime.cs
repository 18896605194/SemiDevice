using xyz.Common.Log;

namespace xyz.Modules;

/// <summary>
/// Job 的几个上限（取自 JobManager 的 SC / EC，现取现用：EC 在线改了下一拍就生效）。
/// </summary>
internal sealed record JobLimits(
    Func<int> HistoryKeep,
    Func<int> MaxActiveControlJobs,
    Func<int> ControlJobCapacity,
    Func<int> ProcessJobCapacity);

/// <summary>
/// 规则、效果、命令处理共用的运行环境：账本、设备那一面、事件出口、上限，以及"出过执行故障、自动派单暂停"这个闩。
/// 只在 JobManager 的扫描线程上用。
/// </summary>
internal sealed class JobRuntime
{
    private const string LogModule = "Job";

    public JobRuntime(JobBook book, JobEnvironment environment, JobEvents events, JobLimits limits)
    {
        Book = book;
        Environment = environment;
        Events = events;
        Limits = limits;
    }

    public JobBook Book { get; }

    public JobEnvironment Environment { get; }

    public JobEvents Events { get; }

    public JobLimits Limits { get; }

    /// <summary>自动派单为什么暂停（出过执行故障）；没暂停为 null。</summary>
    public JobWait? Hold { get; private set; }

    /// <summary>
    /// 出执行故障：暂停自动派单、保留现场（第一版不缩小范围，任何 Job 都不派新动作，在途的照常做完），等人工确认后恢复。
    /// 已经暂停的不改原因（保留第一个故障）。
    /// </summary>
    public void Halt(JobWait reason)
    {
        if (Hold is not null)
        {
            return;
        }

        Hold = reason;
        Book.Touch();
        LogHelper.Error(LogModule, $"自动派单暂停：{reason.Code} [{string.Join(", ", reason.Args)}]，到现场确认片位、对好账后恢复派单");
    }

    /// <summary>人工确认后解除暂停。</summary>
    public void Release()
    {
        if (Hold is null)
        {
            return;
        }

        Hold = null;
        Book.Touch();
        LogHelper.Info(LogModule, "自动派单已恢复（人工确认过片位）");
    }
}
