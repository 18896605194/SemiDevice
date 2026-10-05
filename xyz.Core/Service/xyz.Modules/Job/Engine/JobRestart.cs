using xyz.Shared.Dtos;

namespace xyz.Modules;

/// <summary>
/// 重启以后的 Job：不接着跑——重启前在途的搬运、工艺做没做完说不准，接着派只会把错放大。
/// 上次没删的 CJ 一律记成中止结束（E94 #12，标着"设备重启"），跟上次的历史一起放进历史；
/// 机内的片由人确认片位后全部回片，再重新建 Job（行业里也是这么收场：回片、记异常结束、由 MES / 工程师决定返工还是重做）。
/// </summary>
internal static class JobRestart
{
    /// <summary>
    /// 读回来的上一份全貌 → 本次的历史；返回这次被记成中止的 CJ（日志用）。没有上一份返回空。
    /// </summary>
    public static IReadOnlyList<ControlJobDto> CloseOut(JobListDto? last, JobBook book, int keep, DateTime now)
    {
        if (last is null)
        {
            return [];
        }

        var interrupted = new List<ControlJobDto>();
        var restored = new List<ControlJobDto>();
        foreach (var job in last.ControlJobs)
        {
            if (job.State != (int)CtrlJobState.Completed)
            {
                job.State = (int)CtrlJobState.Completed;
                job.CompletedBy = E94Transitions.Aborted;
                job.Ending = CtrlJobEnding.Abort.ToString();
                job.CompletedAt = now;
                job.Restarted = true;
                interrupted.Add(job);
            }

            // 完成了还没删的（载具还在）也一样：重启后从队列里拿掉，进历史
            if (job.EndedBy == 0)
            {
                job.EndedBy = E94Transitions.Deleted;
                job.EndedAt = now;
            }

            job.NeedsRecovery = false;
            restored.Add(job);
        }

        restored.AddRange(last.History);
        book.Restore(restored, keep);
        return interrupted;
    }
}
