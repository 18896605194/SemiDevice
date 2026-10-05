using System.Globalization;
using xyz.Components;
using xyz.Components.Attributes;
using xyz.Components.Enums;
using xyz.Shared.Errors;

namespace xyz.Modules;

/// <summary>
/// Job 调度（sc.xml Job 节点下的 Scheduler 子节点，跟着 JobManager 的扫描线程）：只做计算——拿 Job 进度和设备视图，
/// 算出这一拍起哪些工艺、下哪些搬运单，没排上的片说清在等什么。不碰设备、不改 Job，提交由 JobManager 做。
/// 默认策略：先起工艺；再给做完、占着站点的片找下一站（先腾地方）；最后从载具投新片。同一步的站点按 sc.xml 先后挑第一个空的，
/// 目标空着、还没被锁才派（放不下就不取）。不认机型名字：机型要别的策略，写个子类重写这里的方法，sc.xml 里把 Type 换掉。
/// </summary>
[Component(description: "Job 调度：按 Job 进度和设备状态算这一拍起哪些工艺、下哪些搬运单")]
public class SchedulerComponent : ComponentBase
{
    #region EC

    [VariableMark(VariableType.EC, ValueFormat.Int, min: "0", max: "100", @default: "0",
        description: "机内最多同时几片（投新片前查）；0 = 不限，靠目标站点空不空自然限住")]
    public int MaxWafersInMachine
    {
        get { return GetEcInt(nameof(MaxWafersInMachine)); }
        set { SetEcInt(nameof(MaxWafersInMachine), value); }
    }

    #endregion

    /// <summary>
    /// 算这一拍的计划。jobs 按优先级排好（CJ 队列先后、PJ 在 CJ 里的先后），片按投片顺序。
    /// </summary>
    public virtual JobPlan Plan(IReadOnlyList<ProcessJob> jobs, IJobPlanEnvironment environment)
    {
        var plan = new JobPlan();
        var claimedSlots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var claimedRobots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        PlanProcesses(jobs, environment, plan);

        // 先腾地方：做完的片占着站点，不挪走后面的片进不来
        foreach (var job in jobs)
        {
            if ((JobGates.Of(job) & JobDispatch.Advance) == 0)
            {
                continue;
            }

            foreach (var wafer in job.Wafers)
            {
                if (wafer.Phase == JobWaferPhase.Processed)
                {
                    PlanMove(job, wafer, environment, plan, claimedSlots, claimedRobots);
                }
            }
        }

        // 再投新片
        int limit = MaxWafersInMachine;
        int inMachine = jobs.Sum(job => job.Wafers.Count(wafer => wafer.IsInMachine));
        foreach (var job in jobs)
        {
            if ((JobGates.Of(job) & JobDispatch.Feed) == 0)
            {
                continue;
            }

            foreach (var wafer in job.Wafers)
            {
                if (wafer.Phase != JobWaferPhase.Waiting)
                {
                    continue;
                }

                if (limit > 0 && inMachine >= limit)
                {
                    plan.Waits[wafer.Id] = JobWait.Of(ErrorCodes.JobWaitLimit, limit.ToString(CultureInfo.InvariantCulture));
                    continue;
                }

                if (PlanMove(job, wafer, environment, plan, claimedSlots, claimedRobots))
                {
                    inMachine++;
                }
            }
        }

        return plan;
    }

    /// <summary>
    /// 到了要加工站点的片起工艺；起不了的记下原因（站点忙、配方对不上……）。
    /// </summary>
    protected virtual void PlanProcesses(IReadOnlyList<ProcessJob> jobs, IJobPlanEnvironment environment, JobPlan plan)
    {
        foreach (var job in jobs)
        {
            if ((JobGates.Of(job) & JobDispatch.Advance) == 0)
            {
                continue;
            }

            foreach (var wafer in job.Wafers)
            {
                string? station = wafer.Station;
                if (wafer.Phase != JobWaferPhase.Arrived || station is null || wafer.Step < 0 || wafer.Step >= job.Recipe.Steps.Count)
                {
                    continue;
                }

                var step = job.Recipe.Steps[wafer.Step];
                var request = new ProcessRequest
                {
                    Origin = ProcessOrigin.Job,
                    Owner = job.Id,
                    WaferId = wafer.Id,
                    Slot = wafer.Slot,
                    Step = wafer.Step,
                    RecipeName = step.RecipeName,
                    Recipe = step.Recipe,
                };
                var rejection = environment.CheckProcess(station, request);
                if (rejection is null)
                {
                    plan.Processes.Add(new PlannedProcess(job, wafer, station, wafer.Slot, step));
                }
                else
                {
                    plan.Waits[wafer.Id] = new JobWait(rejection.Code, rejection.Args);
                }
            }
        }
    }

    /// <summary>
    /// 给一片排下一趟搬运：路线上的下一站（加工失败的直接回片），或回片槽。排上返回 true；排不上记下在等什么。
    /// 源站点要能服务；目标要空着、没被锁、这一拍没被别的片挑走；要有一台闲着、两边都到得了、有空手的机械手。
    /// </summary>
    protected virtual bool PlanMove(ProcessJob job, JobWafer wafer, IJobPlanEnvironment environment, JobPlan plan,
        ISet<string> claimedSlots, ISet<string> claimedRobots)
    {
        bool fromCarrier = wafer.Phase == JobWaferPhase.Waiting;
        string? source = fromCarrier ? wafer.SourcePort : wafer.Station;
        int sourceSlot = fromCarrier ? wafer.SourceSlot : wafer.Slot;
        if (source is null)
        {
            return false;
        }

        if (!environment.IsStationReady(source))
        {
            plan.Waits[wafer.Id] = JobWait.Of(ErrorCodes.JobWaitStation, source);
            return false;
        }

        int stepCount = job.Recipe.Steps.Count;
        int next = wafer.Failed ? stepCount : wafer.Step + 1;
        var movesWithoutRobot = new List<string>();

        if (next >= stepCount)
        {
            // 回片：回片槽是建 PJ 时就定好的
            string port = wafer.ReturnPort;
            int slot = wafer.ReturnSlot;
            if (!environment.IsStationReady(port))
            {
                plan.Waits[wafer.Id] = JobWait.Of(ErrorCodes.JobWaitStation, port);
                return false;
            }

            if (!environment.IsSlotFree(port, slot) || claimedSlots.Contains(TransferManager.SlotKey(port, slot)))
            {
                plan.Waits[wafer.Id] = JobWait.Of(ErrorCodes.JobWaitReturnSlot, port, slot.ToString(CultureInfo.InvariantCulture));
                return false;
            }

            return TryClaim(job, wafer, source, sourceSlot, port, slot, stepCount, environment, plan, claimedSlots, claimedRobots, movesWithoutRobot)
                || Wait(plan, wafer, ErrorCodes.JobWaitRobot, movesWithoutRobot);
        }

        var step = job.Recipe.Steps[next];
        foreach (string station in step.Stations)
        {
            if (!environment.IsStationReady(station))
            {
                continue;
            }

            int slot = FreeSlot(station, environment, claimedSlots);
            if (slot == 0)
            {
                continue;
            }

            if (TryClaim(job, wafer, source, sourceSlot, station, slot, next, environment, plan, claimedSlots, claimedRobots, movesWithoutRobot))
            {
                return true;
            }
        }

        return movesWithoutRobot.Count > 0
            ? Wait(plan, wafer, ErrorCodes.JobWaitRobot, movesWithoutRobot)
            : Wait(plan, wafer, ErrorCodes.JobWaitStation, step.Stations);
    }

    /// <summary>站点上第一个空着、没被锁、这一拍没被挑走的槽；没有返回 0。</summary>
    protected static int FreeSlot(string station, IJobPlanEnvironment environment, ISet<string> claimedSlots)
    {
        int count = environment.SlotCount(station);
        for (int slot = 1; slot <= count; slot++)
        {
            if (environment.IsSlotFree(station, slot) && !claimedSlots.Contains(TransferManager.SlotKey(station, slot)))
            {
                return slot;
            }
        }

        return 0;
    }

    /// <summary>找机械手；找到就记进计划、占住目标槽和机械手。没找到把这一趟（源→目标）记下来（等待原因用）。</summary>
    private static bool TryClaim(ProcessJob job, JobWafer wafer, string source, int sourceSlot, string target, int targetSlot, int targetStep,
        IJobPlanEnvironment environment, JobPlan plan, ISet<string> claimedSlots, ISet<string> claimedRobots, List<string> movesWithoutRobot)
    {
        var exclude = new HashSet<string>(claimedRobots, StringComparer.OrdinalIgnoreCase);
        string? robot = environment.RobotFor(source, target, exclude);
        if (robot is null)
        {
            movesWithoutRobot.Add($"{source}→{target}");
            return false;
        }

        plan.Transfers.Add(new PlannedTransfer(job, wafer, source, sourceSlot, target, targetSlot, targetStep, robot));
        claimedSlots.Add(TransferManager.SlotKey(target, targetSlot));
        claimedRobots.Add(robot);
        plan.Waits.Remove(wafer.Id);
        return true;
    }

    private static bool Wait(JobPlan plan, JobWafer wafer, string code, IEnumerable<string> what)
    {
        plan.Waits[wafer.Id] = JobWait.Of(code, string.Join(", ", what.Distinct(StringComparer.OrdinalIgnoreCase)));
        return false;
    }
}
