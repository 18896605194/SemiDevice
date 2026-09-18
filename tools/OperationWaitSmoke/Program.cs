using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using xyz.Components;
using xyz.Components.Components;
using xyz.Components.Wafers;
using xyz.Configs;
using xyz.Configs.Models;
using xyz.Drivers.Communication;
using xyz.Drivers.Loadport;
using xyz.Drivers.Loadport.FCD;
using xyz.Drivers.Robot;
using xyz.Drivers.Robot.Reje;
using xyz.Modules.Enums;
using xyz.Modules;
using xyz.Service;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;

// No host, hardware connection, or configuration-file writes are used by these checks.
var checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    checks++;
}

void Throws<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { checks++; return; }
    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}

ModuleOperation? rejected = null;
Check(!rejected.Wait(0), "A rejected operation should return false.");

var pending = new ProbeOperation();
Throws<TimeoutException>(() => pending.Wait(0));
Check(pending.State == OperationState.Running && pending.Code.Length == 0,
    "Wait timeout must not turn the operation into a device failure.");
pending.Succeed();
Check(pending.Wait(0), "An operation must remain able to complete after a wait timeout.");

var failed = new ProbeOperation();
failed.Reject();
Check(!failed.Wait(0) && failed.Code == ErrorCodes.DeviceFailed
    && failed.ErrorArgs.SequenceEqual(new[] { "Probe", "device error" }),
    "Device failure must preserve its error code and arguments.");

var actionTimeout = new ProbeOperation();
actionTimeout.TimeOut();
Check(!actionTimeout.Wait(0) && actionTimeout.Code == ErrorCodes.Timeout,
    "An action timeout must remain distinct from a wait timeout.");

var aborted = new ProbeOperation();
aborted.AbortByHost("replacement");
aborted.Reject();
aborted.AbortByHost("duplicate");
Check(!aborted.Wait(0) && aborted.Code == ErrorCodes.Aborted
    && aborted.ErrorArgs.SequenceEqual(new[] { "Probe" }) && aborted.AbortCount == 1,
    "Abort must publish its code once; late failure must not overwrite it.");

using (var cancellation = new CancellationTokenSource())
using (var entered = new ManualResetEventSlim())
{
    var operation = new ProbeOperation();
    var waiter = Task.Run(() =>
    {
        entered.Set();
        Throws<OperationCanceledException>(() => operation.Wait(2000, cancellation.Token));
    });
    Check(entered.Wait(2000), "Waiter did not start.");
    cancellation.Cancel();
    await waiter.WaitAsync(TimeSpan.FromSeconds(5));
    Check(operation.State == OperationState.Running, "Cancellation must only stop waiting.");
    operation.Succeed();
    Check(operation.Wait(0), "Cancellation must not cancel a future wait for the operation.");
}

// Hold the module's completion callback to prove no waiter is released before state cleanup.
using (var entered = new ManualResetEventSlim())
using (var release = new ManualResetEventSlim())
{
    var module = new ProbeModule { Completing = entered, ReleaseCompletion = release };
    var operation = new ProbeOperation { FinishOnScan = true };
    Check(module.StartOperation(operation), "Failed to attach the operation.");
    var scanner = Task.Run(module.Tick);
    try
    {
        Check(entered.Wait(2000), "Module completion callback did not start.");
        Check(!operation.WaitReply(0), "Waiter was released before module state cleanup.");
    }
    finally { release.Set(); }
    await scanner.WaitAsync(TimeSpan.FromSeconds(5));
    Check(operation.Wait(0) && module.CleanupDone && module.CurrentOperation is null,
        "Successful wait must observe a completed module transition and an empty slot.");
}

var replacementModule = new ProbeModule();
var oldOperation = new ProbeOperation();
var replacement = new ProbeOperation { FinishOnScan = true };
Check(replacementModule.StartOperation(oldOperation), "Initial attach failed.");
Check(!replacementModule.StartOperation(new ProbeOperation()), "Busy module accepted another action.");
Check(replacementModule.StartOperation(replacement, replace: true), "Replacement failed.");
Check(!oldOperation.Wait(0) && oldOperation.Code == ErrorCodes.Aborted,
    "Replaced operation did not release its waiter with Aborted.");
replacementModule.Tick();
Check(replacement.Wait(0) && replacementModule.CurrentOperation is null,
    "Old operation cleanup lost the replacement operation.");

for (var i = 0; i < 200; i++)
{
    var operation = new ProbeOperation();
    Parallel.Invoke(operation.Succeed, operation.Reject, () => operation.AbortByHost("race"));
    Check(operation.WaitReply(0), "Concurrent completion did not release the waiter.");
    Check(operation.State switch
    {
        OperationState.Completed => operation.Code.Length == 0 && operation.ErrorArgs.Count == 0,
        OperationState.Failed => operation.Code == ErrorCodes.DeviceFailed && operation.ErrorArgs.Count == 2,
        OperationState.Aborted => operation.Code == ErrorCodes.Aborted && operation.ErrorArgs.Count == 1,
        _ => false
    }, "Concurrent terminal paths produced a mixed result.");
}

var port = new ProbePort();
var service = new LoadPortService(new ComponentBase[] { port });
var actions = new Func<string, Task<RpcResponse>>[]
{
    service.LoadAsync, service.UnloadAsync, service.HomeAsync, service.ResetAsync, service.AbortAsync
};
foreach (var action in actions)
{
    var response = await action("missing");
    Check(!response.Success && response.Code == ErrorCodes.ModuleNotFound, "Missing module response changed.");

    port.Next = null;
    response = await action(port.Name);
    Check(!response.Success && response.Code == ErrorCodes.ActionRejected, "Rejected action response changed.");

    port.Next = new ProbeOperation();
    port.Next.Succeed();
    response = await action(port.Name);
    Check(response.Success, "RPC did not return a successful operation.");

    port.Next = new ProbeOperation();
    port.Next.Reject();
    response = await action(port.Name);
    Check(!response.Success && response.Code == ErrorCodes.DeviceFailed && response.Args.Count == 2,
        "RPC lost device failure details.");

    port.Next = new ProbeOperation();
    port.Next.TimeOut();
    response = await action(port.Name);
    Check(!response.Success && response.Code == ErrorCodes.Timeout, "RPC lost action timeout details.");

    port.Next = new ProbeOperation();
    port.Next.AbortByHost("replacement");
    response = await action(port.Name);
    Check(!response.Success && response.Code == ErrorCodes.Aborted, "RPC returned an empty Abort error.");

    port.Next = new ProbeOperation();
    response = await action(port.Name);
    Check(!response.Success && response.Code == ErrorCodes.WaitTimeout
        && response.Args.SequenceEqual(new[] { "Probe", "0" })
        && port.Next.State == OperationState.Running,
        "RPC must report wait timeout without changing device operation state.");
    port.Next.Succeed();
    Check(port.Next.Wait(0), "RPC timeout prevented later completion.");
}

// Online/Offline 只改模块模式 Mode，Auto/Manual 只改 LoadPort 的 Access Mode：
// 都不经设备协议，置位即成功，不产生操作、不等待，而且互不影响。
var modeResponse = await service.OnlineAsync("missing");
Check(!modeResponse.Success && modeResponse.Code == ErrorCodes.ModuleNotFound,
    "Online must report a missing module.");
modeResponse = await service.AutoAsync("missing");
Check(!modeResponse.Success && modeResponse.Code == ErrorCodes.ModuleNotFound,
    "Auto must report a missing module.");

Check(port.Mode == ModuleMode.Offline && !port.IsAutoMode, "Probe port must start offline and in manual mode.");
var callsBeforeMode = port.Calls;
var modeSnapshot = port.CreateStateDto();
modeResponse = await service.OnlineAsync(port.Name);
Check(modeResponse.Success && port.Mode == ModuleMode.Online && !port.IsAutoMode,
    "Online must only set the module mode.");
Check(port.CreateStateDto() is { Mode: ModuleMode.Online } && port.CreateStateDto().HasStateChanged(modeSnapshot),
    "The module mode must reach the state snapshot and count as a change.");

modeResponse = await service.AutoAsync(port.Name);
Check(modeResponse.Success && port.IsAutoMode && port.Mode == ModuleMode.Online,
    "Auto must only set the access mode.");
modeResponse = await service.ManualAsync(port.Name);
Check(modeResponse.Success && !port.IsAutoMode && port.Mode == ModuleMode.Online,
    "Manual must only clear the access mode.");

modeResponse = await service.OfflineAsync(port.Name);
Check(modeResponse.Success && port.Mode == ModuleMode.Offline && port.Calls == callsBeforeMode,
    "Offline must clear the module mode without starting a device action.");

// EAP 口子：回调走专用派发线程（本工具不跑扫描循环，正好证明派发不再依赖扫描）。
var eap = new RecordingE87Callback();
port.E87Callback = eap;
port.SetAutoMode(true);
Check(eap.Wait(nameof(IE87Callback.AutoModeChanged)), "EAP callbacks must be delivered without a scan loop.");
port.SetAutoMode(false);

// 载具 ID 与 Mapping 结果要能进状态快照（DTO），否则出不了服务进程。
port.NoteMap([SlotState.CorrectlyOccupied, SlotState.Empty, SlotState.CrossSlotted]);
Check(eap.Wait(nameof(IE87Callback.SlotMapRead)), "Mapping must report SlotMapRead.");
port.SetCarrierId("FOUP-001");

var snapshot = port.CreateStateDto();
Check(snapshot.CarrierId == "FOUP-001", "The state snapshot must carry the carrier id.");
Check(snapshot.Slots.Count == 3
      && snapshot.Slots[0].Slot == 1 && snapshot.Slots[0].State == LoadPortSlotState.CorrectlyOccupied && snapshot.Slots[0].HasWafer
      && snapshot.Slots[1].State == LoadPortSlotState.Empty && !snapshot.Slots[1].HasWafer
      && snapshot.Slots[2].State == LoadPortSlotState.CrossSlotted && snapshot.Slots[2].HasWafer,
    "The state snapshot must carry the slot map.");
Check(!port.CreateStateDto().HasStateChanged(snapshot), "An unchanged snapshot must not count as a change.");
port.NoteMap([SlotState.Empty, SlotState.Empty, SlotState.CrossSlotted]);
Check(port.CreateStateDto().HasStateChanged(snapshot), "A slot map change must count as a change.");

// ── 载具对象：放上到取走这一程 ──────────────────────────────────────────
// 载具 ID 是空的不等于没载具——这就是要有载具对象的原因。
Check(port.Carrier is null, "还没放 FOUP 时不该有载具对象");
port.SetCarrierId("GHOST");
Check(port.Carrier is null, "没有载具时改 ID 不该凭空造出一个载具对象");

var ledger = new WaferManager();
port.NotePodPlaced(true);
port.Tick();

var carrier = port.Carrier;
Check(carrier is not null, "FOUP 放上后应建出载具对象");
Check(carrier!.Location == port.Name && carrier.Capacity == port.SlotCount,
    "载具应记住在哪个端口、几个槽");
Check(carrier.IdStatus == CarrierIdStatus.NotRead
      && carrier.SlotMapStatus == CarrierSlotMapStatus.NotRead
      && carrier.AccessStatus == CarrierAccessStatus.NotAccessed,
    "刚放上的载具三个状态都应是初始值");
Check(eap.Wait(nameof(IE87Callback.CarrierArrived)), "FOUP 放上应上报 CarrierArrived");

// Mapping：载具状态推进，同时落到晶圆账
port.NoteMap([SlotState.CorrectlyOccupied, SlotState.Empty, SlotState.CrossSlotted, SlotState.Undefined]);
Check(port.Carrier!.SlotMapStatus == CarrierSlotMapStatus.Read, "Mapping 后槽图状态应转 Read");
Check(ledger.CountWafers(port.Name) == 3, $"Mapping 应落账 3 片，实际 {ledger.CountWafers(port.Name)}");
Check(ledger.Get(port.Name, 1)?.Status == WaferStatus.Normal, "正常片应记 Normal");
Check(ledger.Get(port.Name, 2) is null, "空槽不该建片");
Check(ledger.Get(port.Name, 3)?.Status == WaferStatus.Crossed, "交叉片应记 Crossed");
Check(ledger.Get(port.Name, 4)?.Status == WaferStatus.Unknown,
    "识别不出的槽要按有片记——记成空槽机械手会往上放，那是撞片");

// 读码晚于 Mapping：账上的片要能补上载具号
Check(string.IsNullOrEmpty(ledger.Get(port.Name, 1)?.CarrierId), "Mapping 时还没读码，载具号应为空");
port.SetCarrierId("FOUP-777");
Check(port.Carrier!.CarrierId == "FOUP-777" && port.Carrier.IdStatus == CarrierIdStatus.Verified,
    "Host 改写载具 ID 即认定");
Check(ledger.Get(port.Name, 1)?.CarrierId == "FOUP-777", "读码回来后应补上账上所有片的载具号");
Check(ledger.Get(port.Name, 4)?.CarrierId == "FOUP-777", "补载具号应覆盖整个模块");

// 访问状态：走真路径（Begin → 状态表 → 操作终结 → OnOperationCompleted），
// 顺带把 LoadCompleted/AccessStarted 那条完成分支也覆盖掉。
port.Open();
port.NoteState(ModuleState.Idle);

var loadOp = new ProbeOperation();
Check(port.BeginAction(LoadPortAction.Load, loadOp) is not null, "Idle 状态应能发起 Load");
loadOp.Succeed();
port.Tick();
Check(port.State == LoadPortState.Loaded, $"Load 成功应落 Loaded，实际 {port.State}");
Check(port.Carrier!.AccessStatus == CarrierAccessStatus.InAccess, "Load 完成后载具应进 InAccess");
Check(eap.Wait(nameof(IE87Callback.LoadCompleted)), "Load 完成应上报 LoadCompleted");
Check(eap.Wait(nameof(IE87Callback.AccessStarted)), "Load 完成应上报 AccessStarted");

// Unload：这一轮取放结束，但"干完了"不由 Unload 判——只把 InAccess 退回未取放
var unloadOp = new ProbeOperation();
Check(port.BeginAction(LoadPortAction.Unload, unloadOp) is not null, "Loaded 状态应能发起 Unload");
unloadOp.Succeed();
port.Tick();
Check(port.Carrier!.AccessStatus == CarrierAccessStatus.NotAccessed,
    "Unload 只结束这一轮取放，应把 InAccess 退回 NotAccessed");

// 再开一轮，这次上层判完成：Complete 之后 Unload 不能把它退回去
var reloadOp = new ProbeOperation();
port.BeginAction(LoadPortAction.Load, reloadOp);
reloadOp.Succeed();
port.Tick();
port.NoteCarrierComplete();
Check(port.Carrier!.AccessStatus == CarrierAccessStatus.Complete, "上层判完成后应转 Complete");

var lastUnload = new ProbeOperation();
port.BeginAction(LoadPortAction.Unload, lastUnload);
lastUnload.Succeed();
port.Tick();
Check(port.Carrier!.AccessStatus == CarrierAccessStatus.Complete,
    "已经判完成的载具，Unload 不该把它退回未取放");

// 取放途中出错：载具算没干完，落 Stopped
port.NoteState(ModuleState.Idle);
var breakLoad = new ProbeOperation();
port.BeginAction(LoadPortAction.Load, breakLoad);
breakLoad.Succeed();
port.Tick();
Check(port.Carrier!.AccessStatus == CarrierAccessStatus.InAccess, "重新 Load 应回到 InAccess");

var brokenUnload = new ProbeOperation();
port.BeginAction(LoadPortAction.Unload, brokenUnload);
brokenUnload.Reject();
port.Tick();
Check(port.Carrier!.AccessStatus == CarrierAccessStatus.Stopped, "取放途中出错应落 Stopped");
Check(eap.Wait(nameof(IE87Callback.PortError)), "动作失败应上报 PortError");
port.NoteCarrierComplete();

// 载具状态要能出到 DTO，并且被 HasStateChanged 认出来
var carrierSnapshot = port.CreateStateDto();
Check(carrierSnapshot is { HasCarrier: true, CarrierId: "FOUP-777" }
      && carrierSnapshot.CarrierIdStatus == CarrierIdStatus.Verified
      && carrierSnapshot.CarrierSlotMapStatus == CarrierSlotMapStatus.Read
      && carrierSnapshot.CarrierAccessStatus == CarrierAccessStatus.Complete,
    "状态快照应带出载具的三个状态");
Check(!port.CreateStateDto().HasStateChanged(carrierSnapshot), "载具没变时不该算变化");

// 取走：载具对象与这个端口的晶圆账一起清掉
port.NotePodPlaced(false);
port.Tick();
Check(port.Carrier is null, "FOUP 取走后载具对象应清掉");
Check(ledger.CountWafers(port.Name) == 0, "FOUP 取走后端口上的片也应从账上清掉，不能留幽灵片");
Check(port.CreateStateDto().HasStateChanged(carrierSnapshot), "载具走了应算状态变化");
Check(eap.Wait(nameof(IE87Callback.CarrierRemoved)), "FOUP 取走应上报 CarrierRemoved");

WaferManager.Current = null;
port.E87Callback = null;

// ── 机械手取放写晶圆账 ──────────────────────────────────────────────────
{
    var robotLedger = new WaferManager();
    var robot = new ProbeRobot();
    robot.NoteSettings(new ModuleConfig
    {
        Name = "SmokeRobot",
        Children =
        [
            new ModuleConfig
            {
                Name = "Stations",
                Children =
                [
                    new ModuleConfig
                    {
                        Name = "SmokeLP",
                        Values =
                        [
                            new ValueConfig { Name = "Number", Value = "1" },
                            new ValueConfig { Name = "Rotation", Value = "South" },
                            new ValueConfig { Name = "Travel", Value = "20" },
                        ],
                    },
                ],
            },
        ],
    });
    Check(robot.TryGetStation("SmokeLP", out var lpStation) && lpStation.Number == 1
          && lpStation.Rotation == RobotDirection.South && lpStation.Travel == 20,
        "站点表应从 sc.xml 节点读进来");

    robotLedger.RegisterLocation("SmokeLP", 5);
    var carried = robotLedger.Create("SmokeLP", 3, WaferStatus.Normal, "FOUP-ROBOT")!;

    Check(robot.Open(), "探针机械手应能打开");
    Check(robotLedger.GetSlots(robot.Name).Count == robot.ArmCount,
        $"开机应把手指注册成账本槽位，实际 {robotLedger.GetSlots(robot.Name).Count} 个");

    robot.NoteState(ModuleState.Idle);
    Check(robot.Pick(1, "NoSuchStation", 1) is null, "没配在站点表里的站点应被拒");

    // Pick 成功：片从花篮槽位挪到手指上
    robot.Next = new ProbeOperation();
    Check(robot.Pick(1, "SmokeLP", 3) is not null, "Idle 状态应能发起 Pick");
    robot.Next!.Succeed();
    robot.Tick();
    Check(robotLedger.Get("SmokeLP", 3) is null, "Pick 成功后原槽位应变空");
    Check(robotLedger.Get(robot.Name, 1)?.Id == carried.Id, "Pick 成功后片应在手指上，且还是同一片");

    // Place 成功：片从手指放回另一个槽位
    robot.Next = new ProbeOperation();
    Check(robot.Place(1, "SmokeLP", 5) is not null, "应能发起 Place");
    robot.Next!.Succeed();
    robot.Tick();
    Check(robotLedger.Get(robot.Name, 1) is null, "Place 成功后手指应空");
    Check(robotLedger.Get("SmokeLP", 5)?.Id == carried.Id, "Place 成功后片应落到目标槽位");

    // 取放失败不动账：片到底在手上还是在槽里已经说不准了，乱改比不改更糟
    robot.NoteState(ModuleState.Idle);
    robot.Next = new ProbeOperation();
    robot.Pick(1, "SmokeLP", 5);
    robot.Next!.Reject();
    robot.Tick();
    Check(robotLedger.Get("SmokeLP", 5)?.Id == carried.Id, "Pick 失败不该把片从账上挪走");
    Check(robotLedger.Get(robot.Name, 1) is null, "Pick 失败不该把片记到手指上");

    // 非取放动作终结时不碰账
    robot.NoteState(ModuleState.Idle);
    robot.Next = new ProbeOperation();
    robot.Home();
    robot.Next!.Succeed();
    robot.Tick();
    Check(robotLedger.Get("SmokeLP", 5)?.Id == carried.Id, "Home 不该动账");

    // 账实不符：设备说取成功，但账上那个槽位本来就没片
    robot.NoteState(ModuleState.Idle);
    robot.Next = new ProbeOperation();
    robot.Pick(1, "SmokeLP", 2);
    robot.Next!.Succeed();
    robot.Tick();
    Check(robotLedger.Get(robot.Name, 1) is null, "源上没片时不该凭空在手指上造出一片（上面那条 Error 日志是预期的）");

    WaferManager.Current = null;
}

// ── 报警：LoadPort / Robot 出故障要报出来，恢复了要消掉 ────────────────────
{
    var alarms = new AlarmComponent();
    bool Active(ComponentBase source, string code) =>
        alarms.ActiveAlarms.Any(alarm => alarm.SourcePath == source.FullPath && alarm.AlarmCode == code);

    // LoadPort 设备报警：跟着状态查询里的报警位走
    port.NoteStatus(new LoadPortStatus { DeviceAlarm = true });
    port.Tick();
    Check(Active(port, port.LoadPortDeviceAlarm), "状态查询里报警位亮，应报 LoadPort 设备报警");
    port.NoteStatus(null);
    port.Tick();
    Check(Active(port, port.LoadPortDeviceAlarm), "查不到状态不算恢复——不知道不等于没事");
    port.NoteStatus(new LoadPortStatus { DeviceAlarm = false });
    port.Tick();
    Check(!Active(port, port.LoadPortDeviceAlarm), "报警位灭了应恢复");

    // LoadPort 动作失败 → 受控停止
    port.NoteState(ModuleState.Idle);
    var failedLoad = new ProbeOperation();
    Check(port.BeginAction(LoadPortAction.Load, failedLoad) is not null, "Idle 应能发起 Load");
    failedLoad.Reject();
    port.Tick();
    Check(Active(port, port.ControlledStopAlarm), "Load 失败应报受控停止");
    Check(!Active(port, port.InitTimeoutAlarm), "不是 Home 超时，不该报初始化超时");

    // Home 超时 → 再加报初始化超时
    var slowHome = new ProbeOperation();
    Check(port.BeginAction(LoadPortAction.Home, slowHome) is not null, "Error 状态应允许 Home");
    slowHome.TimeOut();
    port.Tick();
    Check(Active(port, port.InitTimeoutAlarm) && Active(port, port.ControlledStopAlarm), "Home 超时应报初始化超时");

    // Home 成功回到 Idle → 动作类报警全恢复
    var goodHome = new ProbeOperation();
    port.BeginAction(LoadPortAction.Home, goodHome);
    goodHome.Succeed();
    port.Tick();
    Check(!Active(port, port.ControlledStopAlarm) && !Active(port, port.InitTimeoutAlarm),
        "Home 成功回到 Idle，动作类报警都应恢复");

    // 操作员急停顶掉的动作不报
    var interrupted = new ProbeOperation();
    var abort = new ProbeOperation();
    port.BeginAction(LoadPortAction.Load, interrupted);
    port.BeginAction(LoadPortAction.Abort, abort);
    abort.Succeed();
    port.Tick();
    Check(!Active(port, port.ControlledStopAlarm), "操作员急停顶掉的动作不该报受控停止");

    // Robot 设备报警：跟着设备报错走
    var robot = new ProbeRobot();
    Check(robot.Open(), "探针机械手应能打开");
    robot.NoteDeviceError("40010006#Arm2 No Wafer When Put");
    robot.Tick();
    Check(Active(robot, robot.RobotDeviceAlarm), "设备报错应报 Robot 设备报警");
    robot.NoteDeviceError(null);
    robot.Tick();
    Check(!Active(robot, robot.RobotDeviceAlarm), "查询确认没报错了应恢复");

    robot.NoteDeviceError("stale");
    robot.Close();
    robot.Tick();
    Check(!Active(robot, robot.RobotDeviceAlarm), "没连上时 DeviceError 是旧值，不该据此报警");

    // Robot 动作失败 → 受控停止；复位回 NotInit 还不算恢复，Home 回 Idle 才算
    Check(robot.Open(), "重新打开");
    robot.NoteDeviceError(null);
    robot.NoteState(ModuleState.Idle);
    robot.Next = new ProbeOperation();
    robot.Home();
    robot.Next!.Reject();
    robot.Tick();
    Check(Active(robot, robot.ControlledStopAlarm), "Robot 动作失败应报受控停止");

    robot.Next = new ProbeOperation();
    robot.Reset();
    robot.Next!.Succeed();
    robot.Tick();
    Check(robot.State == ModuleState.NotInit && Active(robot, robot.ControlledStopAlarm),
        "复位后是 NotInit（位置不可信，还得回原点），不该算恢复");

    robot.Next = new ProbeOperation();
    robot.Home();
    robot.Next!.Succeed();
    robot.Tick();
    Check(robot.State == ModuleState.Idle && !Active(robot, robot.ControlledStopAlarm),
        "Home 成功回到 Idle 才算恢复");

    AlarmComponent.Current = null;
}

// ── E84：按 CTC 时序走一遍送盒、取盒，闸门（没开/Manual/下线）、中途打断、超时锁住与人工恢复 ─────
{
    var alarms = new AlarmComponent();

    // 送盒/取盒整程：计时给足，不让超时掺和
    var lp = new ProbePort("E84SmokePort");
    var e84 = new ProbeE84(lp.Name, timeoutMs: 60000);
    lp.AddChild(e84);
    var events = new RecordingE84Callback();
    lp.E84Callback = events;
    Check(ReferenceEquals(lp.E84, e84), "LoadPort 应能按接口找到 E84 子组件");
    Check(lp.Open(), "带 E84 的端口应能打开");
    lp.NoteState(ModuleState.Idle);

    lp.Tick();
    Check(e84.State == E84State.NotAvailable && e84.Outputs == default, "Manual + 下线时 E84 不可交接，输出全灭");
    lp.SetAutoMode(true);
    lp.Tick();
    Check(e84.State == E84State.NotAvailable && !e84.Outputs.HoAvbl, "只切 Auto、没上线（Out Of Service）也不交接");
    lp.Online();
    lp.Tick();
    Check(e84.State == E84State.Available && e84.Outputs is { HoAvbl: true, Es: true, LReq: false, UReq: false },
        "Auto + 上线 + 空闲 + 没载具：亮 HO_AVBL 等搬运车");
    Check(events.Wait("AvailabilityChanged:True"), "HO_AVBL 亮了应上报");

    // 送盒：CS_0+VALID → L_REQ；TR_REQ → READY（交接开始）；BUSY 时载具放上 → 撤 L_REQ；BUSY 撤；COMPT → 撤 READY；信号全撤 → 完成
    e84.Set(cs0: true, valid: true);
    lp.Tick();
    Check(e84.State == E84State.Requesting && e84.Outputs is { LReq: true, UReq: false, Ready: false },
        "空端口被选中应亮 L_REQ");
    e84.Set(cs0: true, valid: true, trReq: true);
    lp.Tick();
    Check(e84.State == E84State.Loading && e84.Outputs.Ready, "TR_REQ 来了应给 READY");
    Check(events.Wait("HandoffStarted:True"), "送盒交接开始应上报");
    e84.Set(cs0: true, valid: true, trReq: true, busy: true);
    lp.Tick();
    Check(e84.Outputs is { LReq: true, Ready: true }, "载具还没放上，L_REQ 保持");
    lp.NotePodPlaced(true);
    lp.Tick();
    Check(e84.Outputs is { LReq: false, Ready: true }, "载具放上应撤 L_REQ");
    e84.Set(cs0: true, valid: true, trReq: true);
    lp.Tick();
    e84.Set(cs0: true, valid: true, trReq: true, compt: true);
    lp.Tick();
    Check(!e84.Outputs.Ready && e84.State == E84State.Loading, "COMPT 来了应撤 READY，等信号全撤");
    e84.Set();
    lp.Tick();
    Check(events.Wait("HandoffCompleted:True"), "信号全撤应算送盒完成并上报");
    Check(e84.State == E84State.Available && e84.Outputs is { HoAvbl: true, LReq: false, UReq: false },
        "送盒完成后回到可交接");

    // 盒子刚到、还没干完：搬运车再来也不给取
    e84.Set(cs0: true, valid: true);
    lp.Tick();
    Check(e84.State == E84State.Available && !e84.Outputs.UReq, "这一盒还没干完，不该亮 U_REQ");

    // 取盒：干完了才亮 U_REQ；载具被取走 → 撤 U_REQ；COMPT → 撤 READY；信号全撤 → 完成
    lp.NoteCarrierComplete();
    lp.Tick();
    Check(e84.State == E84State.Requesting && e84.Outputs is { UReq: true, LReq: false }, "这一盒干完了应亮 U_REQ");
    e84.Set(cs0: true, valid: true, trReq: true);
    lp.Tick();
    Check(e84.State == E84State.Unloading && e84.Outputs.Ready, "取盒 TR_REQ 来了应给 READY");
    Check(events.Wait("HandoffStarted:False"), "取盒交接开始应上报");
    e84.Set(cs0: true, valid: true, trReq: true, busy: true);
    lp.Tick();
    Check(e84.Outputs.UReq, "载具还在，U_REQ 保持");
    lp.NotePodPlaced(false);
    lp.Tick();
    Check(e84.Outputs is { UReq: false, Ready: true }, "载具取走应撤 U_REQ");
    e84.Set(cs0: true, valid: true, trReq: true);
    lp.Tick();
    e84.Set(cs0: true, valid: true, trReq: true, compt: true);
    lp.Tick();
    e84.Set();
    lp.Tick();
    Check(events.Wait("HandoffCompleted:False"), "取盒完成应上报");
    Check(e84.State == E84State.Available, "取盒完成后空端口回到可交接");

    // 送盒中途切 Manual：输出全灭，按中止上报；切回 Auto 重新可交接
    e84.Set(cs0: true, valid: true);
    lp.Tick();
    e84.Set(cs0: true, valid: true, trReq: true);
    lp.Tick();
    Check(e84.State == E84State.Loading, "又开始一次送盒");
    lp.SetAutoMode(false);
    lp.Tick();
    Check(e84.State == E84State.NotAvailable && e84.Outputs == default, "切 Manual 应立刻撤掉全部输出");
    Check(events.Wait("HandoffAborted:True"), "交接进行中被打断应按中止上报");
    e84.Set();
    lp.SetAutoMode(true);
    lp.Tick();
    Check(e84.State == E84State.Available, "切回 Auto 应重新可交接");

    // 选中后没等到 TR_REQ 搬运车就撤了：收回请求，不算交接
    e84.Set(cs0: true, valid: true);
    lp.Tick();
    e84.Set();
    lp.Tick();
    Check(e84.State == E84State.Available && !e84.Outputs.LReq, "搬运车撤销选中应收回 L_REQ");

    // EC 没开：什么都不亮
    var offPort = new ProbePort("E84OffPort");
    var offE84 = new ProbeE84(offPort.Name, enabled: false);
    offPort.AddChild(offE84);
    Check(offPort.Open(), "E84 没开的端口也应能打开");
    offPort.NoteState(ModuleState.Idle);
    offPort.Online();
    offPort.SetAutoMode(true);
    offE84.Set(cs0: true, valid: true);
    offPort.Tick();
    Check(offE84.State == E84State.NotAvailable && offE84.Outputs == default, "E84 没开（EC）不该理搬运车");

    // 超时：计时调短；TP1 没等到 TR_REQ → 锁住、撤输出、报警；Retry 解锁
    var tpPort = new ProbePort("E84TimeoutPort");
    var tpE84 = new ProbeE84(tpPort.Name, timeoutMs: 100);
    tpPort.AddChild(tpE84);
    var tpEvents = new RecordingE84Callback();
    tpPort.E84Callback = tpEvents;
    Check(tpPort.Open(), "超时用的端口应能打开");
    tpPort.NoteState(ModuleState.Idle);
    tpPort.Online();
    tpPort.SetAutoMode(true);
    tpE84.Set(cs0: true, valid: true);
    tpPort.Tick();
    Check(tpE84.State == E84State.Requesting, "亮了 L_REQ 等 TR_REQ");
    Thread.Sleep(150);
    tpPort.Tick();
    Check(tpE84.State == E84State.TimedOut && tpE84.TimedOutTimer == E84Timer.TP1
          && tpE84.Outputs is { LReq: false, HoAvbl: false, Es: true },
        "TP1 超时应锁住并撤 L_REQ/HO_AVBL（ES 保持）");
    Check(tpEvents.Wait("HandoffTimeout:True:TP1"), "TP1 超时应上报");
    Check(Active(tpE84, tpE84.E84TimeoutAlarm, alarms), "超时应报 E84 交接超时");
    tpE84.Set();
    tpPort.Tick();
    Check(tpE84.State == E84State.TimedOut, "搬运车撤了也不自动解锁，得人工恢复");
    Check(!tpE84.Complete(), "送盒没放上载具，Complete 应被拒");
    tpE84.Retry();
    tpPort.Tick();
    Check(tpE84.State == E84State.Available && tpE84.Outputs.HoAvbl && tpE84.TimedOutTimer is null,
        "Retry 后应重新可交接");
    Check(!Active(tpE84, tpE84.E84TimeoutAlarm, alarms), "Retry 应消掉超时报警");

    // TP3 超时（BUSY 了载具迟迟没到），人工确认载具其实放好了 → Complete 按送盒完成收尾
    tpE84.Set(cs0: true, valid: true);
    tpPort.Tick();
    tpE84.Set(cs0: true, valid: true, trReq: true);
    tpPort.Tick();
    tpE84.Set(cs0: true, valid: true, trReq: true, busy: true);
    tpPort.Tick();
    Thread.Sleep(150);
    tpPort.Tick();
    Check(tpE84.State == E84State.TimedOut && tpE84.TimedOutTimer == E84Timer.TP3, "等载具放上超时应锁在 TP3");
    Check(tpEvents.Wait("HandoffTimeout:True:TP3"), "TP3 超时应上报");
    tpE84.Set();
    tpPort.NotePodPlaced(true);
    Check(tpE84.Complete(), "载具确实放上了，Complete 应按完成收尾");
    Check(tpEvents.Wait("HandoffCompleted:True"), "人工 Complete 应上报交接完成");
    tpPort.Tick();
    Check(tpE84.State == E84State.Available && !Active(tpE84, tpE84.E84TimeoutAlarm, alarms),
        "Complete 后回到可交接，报警消掉");

    AlarmComponent.Current = null;

    static bool Active(ComponentBase source, string code, AlarmComponent alarms) =>
        alarms.ActiveAlarms.Any(alarm => alarm.SourcePath == source.FullPath && alarm.AlarmCode == code);
}

Console.WriteLine($"PASS: {checks} operation wait checks (including 200 completion races, five device RPC actions, the online/offline and auto/manual mode switches, the EAP callback path, the carrier lifecycle from arrival to removal, and robot pick/place writing the wafer ledger, LoadPort/Robot alarms raised and cleared, and the E84 handoff flow: load, unload, gating, abort, timeout and recovery).");

// 只为满足"驱动已连接"这个前置条件；真实帧收发不在本工具的范围内。
sealed class FakeFrameCommunication : IFrameCommunication
{
    public event Action<string>? FrameReceived;
    public bool IsConnected { get; private set; }
    public bool Open() { IsConnected = true; return true; }
    public void Close() => IsConnected = false;
    public void Send(string body) { }
    public void Push(string body) => FrameReceived?.Invoke(body);
}

sealed class ProbeOperation() : ModuleOperation("Probe")
{
    public bool FinishOnScan { get; init; }
    public int AbortCount { get; private set; }
    public void Succeed() => Complete();
    public void Reject() => Fail(ErrorCodes.DeviceFailed, "device error", Name, "device error");
    public void TimeOut() => Fail(ErrorCodes.Timeout, "action timeout", Name, "1000");
    protected override void OnScan() { if (FinishOnScan) Complete(); }
    protected override void OnAborted(string reason) => AbortCount++;
}

sealed class ProbeModule : BaseModule
{
    public ManualResetEventSlim? Completing { get; init; }
    public ManualResetEventSlim? ReleaseCompletion { get; init; }
    public bool CleanupDone { get; private set; }
    public bool StartOperation(ModuleOperation operation, bool replace = false) => Run(operation, replace);
    public void Tick() => OnScan();
    protected override void OnOperationCompleted(ModuleOperation operation)
    {
        Completing?.Set();
        if (ReleaseCompletion is not null && !ReleaseCompletion.Wait(2000))
            throw new TimeoutException("Test completion callback was not released.");
        CleanupDone = true;
    }
}

sealed class ProbePort : BaseLoadPortModule
{
    public ProbeOperation? Next { get; set; }
    public int Calls { get; private set; }
    public ProbePort(string name = "WaitSmokePort")
    {
        // Production names are assigned by ComponentLoader through internal setters.
        typeof(ComponentBase).GetProperty(nameof(Name))!.SetValue(this, name);
        typeof(ComponentBase).GetProperty(nameof(FullPath))!.SetValue(this, Name);
        foreach (var key in new[]
                 {
                     nameof(LoadTimeout), nameof(UnloadTimeout), nameof(HomeTimeout), nameof(ResetTimeout),
                     nameof(AbortTimeout)
                 })
        {
            // Seed only this process's EC memory; never load or flush a configuration file.
            EC.UpsertValueMetadata(Name, key, "0", "Int", "ms", null, null, null, null, null);
        }
    }
    public void NoteMap(IReadOnlyList<SlotState> slotMap) => UpdateSlotMap(slotMap);

    /// <summary>顶替驱动的 PODON/PODOF 主动事件翻在位位（生产里由驱动路由线程翻）。</summary>
    public void NotePodPlaced(bool placed) =>
        typeof(BaseLoadPortModule).GetProperty(nameof(IsPodPlaced))!.SetValue(this, placed);

    /// <summary>顶替扫描线程推一拍（本工具不跑扫描循环）。</summary>
    public void Tick() => OnScan();

    /// <summary>假驱动：Begin() 要求有驱动且已连接，这里只为把那道门打开，不收发真实帧。</summary>
    protected override ILoadPortDriver CreateDriver() => new FcdLoadPortDriver(new FakeFrameCommunication());

    /// <summary>直接摆状态，省去为了进 Idle 先跑一遍 Home。</summary>
    public void NoteState(int state) => State = state;

    /// <summary>摆一个设备状态查询结果（生产里由机型扫描下发 GET:STATE 刷新）。</summary>
    public void NoteStatus(LoadPortStatus? status) => Status = status;

    /// <summary>走真路径发起动作（状态表 + 操作登记），不是 Load() 那种直接返回。</summary>
    public ModuleOperation? BeginAction(LoadPortAction action, ModuleOperation operation) => Begin(action, operation);
    private ModuleOperation? Take() { Calls++; return Next; }
    public override ModuleOperation? Load() => Take();
    public override ModuleOperation? Unload() => Take();
    public override ModuleOperation? Home() => Take();
    public override ModuleOperation? Reset() => Take();
    public override ModuleOperation? Abort() => Take();
    public override ModuleOperation? Clamp() => Take();
    public override ModuleOperation? Unclamp() => Take();
}

// 探针 E84：IO 读写层还没接，输入由测试直接摆，输出只记次数；EC 只在本进程内存里种，不落盘。
sealed class ProbeE84 : E84Component
{
    private E84Inputs _next;

    public int Writes { get; private set; }

    public ProbeE84(string portName, int timeoutMs = 60000, bool enabled = true)
    {
        typeof(ComponentBase).GetProperty(nameof(Name))!.SetValue(this, "E84");
        typeof(ComponentBase).GetProperty(nameof(FullPath))!.SetValue(this, $"{portName}.E84");
        EC.UpsertValueMetadata(FullPath, nameof(E84Enabled), enabled.ToString(), "Bool", null, null, null, null, null, null);
        foreach (var key in new[] { nameof(Tp1Timeout), nameof(Tp2Timeout), nameof(Tp3Timeout), nameof(Tp4Timeout), nameof(Tp5Timeout) })
        {
            EC.UpsertValueMetadata(FullPath, key, timeoutMs.ToString(), "Int", "ms", null, null, null, null, null);
        }
    }

    /// <summary>摆下一拍搬运车给的信号（没给的都当 OFF）。</summary>
    public void Set(bool cs0 = false, bool valid = false, bool trReq = false, bool busy = false, bool compt = false) =>
        _next = new E84Inputs { Cs0 = cs0, Valid = valid, TrReq = trReq, Busy = busy, Compt = compt };

    protected override E84Inputs ReadInputs() => _next;

    protected override void WriteOutputs(E84Outputs outputs) => Writes++;
}

// 只记下收到了哪些 E84 回调（带方向/计时段），派发在别的线程上，等到为止。
sealed class RecordingE84Callback : IE84Callback
{
    private readonly ConcurrentDictionary<string, bool> _received = new();

    public bool Wait(string name, int timeoutMs = 2000)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < timeoutMs)
        {
            if (_received.ContainsKey(name)) return true;
            Thread.Sleep(5);
        }

        return false;
    }

    public void HandoffStarted(ILoadPort port, bool isLoad) => _received[$"HandoffStarted:{isLoad}"] = true;
    public void HandoffCompleted(ILoadPort port, bool isLoad) => _received[$"HandoffCompleted:{isLoad}"] = true;
    public void HandoffTimeout(ILoadPort port, bool isLoad, E84Timer timer) => _received[$"HandoffTimeout:{isLoad}:{timer}"] = true;
    public void HandoffAborted(ILoadPort port, bool isLoad, string reason) => _received[$"HandoffAborted:{isLoad}"] = true;
    public void AvailabilityChanged(ILoadPort port, bool available) => _received[$"AvailabilityChanged:{available}"] = true;
}

// 只记下收到了哪些回调：派发在别的线程上，等到为止。
sealed class RecordingE87Callback : IE87Callback
{
    private readonly ConcurrentDictionary<string, bool> _received = new();

    public bool Wait(string name, int timeoutMs = 2000)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < timeoutMs)
        {
            if (_received.ContainsKey(name)) return true;
            Thread.Sleep(5);
        }

        return false;
    }

    private void Note([CallerMemberName] string? name = null) => _received[name!] = true;

    public void CarrierArrived(ILoadPort port) => Note();
    public void CarrierRemoved(ILoadPort port, string? carrierId) => Note();
    public void CarrierIdRead(ILoadPort port, string carrierId) => Note();
    public void CarrierIdReadFailed(ILoadPort port) => Note();
    public void SlotMapRead(ILoadPort port, IReadOnlyList<SlotState> slotMap) => Note();
    public void LoadCompleted(ILoadPort port) => Note();
    public void UnloadCompleted(ILoadPort port) => Note();
    public void Homed(ILoadPort port) => Note();
    public void ClampCompleted(ILoadPort port) => Note();
    public void UnclampCompleted(ILoadPort port) => Note();
    public void AutoModeChanged(ILoadPort port, bool autoMode) => Note();
    public void AccessStarted(ILoadPort port) => Note();
    public void AccessStopped(ILoadPort port) => Note();
    public void CarrierComplete(ILoadPort port) => Note();
    public void PortError(ILoadPort port, string error) => Note();
}

// 探针机械手：动作体不连设备，只为验"取放成功之后账有没有跟着动"。
sealed class ProbeRobot : BaseRobotModule
{
    public ProbeOperation? Next { get; set; }

    public ProbeRobot()
    {
        typeof(ComponentBase).GetProperty(nameof(Name))!.SetValue(this, "SmokeRobot");
        typeof(ComponentBase).GetProperty(nameof(FullPath))!.SetValue(this, Name);
        foreach (var key in new[] { nameof(QueryDataTimeOut), nameof(HomeTimeout), nameof(PickTimeout),
                     nameof(PlaceTimeout), nameof(ResetTimeout), nameof(AbortTimeout), nameof(PowerTimeout) })
        {
            EC.UpsertValueMetadata(Name, key, "0", "Int", "ms", null, null, null, null, null);
        }
    }

    protected override IRobotDriver CreateDriver() => new RejeRobotDriver(new FakeFrameCommunication());

    /// <summary>直接摆状态，省去为了进 Idle 先跑一遍 Home。</summary>
    public void NoteState(int state) => State = state;

    /// <summary>顶替扫描线程推一拍。</summary>
    public void Tick() => OnScan();

    /// <summary>灌站点表（生产里由 ComponentLoader 把 sc.xml 节点交给 OnSettingLoaded）。</summary>
    public void NoteSettings(ModuleConfig setting) => OnSettingLoaded(setting);

    /// <summary>摆一个设备报错（生产里由查询或主动推送刷新）。</summary>
    public void NoteDeviceError(string? error) => DeviceError = error;

    private ModuleOperation? Take(RobotAction action) => Next is null ? null : Begin(action, Next);

    public override ModuleOperation? Home() => Take(RobotAction.Home);
    public override ModuleOperation? Reset() => Take(RobotAction.Reset);
    public override ModuleOperation? Abort() => Take(RobotAction.Abort);
    public override ModuleOperation? PowerOn() => Take(RobotAction.PowerOn);
    public override ModuleOperation? PowerOff() => Take(RobotAction.PowerOff);
    protected override ModuleOperation CreatePickOperation(int arm, int stationNumber, int slot) => Supply();
    protected override ModuleOperation CreatePlaceOperation(int arm, int stationNumber, int slot) => Supply();

    private ModuleOperation Supply() =>
        Next ?? throw new InvalidOperationException("用例忘了给 ProbeRobot.Next 摆一个操作。");
}
