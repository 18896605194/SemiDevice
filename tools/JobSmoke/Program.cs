using System.Collections.Concurrent;
using xyz.Components.Interfaces;
using xyz.Components.Models;
using xyz.Components;
using xyz.Components.Components;
using xyz.Components.Enums;
using xyz.Configs.Models;
using xyz.Database.DbProvider;
using xyz.Database.Jobs;
using xyz.Drivers.Communication;
using xyz.Drivers.Loadport;
using xyz.Drivers.Loadport.FCD;
using xyz.Drivers.Robot;
using xyz.Drivers.Robot.Reje;
using xyz.Modules;
using xyz.Modules.Enums;
using xyz.Service.Jobs;
using xyz.Service.Systems;
using xyz.Service.Transfers;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Shared.Rpc;
using xyz.Tools;

// Job 冒烟：搬运管理（受理时的各项检查、两次操作抢一个槽、取片确认、任务顺序执行、没动手失败放锁、动过手失败留锁、中止收尾）
// 和 Job（SEMI E94 CJ / E40 PJ）：建 Job 的各项检查和整个不留（含站点不支持要用的任务、一站的站点都用不了、工艺配方不在库里）、任务表（一片一行：取片、放片、工艺……）、
// 一篮两个 Sequence 跑完（两步加工、转换号顺序、重发被正常检查拦住、建 CJ 被拒撤掉已建的 PJ）、配方快照、回到别的 LoadPort、PJ 暂停 / 恢复、CJ 暂停（只不启动新 PJ）、
// CJ 停止、PJ 中止、工艺出错停住等人（别的片照常跑）后重做 / 标记完成、片不在该在的地方、搬运动过手才失败、Host 先建 PJ 再建 CJ（EAP 按载具号找 CJ、PJ）、整机停止走 Job 中止、
// 服务层错误码、重启收场。
// 不连设备：机械手、LoadPort、腔体都是假的（动作按扫描拍数做完），扫描由测试一拍一拍推；配方文件写在临时目录，跑完删掉。
var checks = 0;
void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException("FAIL: " + message);
    }

    checks++;
}

// 任务表里的几种情况（推送里的一片：一行任务）
static bool IsProcessing(JobWaferDto wafer) => wafer.Tasks.Any(task => task.Kind == StationTaskAction.Process && task.State == "Running");
static bool IsWaiting(JobWaferDto wafer) => wafer.Tasks.Count > 0 && wafer.Tasks[0].State == "Waiting";
static bool IsReturned(JobWaferDto wafer) => wafer.Tasks.Count > 0 && wafer.Tasks[^1].State == "Done";
static JobTaskDto? ErrorOf(JobWaferDto wafer) => wafer.Tasks.FirstOrDefault(task => task.State == "Error");
static bool IsDone(JobWaferDto wafer) => wafer.Tasks.All(task => task.State == "Done");
static bool IsNotRun(JobWaferDto wafer) => wafer.Tasks.All(task => task.State == "Cancelled");

// EC 只放在本进程内存里：不读也不写 ec.xml。
var ec = new EcComponent();
string folder = Path.Combine(Path.GetTempPath(), "xyz-job-smoke-" + Guid.NewGuid().ToString("N"));
string jobDb = folder + ".db";
try
{
    // 0. 装一台假机器：两个 LoadPort、两个腔体、一台两指机械手（四个站点都到得了），再加一个机械手到不了的腔体。
    var ledger = new WaferManagerComponent();
    var lp1 = new SmokePort("LP1");
    var lp2 = new SmokePort("LP2");
    var pm1 = new SmokeChamber("PM1");
    var pm2 = new SmokeChamber("PM2");
    var pm9 = new SmokeChamber("PM9");
    var robot = new SmokeRobot("Robot1", "LP1", "LP2", "PM1", "PM2");
    BaseModule[] modules = [lp1, lp2, robot, pm1, pm2, pm9];
    foreach (var module in modules)
    {
        Check(module.Open(), $"{module.Name} 应能打开");
    }

    robot.NoteState(ModuleState.Idle);
    foreach (var chamber in new[] { pm1, pm2, pm9 })
    {
        chamber.NoteState(ModuleState.Idle);
        chamber.Online();
    }

    var transfers = new SmokeTransfers();
    Probe.Name(transfers, "Transfer");
    transfers.Bind(modules);
    transfers.StationWaitTimeoutMs = 1000;

    // 配方库：用真 sc.xml 的 Sequence、ProcessRecipe 节点（字段表照 sc），目录换成临时目录
    var scConfig = XmlHelper.Deserialize<ScConfig>(Path.Combine(AppContext.BaseDirectory, "Config", "sc.xml"));
    Check(scConfig is not null, "sc.xml 解析失败");
    ModuleConfig Node(string name)
    {
        var node = scConfig!.Modules.First(setting => string.Equals(setting.Name, name, StringComparison.OrdinalIgnoreCase));
        node.Values.First(value => string.Equals(value.Name, "Folder", StringComparison.OrdinalIgnoreCase)).Value = Path.Combine(folder, name);
        return node;
    }

    var jobNode = scConfig!.Modules.FirstOrDefault(setting => string.Equals(setting.Name, "Job", StringComparison.OrdinalIgnoreCase));
    Check(jobNode is not null && jobNode.Type == typeof(JobManager).FullName
          && jobNode.Children.Any(child => child.Name == "Task" && child.Type == "xyz._35021.Module.Job.TaskComponent")
          && jobNode.Children.Any(child => child.Name == "Scheduler" && child.Type == typeof(SchedulerComponent).FullName),
        "sc.xml 里应有 Job 节点（JobManager），下面挂 Task（35021 的任务组件）和 Scheduler（SchedulerComponent）");
    Check(jobNode!.Values.Any(value => value.Name == nameof(JobManager.ProcessJobAutoStart) && value.Value == "True")
          && jobNode.Values.Any(value => value.Name == nameof(JobManager.ControlJobAutoStart) && value.Value == "False"),
        "sc.xml 配置 PJ 自动开始、CJ 等待 Start，创建请求不传启动方式");

    var libraryRoots = ComponentLoader.Load([Node("Sequence"), Node("ProcessRecipe")]);
    var sequences = libraryRoots.OfType<SequenceComponent>().Single();
    var recipes = libraryRoots.OfType<ProcessRecipeComponent>().Single();
    recipes.Bind(modules);
    ModuleConfig Module(string name)
    {
        return new ModuleConfig { Name = name, Type = "Probe" };
    }

    ModuleConfig Group(string name, params ModuleConfig[] children)
    {
        return new ModuleConfig { Name = name, Children = children.ToList() };
    }

    sequences.Bind(
        [
            Group("LoadPort", Module("LP1"), Module("LP2")),
            Group("Robot", Module("Robot1")),
            Group("Chamber", Module("PM1"), Module("PM2")),
        ],
        modules);
    Check(recipes.Create(1, "R1", "Smoke").IsOk && recipes.Create(2, "R2", "Smoke").IsOk, "建两个工艺配方");

    SequenceStep Step(string group, string recipe, params string[] stations)
    {
        return new SequenceStep(group, stations, recipe);
    }

    void Sequence(int index, string name, params SequenceStep[] steps)
    {
        Check(sequences.Create(index, name, "Smoke").IsOk, $"建流程配方 {name}");
        var saved = sequences.Save(index, 1, string.Empty, steps, "Smoke");
        Check(saved.IsOk, $"存流程配方 {name}：{saved.Code} [{string.Join(",", saved.Args)}]");
    }

    Sequence(1, "SEQ_A", Step("LoadPort", "", "LP1"), Step("Chamber", "R1", "PM1", "PM2"), Step("LoadPort", "", "LP1"));
    Sequence(2, "SEQ_B", Step("LoadPort", "", "LP1"), Step("Chamber", "R1", "PM1"), Step("Chamber", "R2", "PM2"), Step("LoadPort", "", "LP1"));
    Sequence(3, "SEQ_C", Step("LoadPort", "", "LP1"), Step("Chamber", "R1", "PM1", "PM2"), Step("LoadPort", "", "LP2"));
    Sequence(4, "SEQ_D", Step("LoadPort", "", "LP2"), Step("Chamber", "R1", "PM1"), Step("LoadPort", "", "LP2"));

    // Job 节点下没配 Task（机型的任务组件）是配错了：开机就抛，不能悄悄没有任务表
    var bare = new BareJobs();
    Probe.Name(bare, "Job");
    bare.IsPersistent = false;
    bool bareThrew = false;
    try
    {
        bare.Bind(modules);
    }
    catch (InvalidOperationException)
    {
        bareThrew = true;
    }

    Check(bareThrew, "Job 节点下没配 Task 子节点：装配时就抛");

    // Job 存盘写临时库（第 18 节"重启"时读回来），冒烟结束删掉
    XyzDb.Register("JobSmoke", $"DataSource={jobDb}", SqlSugar.DbType.Sqlite);
    var jobs = new SmokeJobs();
    Probe.Name(jobs, "Job");
    jobs.Database = "JobSmoke";
    jobs.Bind(modules);

    // CJ 管理按参考用对象提交事件，注册与入队分开，实体和状态机不对外暴露。
    ICjManager controls = new CjManager();
    var controlEvents = new List<(string Id, ControlJobState State)>();
    var controlTransitions = new List<(string Id, ControlJobState State, int Number)>();
    controls.StateChanged += (job, state, number) =>
    {
        controlEvents.Add((job.Id, state));
        controlTransitions.Add((job.Id, state, number));
    };
    var controlA = new ControlJob { Id = "CJ-DICT-A", LoadPort = "LP1" };
    var controlB = new ControlJob { Id = "CJ-DICT-B", LoadPort = "LP2" };
    controls.Add(controlA);
    controls.Add(controlB);
    controls.Add(controlA);
    Check(controlA.State == ControlJobState.Created && controlB.State == ControlJobState.Created
          && controlEvents.Count == 0 && controls.ControlJobs.Count == 2,
        "Add 只注册；重复添加同一对象不重复注册、不自动入队");
    bool duplicateRejected = false;
    try
    {
        controls.Add(new ControlJob { Id = controlA.Id.ToLowerInvariant(), LoadPort = "LP1" });
    }
    catch (InvalidOperationException)
    {
        duplicateRejected = true;
    }
    Check(duplicateRejected && ReferenceEquals(controls.Get("cj-dict-a"), controlA),
        "同 ID 的不同对象拒绝覆盖，字典按名称忽略大小写查询");
    Check(controls.Queue(controlA).IsSuccess && controls.Queue(controlB).IsSuccess
          && controlEvents.SequenceEqual(new[]
          {
              ("CJ-DICT-A", ControlJobState.Queued), ("CJ-DICT-B", ControlJobState.Queued),
          }), "Queue 提交独立动作入队，状态通知同步更新 CJ");
    Check(controls.ControlJobs.Select(control => control.Id).SequenceEqual(new[] { "CJ-DICT-A", "CJ-DICT-B" }),
        "CJ 保持添加顺序，不主动调整字典");
    Check(!controls.Activate(controlA).IsSuccess && controlA.State == ControlJobState.Queued,
        "状态表拒绝 Queued 直接 Activate");
    foreach (var control in controls.ControlJobs)
    {
        Check(controls.Select(control).IsSuccess && controls.WaitForStart(control).IsSuccess,
            "每个 CJ 可以独立选中并等待手动启动");
    }
    Check(controls.Activate(controlA).IsSuccess && controlA.State == ControlJobState.Executing
          && controlB.State == ControlJobState.WaitingForStart, "按对象启动独立状态机，不影响另一个 CJ");
    Check(controlTransitions.Last() == (controlA.Id, ControlJobState.Executing, 7),
        "手动启动时直接通过事件传 E94 #7，不从 CJ 对象读取转换编号");
    Check(controls.Pause(controlA).IsSuccess && controls.Resume(controlA).IsSuccess,
        "执行中的 CJ 暂停后可以恢复");
    Check(controls.Rollback(controlA).IsSuccess && controlA.State == ControlJobState.Selected
          && controls.Activate(controlA).IsSuccess, "启动失败可回滚 Selected 后重新激活");
    Check(controlTransitions.Last() == (controlA.Id, ControlJobState.Executing, 5),
        "同样进入 Executing，Selected 激活通过事件传 E94 #5");
    Check(controls.Pause(controlA).IsSuccess && controls.Complete(controlA).IsSuccess
          && controlA.State == ControlJobState.Completed && controlA.CompletedBy == 10,
        "最后一行完成与 Pause 相遇时，CJ 仍能完成收口");
    controls.Remove(controlA);
    Check(controls.Get(controlA.Id) is null && controlA.State == ControlJobState.Completed
          && ReferenceEquals(controls.Get(controlB.Id), controlB), "Remove 只移除指定对象，不改变状态或其他 CJ");
    var replacementControl = new ControlJob { Id = controlA.Id, LoadPort = "LP1" };
    controls.Add(replacementControl);
    controls.Remove(controlA);
    Check(ReferenceEquals(controls.Get(controlA.Id), replacementControl)
          && !controls.Queue(controlA).IsSuccess, "旧对象的 Remove 或动作不能操作同 ID 的新对象");
    Check(controls.Queue(replacementControl).IsSuccess && controls.Dequeue(replacementControl).IsSuccess
          && replacementControl.EndedBy == 2, "排队删除保留 E94 #2 记录");
    controls.Remove(replacementControl);
    Check(controls.Activate(controlB).IsSuccess && controls.Abort(controlB).IsSuccess
          && controlB.State == ControlJobState.Aborting && !controlB.CanStartProcessJobs,
        "Abort 立即进入 Aborting，并停止启动新 PJ");
    Check(!controls.Complete(controlB).IsSuccess && controls.FinishAbort(controlB).IsSuccess
          && controlB.State == ControlJobState.Aborted && controlB.CompletedBy == 12
          && controlB.CompletedAt is not null, "中止收尾进入 Aborted，完成时间及 E94 #12 保留");
    Check(controls.Delete(controlB).IsSuccess && controlB.EndedBy == 13,
        "中止后的载具离位删除走 E94 #13");
    int endedControlEvents = controlEvents.Count;
    var endedControlAt = controlB.EndedAt;
    Check(!controls.Delete(controlB).IsSuccess && controlEvents.Count == endedControlEvents
          && controlB.EndedAt == endedControlAt, "结束对象拒绝重复动作，不覆盖结束时间或重报事件");
    controls.Remove(controlB);
    Check(controls.ControlJobs.Count == 0, "Remove 后所有实体都从字典移除");

    // PJ 与 CJ 一样按字典登记，每个 PJ 有独立状态机，登记不自动入队。
    IPjManager processes = new PjManager();
    var processEvents = new List<(string Id, ProcessJobState State, int Number)>();
    processes.StateChanged += (process, state, number) => processEvents.Add((process.Id, state, number));
    ProcessJob ProcessForStateMachine(string id)
    {
        var process = new ProcessJob { Id = id, Sequence = new SequenceData { Name = "SMOKE" } };
        process.Rows.Add(new TaskRow
        {
            Owner = id,
            WaferId = Guid.NewGuid(),
            WaferName = id + "-W1",
            SourcePort = "LP1",
            SourceSlot = 1,
            ReturnPort = "LP1",
            ReturnSlot = 1,
        });
        return process;
    }
    var processA = ProcessForStateMachine("PJ-DICT-A");
    var processB = ProcessForStateMachine("PJ-DICT-B");
    processes.Add(processA);
    processes.Add(processB);
    processes.Add(processA);
    Check(processA.State == ProcessJobState.Created && processB.State == ProcessJobState.Created
          && processEvents.Count == 0 && processes.ProcessJobs.Count == 2
          && processes.OwnerOf(processA.Rows[0].WaferId) is null,
        "PJ Add 只登记，同一对象重复 Add 不重建状态机、不入队或占片");
    bool duplicateProcessRejected = false;
    try
    {
        processes.Add(ProcessForStateMachine("pj-dict-a"));
    }
    catch (InvalidOperationException)
    {
        duplicateProcessRejected = true;
    }
    Check(duplicateProcessRejected && ReferenceEquals(processes.Get("pj-dict-a"), processA),
        "PJ 同 ID 的不同对象拒绝覆盖，查询忽略大小写");
    Check(processes.Queue(processA).IsSuccess && processes.Queue(processB).IsSuccess
          && processes.OwnerOf(processA.Rows[0].WaferId) == processA.Id
          && processes.OwnerOf(processB.Rows[0].WaferId) == processB.Id
          && processEvents.Select(item => item.Number).SequenceEqual([1, 1]),
        "Queue 入队时登记晶圆归属并通知 E40 #1");
    Check(!processes.Start(processA).IsSuccess && processA.State == ProcessJobState.QueuedPooled,
        "PJ 状态表拒绝排队时直接 Start");
    Check(processes.Setup(processA).IsSuccess && processes.Setup(processB).IsSuccess
          && processes.Pause(processA).IsSuccess && processes.FinishPause(processA).IsSuccess,
        "独立 PJ 可以准备，并在准备时暂停到位");
    processes.Add(processA);
    Check(processes.Resume(processA).IsSuccess && processA.State == ProcessJobState.SettingUp
          && processB.State == ProcessJobState.SettingUp && processEvents.Last().Number == 10,
        "重复登记保留原状态机，准备时暂停后恢复 SettingUp");
    Check(processes.WaitForStart(processB).IsSuccess && processes.Pause(processB).IsSuccess
          && processes.FinishPause(processB).IsSuccess && processes.Resume(processB).IsSuccess
          && processB.State == ProcessJobState.WaitingForStart,
        "等待启动时暂停后恢复 WaitingForStart，不错误启动处理");
    Check(processes.Activate(processA).IsSuccess && processes.Start(processB).IsSuccess
          && processes.Pause(processA).IsSuccess && processes.Resume(processA).IsSuccess
          && processA.State == ProcessJobState.Processing && processB.State == ProcessJobState.Processing,
        "处理时在 Pausing 阶段即可恢复，各 PJ 的恢复目标互不影响");
    Check(processes.Complete(processB).IsSuccess && processB.State == ProcessJobState.ProcessComplete
          && !processB.IsEnded && processEvents.Last().Number == 6,
        "PJ 工艺完成先进入 ProcessComplete，回片之前保留登记和归属");
    Check(processes.Finish(processB).IsSuccess && processB.EndedBy == 7
          && processEvents.Last() == (processB.Id, ProcessJobState.ProcessComplete, 7),
        "回片完成通过事件传 E40 #7，不缓存最近一次转换号");
    processes.Remove(processB);
    Check(processes.Get(processB.Id) is null && processes.OwnerOf(processB.Rows[0].WaferId) is null,
        "PJ Remove 移除指定实体并释放其晶圆归属");
    var replacementProcess = ProcessForStateMachine(processB.Id);
    processes.Add(replacementProcess);
    processes.Queue(replacementProcess);
    processes.Remove(processB);
    Check(ReferenceEquals(processes.Get(processB.Id), replacementProcess)
          && processes.OwnerOf(replacementProcess.Rows[0].WaferId) == replacementProcess.Id
          && !processes.Pause(processB).IsSuccess,
        "旧 PJ 对象不能移除或操作同 ID 的新对象，也不能释放新对象的归属");
    Check(processes.Dequeue(replacementProcess).IsSuccess && replacementProcess.EndedBy == 18,
        "排队取消仍走 E40 #18");
    processes.Remove(replacementProcess);
    Check(processes.Pause(processA).IsSuccess && processes.FinishPause(processA).IsSuccess
          && processes.Stop(processA).IsSuccess && processEvents.Last().Number == 12,
        "暂停中的 PJ Stop 走 E40 #12，进入 Stopping");
    Check(processes.Abort(processA).IsSuccess && processEvents.Last().Number == 14
          && processes.FinishAbort(processA).IsSuccess && processA.State == ProcessJobState.Aborted
          && processA.EndedBy == 16, "Stopping 升级 Abort 走 #14，收尾后通过 #16 中止结束");
    int processEventCount = processEvents.Count;
    var processEndedAt = processA.EndedAt;
    Check(!processes.FinishAbort(processA).IsSuccess && processEvents.Count == processEventCount
          && processA.EndedAt == processEndedAt, "已结束 PJ 拒绝重复收尾，不重复事件或覆盖结束时间");
    processes.Remove(processA);
    var processStopped = ProcessForStateMachine("PJ-DICT-S");
    processes.Add(processStopped);
    Check(processes.Queue(processStopped).IsSuccess && processes.Setup(processStopped).IsSuccess
          && processes.Activate(processStopped).IsSuccess && processes.Stop(processStopped).IsSuccess
          && processEvents.Last().Number == 11 && processes.FinishStop(processStopped).IsSuccess
          && processStopped.State == ProcessJobState.Stopped && processStopped.EndedBy == 17,
        "处理中的 Stop 走 #11，停止收尾走 #17");
    processes.Remove(processStopped);
    Check(processes.ProcessJobs.Count == 0, "PJ 清理后字典不保留结束实体");

    // 泛型状态机验证检查失败、动作顺序、执行错误和收尾错误；规则可复用于其他模块。
    var stateFlow = new SmokeStateMachine();
    var flowTrace = new List<string>();
    bool ready = false;
    var flowTransition = new xyz.Modules.StateMachines.StateTransition<ControlJobState>
    {
        TargetState = ControlJobState.Queued,
        ProcessState = ControlJobState.Selected,
        OnEntry = _ => flowTrace.Add("Entry"),
        PreCheck = _ => { flowTrace.Add("Check"); return ready; },
        Execute = _ => { flowTrace.Add("Execute"); return HandleResult.Success("payload"); },
        OnExit = _ => { flowTrace.Add("Exit"); return HandleResult.Success(); },
        ErrorHandler = (_, _) => flowTrace.Add("Error"),
    };
    stateFlow.Transitions[(ControlJobState.Created, ControlStateAction.Queue)] = flowTransition;
    stateFlow.OnStateChanged += (_, current) => flowTrace.Add("State:" + current);
    Check(!stateFlow.StateChange(ControlStateAction.Queue).IsSuccess
          && stateFlow.CurrentState == ControlJobState.Created && flowTrace.SequenceEqual(["Entry", "Check"]),
        "PreCheck 未通过时不执行动作，也不进入中间或目标状态");
    ready = true;
    flowTrace.Clear();
    var flowResult = stateFlow.StateChange(ControlStateAction.Queue);
    Check(flowResult.IsSuccess && (string?)flowResult.Result == "payload"
          && flowTrace.SequenceEqual(["Entry", "Check", "State:Selected", "Execute", "State:Queued", "Exit"]),
        "成功转换按 Entry、Check、中间状态、Execute、目标状态、Exit 顺序，保留动作结果");
    stateFlow.CurrentState = ControlJobState.Created;
    flowTrace.Clear();
    flowTransition.Execute = _ => HandleResult.Fail(ErrorCodes.JobDisabled);
    Check(!stateFlow.StateChange(ControlStateAction.Queue).IsSuccess
          && stateFlow.CurrentState == ControlJobState.Aborted
          && flowTrace.Last() == "Error" && !flowTrace.Contains("Exit"),
        "Execute 失败进入派生类的错误状态并调用错误处理，不执行正常收尾");
    stateFlow.CurrentState = ControlJobState.Created;
    flowTrace.Clear();
    flowTransition.Execute = _ => throw new InvalidOperationException("smoke failure");
    Check(stateFlow.StateChange(ControlStateAction.Queue).ErrorMessage == ErrorCodes.OperationFaulted
          && stateFlow.CurrentState == ControlJobState.Aborted && flowTrace.Count(item => item == "Error") == 1,
        "动作异常返回失败，进入错误状态并调用一次错误处理");
    stateFlow.CurrentState = ControlJobState.Created;
    flowTrace.Clear();
    flowTransition.Execute = _ => HandleResult.Success();
    flowTransition.OnExit = _ => HandleResult.Fail(ErrorCodes.JobDisabled);
    Check(!stateFlow.StateChange(ControlStateAction.Queue).IsSuccess
          && stateFlow.CurrentState == ControlJobState.Queued && flowTrace.Last() == "Error",
        "目标状态已到达但收尾失败时返回失败并调用错误处理");

    // 展开命令公共处理后，所有入口仍检查禁用状态，并在与扫描争用锁时返回超时。
    (string Name, Func<Task<HandleResult>> Run)[] guardedCommands =
    [
        ("创建 PJ", () => jobs.CreateProcessJobAsync("LP1", "PJ-GUARD", [1], "SEQ_A", null)),
        ("创建 CJ", () => jobs.CreateControlJobAsync("LP1", ["PJ-GUARD"], "CJ-GUARD")),
        ("CJ 启动", () => jobs.StartControlJobAsync("CJ-GUARD")),
        ("CJ 暂停", () => jobs.PauseControlJobAsync("CJ-GUARD")),
        ("CJ 恢复", () => jobs.ResumeControlJobAsync("CJ-GUARD")),
        ("CJ 停止", () => jobs.StopControlJobAsync("CJ-GUARD")),
        ("CJ 中止", () => jobs.AbortControlJobAsync("CJ-GUARD")),
        ("CJ 取消", () => jobs.CancelControlJobAsync("CJ-GUARD")),
        ("CJ 取消选中", () => jobs.DeselectControlJobAsync("CJ-GUARD")),
        ("PJ 启动", () => jobs.StartProcessJobAsync("PJ-GUARD")),
        ("PJ 暂停", () => jobs.PauseProcessJobAsync("PJ-GUARD")),
        ("PJ 恢复", () => jobs.ResumeProcessJobAsync("PJ-GUARD")),
        ("PJ 停止", () => jobs.StopProcessJobAsync("PJ-GUARD")),
        ("PJ 中止", () => jobs.AbortProcessJobAsync("PJ-GUARD")),
        ("PJ 取消", () => jobs.CancelProcessJobAsync("PJ-GUARD")),
        ("CJ 编号入口", () => jobs.ExecuteControlJobCommandAsync("CJ-GUARD", ControlJobCommand.Start, ControlJobAction.SaveJobs)),
        ("PJ 编号入口", () => jobs.ExecuteProcessJobCommandAsync("PJ-GUARD", ProcessJobCommand.Start)),
        ("CJ 不支持的编号", () => jobs.ExecuteControlJobCommandAsync("CJ-GUARD", ControlJobCommand.HeadOfQueue, ControlJobAction.SaveJobs)),
        ("PJ 不支持的编号", () => jobs.ExecuteProcessJobCommandAsync("PJ-GUARD", (ProcessJobCommand)99)),
        ("整机中止", () => jobs.AbortAllAsync()),
        ("任务重试", () => jobs.RetryTaskAsync("PJ-GUARD", 1, 0)),
        ("任务完成", () => jobs.CompleteTaskAsync("PJ-GUARD", 1, 0)),
    ];
    jobs.IsEnable = false;
    foreach (var command in guardedCommands)
    {
        Check(command.Run().Result.ErrorMessage == ErrorCodes.JobDisabled, $"Job 禁用：{command.Name} 被拒绝");
    }
    jobs.IsEnable = true;

    int savedCommandTimeout = jobs.CommandTimeoutMs;
    jobs.CommandTimeoutMs = 100;
    object scanGate = typeof(JobManager).GetField("_gate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(jobs)!;
    using (var lockHeld = new ManualResetEventSlim())
    using (var releaseLock = new ManualResetEventSlim())
    {
        var scanning = Task.Run(() =>
        {
            lock (scanGate)
            {
                lockHeld.Set();
                releaseLock.Wait();
            }
        });
        try
        {
            Check(lockHeld.Wait(2000), "另一个线程持有扫描锁");
            foreach (var command in guardedCommands)
            {
                var result = command.Run().Result;
                Check(result.ErrorMessage == ErrorCodes.JobCommandTimeout && result.Args.SequenceEqual(new[] { "100" }),
                    $"扫描锁未释放：{command.Name} 返回命令超时");
            }
        }
        finally
        {
            releaseLock.Set();
            scanning.GetAwaiter().GetResult();
            jobs.CommandTimeoutMs = savedCommandTimeout;
        }
    }
    Check(jobs.CreateProcessJobAsync("LP1", null!, [1], "SEQ_A", null).Result.ErrorMessage == ErrorCodes.OperationFaulted,
        "创建 PJ 出异常仍返回错误结果");
    var afterFault = Task.Run(() => jobs.PauseProcessJobAsync("PJ-GUARD").GetAwaiter().GetResult());
    Check(afterFault.Wait(2000) && afterFault.Result.ErrorMessage == ErrorCodes.JobNotFound,
        "异常后释放锁，其他线程仍能执行 Job 命令");

    // 上报口直接挂假的 EAP；上报都经 EAP 的派发组件发（宿主里是 sc.xml Eap 下的 Notifier）
    _ = new EapNotifierComponent();
    var events = new RecordingJobEvents();
    jobs.E40Callback = events;
    jobs.E94Callback = events;

    SmokeRobot? secondRobot = null;

    void Tick()
    {
        robot.Tick();
        secondRobot?.Tick();
        pm1.Tick();
        pm2.Tick();
        lp1.Tick();
        lp2.Tick();
        transfers.Tick();
        jobs.Tick();
    }

    // sleepMs：要等真实时间的（站点等待超时按毫秒算）每拍之间睡一下，别的一拍接一拍推
    bool RunUntil(Func<bool> done, int maxTicks = 3000, int sleepMs = 0)
    {
        for (int tick = 0; tick < maxTicks; tick++)
        {
            Tick();
            if (done())
            {
                return true;
            }

            if (sleepMs > 0)
            {
                Thread.Sleep(sleepMs);
            }
        }

        return false;
    }

    // Job 记录（control_job、process_job）由 Job 管理的写库线程写：查库要等它写到，最多等 3 秒
    bool WaitDb(Func<SqlSugar.ISqlSugarClient, bool> written)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < 3000)
        {
            using (var db = XyzDb.Create("JobSmoke"))
            {
                if (written(db))
                {
                    return true;
                }
            }

            Thread.Sleep(20);
        }

        return false;
    }

    List<JobWaferDto> WafersOf(ProcessJobEntity row)
    {
        return JsonHelper.Deserialize<List<JobWaferDto>>(row.Wafers) ?? [];
    }

    HandleResult Do(Task<HandleResult> task)
    {
        for (int tick = 0; tick < 50 && !task.IsCompleted; tick++)
        {
            jobs.Tick();
            task.Wait(20);
        }

        Check(task.IsCompleted, "命令应在一两拍里受理");
        return task.Result;
    }

    TransferRoutine Finish(HandleResult<TransferRoutine> started, int maxTicks = 3000, int sleepMs = 0)
    {
        var operation = started.Result;
        Check(started.IsSuccess && operation is not null, $"搬运应启动：{started.ErrorMessage} [{string.Join(",", started.Args)}]");
        Check(RunUntil(() => operation!.IsSettled, maxTicks, sleepMs), "搬运操作应收尾完成");
        return operation!;
    }

    void LoadCarrier(SmokePort port, params int[] slots)
    {
        var map = new SlotState[port.SlotCount];
        for (int index = 0; index < map.Length; index++)
        {
            map[index] = slots.Contains(index + 1) ? SlotState.CorrectlyOccupied : SlotState.Empty;
        }

        port.NoteState(LoadPortState.Loaded);
        port.NotePodPlaced(true);
        port.Tick();
        port.NoteMap(map);
    }

    void UnloadCarrier(SmokePort port)
    {
        port.NotePodPlaced(false);
        port.Tick();
        port.NoteState(ModuleState.Idle);
    }

    ControlJobDto? CjOf(string id)
    {
        return jobs.Snapshot.ControlJobs.FirstOrDefault(job => job.Id == id);
    }

    ProcessJobDto? PjOf(string id)
    {
        return jobs.Snapshot.ProcessJobs.FirstOrDefault(job => job.Id == id);
    }

    // 本地建 Job 走 Job 服务，跟界面一样（先建 PJ、再建 CJ，跟 Host 同一套方法）；回包翻回 HandleResult，跟别的命令一样查
    var jobService = new JobService(modules);
    HandleResult Create(string lot, string port, bool controlJobAutoStart, params (int Slot, string Sequence)[] slots)
    {
        jobs.ControlJobAutoStart = controlJobAutoStart;
        var task = jobService.CreateAsync(new JobCreateRequest
        {
            LoadPort = port,
            LotId = lot,
            Slots = slots.Select(item => new JobSlotDto { Slot = item.Slot, Sequence = item.Sequence }).ToList(),
            Operator = "Smoke",
        });
        Check(task.IsCompleted, "建 Job 应当场做完");
        var response = task.Result;
        return response.Success
            ? HandleResult.Success(response.DeserializeData<JobCreatedDto>())
            : HandleResult.Fail(response.Code, [.. response.Args]);
    }

    // 1. 搬运管理：受理时的检查——站点、槽、片、机械手、手臂，不收的不占锁。
    LoadCarrier(lp1, 1, 2, 3, 4, 5);
    var first = ledger.Get("LP1", 1)!;
    HandleResult<TransferRoutine> StartTransfer(string source, int sourceSlot, string target, int targetSlot, Guid? wafer = null, int arm = 0,
        TransferOrigin origin = TransferOrigin.Manual)
    {
        return transfers.Start(new TransferRequest
        {
            Origin = origin,
            Source = source,
            SourceSlot = sourceSlot,
            Target = target,
            TargetSlot = targetSlot,
            WaferId = wafer,
            Arm = arm,
        });
    }

    void Rejects(HandleResult<TransferRoutine> started, string code, string[] args, string message)
    {
        Check(!started.IsSuccess && started.ErrorMessage == code && started.Args.SequenceEqual(args),
            $"{message}，实际 {started.ErrorMessage} [{string.Join(",", started.Args)}]");
    }

    Rejects(StartTransfer("NOPE", 1, "PM1", 1), ErrorCodes.TransferStationNotFound, ["NOPE"], "站点不在搬运模块表里");
    Rejects(StartTransfer("LP1", 26, "PM1", 1), ErrorCodes.TransferSlotOutOfRange, ["LP1", "26", "25"], "槽号超出槽数");
    Rejects(StartTransfer("LP1", 1, "LP1", 1), ErrorCodes.TransferSameSlot, [], "源和目标同一个槽");
    Rejects(StartTransfer("LP1", 10, "PM1", 1), ErrorCodes.WaferNoWafer, ["LP1", "10"], "源槽上没片");
    Rejects(StartTransfer("LP1", 1, "PM1", 1, Guid.NewGuid()), ErrorCodes.TransferWaferMismatch, ["LP1", "1", first.WaferId], "源槽上不是要搬的那一片");
    Rejects(StartTransfer("LP1", 1, "LP1", 2), ErrorCodes.WaferSlotOccupied, ["LP1", "2", ledger.Get("LP1", 2)!.WaferId], "目标槽上有片");
    Rejects(StartTransfer("LP1", 1, "PM9", 1), ErrorCodes.TransferNoRobot, ["LP1", "PM9"], "没有机械手两边都到得了");
    Rejects(StartTransfer("LP1", 1, "PM1", 1, arm: 3), ErrorCodes.TransferArmUnavailable, ["Robot1", "3"], "没有 3 号手");
    Check(!transfers.IsSlotLocked("LP1", 1) && !transfers.IsRobotInTransfer("Robot1"), "不收的单不占锁");

    // 2. 两次操作抢同一个目标槽、同一片：只有一次能启动；设备操作完成（设备、站点、晶圆账都收尾）才确认完成、放锁。
    var winner = StartTransfer("LP1", 1, "PM1", 1);
    Check(winner.IsSuccess && winner.Result is not null, "第一张单受理");
    Rejects(StartTransfer("LP1", 2, "PM1", 1), ErrorCodes.TransferSlotLocked, ["PM1", "1"], "目标槽已经被别的单锁着");
    Rejects(StartTransfer("LP1", 1, "PM2", 1), ErrorCodes.TransferSlotLocked, ["LP1", "1"], "这一片已经在别的单里");
    Check(transfers.IsSlotLocked("LP1", 1) && transfers.IsSlotLocked("pm1", 1)
          && transfers.IsRobotInTransfer("robot1"),
        "受理就锁源槽、目标槽、片、机械手（站点名不分大小写）");
    var moved = Finish(winner);
    Check(moved.IsSuccess && moved.HasPicked && moved.Robot.Name == "Robot1" && moved.Arm == 1 && moved.Origin == TransferOrigin.Manual,
        "搬完：结果带机械手和手，记着取片做完了");
    Check(ledger.Get("PM1", 1)?.Id == first.Id && ledger.Get("LP1", 1) is null, "账跟着走：片在 PM1");
    Check(!transfers.IsSlotLocked("LP1", 1) && !transfers.IsSlotLocked("PM1", 1) && !transfers.IsRobotInTransfer("Robot1"),
        "搬完放锁");
    Check(lp1.State == LoadPortState.Loaded && pm1.State == ModuleState.Idle && robot.State == ModuleState.Idle,
        "两个站点都收尾回到待命，机械手空闲");
    Check(Finish(StartTransfer("PM1", 1, "LP1", 1)).IsSuccess && ledger.Get("LP1", 1)?.Id == first.Id, "搬回原槽");

    // 3. 没动手就失败（目标一直不在待命，等站点超时）：片没动过，锁放开，不用人工确认。
    pm1.NoteState(ModuleState.NotInit);
    var notReady = Finish(StartTransfer("LP1", 1, "PM1", 1), sleepMs: 5);
    Check(!notReady.IsSuccess && notReady.State == OperationState.Failed && notReady.Code == ErrorCodes.StationBusy
          && notReady.ErrorArgs.SequenceEqual(new[] { "PM1", "1000" }) && !notReady.NeedsRecovery,
        $"目标等不到：transfer.station_busy，参数是站点和等待毫秒数，不用人工确认，实际 {notReady.Code}");
    Check(ledger.Get("LP1", 1)?.Id == first.Id && !transfers.IsSlotLocked("LP1", 1) && lp1.State == LoadPortState.Loaded,
        "片还在源槽，锁放开，源站点也没被碰");
    pm1.NoteState(ModuleState.Idle);

    // 4. 动过手才失败（取片失败）：片在哪说不准，锁留着、站点停在交互中，等人工确认后 ReleaseHold。
    robot.FailNextPick = true;
    var picked = Finish(StartTransfer("LP1", 1, "PM1", 1));
    Check(!picked.IsSuccess && picked.Code == ErrorCodes.TransferFailed && picked.ErrorArgs.SequenceEqual(new[] { "Robot1", "Pick" })
          && picked.NeedsRecovery && !picked.HasPicked, "取片失败：transfer.failed，要人工确认，结果记着没取到（Job 据此把出错记在取片上）");
    Check(transfers.HeldOperations.Count == 1 && transfers.IsSlotLocked("LP1", 1) && transfers.IsSlotLocked("PM1", 1)
          && lp1.State == TransferModuleState.Transferring, "锁留着，源站点停在交互中挡住后续动作");
    Rejects(StartTransfer("LP1", 1, "PM2", 1), ErrorCodes.TransferSlotLocked, ["LP1", "1"], "保留占用的槽不接受新搬运");
    Check(transfers.ReleaseHold(picked.WaferId) && !transfers.ReleaseHold(picked.WaferId) && transfers.HeldOperations.Count == 0
          && !transfers.IsSlotLocked("LP1", 1), "人工确认后放锁（只放一次）");
    robot.NoteState(ModuleState.Idle);
    lp1.NoteState(LoadPortState.Loaded);
    pm1.NoteState(ModuleState.Idle);

    // 5. 不另排搬运队列：机械手占着时拒绝下一次搬运，原任务等待资源；未动手中止后放开资源。
    var running = StartTransfer("LP1", 1, "PM1", 1);
    Check(running.IsSuccess && running.Result is not null, "开始一次搬运");
    Rejects(StartTransfer("LP1", 2, "PM2", 1), ErrorCodes.ActionRejected, ["Robot1", robot.State.ToString()], "机械手正执行，新的请求不排队");
    Check(!transfers.IsSlotLocked("LP1", 2) && !transfers.IsSlotLocked("PM2", 1), "被拒的请求不占资源");
    Check(transfers.Cancel(running.Result!, "smoke"), "请求中止当前搬运");
    var cancelled = Finish(running);
    Check(cancelled.State == OperationState.Aborted && !cancelled.MotionStarted && !cancelled.NeedsRecovery, "未动手中止，不需要人工确认");
    Check(!transfers.IsSlotLocked("LP1", 1) && !transfers.IsRobotInTransfer("Robot1"), "中止收尾后释放资源");
    Check(Finish(StartTransfer("LP1", 1, "PM1", 1)).IsSuccess && Finish(StartTransfer("PM1", 1, "LP1", 1)).IsSuccess, "资源空出来后重新执行并搬回");

    // 设备取片中止后，等设备 Abort 收尾再唤醒调用方；片位不确定时保留资源。
    var moving = StartTransfer("LP1", 1, "PM1", 1).Result!;
    Check(RunUntil(() => moving.IsMoving), "机械手进入取片阶段");
    Check(transfers.Cancel(moving, "smoke motion abort"), "中止正在取片的操作");
    transfers.Tick();
    Check(moving.IsTerminal && !moving.IsSettled && transfers.IsRobotInTransfer("Robot1"), "设备中止未收尾，搬运不能提前完成或释放机械手");
    Check(RunUntil(() => moving.IsSettled) && moving.NeedsRecovery, "设备中止收尾后完成搬运，仍等待人工确认片位");
    Check(transfers.ReleaseHold(moving.WaferId), "人工确认后按晶圆释放资源");
    robot.NoteState(ModuleState.Idle);
    lp1.NoteState(LoadPortState.Loaded);
    pm1.NoteState(ModuleState.Idle);

    // 6. 建 Job 的检查：不建就什么都不留（不建 CJ / PJ、不占片）。
    void Refuses(HandleResult result, string code, string[] args, string message)
    {
        Check(!result.IsSuccess && result.ErrorMessage == code && result.Args.SequenceEqual(args),
            $"{message}，实际 {result.ErrorMessage} [{string.Join(",", result.Args)}]");
    }

    Refuses(Create("LOT-X", "LPX", false, (1, "SEQ_A")), ErrorCodes.JobLoadPortNotFound, ["LPX"], "没有这个 LoadPort");
    Refuses(Create("LOT-X", "LP2", false, (1, "SEQ_D")), ErrorCodes.JobCarrierNotReady, ["LP2"], "LoadPort 上没载具");
    Refuses(Create("LOT-X", "LP1", false), ErrorCodes.JobNoWafers, ["LP1"], "没选片");
    Refuses(Create("LOT-X", "LP1", false, (1, "SEQ_A"), (10, "SEQ_A")), ErrorCodes.JobSlotEmpty, ["LP1", "10"],
        "有一槽没片：整个不建");
    Refuses(Create("LOT-X", "LP1", false, (1, "NOPE")), ErrorCodes.JobSequenceNotFound, ["NOPE"], "流程配方不在库里");
    Refuses(Create("LOT-X", "LP1", false, (1, "SEQ_D")), ErrorCodes.JobSequenceSourceMismatch, ["SEQ_D", "LP1"],
        "流程配方第 1 步没勾这个 LoadPort");
    Refuses(Create("BAD:ID", "LP1", false, (1, "SEQ_A")), ErrorCodes.JobIdInvalid, ["BAD:ID-1"], "名字里有冒号（先建 PJ，报的是 PJ 名）");
    pm2.NoProcess = true;
    Refuses(Create("LOT-X", "LP1", false, (1, "SEQ_A")), ErrorCodes.JobStationTaskUnsupported,
        ["SEQ_A", "2", "PM2", StationTaskAction.Process], "站点组里有个站点不能做工艺（站点声明的任务里没有）：整个不建");
    pm2.NoProcess = false;
    Probe.Enabled(pm1, false);
    Refuses(Create("LOT-X", "LP1", false, (1, "SEQ_B")), ErrorCodes.JobStepNoStation, ["SEQ_B", "2", "R1"],
        "这一站能去的站点都用不了（PM1 停用）：整个不建");
    Probe.Enabled(pm1, true);
    Check(recipes.Create(9, "R9", "Smoke").IsOk, "建工艺配方 R9");
    Sequence(5, "SEQ_E", Step("LoadPort", "", "LP1"), Step("Chamber", "R9", "PM1"), Step("LoadPort", "", "LP1"));
    Check(recipes.Delete(9, "Smoke").IsOk, "流程配方存好以后，R9 从工艺配方库里删掉");
    Refuses(Create("LOT-X", "LP1", false, (1, "SEQ_E")), ErrorCodes.JobRecipeNotFound, ["SEQ_E", "R9"],
        "流程配方用的工艺配方不在库里：整个不建");
    Check(jobs.Snapshot.ControlJobs.Count == 0 && jobs.Snapshot.ProcessJobs.Count == 0 && jobs.OwnerOf(first.Id) is null,
        "不建就什么都不留：没有 CJ / PJ，片不归任何 Job");

    // 入队通知异常被状态机转换成失败结果；创建入口必须撤销本次注册、任务和关联。
    var processManager = (IPjManager)typeof(JobManager).GetField("_processJobs",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(jobs)!;
    var controlManager = (ICjManager)typeof(JobManager).GetField("_controlJobs",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(jobs)!;
    void RejectProcessQueue(ProcessJob job, ProcessJobState state, int transition)
    {
        if (job.Id == "PJ-QUEUE-FAIL" && transition == 1)
        {
            throw new InvalidOperationException("模拟 PJ 入队通知失败");
        }
    }
    processManager.StateChanged += RejectProcessQueue;
    try
    {
        var rejectedQueue = Do(jobs.CreateProcessJobAsync("LP1", "PJ-QUEUE-FAIL", [1], "SEQ_A", null));
        Check(!rejectedQueue.IsSuccess && rejectedQueue.ErrorMessage == ErrorCodes.OperationFaulted
              && processManager.Get("PJ-QUEUE-FAIL") is null && jobs.OwnerOf(first.Id) is null
              && !jobs.FindChild<SmokeTasks>()!.Rows.Any(row => row.Owner == "PJ-QUEUE-FAIL"),
            "PJ 入队失败返回失败并移除注册、任务行和晶圆归属");
    }
    finally
    {
        processManager.StateChanged -= RejectProcessQueue;
    }
    Check(Do(jobs.CreateProcessJobAsync("LP1", "PJ-QUEUE-FAIL", [1], "SEQ_A", null)).IsSuccess,
        "PJ 入队失败后可用相同名称和晶圆重新创建");
    void RejectControlQueue(ControlJob job, ControlJobState state, int transition)
    {
        if (job.Id == "CJ-QUEUE-FAIL" && transition == 1)
        {
            throw new InvalidOperationException("模拟 CJ 入队通知失败");
        }
    }
    controlManager.StateChanged += RejectControlQueue;
    try
    {
        var rejectedQueue = Do(jobs.CreateControlJobAsync("LP1", ["PJ-QUEUE-FAIL"], "CJ-QUEUE-FAIL"));
        Check(!rejectedQueue.IsSuccess && rejectedQueue.ErrorMessage == ErrorCodes.OperationFaulted
              && controlManager.Get("CJ-QUEUE-FAIL") is null
              && processManager.Get("PJ-QUEUE-FAIL")?.ControlJob is null
              && jobs.OwnerOf(first.Id) == "PJ-QUEUE-FAIL",
            "CJ 入队失败移除本次 CJ、解除关联，保留之前创建的 PJ 和晶圆归属");
    }
    finally
    {
        controlManager.StateChanged -= RejectControlQueue;
    }
    Check(Do(jobs.CreateControlJobAsync("LP1", ["PJ-QUEUE-FAIL"], "CJ-QUEUE-FAIL")).IsSuccess
          && Do(jobs.CancelControlJobAsync("CJ-QUEUE-FAIL")).IsSuccess
          && jobs.OwnerOf(first.Id) is null,
        "CJ 入队失败后可重新关联原 PJ，取消时释放晶圆归属");

    // 7. 一篮两个 Sequence：1、2 槽 SEQ_A、3 槽 SEQ_B（两步加工）→ 一个 CJ 两个 PJ（跟 Host 一样先建 PJ、再建 CJ）；
    //    同一个建 Job 重发被正常的检查拦住（名字已经在用）；建 CJ 那一步被拒，已经建好的 PJ 撤掉。
    var created = Create("LOT-A", "LP1", false, (1, "SEQ_A"), (2, "SEQ_A"), (3, "SEQ_B"));
    Check(created.IsSuccess && created.Result is JobCreatedDto createdJob && createdJob.ControlJob == "LOT-A" && createdJob.ProcessJobs.SequenceEqual(new[] { "LOT-A-1", "LOT-A-2" }),
        "建好：CJ LOT-A，PJ 按投片顺序 LOT-A-1（SEQ_A）、LOT-A-2（SEQ_B）");
    Check(CjOf("LOT-A")?.LotId == "LOT-A" && CjOf("LOT-A")!.ProcessJobs.SequenceEqual(new[] { "LOT-A-1", "LOT-A-2" })
          && PjOf("LOT-A-2")?.ControlJob == "LOT-A", "CJ 记着批次号，按顺序收下两个 PJ（命令做完当场发布）");
    Refuses(Create("LOT-A", "LP1", false, (1, "SEQ_A"), (2, "SEQ_A"), (3, "SEQ_B")), ErrorCodes.JobIdDuplicate, ["LOT-A-1"],
        "同一个建 Job 重发：名字已经在用，拒");
    Check(jobs.Snapshot.ControlJobs.Count == 1 && jobs.Snapshot.ProcessJobs.Count == 2, "重发没建出第二份");
    var slot4 = ledger.Get("LP1", 4);
    Check(slot4 is not null, "LP1 第 4 槽有片");
    Refuses(Create("LOT-B", "LP1", false, (4, "SEQ_A")), ErrorCodes.JobLoadPortBusy, ["LP1", "LOT-A"],
        "一个 LoadPort 同时只有一个没结束的 CJ（建 CJ 那一步拒）");
    Check(PjOf("LOT-B-1") is null && jobs.OwnerOf(slot4!.Id) is null && events.WaitFor("PJ LOT-B-1 #18"),
        "CJ 没建成：已经建好的 PJ LOT-B-1 撤掉（#18），片放开");
    Check(jobs.OwnerOf(first.Id) == "LOT-A-1", "片归到 PJ 名下");
    Rejects(StartTransfer("LP1", 1, "PM1", 1), ErrorCodes.TransferWaferOwned, [first.WaferId, "LOT-A-1"], "手动搬 Job 的片：拒，带片号和 Job");

    // 任务表：一片一行，按流程配方一站一站拼——来源 LoadPort 取片 → 每一站放片、工艺、取片 → 回片 LoadPort 放片
    var rowA = PjOf("LOT-A-1")!.Wafers[0];
    Check(rowA.Tasks.Select(task => task.Kind).SequenceEqual(new[] { "Pick", "Place", "Process", "Pick", "Place" })
          && rowA.Tasks[1].Stations.SequenceEqual(new[] { "PM1", "PM2" }) && rowA.Tasks[2].Recipe == "R1"
          && rowA.Tasks[4].Stations.SequenceEqual(new[] { "LP1" }) && rowA.Tasks.All(task => task.State == "Waiting" && task.Station.Length == 0),
        "SEQ_A 一行：取片 → 放片（站点组 PM1、PM2，到时候再挑）→ 工艺 R1 → 取片 → 放片回 LP1，都是待做");
    var rowB = PjOf("LOT-A-2")!.Wafers[0];
    Check(rowB.Tasks.Select(task => task.Kind).SequenceEqual(new[] { "Pick", "Place", "Process", "Pick", "Place", "Process", "Pick", "Place" })
          && rowB.Tasks[2].Recipe == "R1" && rowB.Tasks[5].Recipe == "R2" && rowB.Tasks.Select(task => task.Step).SequenceEqual(new[] { 0, 0, 0, 1, 1, 1, 2, 2 }),
        "SEQ_B 两站：每一站放片、工艺、取片，最后回片（任务记着属于路线第几站）");

    Check(RunUntil(() => CjOf("LOT-A")?.State == (int)ControlJobState.WaitingForStart, 20), "CJ 选中（#3，不限同时跑几个），料到了等启动（#6）");
    Refuses(Do(jobs.ExecuteControlJobCommandAsync("LOT-A", ControlJobCommand.HeadOfQueue, ControlJobAction.SaveJobs)),
        ErrorCodes.JobCommandNotAllowed, ["LOT-A", "CJHeadOfQueue", "WAITINGFORSTART"],
        "HOQ 明确拒绝，不更改 CJ 状态或字典顺序");
    Check(CjOf("LOT-A")?.E94State == 2 && CjOf("LOT-A")?.State == ControlJobDto.StateWaitingForStart,
        "手动启动的内部状态编号与界面一致，E94 仍上报 WAITINGFORSTART = 2");
    Check(CjOf("LOT-A")?.AutoStart == false, "SC 关闭 CJ 自动启动时，查询与上报也反映等待 Start");
    Check(PjOf("LOT-A-1")?.State == (int)ProcessJobState.QueuedPooled && PjOf("LOT-A-2")?.State == (int)ProcessJobState.QueuedPooled,
        "CJ 没启动前 PJ 都排着");
    Refuses(Do(jobs.StartControlJobAsync("LOT-A")),
        ErrorCodes.JobNotAuto, [], "Manual 下启动不了");
    Refuses(Do(jobs.ExecuteControlJobCommandAsync("LOT-A", ControlJobCommand.Resume, ControlJobAction.SaveJobs)),
        ErrorCodes.JobCommandNotAllowed, ["LOT-A", "CJResume", "WAITINGFORSTART"], "转换表里没有的命令：拒，带当前状态");
    transfers.StartAutoDispatch();
    Check(Do(jobs.StartControlJobAsync("LOT-A")).IsSuccess
          && CjOf("LOT-A")?.State == (int)ControlJobState.Executing, "Auto 下启动：EXECUTING（#7）");

    var liveRow = jobs.FindChild<SmokeTasks>()!.Rows.First(row => row.Owner == "LOT-A-1" && row.SourceSlot == 1);
    Check(RunUntil(() => liveRow.Current?.Kind == StationTaskAction.Pick && liveRow.Current.State == WaferTaskState.Running), "执行器开始当前取片任务");
    var liveTransfer = liveRow.Tasks[0].Operation;
    Check(liveTransfer is TransferRoutine && liveRow.Tasks[1].State == WaferTaskState.Waiting
          && liveRow.Tasks.Count(task => task.State == WaferTaskState.Running) == 1, "操作直接挂在取片格上，放片仍等待，同一行只有当前格运行");
    Check(RunUntil(() => liveRow.Tasks[0].State == WaferTaskState.Done && liveRow.Tasks[1].State == WaferTaskState.Running), "取片确认后进入放片格");
    Check(liveRow.Tasks[0].Operation is null && ReferenceEquals(liveRow.Tasks[1].Operation, liveTransfer)
          && liveRow.Current == liveRow.Tasks[1] && ledger.FindById(liveRow.WaferId)?.Module == "Robot1", "片在机械手上，操作沿用到放片格，取片格清掉执行引用");
    Check(RunUntil(() => liveRow.Tasks[2].State == WaferTaskState.Running)
          && liveRow.Tasks[2].Operation is not null && liveRow.Tasks[1].Operation is null, "工艺同样直接挂在当前任务上，放片已完成并清掉引用");

    Check(RunUntil(() => CjOf("LOT-A")?.State == (int)ControlJobState.Completed), "跑完：CJ 进 COMPLETED");
    Check(liveRow.Tasks.All(task => task.Operation is null), "整行完成后不留执行操作或第二份执行记录");
    var cjA = CjOf("LOT-A")!;
    Check(cjA.CompletedBy == 10 && PjOf("LOT-A-1")?.EndedBy == 7 && PjOf("LOT-A-2")?.EndedBy == 7, "正常完成：CJ #10，PJ #7");
    for (int slot = 1; slot <= 3; slot++)
    {
        var wafer = ledger.Get("LP1", slot);
        Check(wafer is not null && wafer.ProcessState == WaferProcessState.Completed, $"LP1 第 {slot} 槽：回到原槽，账上工艺状态是完成");
    }

    var twoStep = PjOf("LOT-A-2")!.Wafers.Single();
    var twoStepProcesses = twoStep.Tasks.Where(task => task.Kind == "Process").ToList();
    Check(twoStep.Tasks.All(task => task.State == "Done")
          && twoStepProcesses.Count == 2 && twoStepProcesses[0].Station == "PM1" && twoStepProcesses[1].Station == "PM2"
          && twoStepProcesses[1].Recipe == "R2",
        "两步加工：PM1（R1）、PM2（R2）都做了，第一站做完没跳过第二站；每一格都完成");
    Check(twoStep.Tasks[0].Station == "LP1" && twoStep.Tasks[0].Slot == 3 && twoStep.Tasks[0].Robot == "Robot1"
          && twoStep.Tasks[1].Station == "PM1" && twoStep.Tasks[^1].Station == "LP1" && twoStep.Tasks[^1].Slot == 3,
        "每一格记下实际的站点、槽、机械手：从 LP1 第 3 槽取、放进 PM1，最后放回 LP1 第 3 槽");
    Check(PjOf("LOT-A-1")!.Wafers.All(wafer => IsDone(wafer)
          && wafer.Tasks.Count(task => task.Kind == "Process" && task.State == "Done") == 1), "SEQ_A 的片各做一站");
    Check(PjOf("LOT-A-1")!.Wafers.Select(wafer => wafer.Tasks[1].Station).Distinct().Count() == 2,
        "站点组：两片分到了 PM1、PM2 两个腔（放片那一刻在组里挑空的）");
    Check(lp1.Carrier?.AccessStatus == CarrierAccessStatus.Complete, "CJ 完成告诉 LoadPort 载具干完了（E87 CarrierComplete）");
    Check(events.WaitFor("CJ LOT-A #10"), "上报口收到 CJ 完成");
    Check(events.Numbers("PJ LOT-A-1").SequenceEqual(new[] { 1, 2, 4, 6, 7 }) && events.Numbers("PJ LOT-A-2").SequenceEqual(new[] { 1, 2, 4, 6, 7 }),
        "PJ 转换号照 E40：#1 建、#2 准备、#4 自动开始、#6 加工完、#7 结束，实际 " + string.Join(",", events.Numbers("PJ LOT-A-2")));
    Check(events.Numbers("CJ LOT-A").SequenceEqual(new[] { 1, 3, 6, 7, 10 }),
        "CJ 转换号照 E94：#1 建、#3 选中、#6 等启动、#7 启动、#10 完成，实际 " + string.Join(",", events.Numbers("CJ LOT-A")));
    Check(events.IndexOf("PJ LOT-A-2 #7") < events.IndexOf("CJ LOT-A #10"), "PJ 结束先于 CJ 完成报出去（同一条派发线程）");
    Check(jobs.OwnerOf(first.Id) is null, "PJ 结束放开它名下的片");

    Tick();
    Check(CjOf("LOT-A")?.State == (int)ControlJobState.Completed && lp1.IsCarrierArrived,
        "CJ 完成但载具仍在位时保留，等待载具移走");
    UnloadCarrier(lp1);
    Check(RunUntil(() => CjOf("LOT-A") is null, 20), "载具拿走：完成的 CJ 删掉（#13）");
    Check(WaitDb(db => db.Queryable<ControlJobEntity>().Where(row => row.Name == "LOT-A").ToList()
            .Any(row => row.CompletedBy == 10 && row.EndedBy == 13 && row.LoadPort == "LP1" && !row.Restarted)),
        "CJ 记进库：control_job 一个 CJ 一行，#10 完成、#13 删掉");
    Check(WaitDb(db =>
        {
            var control = db.Queryable<ControlJobEntity>().Where(row => row.Name == "LOT-A").ToList();
            var rows = db.Queryable<ProcessJobEntity>().Where(row => row.ControlJob == "LOT-A").ToList();
            return control.Count == 1 && rows.Count == 2
                && rows.All(row => row.EndedBy == 7 && row.ControlJobRowId == control[0].Id && row.LotId == "LOT-A"
                    && WafersOf(row).Count > 0 && WafersOf(row).All(wafer => wafer.Tasks.All(task => task.State == "Done")));
        }),
        "PJ 记进库：process_job 一个 PJ 一行，记着 CJ 的行号，#7 结束，每片的任务明细（都做完了）跟着进库");

    // 8. 配方快照 + 回到别的 LoadPort：建好之后流程配方改名、删掉都不影响；最后一步只勾了 LP2，片放到 LP2 同号槽。
    LoadCarrier(lp1, 1, 2);
    LoadCarrier(lp2);
    var snapshotJob = Create("LOT-C", "LP1", true, (1, "SEQ_C"), (2, "SEQ_C"));
    Check(snapshotJob.IsSuccess, $"建 LOT-C：{snapshotJob.ErrorMessage}");
    Check(sequences.Rename(3, "SEQ_C2", "Smoke").IsOk && sequences.Delete(3, "Smoke").IsOk, "库里改名、再删掉");
    var movedIds = new[] { ledger.Get("LP1", 1)!.Id, ledger.Get("LP1", 2)!.Id };
    Check(RunUntil(() => CjOf("LOT-C")?.State == (int)ControlJobState.Completed), "自动启动（#5）跑完");
    Check(PjOf("LOT-C-1")?.Sequence == "SEQ_C" && events.Numbers("CJ LOT-C").SequenceEqual(new[] { 1, 3, 5, 10 }),
        "照快照跑：PJ 记的还是 SEQ_C；CJ 自动启动 #5");
    Check(ledger.Get("LP2", 1)?.Id == movedIds[0] && ledger.Get("LP2", 2)?.Id == movedIds[1] && ledger.Get("LP1", 1) is null,
        "回到 LP2 的同号槽，不回原槽");
    UnloadCarrier(lp1);
    UnloadCarrier(lp2);
    Check(RunUntil(() => CjOf("LOT-C") is null, 20), "载具拿走，LOT-C 删掉");

    // 9. PJ 暂停：停投新片，机内的照常做完回片，机内没这个 PJ 的片了才 PAUSED；恢复接着投。
    LoadCarrier(lp1, 1, 2, 3, 4);
    Check(Create("LOT-P", "LP1", true, (1, "SEQ_A"), (2, "SEQ_A"), (3, "SEQ_A"), (4, "SEQ_A")).IsSuccess, "建 LOT-P");
    Check(RunUntil(() => PjOf("LOT-P-1")?.Wafers.Any(IsProcessing) == true), "跑到有片在加工");
    Check(Do(jobs.PauseProcessJobAsync("LOT-P-1")).IsSuccess
          && PjOf("LOT-P-1")?.State == (int)ProcessJobState.Pausing, "PJ 暂停：PAUSING（#8）");
    int fedAtPause = PjOf("LOT-P-1")!.Wafers.Count(wafer => !IsWaiting(wafer));
    Check(RunUntil(() => PjOf("LOT-P-1")?.State == (int)ProcessJobState.Paused), "机内的片做完回来了：PAUSED（#9）");
    var paused = PjOf("LOT-P-1")!;
    Check(paused.Wafers.Count(wafer => !IsWaiting(wafer)) == fedAtPause && paused.Wafers.Any(IsWaiting)
          && paused.Wafers.Where(wafer => !IsWaiting(wafer)).All(IsReturned),
        "暂停后没再投新片，投出去的都回片了");
    Check(paused.Wafers.Where(IsWaiting).All(wafer => ledger.Get(wafer.SourcePort, wafer.SourceSlot)?.WaferId == wafer.WaferId),
        "没投的片还在来源槽（账上）");
    Check(Do(jobs.ResumeProcessJobAsync("LOT-P-1")).IsSuccess
          && PjOf("LOT-P-1")?.State == (int)ProcessJobState.Processing, "恢复：回到 PROCESSING（#10）");
    Check(RunUntil(() => CjOf("LOT-P")?.State == (int)ControlJobState.Completed), "恢复后接着投，跑完");
    Check(events.Numbers("PJ LOT-P-1").SequenceEqual(new[] { 1, 2, 4, 8, 9, 10, 6, 7 }), "转换号：#8 暂停、#9 暂停到位、#10 恢复");
    UnloadCarrier(lp1);
    Check(RunUntil(() => CjOf("LOT-P") is null, 20), "LOT-P 删掉");

    // 10. CJ 暂停（E94）：只是不再启动新的 PJ，在跑的 PJ 照常投片做完；恢复后再启动下一个 PJ。
    LoadCarrier(lp1, 1, 2, 3);
    Check(Create("LOT-Q", "LP1", true, (1, "SEQ_A"), (2, "SEQ_A"), (3, "SEQ_B")).IsSuccess, "建 LOT-Q");
    Check(RunUntil(() => PjOf("LOT-Q-1")?.State == (int)ProcessJobState.Processing), "第一个 PJ 在跑");
    Check(Do(jobs.PauseControlJobAsync("LOT-Q")).IsSuccess
          && CjOf("LOT-Q")?.State == (int)ControlJobState.Paused, "CJ 暂停：PAUSED（#8）");
    Check(RunUntil(() => PjOf("LOT-Q-1")?.EndedBy == 7), "在跑的 PJ 照常投片、做完（#7）");
    Tick();
    Check(PjOf("LOT-Q-2")?.State == (int)ProcessJobState.QueuedPooled && CjOf("LOT-Q")?.State == (int)ControlJobState.Paused,
        "下一个 PJ 不启动，CJ 还是 PAUSED");
    Check(Do(jobs.ResumeControlJobAsync("LOT-Q")).IsSuccess,
        "CJ 恢复（#9）");
    Check(RunUntil(() => CjOf("LOT-Q")?.State == (int)ControlJobState.Completed) && CjOf("LOT-Q")?.CompletedBy == 10,
        "恢复后启动下一个 PJ，都做完 #10");
    UnloadCarrier(lp1);
    Check(RunUntil(() => CjOf("LOT-Q") is null, 20), "LOT-Q 删掉");

    // 11. CJ 停止：不再投片，机内的走完回片；没投的记未执行；PJ 停完（#17）后 CJ 进 COMPLETED（#11）。
    LoadCarrier(lp1, 1, 2, 3, 4);
    Check(Create("LOT-S", "LP1", true, (1, "SEQ_A"), (2, "SEQ_A"), (3, "SEQ_A"), (4, "SEQ_A")).IsSuccess, "建 LOT-S");
    Check(RunUntil(() => PjOf("LOT-S-1")?.Wafers.Any(IsProcessing) == true), "跑到有片在加工");
    Check(Do(jobs.StopControlJobAsync("LOT-S", ControlJobAction.SaveJobs)).IsSuccess
          && PjOf("LOT-S-1")?.State == (int)ProcessJobState.Stopping && CjOf("LOT-S")?.Ending == "Stop"
          && CjOf("LOT-S")?.State == (int)ControlJobState.Executing, "CJ 停止：PJ 进 STOPPING（#11），CJ 状态值不变、标着停止中");
    Refuses(Do(jobs.ExecuteControlJobCommandAsync("LOT-S", ControlJobCommand.Pause, ControlJobAction.SaveJobs)),
        ErrorCodes.JobEnding, ["LOT-S", "CJPause"], "停止中不收暂停");
    Check(RunUntil(() => CjOf("LOT-S")?.State == (int)ControlJobState.Completed), "停完");
    var stopped = PjOf("LOT-S-1")!;
    Check(CjOf("LOT-S")?.CompletedBy == 11 && stopped.EndedBy == 17 && stopped.Wafers.Any(IsNotRun)
          && stopped.Wafers.Where(wafer => !IsNotRun(wafer)).All(IsDone),
        "CJ #11、PJ #17；投出去的片任务都做完，没投的片任务都记未执行");
    Check(stopped.Wafers.All(wafer => ledger.Get(wafer.SourcePort, wafer.SourceSlot)?.ProcessState
          == (IsNotRun(wafer) ? WaferProcessState.Idle : WaferProcessState.Completed)),
        "做没做成看晶圆账（都回原槽）：投出去的工艺完成，没投的还是没做");
    UnloadCarrier(lp1);
    Check(RunUntil(() => CjOf("LOT-S") is null, 20), "LOT-S 删掉");

    // 12. PJ 中止：中止搬运、给在做工艺的腔体发中止；设备中止做完、在途动作都结束、片位都确定才结束（#16）。机内的片记中止，没投的记未执行。
    //     PM2 离线：片都去 PM1，加工时机械手闲着（手臂在动时中止，片位要人工确认，那条路在第 4 节验过）。
    LoadCarrier(lp1, 1, 2, 3);
    pm2.Offline();
    Check(Create("LOT-T", "LP1", true, (1, "SEQ_A"), (2, "SEQ_A"), (3, "SEQ_A")).IsSuccess, "建 LOT-T");
    Check(RunUntil(() => PjOf("LOT-T-1")?.Wafers.Any(IsProcessing) == true), "跑到有片在加工");
    var processingIn = PjOf("LOT-T-1")!.Wafers.SelectMany(wafer => wafer.Tasks).First(task => task.Kind == StationTaskAction.Process && task.State == "Running").Station;
    var processingChamber = processingIn == "PM1" ? pm1 : pm2;
    int abortsBefore = processingChamber.Aborts;
    Check(Do(jobs.AbortProcessJobAsync("LOT-T-1")).IsSuccess
          && PjOf("LOT-T-1")?.State == (int)ProcessJobState.Aborting, "PJ 中止：ABORTING（#13）");
    Check(processingChamber.Aborts == abortsBefore + 1, "给在做这个 PJ 工艺的腔体发了中止");
    jobs.Tick();
    Check(PjOf("LOT-T-1")?.State == (int)ProcessJobState.Aborting, "腔体的中止动作还没做完：PJ 还在 ABORTING");
    Check(RunUntil(() => CjOf("LOT-T")?.State == (int)ControlJobState.Completed), "中止做完");
    var aborted = PjOf("LOT-T-1")!;
    Check(aborted.EndedBy == 16 && aborted.Wafers.Any(wafer => !IsNotRun(wafer) && !IsReturned(wafer)) && aborted.Wafers.Any(IsNotRun),
        "PJ #16：机内的片停在半路（后面的任务记未执行），没投的片任务都记未执行");
    Check(ledger.Get(processingIn, 1)?.ProcessState == WaferProcessState.Aborted, "腔里那片的工艺被中止：晶圆账记着中止");
    Check(events.Numbers("PJ LOT-T-1").Contains(13) && events.Numbers("PJ LOT-T-1").Last() == 16, "转换号：#13 中止、#16 中止做完");
    // 中止后留在腔里的片人工收回（账跟着挪），腔体恢复待命
    foreach (var chamber in new[] { pm1, pm2 })
    {
        var left = ledger.Get(chamber.Name, 1);
        if (left is not null)
        {
            int free = Enumerable.Range(1, 25).First(slot => ledger.Get("LP1", slot) is null);
            Check(ledger.Move(chamber.Name, 1, "LP1", free), "中止后人工收回腔里的片");
        }

        chamber.NoteState(ModuleState.Idle);
    }

    robot.NoteState(ModuleState.Idle);
    pm2.Online();
    UnloadCarrier(lp1);
    Check(RunUntil(() => CjOf("LOT-T") is null, 20), "LOT-T 删掉");

    // 13. 工艺没做成：那一格出错停住（不跳过），这片后面的任务都等着；别的片照常用组里别的腔跑完，整机不停。
    //     人把腔体复位后点"重做"：工艺重新起，做成了接着回片。人工处理只认出错的任务。
    LoadCarrier(lp1, 1, 2);
    pm1.FailNextProcess = true;
    Check(Create("LOT-F", "LP1", true, (1, "SEQ_A"), (2, "SEQ_A")).IsSuccess, "建 LOT-F（PM1、PM2 都在线）");
    Check(RunUntil(() => PjOf("LOT-F-1")?.Wafers.Any(wafer => ErrorOf(wafer) is not null) == true), "PM1 的工艺没做成：那一格记成出错");
    var failedRow = PjOf("LOT-F-1")!.Wafers.Single(wafer => ErrorOf(wafer) is not null);
    var failedTask = ErrorOf(failedRow)!;
    int failedIndex = failedRow.Tasks.IndexOf(failedTask);
    Check(failedTask.Kind == StationTaskAction.Process && failedTask.Station == "PM1" && failedTask.Code == ErrorCodes.DeviceFailed && failedIndex == 2
          && failedRow.Tasks.Skip(3).All(task => task.State == "Waiting")
          && pm1.State == ModuleState.Error && ledger.Get("PM1", 1)?.ProcessState == WaferProcessState.Failed,
        "停在工艺那一格（带错误码），后面的取片、回片都等着，没跳过；腔体报错停着，账上工艺状态是失败");
    string otherName = PjOf("LOT-F-1")!.Wafers.Single(wafer => ErrorOf(wafer) is null).WaferId;
    Check(RunUntil(() => PjOf("LOT-F-1")?.Wafers.Single(wafer => wafer.WaferId == otherName) is JobWaferDto other && IsReturned(other)),
        "另一片照常用 PM2 做完回片（整机不停）");
    Check(PjOf("LOT-F-1")?.State == (int)ProcessJobState.Processing && PjOf("LOT-F-1")!.Wafers.Count(wafer => ErrorOf(wafer) is not null) == 1,
        "PJ 还在 PROCESSING，标着有片的任务出错等人处理");
    var otherRow = PjOf("LOT-F-1")!.Wafers.Single(wafer => wafer.WaferId == otherName);
    Refuses(Do(jobs.RetryTaskAsync("LOT-F-1", otherRow.SourceSlot, 2)), ErrorCodes.JobTaskNotError, ["LOT-F-1", otherName, "3"],
        "没出错的任务不用处理：拒");
    Refuses(Do(jobs.RetryTaskAsync("LOT-F-1", 99, 0)), ErrorCodes.JobTaskNotFound, ["LOT-F-1", "99", "1"], "没有这一片：拒");
    pm1.NoteState(ModuleState.Idle);
    Check(Do(jobs.RetryTaskAsync("LOT-F-1", failedRow.SourceSlot, failedIndex)).IsSuccess, "腔体复位后重做");
    Check(RunUntil(() => CjOf("LOT-F")?.State == (int)ControlJobState.Completed), "重做的工艺做成了，接着回片，跑完");
    var retried = PjOf("LOT-F-1")!;
    var retriedTask = retried.Wafers.Single(wafer => wafer.WaferId == failedRow.WaferId).Tasks[failedIndex];
    Check(retried.EndedBy == 7 && retried.Wafers.All(IsDone)
          && retriedTask.State == "Done" && retriedTask.Code.Length == 0
          && ledger.Get("LP1", failedRow.SourceSlot)?.ProcessState == WaferProcessState.Completed,
        "两片都做完：重做的那一格完成、出错原因清掉；设备重做成了，账上工艺状态是完成");
    UnloadCarrier(lp1);
    Check(RunUntil(() => CjOf("LOT-F") is null, 20), "LOT-F 删掉");

    // 13b. 再来一次没做成，这回人在腔体手动页把工艺做完了：点"标记完成"，这一格算完成（手动），接着回片。
    LoadCarrier(lp1, 1);
    pm2.Offline();
    pm1.FailNextProcess = true;
    Check(Create("LOT-G", "LP1", true, (1, "SEQ_A")).IsSuccess, "建 LOT-G（PM2 离线，去 PM1）");
    Check(RunUntil(() => PjOf("LOT-G-1")?.Wafers.Any(wafer => ErrorOf(wafer) is not null) == true), "工艺没做成：出错停住");
    pm1.NoteState(ModuleState.Idle);
    Check(Do(jobs.CompleteTaskAsync("LOT-G-1", 1, failedIndex)).IsSuccess, "人工做完了：标记完成");
    Check(RunUntil(() => CjOf("LOT-G")?.State == (int)ControlJobState.Completed), "接着回片，跑完");
    var manualTask = PjOf("LOT-G-1")!.Wafers[0].Tasks[failedIndex];
    Check(manualTask.State == "Done" && IsDone(PjOf("LOT-G-1")!.Wafers[0]), "那一格记成完成，这片照常回片");
    Check(ledger.Get("LP1", 1)?.ProcessState == WaferProcessState.Failed,
        "标记完成不改账：片的工艺状态跟着腔体走（这里没人真做，还是失败）");
    pm2.Online();
    UnloadCarrier(lp1);
    Check(RunUntil(() => CjOf("LOT-G") is null, 20), "LOT-G 删掉");

    // 14. 片不在该在的地方（人改了账）：那一行的当前任务出错停住，别的片不受影响；标记完成要片真在这一步做完的地方；
    //     人用恢复操作把片搬回原槽（同一个 LoadPort 里换槽，只抢一次环），点重做，接着走。顺带：Manual 下自动启动的 Job 也会开始，只是不派动作。
    LoadCarrier(lp1, 1, 2);
    transfers.StopAutoDispatch();
    Check(Create("LOT-L", "LP1", true, (1, "SEQ_A"), (2, "SEQ_A")).IsSuccess, "建 LOT-L");
    Check(RunUntil(() => PjOf("LOT-L-1")?.State == (int)ProcessJobState.Processing, 20), "Manual 下 PJ 也会开始（#4），只是不派动作");
    Check(PjOf("LOT-L-1")!.Wafers.All(IsWaiting), "Manual 下没派动作：片都还没投");
    string lostName = ledger.Get("LP1", 2)!.WaferId;
    Check(ledger.ManualMove("LP1", 2, "LP1", 10, "Smoke", "smoke") == WaferAdjustResult.Ok, "人工把第 2 槽的片挪到第 10 槽");
    Tick();
    var lostRow = PjOf("LOT-L-1")!.Wafers.Single(wafer => wafer.WaferId == lostName);
    var lostTask = ErrorOf(lostRow);
    Check(lostTask is not null && lostRow.Tasks.IndexOf(lostTask) == 0 && lostTask.Code == ErrorCodes.JobWaferMoved
          && lostTask.Args.SequenceEqual(new[] { lostName, "LP1.02" })
          && PjOf("LOT-L-1")!.Wafers.Count(wafer => ErrorOf(wafer) is not null) == 1,
        "片不在该在的地方：那一行的当前任务（取片）出错，写着该在 LP1.02；另一片不受影响");
    Refuses(Do(jobs.CompleteTaskAsync("LOT-L-1", 2, 0)), ErrorCodes.JobTaskPositionMismatch, [lostName, StationTaskAction.Pick, "LP1.10"],
        "片不在这一步做完该在的地方（不在机械手上，也不在放片能去的站点）：标记不了完成");
    var back = Finish(StartTransfer("LP1", 10, "LP1", 2, origin: TransferOrigin.Recovery));
    Check(back.IsSuccess && ledger.Get("LP1", 2)?.WaferId == lostName && lp1.State == LoadPortState.Loaded,
        "恢复操作把片搬回第 2 槽（同一个 LoadPort 里换槽），LoadPort 回到待命");
    Check(Do(jobs.RetryTaskAsync("LOT-L-1", 2, 0)).IsSuccess && PjOf("LOT-L-1")!.Wafers.All(wafer => ErrorOf(wafer) is null), "片放回去以后重做：出错清掉");
    transfers.StartAutoDispatch();
    Check(RunUntil(() => CjOf("LOT-L")?.State == (int)ControlJobState.Completed), "跑完");
    var lost = PjOf("LOT-L-1")!.Wafers.Single(wafer => wafer.WaferId == lostName);
    Check(IsDone(lost) && ledger.Get("LP1", 2)?.WaferId == lostName && ledger.Get("LP1", 10) is null
          && ledger.Get("LP1", 2)?.ProcessState == WaferProcessState.Completed,
        "放回去的那片照常做完，回到第 2 槽");
    UnloadCarrier(lp1);
    Check(RunUntil(() => CjOf("LOT-L") is null, 20), "LOT-L 删掉");

    // 14b. Job 的搬运动过手才失败（取片失败）：出错记在取片上（操作未确认取片），放片退回等着做，搬运资源保留；
    //      人确认片还在源槽、放锁、设备复位后点重做，接着跑完。
    LoadCarrier(lp1, 1);
    robot.FailNextPick = true;
    Check(Create("LOT-K", "LP1", true, (1, "SEQ_A")).IsSuccess, "建 LOT-K");
    Check(RunUntil(() => PjOf("LOT-K-1")?.Wafers.Any(wafer => ErrorOf(wafer) is not null) == true), "取片动过手才失败：那一格出错");
    var pickRow = PjOf("LOT-K-1")!.Wafers[0];
    var pickError = ErrorOf(pickRow)!;
    Check(pickRow.Tasks.IndexOf(pickError) == 0 && pickError.Code == ErrorCodes.TransferFailed && pickRow.Tasks[1].State == "Waiting"
          && transfers.HeldOperations.Count == 1,
        "出错记在取片上（操作未确认取片），放片退回等着做；搬运资源保留等人确认");
    Check(transfers.ReleaseHold(transfers.HeldOperations.Single().WaferId), "人确认片还在源槽，放锁");
    robot.NoteState(ModuleState.Idle);
    lp1.NoteState(LoadPortState.Loaded);
    pm1.NoteState(ModuleState.Idle);
    pm2.NoteState(ModuleState.Idle);
    Check(Do(jobs.RetryTaskAsync("LOT-K-1", 1, 0)).IsSuccess, "重做取片");
    Check(RunUntil(() => CjOf("LOT-K")?.State == (int)ControlJobState.Completed) && IsDone(PjOf("LOT-K-1")!.Wafers[0]), "接着跑完");
    UnloadCarrier(lp1);
    Check(RunUntil(() => CjOf("LOT-K") is null, 20), "LOT-K 删掉");

    // 放片失败：取片格已完成，错误留在当前放片格；恢复后只放片，不重复取片。
    LoadCarrier(lp1, 1);
    robot.FailNextPlace = true;
    Check(Create("LOT-V", "LP1", true, (1, "SEQ_A")).IsSuccess, "建 LOT-V");
    Check(RunUntil(() => PjOf("LOT-V-1")?.Wafers.Any(wafer => ErrorOf(wafer) is not null) == true), "放片失败后当前任务出错");
    var placeRow = jobs.FindChild<SmokeTasks>()!.Rows.Single(row => row.Owner == "LOT-V-1");
    Check(placeRow.Tasks[0].State == WaferTaskState.Done && placeRow.Tasks[1].State == WaferTaskState.Error
          && placeRow.Tasks[1].Code == ErrorCodes.TransferFailed && placeRow.Tasks[1].Args.SequenceEqual(new[] { "Robot1", "Place" })
          && placeRow.Tasks[2].State == WaferTaskState.Waiting && placeRow.Tasks.All(task => task.Operation is null),
        "取片完成，放片记错，工艺等待，设备操作引用已收尾清空");
    var heldPlace = transfers.HeldOperations.Single();
    Check(heldPlace.HasPicked && ledger.Get("Robot1", heldPlace.Arm)?.Id == placeRow.WaferId
          && ledger.Get("LP1", 1) is null && ledger.Get("PM1", 1) is null, "取片已记账，失败后片仍在手上");
    Check(transfers.ReleaseHold(placeRow.WaferId), "人工确认片在手上后释放资源");
    robot.NoteState(ModuleState.Idle);
    pm1.NoteState(ModuleState.Idle);
    pm2.NoteState(ModuleState.Idle);
    Check(Do(jobs.RetryTaskAsync("LOT-V-1", 1, 1)).IsSuccess, "只重做当前放片任务");
    Check(RunUntil(() => placeRow.Tasks[1].State == WaferTaskState.Running), "重新启动放片");
    Check(placeRow.Tasks[1].Operation is TransferRoutine { Source: null, SourceName: "Robot1" }
          && placeRow.Tasks[0].State == WaferTaskState.Done, "片在手上时直接放片，取片保持完成");
    Check(RunUntil(() => CjOf("LOT-V")?.State == (int)ControlJobState.Completed) && IsDone(PjOf("LOT-V-1")!.Wafers[0])
          && ledger.Get("LP1", 1)?.Id == placeRow.WaferId, "恢复后工艺和回片正常完成");
    UnloadCarrier(lp1);
    Check(RunUntil(() => CjOf("LOT-V") is null, 20), "LOT-V 删掉");

    // 15. Host 的做法：先建 PJ（不归任何 CJ，排着），再建 CJ 把 PJ 按顺序收进来。
    jobs.ControlJobAutoStart = true;
    LoadCarrier(lp1, 1, 2);
    lp1.SetCarrierId("CAR-1");
    Check(Do(jobs.CreateProcessJobAsync(null, "PJ-H1", [1], "SEQ_A", null, "CAR-1")).IsSuccess, "Host 建 PJ-H1");
    Check(PjOf("PJ-H1")?.ControlJob == string.Empty && PjOf("PJ-H1")?.State == (int)ProcessJobState.QueuedPooled, "PJ 先建：不归任何 CJ，排着");
    Refuses(Do(jobs.CreateProcessJobAsync(null, "PJ-H9", [1], "SEQ_A", null, "NOPE")),
        ErrorCodes.JobCarrierNotFound, ["NOPE"], "载具不在任何 LoadPort 上");
    Refuses(Do(jobs.CreateProcessJobAsync(null, "PJ-H1", [2], "SEQ_A", null, "CAR-1")), ErrorCodes.JobIdDuplicate, ["PJ-H1"], "PJ 名重了");
    Check(Do(jobs.CreateProcessJobAsync(null, "PJ-H2", [2], "SEQ_A", null, "CAR-1")).IsSuccess, "Host 建 PJ-H2");
    Refuses(Do(jobs.CreateControlJobAsync(null, ["PJ-H1", "NOPE"], "CJ-H")),
        ErrorCodes.JobProcessJobUnavailable, ["NOPE"], "CJ 收的 PJ 不存在：整个不建");
    Check(PjOf("PJ-H1")?.ControlJob == string.Empty, "没建成的 CJ 不占 PJ");
    Check(Do(jobs.CreateControlJobAsync(null, ["PJ-H1", "PJ-H2"], "CJ-H")).IsSuccess,
        "Host 建 CJ-H，收下两个 PJ");
    IJobManager forEap = jobs;
    Check(forEap.FindControlJobByCarrier("car-1")?.Id == "CJ-H"
          && forEap.FindProcessJobsByCarrier("CAR-1").Select(job => job.Id).SequenceEqual(new[] { "PJ-H1", "PJ-H2" })
          && forEap.FindControlJobByCarrier("NOPE") is null && forEap.FindProcessJobsByCarrier(" ").Count == 0
          && PjOf("PJ-H1")?.CarrierId == "CAR-1",
        "EAP 按载具号找 CJ、PJ（不分大小写，找不到为空）；PJ 记着建的时候的载具号");
    var foundControl = forEap.FindControlJobByCarrier(" CAR-1 ")!;
    foundControl.CarrierId = "EDITED";
    Check(forEap.FindControlJobByCarrier("CAR-1")?.Id == "CJ-H"
          && forEap.FindControlJobByCarrier("EDITED") is null && CjOf("CJ-H")?.CarrierId == "CAR-1",
        "CJ 按载具号从队列查询，返回独立 DTO；修改查询结果不影响队列或后续查询");
    var previousQuery = forEap.Snapshot;
    bool previousAutoStart = jobs.ProcessJobAutoStart;
    jobs.ProcessJobAutoStart = !previousAutoStart;
    Check(forEap.Snapshot.ProcessJobs.Where(job => job.Id is "PJ-H1" or "PJ-H2")
              .All(job => job.AutoStart == !previousAutoStart)
          && forEap.FindProcessJobsByCarrier("CAR-1").All(job => job.AutoStart == !previousAutoStart)
          && previousQuery.ProcessJobs.First(job => job.Id == "PJ-H1").AutoStart == previousAutoStart,
        "查询读取当前配置，无需先发布；之前取得的 DTO 不随运行对象改变");
    jobs.ProcessJobAutoStart = previousAutoStart;
    previousQuery.ControlJobs.First(job => job.Id == "CJ-H").ProcessJobs.Clear();
    previousQuery.ProcessJobs.First(job => job.Id == "PJ-H1").Wafers[0].Tasks.Clear();
    var foundProcess = forEap.FindProcessJobsByCarrier("CAR-1").First(job => job.Id == "PJ-H1");
    foundProcess.CarrierId = "EDITED";
    foundProcess.Wafers.Clear();
    Check(forEap.Snapshot.ControlJobs.First(job => job.Id == "CJ-H").ProcessJobs.Count == 2
          && forEap.Snapshot.ProcessJobs.First(job => job.Id == "PJ-H1").Wafers[0].Tasks.Count > 0
          && forEap.FindProcessJobsByCarrier("CAR-1").First(job => job.Id == "PJ-H1").Wafers.Count == 1,
        "全量和载具查询均返回独立 DTO，修改嵌套集合不影响实际任务");
    Check(RunUntil(() => CjOf("CJ-H")?.State == (int)ControlJobState.Completed) && CjOf("CJ-H")?.CompletedBy == 10, "Host 建的照样跑完");
    Check(CjOf("CJ-H")?.AutoStart == true, "SC 开启 CJ 自动启动，创建后无需 CJ Start 即执行完成");
    Check(forEap.FindProcessJobsByCarrier("CAR-1").Select(job => job.Id).SequenceEqual(new[] { "PJ-H1", "PJ-H2" })
          && forEap.FindProcessJobsByCarrier("CAR-1").All(job => job.EndedBy == 7),
        "CJ 保留期间按载具查询仍包含其已结束的 PJ");
    UnloadCarrier(lp1);
    Check(RunUntil(() => CjOf("CJ-H") is null, 20), "CJ-H 删掉");
    Check(forEap.FindControlJobByCarrier("CAR-1") is null, "CJ 从队列删除后按载具号查找为空");
    Check(forEap.FindProcessJobsByCarrier("CAR-1").Count == 0, "CJ 删除后按载具查询不再返回已结束的 PJ");

    // 15b. 独立 PJ 的创建、取消，再用名称集合创建 Job；不重新创建 PJ 或任务行。
    using (var requestStream = new MemoryStream())
    {
        ProtoBuf.Serializer.Serialize(requestStream, new ProcessJobCreateRequest
        {
            LoadPort = "LP1", Name = "PJ-WAIT", Slots = [1, 2], Sequence = "SEQ_A", LotId = "LOT-WAIT",
        });
        requestStream.Position = 0;
        var decoded = ProtoBuf.Serializer.Deserialize<ProcessJobCreateRequest>(requestStream);
        Check(decoded.Slots.SequenceEqual(new[] { 1, 2 }) && decoded.LotId == "LOT-WAIT",
            "PJ 创建请求经过 protobuf 后仍保留槽位集合和批次号");
    }
    LoadCarrier(lp1, 1, 2);
    var manualPj = new ProcessJobCreateRequest { LoadPort = "LP1", Name = "PJ-M1", Slots = [1], Sequence = "SEQ_A", LotId = "LOT-MANUAL" };
    Check(Pump(jobService.CreateProcessJobAsync(manualPj)).Success && PjOf("PJ-M1")?.LotId == "LOT-MANUAL",
        "独立创建 PJ：LoadPort、PJ 名、槽位、Sequence、LotId 当场记录");
    Check(Pump(jobService.CancelProcessJobAsync(new JobCommandRequest { JobId = "PJ-M1" })).Success
          && PjOf("PJ-M1") is null && jobs.OwnerOf(ledger.Get("LP1", 1)!.Id) is null, "取消独立 PJ，释放片归属");
    Check(Pump(jobService.CreateProcessJobAsync(manualPj)).Success, "取消后可以再次创建同名 PJ");
    Check(Do(jobs.CreateProcessJobAsync("LP1", "PJ-M2", new[] { 2 }, "SEQ_B", "LOT-MANUAL")).IsSuccess,
        "参数形式创建第二个 PJ");
    var manualRows = jobs.FindChild<SmokeTasks>()!.Rows.Where(row => row.Owner is "PJ-M1" or "PJ-M2").ToList();
    var manualTasks = manualRows.SelectMany(row => row.Tasks).ToList();
    var assembled = new ControlJobCreateRequest { LoadPort = "LP1", Name = "CJ-MANUAL", ProcessJobs = ["PJ-M1", "PJ-M2"] };
    var wrongPort = Pump(jobService.CreateControlJobAsync(new ControlJobCreateRequest
    {
        LoadPort = "LP2", Name = "CJ-WRONG", ProcessJobs = ["PJ-M1", "PJ-M2"],
    }));
    Check(!wrongPort.Success && wrongPort.Code == ErrorCodes.JobProcessJobUnavailable
          && CjOf("CJ-WRONG") is null && PjOf("PJ-M1")?.ControlJob == string.Empty && PjOf("PJ-M2")?.ControlJob == string.Empty,
        "CJ 的 LoadPort 与 PJ 不匹配：不关联、不删除已建好的 PJ");
    var assembledReply = Pump(jobService.CreateJobAsync(assembled));
    Check(assembledReply.Success && assembledReply.DeserializeData<JobCreatedDto>() is JobCreatedDto assembledJob
          && assembledJob.ControlJob == "CJ-MANUAL" && assembledJob.ProcessJobs.SequenceEqual(new[] { "PJ-M1", "PJ-M2" })
          && CjOf("CJ-MANUAL")?.LotId == "LOT-MANUAL", "CreateJob 使用 PJ 名称集合创建 CJ，批次号可从 PJ 取得");
    Check(manualRows.All(row => jobs.FindChild<SmokeTasks>()!.Rows.Contains(row))
          && manualTasks.SequenceEqual(manualRows.SelectMany(row => row.Tasks))
          && PjOf("PJ-M1")?.ControlJob == "CJ-MANUAL" && PjOf("PJ-M2")?.ControlJob == "CJ-MANUAL",
        "已有 PJ 和每片任务原样复用，一个 CJ 关联多个 PJ");
    Refuses(Do(jobs.CreateJobAsync("LP1", new[] { "PJ-M1" }, "CJ-REUSE")), ErrorCodes.JobProcessJobUnavailable,
        ["PJ-M1"], "已归 CJ 的 PJ 不能重复关联另一个 CJ");
    Check(RunUntil(() => CjOf("CJ-MANUAL")?.State == (int)ControlJobState.Completed), "分开创建再关联的 Job 正常执行完成");
    Check(WaitDb(db => db.Queryable<ProcessJobEntity>().Where(row => row.ControlJob == "CJ-MANUAL").ToList()
        .Count(row => row.LotId == "LOT-MANUAL" && row.EndedBy == 7) == 2), "独立 PJ 的 LotId 在关联和完成后仍写进库");
    UnloadCarrier(lp1);
    Check(RunUntil(() => CjOf("CJ-MANUAL") is null, 20), "分开创建的 Job 随载具移走而结束");

    LoadCarrier(lp1, 1);
    Check(Do(jobs.CreateProcessJobAsync("LP1", "PJ-RAW", new[] { 1 }, "SEQ_A", "LOT-RAW")).IsSuccess
          && Pump(jobService.CreateControlJobAsync(new ControlJobCreateRequest
          {
              LoadPort = "LP1", Name = "CJ-RAW", ProcessJobs = ["PJ-RAW"],
          })).Success, "可直接创建 CJ，关联之前创建的 PJ");
    Check(Do(jobs.CancelControlJobAsync("CJ-RAW", ControlJobAction.SaveJobs)).IsSuccess
          && PjOf("PJ-RAW")?.ControlJob == string.Empty && PjOf("PJ-RAW")?.LotId == "LOT-RAW",
        "取消排队 CJ 并保留 PJ：PJ 脱离 CJ，自己的批次号仍保留");
    Check(Do(jobs.CancelProcessJobAsync("PJ-RAW")).IsSuccess, "PJ 有独立取消方法");
    UnloadCarrier(lp1);

    // PJ 启动方式来自 SC，而不是创建请求中的参数。
    jobs.ProcessJobAutoStart = false;
    LoadCarrier(lp1, 1);
    Check(Do(jobs.CreateProcessJobAsync("LP1", "PJ-SC", [1], "SEQ_A", "LOT-SC")).IsSuccess
          && Do(jobs.CreateJobAsync("LP1", ["PJ-SC"], "CJ-SC")).IsSuccess, "SC 关闭 PJ 自动启动时仍可正常创建 PJ 和 CJ");
    Check(RunUntil(() => PjOf("PJ-SC")?.State == (int)ProcessJobState.WaitingForStart), "SC 关闭后 PJ 准备完成进入等待启动");
    Check(PjOf("PJ-SC")?.AutoStart == false && ledger.Get("LP1", 1) is not null
          && PjOf("PJ-SC")!.Wafers[0].Tasks.All(task => task.State == "Waiting"), "等待 PJ Start 时不投片，上报启动方式来自 SC");
    Check(Do(jobs.StartProcessJobAsync("PJ-SC")).IsSuccess
          && RunUntil(() => CjOf("CJ-SC")?.State == (int)ControlJobState.Completed), "PJ Start 后完成取放、工艺和回片");
    UnloadCarrier(lp1);
    Check(RunUntil(() => CjOf("CJ-SC") is null, 20), "SC 手动启动的 Job 随载具移走结束");
    jobs.ProcessJobAutoStart = true;

    // 16. 整机停止：关自动派单、中止搬运操作，Job 走中止（等设备确认、核对片位），不是直接删 Job。
    LoadCarrier(lp1, 1, 2, 3);
    Check(Create("LOT-E", "LP1", true, (1, "SEQ_A"), (2, "SEQ_A"), (3, "SEQ_A")).IsSuccess, "建 LOT-E");
    Check(RunUntil(() => PjOf("LOT-E-1")?.Wafers.Any(IsProcessing) == true && !transfers.IsRobotInTransfer("Robot1")),
        "跑到有片在加工、机械手闲着");
    var equipment = new EquipmentService(modules);
    var stop = equipment.StopAsync(new RpcRequest()).Result;
    Check(stop.Success && stop.DeserializeData<int>() == 0 && !transfers.IsAutoDispatch,
        "整机停止：关自动派单；在给 Job 做工艺的腔体不在这里直接发中止");
    Check(CjOf("LOT-E")?.State == (int)ControlJobState.Aborting && CjOf("LOT-E")?.E94State == 3,
        "内部进入 Aborting，E94 收尾期间仍上报 EXECUTING");
    Check(RunUntil(() => CjOf("LOT-E")?.State == (int)ControlJobState.Aborted) && CjOf("LOT-E")?.CompletedBy == 12
          && PjOf("LOT-E-1")?.EndedBy == 16 && CjOf("LOT-E")?.E94State == 5, "Job 走中止：PJ #16、CJ #12，内部 Aborted 上报 E94 COMPLETED");
    foreach (var chamber in new[] { pm1, pm2 })
    {
        if (ledger.Get(chamber.Name, 1) is not null)
        {
            int free = Enumerable.Range(1, 25).First(slot => ledger.Get("LP1", slot) is null);
            Check(ledger.Move(chamber.Name, 1, "LP1", free), "停止后人工收回腔里的片");
        }

        chamber.NoteState(ModuleState.Idle);
    }

    robot.NoteState(ModuleState.Idle);
    UnloadCarrier(lp1);
    Check(RunUntil(() => CjOf("LOT-E") is null, 20), "LOT-E 删掉");

    // 17. 服务层：Job 服务、搬运服务把结果翻成回包（错误码 + 参数），命令值不认识的拒。
    T Pump<T>(Task<T> task)
    {
        for (int tick = 0; tick < 3000 && !task.IsCompleted; tick++)
        {
            Tick();
            task.Wait(2);
        }

        Check(task.IsCompleted, "服务调用应做完");
        return task.Result;
    }

    var noPort = Pump(jobService.CreateAsync(new JobCreateRequest { LoadPort = "LPX", Slots = [new JobSlotDto { Slot = 1, Sequence = "SEQ_A" }] }));
    Check(!noPort.Success && noPort.Code == ErrorCodes.JobLoadPortNotFound && noPort.Args.SequenceEqual(new[] { "LPX" }), "建 Job 不成：错误码带 LoadPort");
    var unknown = Pump(jobService.ControlJobCommandAsync(new JobCommandRequest { JobId = "NOPE", Command = (int)ControlJobCommand.Start }));
    Check(!unknown.Success && unknown.Code == ErrorCodes.JobNotFound && unknown.Args.SequenceEqual(new[] { "NOPE" }), "没有这个 Job");
    var badCommand = Pump(jobService.ControlJobCommandAsync(new JobCommandRequest { JobId = "NOPE", Command = 99 }));
    Check(!badCommand.Success && badCommand.Code == ErrorCodes.JobCommandNotAllowed, "命令值不认识：拒");
    var noRetry = Pump(jobService.RetryTaskAsync(new JobTaskRequest { ProcessJob = "NOPE", Slot = 1, Task = 0 }));
    var noComplete = Pump(jobService.CompleteTaskAsync(new JobTaskRequest { ProcessJob = "NOPE", Slot = 1, Task = 0 }));
    Check(!noRetry.Success && noRetry.Code == ErrorCodes.JobNotFound && noRetry.Args.SequenceEqual(new[] { "NOPE" })
          && !noComplete.Success && noComplete.Code == ErrorCodes.JobNotFound, "重做、标记完成找不到 PJ：job.not_found");
    var list = Pump(jobService.GetJobsAsync(new RpcRequest())).DeserializeData<JobListDto>();
    Check(list.ControlJobs.Count == 0 && list.ProcessJobs.Count == 0, "查全貌：载具移走后返回当前空的 CJ / PJ 集合");

    LoadCarrier(lp1, 1);
    var transferService = new TransferService(modules);
    var manual = Pump(transferService.TransferAsync(new TransferRequestDto { Source = "LP1", SourceSlot = 1, Target = "PM1", TargetSlot = 1 }));
    Check(manual.Success && manual.DeserializeData<TransferDoneDto>().Robot == "Robot1" && ledger.Get("PM1", 1) is not null,
        "手动传片走搬运管理：搬完回机械手和手");
    var occupied = Pump(transferService.TransferAsync(new TransferRequestDto { Source = "LP1", SourceSlot = 2, Target = "PM1", TargetSlot = 1 }));
    Check(!occupied.Success && occupied.Code == ErrorCodes.WaferNoWafer, "手动传片受理不了：错误码照搬");
    var notHeld = Pump(transferService.ReleaseAsync(new TransferReleaseRequest { WaferId = Guid.Empty }));
    Check(!notHeld.Success && notHeld.Code == ErrorCodes.TransferNotHeld && notHeld.Args.SequenceEqual(new[] { Guid.Empty.ToString() }), "没有留着锁的单：transfer.not_held");
    Check(ledger.Move("PM1", 1, "LP1", 1), "手动传过去的那片人工收回");
    UnloadCarrier(lp1);

    // 17b. 调度只用搬运管理的实时占用：第一台无空手时尝试下一台；两台同时工作也不能抢同一站点。
    secondRobot = new SmokeRobot("Robot2", "LP1", "LP2", "PM1", "PM2");
    Check(secondRobot.Open(), "第二台机械手打开");
    secondRobot.NoteState(ModuleState.Idle);
    transfers.Bind([.. modules, secondRobot]);
    transfers.StartAutoDispatch();
    LoadCarrier(lp1, 1, 2, 3);
    Check(ledger.Move("LP1", 2, "Robot1", 1) && ledger.Move("LP1", 3, "Robot1", 2), "第一台手臂都已持片");
    Check(Create("LOT-M0", "LP1", true, (1, "SEQ_A")).IsSuccess, "建 LOT-M0");
    Check(RunUntil(() => PjOf("LOT-M0-1")?.Wafers[0].Tasks[0].State == "Running"), "取片开始");
    Check(PjOf("LOT-M0-1")!.Wafers[0].Tasks[0].Robot == "Robot2" && !transfers.IsRobotInTransfer("Robot1"),
        "第一台没有空手，直接尝试第二台，失败尝试不占资源");
    Check(RunUntil(() => CjOf("LOT-M0")?.State == (int)ControlJobState.Completed) && IsDone(PjOf("LOT-M0-1")!.Wafers[0]),
        "第二台机械手完成工艺和回片");
    Check(ledger.Move("Robot1", 1, "LP1", 2) && ledger.Move("Robot1", 2, "LP1", 3), "收回第一台手上的片");
    UnloadCarrier(lp1);
    Check(RunUntil(() => CjOf("LOT-M0") is null, 20), "LOT-M0 删掉");

    Sequence(6, "SEQ_F", Step("LoadPort", "", "LP2"), Step("Chamber", "R1", "PM2"), Step("LoadPort", "", "LP2"));
    LoadCarrier(lp1, 1, 2);
    LoadCarrier(lp2, 1);
    Check(Create("LOT-M1", "LP1", true, (1, "SEQ_A"), (2, "SEQ_A")).IsSuccess, "建 LOT-M1");
    Check(Create("LOT-M2", "LP2", true, (1, "SEQ_F")).IsSuccess, "建 LOT-M2");
    Check(RunUntil(() => transfers.IsRobotInTransfer("Robot1") && transfers.IsRobotInTransfer("Robot2")), "不同站点可由两台机械手并行执行");
    var parallelRows = jobs.FindChild<SmokeTasks>()!.Rows;
    Check(parallelRows.Count(row => row.HasRunning) == 2
          && parallelRows.Single(row => row.Owner == "LOT-M1-1" && row.SourceSlot == 2).IsWaiting
          && !transfers.IsSlotLocked("LP1", 2), "同一来源站点的第二片等待，不会因没有本拍占用集合而重复派出");
    Check(RunUntil(() => CjOf("LOT-M1")?.State == (int)ControlJobState.Completed && CjOf("LOT-M2")?.State == (int)ControlJobState.Completed)
          && PjOf("LOT-M1-1")!.Wafers.All(IsDone) && PjOf("LOT-M2-1")!.Wafers.All(IsDone), "两台机械手正常完成全部任务");
    UnloadCarrier(lp1);
    UnloadCarrier(lp2);
    Check(RunUntil(() => CjOf("LOT-M1") is null && CjOf("LOT-M2") is null, 20), "两个 CJ 删掉");
    transfers.Bind(modules);
    secondRobot.Close();
    secondRobot = null;

    // 18. 重启：CJ、PJ 跟着进度记在库里（每次发布交给写库线程），"断电"后新的一个 Job 管理开机查库——
    //     没做完的不接着跑：CJ 记成中止（E94 #12，标着重启）、删掉（#13），PJ 记成中止（E40 #16）；早先做完的不动；片不再归任何 Job。
    LoadCarrier(lp1, 1, 2, 3);
    var lotR = Enumerable.Range(1, 3).Select(slot => ledger.Get("LP1", slot)!.Id).ToArray();
    pm1.ProcessTicks = 200;
    pm2.ProcessTicks = 200;
    transfers.StartAutoDispatch();
    Check(Create("LOT-R", "LP1", true, (1, "SEQ_A"), (2, "SEQ_A"), (3, "SEQ_A")).IsSuccess, "建 LOT-R");
    Check(RunUntil(() => PjOf("LOT-R-1")?.Wafers.Count(IsProcessing) == 2 && !transfers.IsRobotInTransfer("Robot1")),
        "跑到两片在加工、机械手闲着");
    // 等写库线程把最后一次发布的进度写完（写的就是全貌里的那份），旧的 Job 管理之后不再写
    string lotRWafers = JsonHelper.Serialize(PjOf("LOT-R-1")!.Wafers);
    Check(WaitDb(db => db.Queryable<ProcessJobEntity>().Where(row => row.Name == "LOT-R-1").ToList()
            .Any(row => row.State == (int)ProcessJobState.Processing && row.Wafers == lotRWafers && WafersOf(row).Count(IsProcessing) == 2)),
        "Job 进度跟着进库（两片在加工）");
    Thread.Sleep(200);
    transfers.StopAutoDispatch();
    var restarted = new SmokeJobs();
    Probe.Name(restarted, "Job");
    restarted.Database = "JobSmoke";
    restarted.Bind(modules);
    jobs = restarted;
    var afterRestart = jobs.Snapshot;
    Check(afterRestart.ControlJobs.Count == 0 && afterRestart.ProcessJobs.Count == 0, "重启后没有接着跑的 Job");
    Check(WaitDb(db =>
        {
            var rows = db.Queryable<ControlJobEntity>().Where(row => row.Name == "LOT-R").ToList();
            return rows.Count == 1 && rows[0].Restarted && rows[0].CompletedBy == 12 && rows[0].EndedBy == 13
                && rows[0].State == 5; // 历史记录保留 E94 状态编号。
        }),
        "上次没做完的 CJ 库里记成中止（#12）、标着重启、删掉（#13）");
    Check(WaitDb(db =>
        {
            var rows = db.Queryable<ProcessJobEntity>().Where(row => row.ControlJob == "LOT-R").ToList();
            return rows.Count == 1 && rows[0].EndedBy == 16 && rows[0].State == (int)ProcessJobState.Aborted && WafersOf(rows[0]).Count(IsProcessing) == 2;
        }),
        "它下面的 PJ 记成中止（#16），任务明细留着重启前做到哪");
    Check(WaitDb(db => db.Queryable<ControlJobEntity>().Where(row => row.Name == "LOT-E").ToList()
            .Any(row => row.EndedBy == 13 && !row.Restarted)),
        "早先做完的不受影响");
    Check(lotR.All(id => jobs.OwnerOf(id) is null), "片不再归任何 Job");
}
finally
{
    if (Directory.Exists(folder))
    {
        Directory.Delete(folder, recursive: true);
    }

    try
    {
        File.Delete(jobDb);
    }
    catch (IOException)
    {
        // SQLite 连接池可能还占着文件：留在临时目录里，不影响结果
    }
}

Console.WriteLine($"PASS: {checks} job checks (transfer manager: admission checks, competing transfers for one slot, pick confirmation and sequential wafer task execution, " +
    "locks released only after the station rings and the ledger settle, failures without motion releasing locks and failures after motion holding them " +
    "for manual recovery, rejecting a busy robot, aborting and waiting for device confirmation; jobs per SEMI E94/E40: creation checks with nothing left behind (including a station that does not " +
    "support a needed task, a step with no usable station and a missing process recipe), local creation going through PJ-then-CJ like the host " +
    "with already-created PJs cancelled when the CJ is refused, one task row per wafer (pick, place, process, ... return), one carrier with two sequences and a two-step route, " +
    "station groups, two robots sharing live resource occupancy and falling back when an arm is unavailable, transition numbers in order, a resent create refused by the normal checks, sequence snapshots, returning to another LoadPort, PJ pause/resume, " +
    "CJ pause that only stops starting new PJs, CJ stop, PJ abort, a failed process stopping only its own row until retry or manual completion, " +
    "a wafer moved behind the job's back, job pick/place failures and retrying place without repeating pick, host-style PJ-then-CJ creation, finding jobs by carrier ID for EAP, the equipment stop going through job abort, the job and transfer services, " +
    "every CJ and PJ recorded as one database row with each wafer's tasks, " +
    "and a restart that marks unfinished jobs aborted in the database instead of resuming them)");

// ── 假件 ─────────────────────────────────────────────────────────────

// 名字、路径：生产里由 ComponentLoader 经内部 setter 填。
static class Probe
{
    public static void Name(ComponentBase component, string name)
    {
        typeof(ComponentBase).GetProperty(nameof(ComponentBase.Name))!.SetValue(component, name);
        typeof(ComponentBase).GetProperty(nameof(ComponentBase.FullPath))!.SetValue(component, name);
    }

    public static void Enabled(ComponentBase component, bool enabled)
    {
        typeof(ComponentBase).GetProperty(nameof(ComponentBase.IsEnabled))!.SetValue(component, enabled);
    }
}

// 搬运管理、Job 管理：顶替扫描线程推一拍。
sealed class SmokeTransfers : TransferManager
{
    public void Tick() => OnScan();
}

// Job 管理下面挂冒烟的任务组件（生产里是 sc.xml 的 Task 子节点）。
sealed class SmokeJobs : JobManager
{
    public SmokeJobs()
    {
        var tasks = new SmokeTasks();
        Probe.Name(tasks, "Task");
        AddChild(tasks);
    }

    public void Tick() => OnScan();
}

// 没挂任务组件的 Job 管理：装配时应该就抛。
sealed class BareJobs : JobManager
{
}

// 冒烟的任务组件：全用平台的生成规则（跟 35021 的 TaskComponent 一样）。
sealed class SmokeTasks : BaseTaskComponent
{
}

// 假动作：按扫描拍数做完；要失败的到点报失败。
sealed class SmokeMotion : ModuleOperation
{
    private readonly bool _fail;
    private int _left;

    public SmokeMotion(string name, int ticks, bool fail = false) : base(name)
    {
        _left = ticks;
        _fail = fail;
    }

    protected override void OnScan()
    {
        _left--;
        if (_left > 0)
        {
            return;
        }

        if (_fail)
        {
            Fail(ErrorCodes.DeviceFailed, "冒烟里故意失败", Name, "smoke");
        }
        else
        {
            Complete();
        }
    }
}

// 假机械手：取放两拍做完（成功后基类照常记账），取放都可以故意失败一次。
sealed class SmokeRobot : BaseRobotModule
{
    public SmokeRobot(string name, params string[] stations)
    {
        Probe.Name(this, name);
        AddChild(new SmokeRobotShell());
        var nodes = stations
            .Select((station, index) => new ModuleConfig
            {
                Name = station,
                Values = [new ValueConfig { Name = "Number", Value = (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) }],
            })
            .ToList();
        OnSettingLoaded(new ModuleConfig { Name = name, Children = [new ModuleConfig { Name = "Stations", Children = nodes }] });
    }

    public bool FailNextPick { get; set; }

    public bool FailNextPlace { get; set; }

    public void NoteState(int state) => State = state;

    public void Tick() => OnScan();

    public override ModuleOperation? Home() => Begin(RobotAction.Home, new SmokeMotion("Home", 1));

    protected override ModuleOperation? ResetDevice() => Begin(RobotAction.Reset, new SmokeMotion("Reset", 1));

    protected override ModuleOperation? AbortDevice() => Begin(RobotAction.Abort, new SmokeMotion("Abort", 1));

    public override ModuleOperation? PowerOn() => null;

    public override ModuleOperation? PowerOff() => null;

    protected override ModuleOperation CreatePickOperation(int arm, int stationNumber, int slot)
    {
        bool fail = FailNextPick;
        FailNextPick = false;
        return new SmokeMotion("Pick", 2, fail);
    }

    protected override ModuleOperation CreatePlaceOperation(int arm, int stationNumber, int slot)
    {
        bool fail = FailNextPlace;
        FailNextPlace = false;
        return new SmokeMotion("Place", 2, fail);
    }
}

// 假机械手的品牌壳：驱动走假传输，只为把"驱动已连接"这道门打开。
sealed class SmokeRobotShell : RejeRobotComponent
{
    protected override IRobotDriver CreateDriver() => new RejeRobotDriver(new FakeFrameCommunication());
}

// 假 LoadPort：在位、Mapping、状态由测试直接摆；不做 Load / Unload 这些动作。
// 在位走设备上报（Event），下一拍扫描生效；假驱动不回状态查询，用 Query 的话在位永远判不出来。
sealed class SmokePort : BaseLoadPortModule
{
    public SmokePort(string name)
    {
        Probe.Name(this, name);
        PresenceSource = PodPresenceSource.Event;
        AddChild(new SmokePortShell());
    }

    public void NoteMap(IReadOnlyList<SlotState> map) => UpdateSlotMap(map);

    public void NotePodPlaced(bool placed) => NotePodEvent(placed);

    public void NoteState(int state) => State = state;

    public void Tick() => OnScan();

    public override ModuleOperation? Load() => null;

    public override ModuleOperation? Unload() => null;

    public override ModuleOperation? Home() => null;

    protected override ModuleOperation? ResetDevice() => null;

    protected override ModuleOperation? AbortDevice() => null;

    public override ModuleOperation? Clamp() => null;

    public override ModuleOperation? Unclamp() => null;
}

sealed class SmokePortShell : FcdLoadPortComponent
{
    protected override ILoadPortDriver CreateDriver() => new FcdLoadPortDriver(new FakeFrameCommunication());
}

// 假腔体：工艺默认三拍做完（ProcessTicks 可调长），可以故意失败一次；记下收到几次中止。
// NoProcess 打开时声明成只能取放（不能做工艺），验"站点不支持要用的任务就不建 Job"。
sealed class SmokeChamber : BaseChamberModule
{
    public SmokeChamber(string name)
    {
        Probe.Name(this, name);
    }

    public bool NoProcess { get; set; }

    public override IReadOnlyList<string> SupportedTasks => NoProcess ? StationTaskAction.PickPlace : base.SupportedTasks;

    public bool FailNextProcess { get; set; }

    public int ProcessTicks { get; set; } = 3;

    public int Aborts { get; private set; }

    public void NoteState(int state) => State = state;

    public void Tick() => OnScan();

    public override ModuleOperation? Home() => Begin(ChamberAction.Home, new SmokeMotion("Home", 1));

    protected override ModuleOperation? ResetDevice() => Begin(ChamberAction.Reset, new SmokeMotion("Reset", 1));

    protected override ModuleOperation? AbortDevice()
    {
        Aborts++;
        return Begin(ChamberAction.Abort, new SmokeMotion("Abort", 1));
    }

    protected override ModuleOperation? CreateProcessOperation(ProcessRequest request)
    {
        bool fail = FailNextProcess;
        FailNextProcess = false;
        return new SmokeMotion("Process", ProcessTicks, fail);
    }
}

// 只为满足"驱动已连接"这个前置条件；真实帧收发不在本工具的范围内。
sealed class FakeFrameCommunication : IFrameCommunication
{
    public event Action<string>? FrameReceived;

    public bool IsConnected { get; private set; }

    public bool Open()
    {
        IsConnected = true;
        return true;
    }

    public void Close() => IsConnected = false;

    public void Send(string body)
    {
    }

    public void Push(string body) => FrameReceived?.Invoke(body);
}

// 记下 E40 / E94 上报口收到的转换（"PJ 名 #号"、"CJ 名 #号"），派发在别的线程上，等到为止。
sealed class RecordingJobEvents : IE40Callback, IE94Callback
{
    private readonly ConcurrentQueue<string> _events = new();

    public void ProcessJobStateChanged(ProcessJobDto job, int transition) =>
        _events.Enqueue($"PJ {job.Id} #{transition}");

    public void ControlJobStateChanged(ControlJobDto job, int transition) =>
        _events.Enqueue($"CJ {job.Id} #{transition}");

    public bool WaitFor(string item, int timeoutMs = 3000)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < timeoutMs)
        {
            if (_events.Contains(item))
            {
                return true;
            }

            Thread.Sleep(5);
        }

        return false;
    }

    public List<int> Numbers(string job)
    {
        string prefix = job + " #";
        return _events
            .Where(item => item.StartsWith(prefix, StringComparison.Ordinal))
            .Select(item => int.Parse(item[prefix.Length..], System.Globalization.CultureInfo.InvariantCulture))
            .ToList();
    }

    public int IndexOf(string item)
    {
        return _events.ToList().IndexOf(item);
    }
}

sealed class SmokeStateMachine : xyz.Modules.StateMachines.BaseStateMachine<ControlJobState, ControlStateAction>
{
    protected override ControlJobState? GetErrorState()
    {
        return ControlJobState.Aborted;
    }
}
