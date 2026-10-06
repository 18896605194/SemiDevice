using System.Globalization;
using xyz.Common.Log;
using xyz.Components;
using xyz.Components.Components;
using xyz.Components.Models;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;

namespace xyz.Modules;

public abstract class BaseTaskComponent : ComponentBase
{
    #region 任务表
    private readonly List<TaskRow> _rows = [];
    public IReadOnlyList<TaskRow> Rows => _rows;

    #endregion

    /// <summary>站内任务开始了（Job 组件据此往 EAP 报）。</summary>
    internal event Action<TaskRow, WaferTask>? StationTaskBegan;

    /// <summary>站内任务结束了（成没成看任务状态）。</summary>
    internal event Action<TaskRow, WaferTask>? StationTaskFinished;

    /// <summary>内容版本：任务表每改一次加 1，Job 组件据此决定要不要发布。</summary>
    internal long Version { get; private set; }

    /// <summary>按名字找站点（搬运管理的模块表）；不是站点为 null。</summary>
    protected static ITransferStation? Station(string name)
    {
        var transfers = TransferManager.Current;
        return transfers is not null && transfers.TryGetStation(name, out var station) ? station : null;
    }

    #region 生成任务表

    /// <summary>
    /// 给 PJ 生成任务表（建 PJ 时 Job 组件调）：中间的路线照流程配方快照整个 PJ 算一次（<see cref="BuildRoute"/>），
    /// 每片一行：来源 LoadPort 取片 → 路线 → 回片 LoadPort 放片（回片槽建 PJ 时定好）。用到的配方、站点有问题就回原因，什么都不留。
    /// </summary>
    internal HandleResult? Build(ProcessJob job)
    {
        var sequence = job.Sequence;
        var route = new List<WaferTask>();
        var rejected = BuildRoute(sequence, route);
        if (rejected is not null)
        {
            return rejected;
        }

        // 流程配方第 1 步是来源 LoadPort，最后一步是回片 LoadPort
        foreach (var row in job.Rows)
        {
            rejected = Require(sequence, 1, row.SourcePort, StationTaskAction.Pick)
                ?? Require(sequence, sequence.Steps.Count, row.ReturnPort, StationTaskAction.Place);
            if (rejected is not null)
            {
                return rejected;
            }
        }

        int returnStep = sequence.Steps.Count - 2;
        foreach (var row in job.Rows)
        {
            row.Tasks.Add(new WaferTask { Kind = StationTaskAction.Pick, Step = 0 });
            row.Tasks.AddRange(route.Select(Copy));
            row.Tasks.Add(new WaferTask { Kind = StationTaskAction.Place, Step = returnStep, Stations = [row.ReturnPort], FixedSlot = row.ReturnSlot });
        }

        return null;
    }

    /// <summary>
    /// 中间的路线（平台默认的规则）：流程配方第 2 步到倒数第 2 步，每一站：放片 → 站内任务（<see cref="TasksAt"/>）→ 取片。
    /// 这一站的工艺配方在这里取快照（之后库里改了、删了都不影响）；能去的站点去掉用不了的（<see cref="IsUsable"/>），至少剩一个；
    /// 剩下的每个站点都要支持用到的任务，有一个不支持就整个不建（报出是哪个站点）。机型规则不一样就重写。
    /// </summary>
    protected virtual HandleResult? BuildRoute(SequenceData sequence, List<WaferTask> route)
    {
        var library = ProcessRecipeComponent.Current;
        for (int index = 1; index < sequence.Steps.Count - 1; index++)
        {
            var step = sequence.Steps[index];
            string recipeName = step.Recipe.Trim();
            ProcessRecipeData? recipe = null;
            if (recipeName.Length > 0 && library is not null)
            {
                recipe = library.Find(recipeName);
                if (recipe is null)
                {
                    return HandleResult.Fail(ErrorCodes.JobRecipeNotFound, sequence.Name, recipeName);
                }
            }

            var stations = step.Stations.Where(name => IsUsable(name, recipe)).ToList();
            if (stations.Count == 0)
            {
                return HandleResult.Fail(ErrorCodes.JobStepNoStation, sequence.Name,
                    (index + 1).ToString(CultureInfo.InvariantCulture), recipeName);
            }

            var inside = TasksAt(step);
            foreach (string station in stations)
            {
                foreach (string kind in inside.Prepend(StationTaskAction.Place).Append(StationTaskAction.Pick))
                {
                    var rejected = Require(sequence, index + 1, station, kind);
                    if (rejected is not null)
                    {
                        return rejected;
                    }
                }
            }

            // 路线上第几站（从 0 开始）：放片、站内任务属于这一站，后面的取片算去下一站那一趟
            int routeStep = index - 1;
            route.Add(new WaferTask { Kind = StationTaskAction.Place, Step = routeStep, Stations = stations });
            foreach (string kind in inside)
            {
                bool process = kind == StationTaskAction.Process;
                route.Add(new WaferTask
                {
                    Kind = kind,
                    Step = routeStep,
                    Stations = stations,
                    RecipeName = process ? recipeName : string.Empty,
                    Recipe = process ? recipe : null,
                });
            }

            route.Add(new WaferTask { Kind = StationTaskAction.Pick, Step = routeStep + 1 });
        }

        return null;
    }

    /// <summary>一站里做什么（平台默认）：这一步选了工艺配方就做工艺，没选就只取放。</summary>
    protected virtual IReadOnlyList<string> TasksAt(SequenceStep step)
    {
        return step.Recipe.Trim().Length > 0 ? [StationTaskAction.Process] : [];
    }

    /// <summary>
    /// 站点能用：装了（搬运管理的站点表里有）、启用、有机械手到得了；有工艺配方的腔体还要跑得了这个配方。
    /// </summary>
    protected static bool IsUsable(string name, ProcessRecipeData? recipe)
    {
        var transfers = TransferManager.Current;
        if (transfers is null || !transfers.TryGetStation(name, out var station) || station is not BaseModule module || !module.IsEnabled
            || !transfers.Robots.Any(robot => robot.TryGetStation(name, out _)))
        {
            return false;
        }

        var library = ProcessRecipeComponent.Current;
        return recipe is null || library is null || module is not IProcessStation || library.FindMismatch(recipe, name) is null;
    }

    /// <summary>这个站点支持这个任务；不支持回原因（流程配方名、第几步、站点、任务）。</summary>
    protected static HandleResult? Require(SequenceData sequence, int sequenceStep, string station, string kind)
    {
        var found = Station(station);
        if (found is not null && found.SupportedTasks.Contains(kind, StringComparer.Ordinal))
        {
            return null;
        }

        return HandleResult.Fail(ErrorCodes.JobStationTaskUnsupported, sequence.Name,
            sequenceStep.ToString(CultureInfo.InvariantCulture), station, kind);
    }

    /// <summary>每片一份：路线上的任务照着抄一格新的。</summary>
    private static WaferTask Copy(WaferTask task)
    {
        return new WaferTask
        {
            Kind = task.Kind,
            Step = task.Step,
            Stations = task.Stations,
            FixedSlot = task.FixedSlot,
            RecipeName = task.RecipeName,
            Recipe = task.Recipe,
        };
    }

    #endregion

    #region 跟着 PJ 走（Job 组件调）

    /// <summary>PJ 建好：它的行挂进任务表。</summary>
    internal void Add(ProcessJob job)
    {
        _rows.AddRange(job.Rows);
        Touch();
    }

    /// <summary>
    /// PJ 结束：没做的（等着做的、出错的）记未执行，它的行从任务表拿掉（PJ 自己还留着给界面看）。
    /// 片做没做成看晶圆账（WaferInfo 的工艺状态），这里不另记。
    /// </summary>
    internal void Close(ProcessJob job)
    {
        foreach (var row in job.Rows)
        {
            foreach (var task in row.Tasks)
            {
                if (task.State is WaferTaskState.Waiting or WaferTaskState.Error)
                {
                    task.State = WaferTaskState.Cancelled;
                }
            }

            row.Permission = TaskPermission.None;
            _rows.Remove(row);
        }

        Touch();
    }

    /// <summary>PJ 进中止：它的行标上，之后被中止打断的站内任务记成未执行（不是出错，片就停在站点上）。</summary>
    internal void Abort(ProcessJob job)
    {
        foreach (var row in job.Rows)
        {
            row.IsAborting = true;
        }

        Touch();
    }

    #endregion

    #region 改任务状态（调度引擎执行任务时调）

    /// <summary>任务开始了：进行中，记下在哪个站点、哪一槽（取片是从哪取、放片是放到哪），取放还记下机械手。</summary>
    public void Start(TaskRow row, WaferTask task, string station, int slot, string? robot = null)
    {
        task.State = WaferTaskState.Running;
        task.Station = station;
        task.Slot = slot;
        task.Robot = robot;
        task.Code = string.Empty;
        task.Args = [];
        Touch();
        if (!task.IsRobotTask)
        {
            StationTaskBegan?.Invoke(row, task);
        }
    }

    /// <summary>任务做完了；取放记下用的哪只手（取片做完片就在这只手上）。</summary>
    public void Done(TaskRow row, WaferTask task, int arm = 0)
    {
        SetDone(task, task.Robot, arm);
        Touch();
        if (!task.IsRobotTask)
        {
            StationTaskFinished?.Invoke(row, task);
        }
    }

    /// <summary>任务出错：停在这一格等人处理（重做或标记完成），这一行后面的任务都等着。</summary>
    public void Fail(TaskRow row, WaferTask task, string code, IReadOnlyList<string> args)
    {
        SetError(row, task, code, args);
        Touch();
        if (!task.IsRobotTask)
        {
            StationTaskFinished?.Invoke(row, task);
        }
    }

    /// <summary>退回等着做：没做成、也没碰到片（搬运没动手就失败、被撤），下一拍按片现在在哪重新派。</summary>
    public void Reset(WaferTask task)
    {
        SetWaiting(task);
        Touch();
    }

    /// <summary>不做了：PJ 中止时被打断的站内任务记成未执行（片停在站点上，不算出错）。</summary>
    public void Cancel(TaskRow row, WaferTask task)
    {
        task.State = WaferTaskState.Cancelled;
        Touch();
        if (!task.IsRobotTask)
        {
            StationTaskFinished?.Invoke(row, task);
        }
    }

    #endregion

    #region 核对片位

    /// <summary>
    /// 核对片位（每拍）：没在跑、没出错、还没走完的行，片要正好在账上它该在的位置；不在（被人改了账、载具被拿走、
    /// 整篮重新 Mapping 换了标识）就把当前任务记成出错（片不在该在的地方），停住等人处理，别的行照常走。
    /// </summary>
    internal void CheckPositions()
    {
        var ledger = WaferManager.Current;
        if (ledger is null)
        {
            return;
        }

        foreach (var row in _rows)
        {
            var current = row.Current;
            if (current is null || row.HasRunning || row.HasError)
            {
                continue;
            }

            var expected = row.ExpectedLocation;
            var found = ledger.FindById(row.WaferId);
            if (found is not null && string.Equals(found.Module, expected.Module, StringComparison.OrdinalIgnoreCase) && found.Slot == expected.Slot)
            {
                continue;
            }

            SetError(row, current, ErrorCodes.JobWaferMoved, [row.WaferName, expected.ToString()]);
            Touch();
        }
    }

    #endregion

    #region 人处理出错的任务（只给本地界面）

    /// <summary>重做：出错的任务退回等着做，调度按片现在在哪重新派。</summary>
    internal HandleResult Retry(ProcessJob job, int slot, int index)
    {
        var rejected = FindError(job, slot, index, out var row, out var task);
        if (rejected is not null)
        {
            return rejected;
        }

        SetWaiting(task);
        Touch();
        LogHelper.Info(Name, $"{job.Id} {row.WaferName} 第 {index + 1} 个任务（{task.Kind}）人工重做");
        return HandleResult.Success(job.Id);
    }

    /// <summary>
    /// 标记完成：人已经把这一步做完了。站内任务直接算完成；取放要片在账上正好在这一步做完该在的地方——
    /// 取片：片在机械手手上（或已经在后面那个放片能去的站点上，连放片一起算完成）；放片：片在这一步能去的站点上。
    /// </summary>
    internal HandleResult Complete(ProcessJob job, int slot, int index)
    {
        var rejected = FindError(job, slot, index, out var row, out var task);
        if (rejected is not null)
        {
            return rejected;
        }

        if (task.IsRobotTask)
        {
            var found = WaferManager.Current?.FindById(row.WaferId);
            string actual = found is null ? "-" : new TaskLocation(found.Module, found.Slot, IsArm: false).ToString();
            var place = task.Kind == StationTaskAction.Pick ? row.NextPlace(task) : task;
            if (found is not null && task.Kind == StationTaskAction.Pick && TransferManager.Current?.TryGetRobot(found.Module, out _) == true)
            {
                SetDone(task, found.Module, found.Slot);
            }
            else if (found is not null && place is not null && Fits(place, found.Module, found.Slot))
            {
                if (!ReferenceEquals(place, task))
                {
                    SetDone(task, null, 0);
                }

                SetDone(place, place.Robot, place.Arm);
                place.Station = found.Module;
                place.Slot = found.Slot;
            }
            else
            {
                return HandleResult.Fail(ErrorCodes.JobTaskPositionMismatch, row.WaferName, task.Kind, actual);
            }
        }
        else
        {
            SetDone(task, task.Robot, task.Arm);
        }

        Touch();
        LogHelper.Info(Name, $"{job.Id} {row.WaferName} 第 {index + 1} 个任务（{task.Kind}）人工标记完成");
        return HandleResult.Success(job.Id);
    }

    /// <summary>按来源槽找这一片、按序号找任务，要是出错的才能处理。</summary>
    private static HandleResult? FindError(ProcessJob job, int slot, int index, out TaskRow row, out WaferTask task)
    {
        row = null!;
        task = null!;
        string number = (index + 1).ToString(CultureInfo.InvariantCulture);
        var found = job.Rows.FirstOrDefault(item => item.SourceSlot == slot);
        if (found is null || index < 0 || index >= found.Tasks.Count)
        {
            return HandleResult.Fail(ErrorCodes.JobTaskNotFound, job.Id, slot.ToString(CultureInfo.InvariantCulture), number);
        }

        row = found;
        task = found.Tasks[index];
        return task.State == WaferTaskState.Error
            ? null
            : HandleResult.Fail(ErrorCodes.JobTaskNotError, job.Id, found.WaferName, number);
    }

    /// <summary>片在的位置是这个放片能去的地方（回片要正好是回片槽）。</summary>
    private static bool Fits(WaferTask place, string module, int slot)
    {
        if (place.FixedSlot > 0)
        {
            return place.Stations.Count > 0 && string.Equals(place.Stations[0], module, StringComparison.OrdinalIgnoreCase) && place.FixedSlot == slot;
        }

        return place.Stations.Contains(module, StringComparer.OrdinalIgnoreCase);
    }

    #endregion

    private static void SetDone(WaferTask task, string? robot, int arm)
    {
        task.State = WaferTaskState.Done;
        task.Robot = robot;
        task.Arm = arm;
        task.Code = string.Empty;
        task.Args = [];
    }

    /// <summary>退回等着做：挑过的站点、机械手都清掉，下次派按片现在在哪重新算。</summary>
    private static void SetWaiting(WaferTask task)
    {
        task.State = WaferTaskState.Waiting;
        task.Station = null;
        task.Slot = 0;
        task.Robot = null;
        task.Arm = 0;
        task.Code = string.Empty;
        task.Args = [];
    }

    private void SetError(TaskRow row, WaferTask task, string code, IReadOnlyList<string> args)
    {
        task.State = WaferTaskState.Error;
        task.Code = code;
        task.Args = args.ToList();
        LogHelper.Warn(Name, $"{row.Owner} {row.WaferName} 的任务 {task.Kind} 出错，停住等人处理：{code} [{string.Join(", ", args)}]");
    }

    private void Touch()
    {
        Version++;
    }
}
