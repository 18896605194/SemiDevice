using System.Globalization;

namespace xyz.Modules;

/// <summary>
/// 片在哪：站点和槽，或机械手和手指（取片做完、还没放的时候）。
/// </summary>
public readonly record struct TaskLocation(string Module, int Slot, bool IsArm)
{
    public override string ToString()
    {
        return string.Create(CultureInfo.InvariantCulture, $"{Module}.{Slot:00}");
    }
}

/// <summary>
/// 任务表里的一行：一片从来源槽取出到放回回片槽要做的全部任务，按顺序走（当前任务 = 第一个还没做完的）。
/// 片在哪、做没做成（WaferInfo 的工艺状态）以晶圆账为准，这里只记做到哪。只由任务组件在 Job 的扫描线程上改。
/// </summary>
public sealed class TaskRow
{
    /// <summary>归哪个 PJ（PJ 名）。</summary>
    public required string Owner { get; init; }

    /// <summary>晶圆账的内部标识：片号改了、片挪了都认得；整篮重新 Mapping 会换新标识。</summary>
    public required Guid WaferId { get; init; }

    /// <summary>片号（建 PJ 时账上的，显示用）。</summary>
    public required string WaferName { get; init; }

    public required string SourcePort { get; init; }

    public required int SourceSlot { get; init; }

    /// <summary>回片 LoadPort（建 PJ 时按规则定好）。</summary>
    public required string ReturnPort { get; init; }

    public required int ReturnSlot { get; init; }

    /// <summary>这一片要做的任务，按顺序。</summary>
    public List<WaferTask> Tasks { get; } = [];

    /// <summary>调度能对这一行做什么（Job 组件按 PJ 状态给）。</summary>
    public TaskPermission Permission { get; internal set; }

    /// <summary>所属 PJ 在中止：被中止打断的站内任务记成未执行（片停在原地），不算出错。</summary>
    public bool IsAborting { get; internal set; }

    /// <summary>当前任务：第一个还没做完的；都做完了为 null。</summary>
    public WaferTask? Current => Tasks.FirstOrDefault(task => !task.IsFinished);

    /// <summary>还在来源槽、没投（第一个任务还没动过）。</summary>
    public bool IsWaiting => Tasks.Count > 0 && Tasks[0].State == WaferTaskState.Waiting;

    /// <summary>回完了（最后一个任务完成）。</summary>
    public bool IsReturned => Tasks.Count > 0 && Tasks[^1].State == WaferTaskState.Done;

    /// <summary>在机内：投了（第一个任务动过）、还没回完。</summary>
    public bool IsInMachine => Tasks.Count > 0 && Tasks[0].State is not (WaferTaskState.Waiting or WaferTaskState.Cancelled) && !IsReturned;

    /// <summary>有在跑的任务。</summary>
    public bool HasRunning => Tasks.Any(task => task.State == WaferTaskState.Running);

    /// <summary>有出错等人处理的任务。</summary>
    public bool HasError => Tasks.Any(task => task.State == WaferTaskState.Error);

    /// <summary>路线上的事都做完了：回片那一趟（最后一站）之前的任务都完成（还在回片路上也算）。</summary>
    public bool IsProcessFinished => Tasks.Count > 0 && Tasks.Where(task => task.Step < Tasks[^1].Step).All(task => task.State == WaferTaskState.Done);

    /// <summary>
    /// 片现在该在哪：最后一个做完的取放说了算（取片做完在机械手手上，放片做完在放到的站点）；一个都没做在来源槽。
    /// </summary>
    public TaskLocation ExpectedLocation
    {
        get
        {
            for (int index = Tasks.Count - 1; index >= 0; index--)
            {
                var task = Tasks[index];
                if (task.State != WaferTaskState.Done || !task.IsRobotTask)
                {
                    continue;
                }

                return task.Kind == StationTaskAction.Pick
                    ? new TaskLocation(task.Robot ?? string.Empty, task.Arm, IsArm: true)
                    : new TaskLocation(task.Station ?? string.Empty, task.Slot, IsArm: false);
            }

            return new TaskLocation(SourcePort, SourceSlot, IsArm: false);
        }
    }

    /// <summary>这个取片后面的放片（取放要一起定）；没有返回 null。</summary>
    public WaferTask? NextPlace(WaferTask pick)
    {
        int index = Tasks.IndexOf(pick);
        if (index < 0)
        {
            return null;
        }

        for (int next = index + 1; next < Tasks.Count; next++)
        {
            if (Tasks[next].Kind == StationTaskAction.Place)
            {
                return Tasks[next];
            }
        }

        return null;
    }
}
