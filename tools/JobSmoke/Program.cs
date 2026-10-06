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

// Job 冒烟：搬运管理（受理时的各项检查、两张单抢一个槽、执行、没动手就失败放锁、动过手才失败留锁、撤单）
// 和 Job（SEMI E94 CJ / E40 PJ）：建 Job 的各项检查和整个不留、一篮两个 Sequence 跑完（两步加工、转换号顺序、请求号去重）、
// 配方快照、回到别的 LoadPort、PJ 暂停 / 恢复、CJ 暂停（只不启动新 PJ）、CJ 停止、PJ 中止、加工失败暂停派单再恢复、
// 片位不对、Host 先建 PJ 再建 CJ、整机停止走 Job 中止、服务层错误码。
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

// EC 只放在本进程内存里：不读也不写 ec.xml。
var ec = new EcComponent();
string folder = Path.Combine(Path.GetTempPath(), "xyz-job-smoke-" + Guid.NewGuid().ToString("N"));
string jobDb = folder + ".db";
try
{
    // 0. 装一台假机器：两个 LoadPort、两个腔体、一台两指机械手（四个站点都到得了），再加一个机械手到不了的腔体。
    var ledger = new WaferManager();
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
          && jobNode.Children.Any(child => child.Name == "Scheduler" && child.Type == typeof(SchedulerComponent).FullName),
        "sc.xml 里应有 Job 节点（JobManager），下面挂 Scheduler 子节点（SchedulerComponent）");

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

    // Job 存盘写临时库（第 18 节"重启"时读回来），冒烟结束删掉
    XyzDb.Register("JobSmoke", $"DataSource={jobDb}", SqlSugar.DbType.Sqlite);
    var jobs = new SmokeJobs();
    Probe.Name(jobs, "Job");
    jobs.Database = "JobSmoke";
    jobs.Bind(modules);
    var events = new RecordingJobEvents();
    jobs.E40Callback = events;
    jobs.E94Callback = events;
    Check(ReferenceEquals(transfers.Ownership, jobs), "Job 管理绑上之后搬运管理按它查晶圆归属");

    void Tick()
    {
        robot.Tick();
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

    JobCommandResult Do(Task<JobCommandResult> task)
    {
        for (int tick = 0; tick < 50 && !task.IsCompleted; tick++)
        {
            jobs.Tick();
            task.Wait(20);
        }

        Check(task.IsCompleted, "命令应在一两拍里受理");
        return task.Result;
    }

    TransferResult Finish(TransferTicket ticket, int maxTicks = 3000, int sleepMs = 0)
    {
        var completion = ticket.Completion;
        Check(ticket.Accepted && completion is not null, $"搬运单应受理：{ticket.Code} [{string.Join(",", ticket.Args)}]");
        Check(RunUntil(() => completion!.IsCompleted, maxTicks, sleepMs), "搬运单应跑完");
        return completion!.Result;
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

    LocalJobRequest Local(string lot, string port, bool autoStart, params (int Slot, string Sequence)[] slots)
    {
        return new LocalJobRequest
        {
            LoadPort = port,
            LotId = lot,
            SlotSequences = slots.ToDictionary(item => item.Slot, item => item.Sequence),
            AutoStart = autoStart,
            Operator = "Smoke",
        };
    }

    // 1. 搬运管理：受理时的检查——站点、槽、片、机械手、手臂，不收的不占锁。
    LoadCarrier(lp1, 1, 2, 3, 4, 5);
    var first = ledger.Get("LP1", 1)!;
    TransferTicket Submit(string source, int sourceSlot, string target, int targetSlot, Guid? wafer = null, int arm = 0,
        TransferOrigin origin = TransferOrigin.Manual)
    {
        return transfers.Submit(new TransferRequest
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

    void Rejects(TransferTicket ticket, string code, string[] args, string message)
    {
        Check(!ticket.Accepted && ticket.Code == code && ticket.Args.SequenceEqual(args),
            $"{message}，实际 {ticket.Code} [{string.Join(",", ticket.Args)}]");
    }

    Rejects(Submit("NOPE", 1, "PM1", 1), ErrorCodes.TransferStationNotFound, ["NOPE"], "站点不在搬运模块表里");
    Rejects(Submit("LP1", 26, "PM1", 1), ErrorCodes.TransferSlotOutOfRange, ["LP1", "26", "25"], "槽号超出槽数");
    Rejects(Submit("LP1", 1, "LP1", 1), ErrorCodes.TransferSameSlot, [], "源和目标同一个槽");
    Rejects(Submit("LP1", 10, "PM1", 1), ErrorCodes.WaferNoWafer, ["LP1", "10"], "源槽上没片");
    Rejects(Submit("LP1", 1, "PM1", 1, Guid.NewGuid()), ErrorCodes.TransferWaferMismatch, ["LP1", "1", first.WaferId], "源槽上不是要搬的那一片");
    Rejects(Submit("LP1", 1, "LP1", 2), ErrorCodes.WaferSlotOccupied, ["LP1", "2", ledger.Get("LP1", 2)!.WaferId], "目标槽上有片");
    Rejects(Submit("LP1", 1, "PM9", 1), ErrorCodes.TransferNoRobot, ["LP1", "PM9"], "没有机械手两边都到得了");
    Rejects(Submit("LP1", 1, "PM1", 1, arm: 3), ErrorCodes.TransferArmUnavailable, ["Robot1", "3"], "没有 3 号手");
    Check(!transfers.GetView().IsSlotLocked("LP1", 1) && !transfers.GetView().IsRobotBusy("Robot1"), "不收的单不占锁");

    // 2. 两张单抢同一个目标槽、同一片：只有一张拿得到；拿到的那张跑完（设备、站点、晶圆账都收尾）才出结果、放锁。
    var winner = Submit("LP1", 1, "PM1", 1);
    Check(winner.Accepted && winner.Id > 0, "第一张单受理");
    Rejects(Submit("LP1", 2, "PM1", 1), ErrorCodes.TransferSlotLocked, ["PM1", "1"], "目标槽已经被别的单锁着");
    Rejects(Submit("LP1", 1, "PM2", 1), ErrorCodes.TransferSlotLocked, ["LP1", "1"], "这一片已经在别的单里");
    var view = transfers.GetView();
    Check(view.IsSlotLocked("LP1", 1) && view.IsSlotLocked("pm1", 1) && view.IsWaferLocked(first.Id) && view.IsRobotBusy("Robot1"),
        "受理就锁源槽、目标槽、片、机械手（站点名不分大小写）");
    var moved = Finish(winner);
    Check(moved.IsSuccess && moved.Robot == "Robot1" && moved.Arm == 1 && moved.Origin == TransferOrigin.Manual,
        "搬完：结果带机械手和手");
    Check(ledger.Get("PM1", 1)?.Id == first.Id && ledger.Get("LP1", 1) is null, "账跟着走：片在 PM1");
    Check(!transfers.GetView().IsSlotLocked("LP1", 1) && !transfers.GetView().IsSlotLocked("PM1", 1) && !transfers.GetView().IsRobotBusy("Robot1"),
        "搬完放锁");
    Check(lp1.State == LoadPortState.Loaded && pm1.State == ModuleState.Idle && robot.State == ModuleState.Idle,
        "两个站点都收尾回到待命，机械手空闲");
    Check(Finish(Submit("PM1", 1, "LP1", 1)).IsSuccess && ledger.Get("LP1", 1)?.Id == first.Id, "搬回原槽");

    // 3. 没动手就失败（目标一直不在待命，等站点超时）：片没动过，锁放开，不用人工确认。
    pm1.NoteState(ModuleState.NotInit);
    var notReady = Finish(Submit("LP1", 1, "PM1", 1), sleepMs: 5);
    Check(!notReady.IsSuccess && notReady.Outcome == TransferOutcome.Failed && notReady.Code == ErrorCodes.StationBusy
          && notReady.Args.SequenceEqual(new[] { "PM1", "1000" }) && !notReady.NeedsRecovery,
        $"目标等不到：transfer.station_busy，参数是站点和等待毫秒数，不用人工确认，实际 {notReady.Code}");
    Check(ledger.Get("LP1", 1)?.Id == first.Id && !transfers.GetView().IsSlotLocked("LP1", 1) && lp1.State == LoadPortState.Loaded,
        "片还在源槽，锁放开，源站点也没被碰");
    pm1.NoteState(ModuleState.Idle);

    // 4. 动过手才失败（取片失败）：片在哪说不准，锁留着、站点停在交互中，等人工确认后 ReleaseHold。
    robot.FailNextPick = true;
    var picked = Finish(Submit("LP1", 1, "PM1", 1));
    Check(!picked.IsSuccess && picked.Code == ErrorCodes.TransferFailed && picked.Args.SequenceEqual(new[] { "Robot1", "Pick" })
          && picked.NeedsRecovery, "取片失败：transfer.failed，要人工确认");
    Check(transfers.HeldResults.Count == 1 && transfers.GetView().IsSlotLocked("LP1", 1) && transfers.GetView().IsSlotLocked("PM1", 1)
          && lp1.State == TransferModuleState.Transferring, "锁留着，源站点停在交互中挡住后续动作");
    Rejects(Submit("LP1", 1, "PM2", 1), ErrorCodes.TransferSlotLocked, ["LP1", "1"], "留着锁的槽不收新单");
    Check(transfers.ReleaseHold(picked.Id) && !transfers.ReleaseHold(picked.Id) && transfers.HeldResults.Count == 0
          && !transfers.GetView().IsSlotLocked("LP1", 1), "人工确认后放锁（只放一次）");
    robot.NoteState(ModuleState.Idle);
    lp1.NoteState(LoadPortState.Loaded);
    pm1.NoteState(ModuleState.Idle);

    // 5. 撤单：一台机械手一次一张，第二张排着；排着的撤掉片没动过、锁放开，在跑的照常跑完。
    var running = Submit("LP1", 1, "PM1", 1);
    var queued = Submit("LP1", 2, "PM2", 1);
    Check(running.Accepted && queued.Accepted, "两张单都受理（不抢同一个槽）");
    Tick();
    Check(transfers.Cancel(queued.Id, "smoke") == 1 && queued.Completion!.IsCompleted
          && queued.Completion.Result.Outcome == TransferOutcome.Cancelled && queued.Completion.Result.Code == ErrorCodes.TransferCancelled,
        "排着的单撤掉：transfer.cancelled");
    Check(!transfers.GetView().IsSlotLocked("LP1", 2) && !transfers.GetView().IsSlotLocked("PM2", 1), "撤掉的单放锁");
    Check(Finish(running).IsSuccess && Finish(Submit("PM1", 1, "LP1", 1)).IsSuccess, "在跑的那张照常跑完，再搬回去");

    // 6. 建 Job 的检查：不建就什么都不留（不建 CJ / PJ、不占片）。
    void Refuses(JobCommandResult result, string code, string[] args, string message)
    {
        Check(!result.Accepted && result.Code == code && result.Args.SequenceEqual(args),
            $"{message}，实际 {result.Code} [{string.Join(",", result.Args)}]");
    }

    Refuses(Do(jobs.CreateLocalJobAsync(Local("LOT-X", "LPX", false, (1, "SEQ_A")))), ErrorCodes.JobLoadPortNotFound, ["LPX"], "没有这个 LoadPort");
    Refuses(Do(jobs.CreateLocalJobAsync(Local("LOT-X", "LP2", false, (1, "SEQ_D")))), ErrorCodes.JobCarrierNotReady, ["LP2"], "LoadPort 上没载具");
    Refuses(Do(jobs.CreateLocalJobAsync(Local("LOT-X", "LP1", false))), ErrorCodes.JobNoWafers, ["LP1"], "没选片");
    Refuses(Do(jobs.CreateLocalJobAsync(Local("LOT-X", "LP1", false, (1, "SEQ_A"), (10, "SEQ_A")))), ErrorCodes.JobSlotEmpty, ["LP1", "10"],
        "有一槽没片：整个不建");
    Refuses(Do(jobs.CreateLocalJobAsync(Local("LOT-X", "LP1", false, (1, "NOPE")))), ErrorCodes.JobSequenceNotFound, ["NOPE"], "流程配方不在库里");
    Refuses(Do(jobs.CreateLocalJobAsync(Local("LOT-X", "LP1", false, (1, "SEQ_D")))), ErrorCodes.JobSequenceSourceMismatch, ["SEQ_D", "LP1"],
        "流程配方第 1 步没勾这个 LoadPort");
    Refuses(Do(jobs.CreateLocalJobAsync(Local("BAD:ID", "LP1", false, (1, "SEQ_A")))), ErrorCodes.JobIdInvalid, ["BAD:ID"], "名字里有冒号");
    Check(jobs.Snapshot.ControlJobs.Count == 0 && jobs.Snapshot.ProcessJobs.Count == 0 && jobs.OwnerOf(first.Id) is null,
        "不建就什么都不留：没有 CJ / PJ，片不归任何 Job");

    // 7. 一篮两个 Sequence：1、2 槽 SEQ_A、3 槽 SEQ_B（两步加工）→ 一个 CJ 两个 PJ；请求号相同的重发回同一个结果。
    var create = Local("LOT-A", "LP1", false, (1, "SEQ_A"), (2, "SEQ_A"), (3, "SEQ_B")) with { RequestId = "req-1" };
    var created = Do(jobs.CreateLocalJobAsync(create));
    Check(created.Accepted && created.JobId == "LOT-A" && created.ProcessJobs.SequenceEqual(new[] { "LOT-A-1", "LOT-A-2" }),
        "建好：CJ LOT-A，PJ 按投片顺序 LOT-A-1（SEQ_A）、LOT-A-2（SEQ_B）");
    var again = Do(jobs.CreateLocalJobAsync(create));
    Check(again == created && jobs.Snapshot.ControlJobs.Count == 1, "同一个请求号重发：回同一个结果，不建第二份");
    IJobManager forHost = jobs;
    Check(forHost.ProcessJobSpace == jobs.ProcessJobCapacity - 2, "还能建几个 PJ（Host 的 S16F21 问它）= 上限减去没结束的 2 个");
    Refuses(Do(jobs.CreateLocalJobAsync(Local("LOT-B", "LP1", false, (4, "SEQ_A")))), ErrorCodes.JobLoadPortBusy, ["LP1", "LOT-A"],
        "一个 LoadPort 同时只有一个没结束的 CJ");
    Check(jobs.OwnerOf(first.Id) == "LOT-A-1", "片归到 PJ 名下");
    Rejects(Submit("LP1", 1, "PM1", 1), ErrorCodes.TransferWaferOwned, [first.WaferId, "LOT-A-1"], "手动搬 Job 的片：拒，带片号和 Job");

    Check(RunUntil(() => CjOf("LOT-A")?.State == (int)CtrlJobState.WaitingForStart, 20), "CJ 排到队首选中（#3），料到了等启动（#6）");
    Check(PjOf("LOT-A-1")?.State == (int)PrJobState.QueuedPooled && PjOf("LOT-A-2")?.State == (int)PrJobState.QueuedPooled,
        "CJ 没启动前 PJ 都排着");
    Refuses(Do(jobs.CommandControlJobAsync("LOT-A", CtrlJobCommand.Start, CtrlJobAction.SaveJobs, JobCommandSource.Local)),
        ErrorCodes.JobNotAuto, [], "Manual 下启动不了");
    Refuses(Do(jobs.CommandControlJobAsync("LOT-A", CtrlJobCommand.Resume, CtrlJobAction.SaveJobs, JobCommandSource.Local)),
        ErrorCodes.JobCommandNotAllowed, ["LOT-A", "CJResume", "WAITINGFORSTART"], "转换表里没有的命令：拒，带当前状态");
    transfers.StartAutoDispatch();
    Check(Do(jobs.CommandControlJobAsync("LOT-A", CtrlJobCommand.Start, CtrlJobAction.SaveJobs, JobCommandSource.Local)).Accepted
          && CjOf("LOT-A")?.State == (int)CtrlJobState.Executing, "Auto 下启动：EXECUTING（#7）");

    Check(RunUntil(() => CjOf("LOT-A")?.State == (int)CtrlJobState.Completed), "跑完：CJ 进 COMPLETED");
    var cjA = CjOf("LOT-A")!;
    Check(cjA.CompletedBy == 10 && PjOf("LOT-A-1")?.EndedBy == 7 && PjOf("LOT-A-2")?.EndedBy == 7, "正常完成：CJ #10，PJ #7");
    for (int slot = 1; slot <= 3; slot++)
    {
        var wafer = ledger.Get("LP1", slot);
        Check(wafer is not null && wafer.ProcessState == WaferProcessState.Completed, $"LP1 第 {slot} 槽：回到原槽，账上工艺状态是完成");
    }

    var twoStep = PjOf("LOT-A-2")!.Wafers.Single();
    Check(twoStep.Outcome == "Completed" && twoStep.Results.Count == 2 && twoStep.Results[0].Station == "PM1" && twoStep.Results[1].Station == "PM2"
          && twoStep.Results.All(result => result.Success && !result.Simulated) && twoStep.Results[1].Recipe == "R2",
        "两步加工：PM1（R1）、PM2（R2）都做了，第一步做完没跳过第二步");
    Check(PjOf("LOT-A-1")!.Wafers.All(wafer => wafer.Outcome == "Completed" && wafer.Results.Count == 1), "SEQ_A 的片各做一站");
    Check(lp1.Carrier?.AccessStatus == CarrierAccessStatus.Complete, "CJ 完成告诉 LoadPort 载具干完了（E87 CarrierComplete）");
    Check(events.WaitFor("CJ LOT-A #10"), "上报口收到 CJ 完成");
    Check(events.Numbers("PJ LOT-A-1").SequenceEqual(new[] { 1, 2, 4, 6, 7 }) && events.Numbers("PJ LOT-A-2").SequenceEqual(new[] { 1, 2, 4, 6, 7 }),
        "PJ 转换号照 E40：#1 建、#2 准备、#4 自动开始、#6 加工完、#7 结束，实际 " + string.Join(",", events.Numbers("PJ LOT-A-2")));
    Check(events.Numbers("CJ LOT-A").SequenceEqual(new[] { 1, 3, 6, 7, 10 }),
        "CJ 转换号照 E94：#1 建、#3 选中、#6 等启动、#7 启动、#10 完成，实际 " + string.Join(",", events.Numbers("CJ LOT-A")));
    Check(events.IndexOf("PJ LOT-A-2 #7") < events.IndexOf("CJ LOT-A #10"), "PJ 结束先于 CJ 完成报出去（同一条派发线程）");
    Check(jobs.OwnerOf(first.Id) is null, "PJ 结束放开它名下的片");

    UnloadCarrier(lp1);
    Check(RunUntil(() => CjOf("LOT-A") is null, 20) && jobs.Snapshot.History.Any(job => job.Id == "LOT-A" && job.EndedBy == 13),
        "载具拿走：完成的 CJ 删掉（#13）转进历史");

    // 8. 配方快照 + 回到别的 LoadPort：建好之后流程配方改名、删掉都不影响；最后一步只勾了 LP2，片放到 LP2 同号槽。
    LoadCarrier(lp1, 1, 2);
    LoadCarrier(lp2);
    var snapshotJob = Do(jobs.CreateLocalJobAsync(Local("LOT-C", "LP1", true, (1, "SEQ_C"), (2, "SEQ_C"))));
    Check(snapshotJob.Accepted, $"建 LOT-C：{snapshotJob.Code}");
    Check(sequences.Rename(3, "SEQ_C2", "Smoke").IsOk && sequences.Delete(3, "Smoke").IsOk, "库里改名、再删掉");
    var movedIds = new[] { ledger.Get("LP1", 1)!.Id, ledger.Get("LP1", 2)!.Id };
    Check(RunUntil(() => CjOf("LOT-C")?.State == (int)CtrlJobState.Completed), "自动启动（#5）跑完");
    Check(PjOf("LOT-C-1")?.Sequence == "SEQ_C" && events.Numbers("CJ LOT-C").SequenceEqual(new[] { 1, 3, 5, 10 }),
        "照快照跑：PJ 记的还是 SEQ_C；CJ 自动启动 #5");
    Check(ledger.Get("LP2", 1)?.Id == movedIds[0] && ledger.Get("LP2", 2)?.Id == movedIds[1] && ledger.Get("LP1", 1) is null,
        "回到 LP2 的同号槽，不回原槽");
    UnloadCarrier(lp1);
    UnloadCarrier(lp2);
    Check(RunUntil(() => CjOf("LOT-C") is null, 20), "载具拿走，LOT-C 删掉");

    // 9. PJ 暂停：停投新片，机内的照常做完回片，机内没这个 PJ 的片了才 PAUSED；恢复接着投。
    LoadCarrier(lp1, 1, 2, 3, 4);
    Check(Do(jobs.CreateLocalJobAsync(Local("LOT-P", "LP1", true, (1, "SEQ_A"), (2, "SEQ_A"), (3, "SEQ_A"), (4, "SEQ_A")))).Accepted, "建 LOT-P");
    Check(RunUntil(() => PjOf("LOT-P-1")?.Wafers.Any(wafer => wafer.Phase == "Processing") == true), "跑到有片在加工");
    Check(Do(jobs.CommandProcessJobAsync("LOT-P-1", PrJobCommand.Pause, JobCommandSource.Local)).Accepted
          && PjOf("LOT-P-1")?.State == (int)PrJobState.Pausing, "PJ 暂停：PAUSING（#8）");
    int fedAtPause = PjOf("LOT-P-1")!.Wafers.Count(wafer => wafer.Phase != "Waiting");
    Check(RunUntil(() => PjOf("LOT-P-1")?.State == (int)PrJobState.Paused), "机内的片做完回来了：PAUSED（#9）");
    var paused = PjOf("LOT-P-1")!;
    Check(paused.Wafers.Count(wafer => wafer.Phase != "Waiting") == fedAtPause && paused.Wafers.Any(wafer => wafer.Phase == "Waiting")
          && paused.Wafers.Where(wafer => wafer.Phase != "Waiting").All(wafer => wafer.Phase == "Done"),
        "暂停后没再投新片，投出去的都回片了");
    Check(paused.Wafers.Where(wafer => wafer.Phase == "Waiting").All(wafer => wafer.WaitCode == ErrorCodes.JobWaitNotFeeding),
        "没投的片写着在等什么：没在投片");
    Check(Do(jobs.CommandProcessJobAsync("LOT-P-1", PrJobCommand.Resume, JobCommandSource.Local)).Accepted
          && PjOf("LOT-P-1")?.State == (int)PrJobState.Processing, "恢复：回到 PROCESSING（#10）");
    Check(RunUntil(() => CjOf("LOT-P")?.State == (int)CtrlJobState.Completed), "恢复后接着投，跑完");
    Check(events.Numbers("PJ LOT-P-1").SequenceEqual(new[] { 1, 2, 4, 8, 9, 10, 6, 7 }), "转换号：#8 暂停、#9 暂停到位、#10 恢复");
    UnloadCarrier(lp1);
    Check(RunUntil(() => CjOf("LOT-P") is null, 20), "LOT-P 删掉");

    // 10. CJ 暂停（E94）：只是不再启动新的 PJ，在跑的 PJ 照常投片做完；恢复后再启动下一个 PJ。
    LoadCarrier(lp1, 1, 2, 3);
    Check(Do(jobs.CreateLocalJobAsync(Local("LOT-Q", "LP1", true, (1, "SEQ_A"), (2, "SEQ_A"), (3, "SEQ_B")))).Accepted, "建 LOT-Q");
    Check(RunUntil(() => PjOf("LOT-Q-1")?.State == (int)PrJobState.Processing), "第一个 PJ 在跑");
    Check(Do(jobs.CommandControlJobAsync("LOT-Q", CtrlJobCommand.Pause, CtrlJobAction.SaveJobs, JobCommandSource.Local)).Accepted
          && CjOf("LOT-Q")?.State == (int)CtrlJobState.Paused, "CJ 暂停：PAUSED（#8）");
    Check(RunUntil(() => PjOf("LOT-Q-1")?.EndedBy == 7), "在跑的 PJ 照常投片、做完（#7）");
    Tick();
    Check(PjOf("LOT-Q-2")?.State == (int)PrJobState.QueuedPooled && CjOf("LOT-Q")?.State == (int)CtrlJobState.Paused,
        "下一个 PJ 不启动，CJ 还是 PAUSED");
    Check(Do(jobs.CommandControlJobAsync("LOT-Q", CtrlJobCommand.Resume, CtrlJobAction.SaveJobs, JobCommandSource.Local)).Accepted,
        "CJ 恢复（#9）");
    Check(RunUntil(() => CjOf("LOT-Q")?.State == (int)CtrlJobState.Completed) && CjOf("LOT-Q")?.CompletedBy == 10,
        "恢复后启动下一个 PJ，都做完 #10");
    UnloadCarrier(lp1);
    Check(RunUntil(() => CjOf("LOT-Q") is null, 20), "LOT-Q 删掉");

    // 11. CJ 停止：不再投片，机内的走完回片；没投的记未执行；PJ 停完（#17）后 CJ 进 COMPLETED（#11）。
    LoadCarrier(lp1, 1, 2, 3, 4);
    Check(Do(jobs.CreateLocalJobAsync(Local("LOT-S", "LP1", true, (1, "SEQ_A"), (2, "SEQ_A"), (3, "SEQ_A"), (4, "SEQ_A")))).Accepted, "建 LOT-S");
    Check(RunUntil(() => PjOf("LOT-S-1")?.Wafers.Any(wafer => wafer.Phase == "Processing") == true), "跑到有片在加工");
    Check(Do(jobs.CommandControlJobAsync("LOT-S", CtrlJobCommand.Stop, CtrlJobAction.SaveJobs, JobCommandSource.Local)).Accepted
          && PjOf("LOT-S-1")?.State == (int)PrJobState.Stopping && CjOf("LOT-S")?.Ending == "Stop"
          && CjOf("LOT-S")?.State == (int)CtrlJobState.Executing, "CJ 停止：PJ 进 STOPPING（#11），CJ 状态值不变、标着停止中");
    Refuses(Do(jobs.CommandControlJobAsync("LOT-S", CtrlJobCommand.Pause, CtrlJobAction.SaveJobs, JobCommandSource.Local)),
        ErrorCodes.JobEnding, ["LOT-S", "CJPause"], "停止中不收暂停");
    Check(RunUntil(() => CjOf("LOT-S")?.State == (int)CtrlJobState.Completed), "停完");
    var stopped = PjOf("LOT-S-1")!;
    Check(CjOf("LOT-S")?.CompletedBy == 11 && stopped.EndedBy == 17 && stopped.Wafers.Any(wafer => wafer.Outcome == "NotRun")
          && stopped.Wafers.Where(wafer => wafer.Outcome != "NotRun").All(wafer => wafer.Outcome == "Completed"),
        "CJ #11、PJ #17；投出去的做完，没投的记未执行");
    UnloadCarrier(lp1);
    Check(RunUntil(() => CjOf("LOT-S") is null, 20), "LOT-S 删掉");

    // 12. PJ 中止：撤单、给在做工艺的腔体发中止；设备中止做完、在途动作都结束、片位都确定才结束（#16）。机内的片记中止，没投的记未执行。
    //     PM2 离线：片都去 PM1，加工时机械手闲着（手臂在动时中止，片位要人工确认，那条路在第 4 节验过）。
    LoadCarrier(lp1, 1, 2, 3);
    pm2.Offline();
    Check(Do(jobs.CreateLocalJobAsync(Local("LOT-T", "LP1", true, (1, "SEQ_A"), (2, "SEQ_A"), (3, "SEQ_A")))).Accepted, "建 LOT-T");
    Check(RunUntil(() => PjOf("LOT-T-1")?.Wafers.Any(wafer => wafer.Phase == "Processing") == true), "跑到有片在加工");
    var processingIn = PjOf("LOT-T-1")!.Wafers.First(wafer => wafer.Phase == "Processing").Station;
    var processingChamber = processingIn == "PM1" ? pm1 : pm2;
    int abortsBefore = processingChamber.Aborts;
    Check(Do(jobs.CommandProcessJobAsync("LOT-T-1", PrJobCommand.Abort, JobCommandSource.Local)).Accepted
          && PjOf("LOT-T-1")?.State == (int)PrJobState.Aborting, "PJ 中止：ABORTING（#13）");
    Check(processingChamber.Aborts == abortsBefore + 1, "给在做这个 PJ 工艺的腔体发了中止");
    jobs.Tick();
    Check(PjOf("LOT-T-1")?.State == (int)PrJobState.Aborting, "腔体的中止动作还没做完：PJ 还在 ABORTING");
    Check(RunUntil(() => CjOf("LOT-T")?.State == (int)CtrlJobState.Completed), "中止做完");
    var aborted = PjOf("LOT-T-1")!;
    Check(aborted.EndedBy == 16 && aborted.Wafers.Any(wafer => wafer.Outcome == "Aborted") && aborted.Wafers.Any(wafer => wafer.Outcome == "NotRun"),
        "PJ #16：机内的片记中止，没投的记未执行");
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

    // 13. 加工没做成：暂停自动派单、保留现场（别的片不再派）；腔体复位后恢复派单，没做成的片不再做后面的步骤直接回片。
    LoadCarrier(lp1, 1, 2);
    pm2.Offline();
    pm1.FailNextProcess = true;
    Check(Do(jobs.CreateLocalJobAsync(Local("LOT-F", "LP1", true, (1, "SEQ_A"), (2, "SEQ_A")))).Accepted, "建 LOT-F（PM2 离线，都去 PM1）");
    Check(RunUntil(() => jobs.Snapshot.IsHeld), "加工没做成：自动派单暂停");
    Check(jobs.Snapshot.HoldCode == ErrorCodes.JobHoldProcessFailed && pm1.State == ModuleState.Error, "暂停原因是加工没做成，腔体报错停着");
    for (int tick = 0; tick < 20; tick++)
    {
        Tick();
    }

    var held = PjOf("LOT-F-1")!;
    Check(held.Wafers.Count(wafer => wafer.Phase == "Waiting") == 1
          && held.Wafers.Single(wafer => wafer.Phase == "Waiting").WaitCode == ErrorCodes.JobWaitHeld,
        "暂停派单期间不投新片，等待原因写着派单暂停");
    var failedWafer = held.Wafers.Single(wafer => wafer.Phase != "Waiting");
    Check(failedWafer.Results.Count == 1 && !failedWafer.Results[0].Success && ledger.Get("PM1", 1)?.ProcessState == WaferProcessState.Failed,
        "这一站的结果记成没做成，账上工艺状态是失败");
    pm1.NoteState(ModuleState.Idle);
    Check(Do(jobs.RecoverAsync(JobCommandSource.Local)).Accepted && !jobs.Snapshot.IsHeld, "到现场把腔体复位、确认过之后恢复派单");
    Check(RunUntil(() => CjOf("LOT-F")?.State == (int)CtrlJobState.Completed), "恢复后跑完");
    var failedJob = PjOf("LOT-F-1")!;
    Check(failedJob.EndedBy == 7 && failedJob.Wafers.Count(wafer => wafer.Outcome == "Failed") == 1
          && failedJob.Wafers.Count(wafer => wafer.Outcome == "Completed") == 1,
        "没做成的那片不再做、回片，记失败；另一片照常做完");
    Refuses(Do(jobs.RecoverAsync(JobCommandSource.Local)), ErrorCodes.JobNotHeld, [], "没在暂停时恢复：job.not_held");
    pm2.Online();
    UnloadCarrier(lp1);
    Check(RunUntil(() => CjOf("LOT-F") is null, 20), "LOT-F 删掉");

    // 14. 片不在该在的地方（人工改了账）：认成片位说不准、暂停派单；恢复时按账上现在的位置认回来，不再做、送回它的回片槽。
    //     顺带：Manual 下自动启动的 Job 也会开始，只是不派动作；同一个 LoadPort 里换槽的搬运只抢一次环。
    LoadCarrier(lp1, 1, 2);
    transfers.StopAutoDispatch();
    Check(Do(jobs.CreateLocalJobAsync(Local("LOT-L", "LP1", true, (1, "SEQ_A"), (2, "SEQ_A")))).Accepted, "建 LOT-L");
    Check(RunUntil(() => PjOf("LOT-L-1")?.State == (int)PrJobState.Processing, 20), "Manual 下 PJ 也会开始（#4），只是不派动作");
    Check(PjOf("LOT-L-1")!.Wafers.All(wafer => wafer.WaitCode == ErrorCodes.JobWaitManual), "等待原因：Manual 模式");
    string lostName = ledger.Get("LP1", 2)!.WaferId;
    Check(ledger.ManualMove("LP1", 2, "LP1", 10, "Smoke", "smoke") == WaferAdjustResult.Ok, "人工把第 2 槽的片挪到第 10 槽");
    Tick();
    var lostJob = PjOf("LOT-L-1")!;
    Check(jobs.Snapshot.IsHeld && jobs.Snapshot.HoldCode == ErrorCodes.JobHoldWaferLost && lostJob.NeedsRecovery
          && lostJob.Wafers.Single(wafer => wafer.WaferId == lostName).Phase == "Lost",
        "片不在该在的地方：认成说不准、暂停派单、PJ 要人工恢复确认");
    Check(Do(jobs.RecoverAsync(JobCommandSource.Local)).Accepted && !jobs.Snapshot.IsHeld && !PjOf("LOT-L-1")!.NeedsRecovery,
        "恢复：按账上现在的位置认回来");
    transfers.StartAutoDispatch();
    Check(RunUntil(() => CjOf("LOT-L")?.State == (int)CtrlJobState.Completed), "跑完");
    var lost = PjOf("LOT-L-1")!.Wafers.Single(wafer => wafer.WaferId == lostName);
    Check(lost.Outcome == "Failed" && lost.Results.Count == 0 && ledger.Get("LP1", 2)?.WaferId == lostName && ledger.Get("LP1", 10) is null,
        "认回来的片不再做（记失败），从第 10 槽送回它的回片槽第 2 槽");
    UnloadCarrier(lp1);
    Check(RunUntil(() => CjOf("LOT-L") is null, 20), "LOT-L 删掉");

    // 15. Host 的做法：先建 PJ（不归任何 CJ，排着），再建 CJ 把 PJ 按顺序收进来。
    LoadCarrier(lp1, 1, 2);
    lp1.SetCarrierId("CAR-1");
    var hostPj = new ProcessJobSpec { Id = "PJ-H1", CarrierId = "CAR-1", Slots = [1], Sequence = "SEQ_A" };
    Check(Do(jobs.CreateProcessJobAsync(hostPj, JobCommandSource.Host)).Accepted, "Host 建 PJ-H1");
    Check(PjOf("PJ-H1")?.ControlJob == string.Empty && PjOf("PJ-H1")?.State == (int)PrJobState.QueuedPooled, "PJ 先建：不归任何 CJ，排着");
    Refuses(Do(jobs.CreateProcessJobAsync(hostPj with { Id = "PJ-H9", CarrierId = "NOPE" }, JobCommandSource.Host)),
        ErrorCodes.JobCarrierNotFound, ["NOPE"], "载具不在任何 LoadPort 上");
    Refuses(Do(jobs.CreateProcessJobAsync(hostPj with { Slots = [2] }, JobCommandSource.Host)), ErrorCodes.JobIdDuplicate, ["PJ-H1"], "PJ 名重了");
    Check(Do(jobs.CreateProcessJobAsync(hostPj with { Id = "PJ-H2", Slots = [2] }, JobCommandSource.Host)).Accepted, "Host 建 PJ-H2");
    Refuses(Do(jobs.CreateControlJobAsync(new ControlJobSpec { Id = "CJ-H", ProcessJobs = ["PJ-H1", "NOPE"], AutoStart = true }, JobCommandSource.Host)),
        ErrorCodes.JobProcessJobUnavailable, ["NOPE"], "CJ 收的 PJ 不存在：整个不建");
    Check(PjOf("PJ-H1")?.ControlJob == string.Empty, "没建成的 CJ 不占 PJ");
    Check(Do(jobs.CreateControlJobAsync(new ControlJobSpec { Id = "CJ-H", ProcessJobs = ["PJ-H1", "PJ-H2"], AutoStart = true }, JobCommandSource.Host)).Accepted,
        "Host 建 CJ-H，收下两个 PJ");
    Check(RunUntil(() => CjOf("CJ-H")?.State == (int)CtrlJobState.Completed) && CjOf("CJ-H")?.CompletedBy == 10, "Host 建的照样跑完");
    UnloadCarrier(lp1);
    Check(RunUntil(() => CjOf("CJ-H") is null, 20), "CJ-H 删掉");

    // 16. 整机停止：关自动派单、撤搬运单，Job 走中止（等设备确认、核对片位），不是直接删 Job。
    LoadCarrier(lp1, 1, 2, 3);
    Check(Do(jobs.CreateLocalJobAsync(Local("LOT-E", "LP1", true, (1, "SEQ_A"), (2, "SEQ_A"), (3, "SEQ_A")))).Accepted, "建 LOT-E");
    Check(RunUntil(() => PjOf("LOT-E-1")?.Wafers.Any(wafer => wafer.Phase == "Processing") == true && !transfers.GetView().IsRobotBusy("Robot1")),
        "跑到有片在加工、机械手闲着");
    var equipment = new EquipmentService(modules);
    var stop = equipment.StopAsync(new RpcRequest()).Result;
    Check(stop.Success && stop.DeserializeData<int>() == 0 && !transfers.IsAutoDispatch,
        "整机停止：关自动派单；在给 Job 做工艺的腔体不在这里直接发中止");
    Check(RunUntil(() => CjOf("LOT-E")?.State == (int)CtrlJobState.Completed) && CjOf("LOT-E")?.CompletedBy == 12
          && PjOf("LOT-E-1")?.EndedBy == 16, "Job 走中止：PJ #16、CJ #12");
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

    var jobService = new JobService(modules);
    var noPort = Pump(jobService.CreateAsync(new JobCreateRequest { LoadPort = "LPX", Slots = [new JobSlotDto { Slot = 1, Sequence = "SEQ_A" }] }));
    Check(!noPort.Success && noPort.Code == ErrorCodes.JobLoadPortNotFound && noPort.Args.SequenceEqual(new[] { "LPX" }), "建 Job 不成：错误码带 LoadPort");
    var unknown = Pump(jobService.ControlJobCommandAsync(new JobCommandRequest { JobId = "NOPE", Command = (int)CtrlJobCommand.Start }));
    Check(!unknown.Success && unknown.Code == ErrorCodes.JobNotFound && unknown.Args.SequenceEqual(new[] { "NOPE" }), "没有这个 Job");
    var badCommand = Pump(jobService.ControlJobCommandAsync(new JobCommandRequest { JobId = "NOPE", Command = 99 }));
    Check(!badCommand.Success && badCommand.Code == ErrorCodes.JobCommandNotAllowed, "命令值不认识：拒");
    var list = Pump(jobService.GetJobsAsync(new RpcRequest())).DeserializeData<JobListDto>();
    Check(list.Version > 0 && list.History.Count > 0 && list.History[0].Id == "LOT-E", "查全貌：带版本号，历史新的在前");

    LoadCarrier(lp1, 1);
    var transferService = new TransferService(modules);
    var manual = Pump(transferService.TransferAsync(new TransferRequestDto { Source = "LP1", SourceSlot = 1, Target = "PM1", TargetSlot = 1 }));
    Check(manual.Success && manual.DeserializeData<TransferDoneDto>().Robot == "Robot1" && ledger.Get("PM1", 1) is not null,
        "手动传片走搬运管理：搬完回机械手和手");
    var occupied = Pump(transferService.TransferAsync(new TransferRequestDto { Source = "LP1", SourceSlot = 2, Target = "PM1", TargetSlot = 1 }));
    Check(!occupied.Success && occupied.Code == ErrorCodes.WaferNoWafer, "手动传片受理不了：错误码照搬");
    var notHeld = Pump(transferService.ReleaseAsync(new TransferReleaseRequest { Id = 12345 }));
    Check(!notHeld.Success && notHeld.Code == ErrorCodes.TransferNotHeld && notHeld.Args.SequenceEqual(new[] { "12345" }), "没有留着锁的单：transfer.not_held");
    Check(ledger.Move("PM1", 1, "LP1", 1), "手动传过去的那片人工收回");
    UnloadCarrier(lp1);

    // 18. 重启：Job 存了盘（每次发布交给写库线程，只写最新的），"断电"后新的一个 Job 管理开机读回来——
    //     没结束的不接着跑，记成中止（E94 #12，标着重启）进历史，上次的历史接着留；片不再归任何 Job。
    bool WaitSaved(long version)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < 3000)
        {
            using (var db = XyzDb.Create("JobSmoke"))
            {
                var row = db.Queryable<JobSnapshotEntity>().InSingle(1);
                if (row is not null && row.Version >= version)
                {
                    return true;
                }
            }

            Thread.Sleep(20);
        }

        return false;
    }

    LoadCarrier(lp1, 1, 2, 3);
    var lotR = Enumerable.Range(1, 3).Select(slot => ledger.Get("LP1", slot)!.Id).ToArray();
    pm1.ProcessTicks = 200;
    pm2.ProcessTicks = 200;
    transfers.StartAutoDispatch();
    Check(Do(jobs.CreateLocalJobAsync(Local("LOT-R", "LP1", true, (1, "SEQ_A"), (2, "SEQ_A"), (3, "SEQ_A")))).Accepted, "建 LOT-R");
    Check(RunUntil(() => PjOf("LOT-R-1")?.Wafers.Count(wafer => wafer.Phase == "Processing") == 2 && !transfers.GetView().IsRobotBusy("Robot1")),
        "跑到两片在加工、机械手闲着");
    Check(WaitSaved(jobs.Snapshot.Version), "Job 全貌存进库了");
    transfers.StopAutoDispatch();
    var restarted = new SmokeJobs();
    Probe.Name(restarted, "Job");
    restarted.Database = "JobSmoke";
    restarted.Bind(modules);
    jobs = restarted;
    var afterRestart = jobs.Snapshot;
    Check(afterRestart.ControlJobs.Count == 0 && afterRestart.ProcessJobs.Count == 0, "重启后没有接着跑的 Job");
    Check(afterRestart.History.Count > 1 && afterRestart.History[0].Id == "LOT-R" && afterRestart.History[0].Restarted
          && afterRestart.History[0].CompletedBy == 12 && afterRestart.History[0].EndedBy == 13,
        "上次没做完的记成中止（#12）、标着重启，进历史");
    Check(afterRestart.History.Any(job => job.Id == "LOT-E" && !job.Restarted), "上次的历史接着留");
    Check(lotR.All(id => jobs.OwnerOf(id) is null), "片不再归任何 Job");

    // 19. 全部回片（重启后、中止后机内留着片时用）：机内每片回它的来源 LoadPort 同号槽，机械手手上的先回（只放片），
    //     一台机械手一张一张做；在做的时候再点回"已经在做"；回不去的（不知道从哪来、载具换过了）写原因不动；整机停止就不再往下下单。
    Check(RunUntil(() => pm1.State == ModuleState.Idle && pm2.State == ModuleState.Idle), "腔体做完手上的工艺");
    pm1.ProcessTicks = 3;
    pm2.ProcessTicks = 3;
    Check(ledger.ManualMove("LP1", 3, "Robot1", 1, "Smoke", "重启前取了没放") == WaferAdjustResult.Ok, "摆一片在机械手手上");
    var returnPlan = transfers.PlanReturnAll();
    Check(returnPlan.Moves.Count == 3 && returnPlan.Skipped.Count == 0 && returnPlan.Moves[0].SourceIsArm && returnPlan.Moves[0].Source == "Robot1"
          && returnPlan.Moves[0].Target == "LP1" && returnPlan.Moves[0].TargetSlot == 3, "计划：机械手手上的先回，三片都回来源槽");
    var returnStarted = equipment.ReturnAllAsync(new RpcRequest()).Result;
    var returnAgain = equipment.ReturnAllAsync(new RpcRequest()).Result;
    Check(returnStarted.Success && returnStarted.DeserializeData<ReturnPlanDto>().Moves.Count == 3 && transfers.IsReturning
          && !returnAgain.Success && returnAgain.Code == ErrorCodes.TransferReturnRunning,
        "开始全部回片；在做的时候再点：已经在做了");
    Check(RunUntil(() => !transfers.IsReturning), "全部回片做完");
    Check(Enumerable.Range(1, 3).All(slot => ledger.Get("LP1", slot)?.Id == lotR[slot - 1]) && ledger.Get("Robot1", 1) is null
          && ledger.Get("PM1", 1) is null && ledger.Get("PM2", 1) is null, "都回到来源槽（手上那片只放片），手上、腔体里都空了");

    var orphan = ledger.Create("PM2", 1)!;
    Check(ledger.Move("LP1", 1, "PM1", 1) && ledger.SetCarrierId("PM1", 1, "CAR-OLD"), "摆两片回不去的：腔体上补账建的一片、载具换过了的一片");
    lp1.SetCarrierId("CAR-NEW");
    var stuck = transfers.PlanReturnAll();
    var noSource = stuck.Skipped.FirstOrDefault(skip => skip.Source == "PM2");
    var carrierChanged = stuck.Skipped.FirstOrDefault(skip => skip.Source == "PM1");
    Check(stuck.Moves.Count == 0 && stuck.Skipped.Count == 2
          && noSource is not null && noSource.Code == ErrorCodes.TransferReturnNoSource && noSource.Args.SequenceEqual(new[] { orphan.WaferId })
          && carrierChanged is not null && carrierChanged.Code == ErrorCodes.TransferReturnCarrierChanged
          && carrierChanged.Args.SequenceEqual(new[] { carrierChanged.WaferName, "LP1", "CAR-OLD", "CAR-NEW" }),
        "回不去的写原因：不知道从哪来、载具换过了");
    var nothingToReturn = transfers.StartReturnAll();
    Check(nothingToReturn is not null && nothingToReturn.Moves.Count == 0 && nothingToReturn.Skipped.Count == 2 && !transfers.IsReturning,
        "没有能回的：不开始，回不去的照样报出来");
    Check(ledger.Delete("PM2", 1) && ledger.SetCarrierId("PM1", 1, "CAR-NEW"), "补账的那片删掉，那片的载具号改对");
    Check(transfers.StartReturnAll()?.Moves.Count == 1 && transfers.IsReturning, "再开始全部回片");
    var stopReturn = equipment.StopAsync(new RpcRequest()).Result;
    for (int tick = 0; tick < 20; tick++)
    {
        Tick();
    }

    Check(stopReturn.Success && !transfers.IsReturning && ledger.Get("PM1", 1) is not null, "整机停止：全部回片不再往下下单，片还在腔体里");
    Check(transfers.StartReturnAll()?.Moves.Count == 1 && RunUntil(() => !transfers.IsReturning)
          && ledger.Get("LP1", 1) is not null && ledger.Get("PM1", 1) is null, "停了以后再点一次，回去了");
    UnloadCarrier(lp1);
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

Console.WriteLine($"PASS: {checks} job checks (transfer manager: admission checks, two orders racing for one slot, locks released only after the station rings and the ledger settle, " +
    "failures without motion releasing locks and failures after motion holding them for manual recovery, cancelling a queued order; " +
    "jobs per SEMI E94/E40: creation checks with nothing left behind, one carrier with two sequences and a two-step route, transition numbers in order, " +
    "request-id de-duplication, sequence snapshots, returning to another LoadPort, PJ pause/resume, CJ pause that only stops starting new PJs, " +
    "CJ stop, PJ abort, process failure holding dispatch until recovery, a wafer moved behind the job's back, host-style PJ-then-CJ creation, " +
    "the equipment stop going through job abort, the job and transfer services, a restart that closes unfinished jobs into history instead of resuming them, " +
    "and return-all: wafers on robot arms placed first, chamber wafers back to their source slots, skip reasons and stopping)");

// ── 假件 ─────────────────────────────────────────────────────────────

// 名字、路径：生产里由 ComponentLoader 经内部 setter 填。
static class Probe
{
    public static void Name(ComponentBase component, string name)
    {
        typeof(ComponentBase).GetProperty(nameof(ComponentBase.Name))!.SetValue(component, name);
        typeof(ComponentBase).GetProperty(nameof(ComponentBase.FullPath))!.SetValue(component, name);
    }
}

// 搬运管理、Job 管理：顶替扫描线程推一拍。
sealed class SmokeTransfers : TransferManager
{
    public void Tick() => OnScan();
}

sealed class SmokeJobs : JobManager
{
    public void Tick() => OnScan();
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

// 假机械手：取放两拍做完（成功后基类照常记账），取片可以故意失败一次。
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

    protected override ModuleOperation CreatePlaceOperation(int arm, int stationNumber, int slot) => new SmokeMotion("Place", 2);
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
sealed class SmokeChamber : BaseChamberModule
{
    public SmokeChamber(string name)
    {
        Probe.Name(this, name);
    }

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

    public void ProcessJobTransitioned(ProcessJobDto job, int transition, PrJobState? from, PrJobState? to) =>
        _events.Enqueue($"PJ {job.Id} #{transition}");

    public void WaferProcessStarted(ProcessJobDto job, JobWaferDto wafer, string station)
    {
    }

    public void WaferProcessEnded(ProcessJobDto job, JobWaferDto wafer, string station, bool success)
    {
    }

    public void ControlJobTransitioned(ControlJobDto job, int transition, CtrlJobState? from, CtrlJobState? to) =>
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
