using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using xyz.Components.Interfaces;
using xyz.Components;
using xyz.Components.Attributes;
using xyz.Components.Components;
using xyz.Components.Enums;
using xyz.Configs.Models;
using xyz.Drivers.Communication;
using xyz.Drivers.Loadport;
using xyz.Drivers.Loadport.FCD;
using xyz.Drivers.Rfid;
using xyz.Drivers.Rfid.FCD;
using xyz.Drivers.Robot;
using xyz.Drivers.Robot.Reje;
using xyz.Modules;
using xyz.Modules.Enums;
using xyz.Service;
using xyz.Service.Events;
using xyz.Service.Systems;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Shared.Rpc;

// No host, hardware connection, or configuration-file writes are used by these checks.
var checks = 0;
void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }

    checks++;
}

void Throws<T>(Action action) where T : Exception
{
    try
    {
        action();
    }
    catch (T)
    {
        checks++;
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}

// EC 只放在本进程内存里：直接 new 的 EC 组件不读也不写 ec.xml，各探针构造时经 EC 属性把值摆进去。
var ec = new EcComponent();

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
    finally
    {
        release.Set();
    }

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

// 跟开机装配一样先做组件初始化：载具组件在这里挂到端口上
var port = new ProbePort();
port.InitComponent();
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
var onlineSnapshot = port.CreateStateDto();
Check(onlineSnapshot is not null && onlineSnapshot.Mode == ModuleMode.Online
      && port.CreateStateDto().HasStateChanged(modeSnapshot),
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

// EAP 口子：回调走 EAP 的派发组件（宿主里是 sc.xml Eap 下的 Notifier）；本工具不跑扫描循环，正好证明派发不依赖扫描。
_ = new EapNotifierComponent();
var eap = new RecordingE87Callback();
port.E87Callback = eap;
port.SetAutoMode(true);
Check(eap.Wait(nameof(IE87Callback.AutoModeChanged)), "EAP callbacks must be delivered without a scan loop.");
port.SetAutoMode(false);

// 载具 ID 与 Mapping 结果要能进状态快照（DTO），否则出不了服务进程。
port.NoteMap([SlotState.CorrectlyOccupied, SlotState.Empty, SlotState.CrossSlotted]);
Check(eap.Wait(nameof(IE87Callback.SlotMapRead)), "Mapping must report SlotMapRead.");
port._carrier.SetId("FOUP-001");

var snapshot = port.CreateStateDto();
Check(snapshot.CarrierId == "FOUP-001", "The state snapshot must carry the carrier id.");
Check(snapshot.Slots.Count == 3
      && snapshot.Slots[0].Slot == 1 && snapshot.Slots[0].State == LoadPortSlotState.CorrectlyOccupied && snapshot.Slots[0].HasWafer
      && snapshot.Slots[1].State == LoadPortSlotState.Empty && !snapshot.Slots[1].HasWafer
      && snapshot.Slots[2].State == LoadPortSlotState.CrossSlotted && snapshot.Slots[2].HasWafer,
    "The state snapshot must carry the slot map.");
Check(!port.CreateStateDto().HasStateChanged(snapshot), "An unchanged snapshot must not count as a change.");
Check(new LoadPortSlotDto { State = LoadPortSlotState.Undefined }.HasWafer
      && new LoadPortSlotDto { State = LoadPortSlotState.NotEmpty }.HasWafer
      && new LoadPortSlotDto { State = LoadPortSlotState.DoubleSlotted }.HasWafer
      && !new LoadPortSlotDto { State = LoadPortSlotState.Empty }.HasWafer,
    "槽位有没有片：不是空槽就算有，认不出的也算（跟记账一样，宁可多记不可漏记）");
port.NoteMap([SlotState.Empty, SlotState.Empty, SlotState.CrossSlotted]);
Check(port.CreateStateDto().HasStateChanged(snapshot), "A slot map change must count as a change.");

// ── 载具对象：放上到取走这一程 ──────────────────────────────────────────
// 载具 ID 是空的不等于没载具——这就是要有载具对象的原因。
Check(port._carrier.Info is null, "还没放 FOUP 时不该有载具对象");
port._carrier.SetId("GHOST");
Check(port._carrier.Info is null, "没有载具时改 ID 不该凭空造出一个载具对象");

var ledger = new WaferManagerComponent();
port.NotePodPlaced(true);
port.Tick();

var carrier = port._carrier.Info;
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
Check(port._carrier.Info!.SlotMapStatus == CarrierSlotMapStatus.Read, "Mapping 后槽图状态应转 Read");
Check(ledger.CountWafers(port.Name) == 3, $"Mapping 应落账 3 片，实际 {ledger.CountWafers(port.Name)}");
Check(ledger.Get(port.Name, 1)?.Status == WaferStatus.Normal, "正常片应记 Normal");
Check(ledger.Get(port.Name, 2) is null, "空槽不该建片");
Check(ledger.Get(port.Name, 3)?.Status == WaferStatus.Crossed, "交叉片应记 Crossed");
Check(ledger.Get(port.Name, 4)?.Status == WaferStatus.Unknown,
    "识别不出的槽要按有片记——记成空槽机械手会往上放，那是撞片");

// 状态推送带着账：LoadPort 页按账画片，取放片、人工改账都跟着变，不用另外通知
var mappedState = port.CreateStateDto();
Check(mappedState.LedgerSlots.Count == port.SlotCount
      && mappedState.LedgerSlots[0].Wafer?.Status == "Normal" && mappedState.LedgerSlots[1].Wafer is null
      && mappedState.LedgerSlots[2].Wafer?.Status == "Crossed",
    "LoadPort 状态推送应带上账上每个槽的片");
ledger.SetProcessState(port.Name, 1, WaferProcessState.InProcess);
Check(port.CreateStateDto().HasStateChanged(mappedState), "账一变（改工艺状态）LoadPort 状态推送就该算变化");
ledger.SetProcessState(port.Name, 1, WaferProcessState.Idle);

// 读码晚于 Mapping：账上的片要能补上载具号
Check(string.IsNullOrEmpty(ledger.Get(port.Name, 1)?.CarrierId), "Mapping 时还没读码，载具号应为空");
port._carrier.SetId("FOUP-777");
Check(port._carrier.Info!.CarrierId == "FOUP-777" && port._carrier.Info.IdStatus == CarrierIdStatus.Verified,
    "Host 改写载具 ID 即认定");
Check(ledger.Get(port.Name, 1)?.CarrierId == "FOUP-777", "读码回来后应补上账上所有片的载具号");
Check(ledger.Get(port.Name, 4)?.CarrierId == "FOUP-777", "补载具号应覆盖整个模块");

// 访问状态：走真路径（Begin → 状态表 → 操作终结 → OnOperationCompleted）。
// E87 的 IN ACCESS 照老 CTC：Load 好了就算开始取放。
port.NoteState(ModuleState.Idle);
Check(port.LocalTransferState == LoadPortTransferState.OutOfService, "端口下线（不参与自动调度）时自己判停用");
port.Online();
Check(port.IsIdle && !port.IsLoaded && port.LocalTransferState == LoadPortTransferState.TransferBlocked,
    "空闲、有载具、还没干完：端口自己判挡着（不让天车取走）");

var loadOp = new ProbeOperation();
Check(port.BeginAction(LoadPortAction.Load, loadOp) is not null, "Idle 状态应能发起 Load");
loadOp.Succeed();
port.Tick();
Check(port.State == LoadPortState.Loaded, $"Load 成功应落 Loaded，实际 {port.State}");
Check(port.IsLoaded && !port.IsIdle, "Loaded 时 IsLoaded 为真、IsIdle 为假");
Check(port._carrier.Info!.AccessStatus == CarrierAccessStatus.InAccess, "Load 好了载具就进 InAccess");
Check(eap.Wait(nameof(IE87Callback.LoadCompleted)), "Load 完成应上报 LoadCompleted");
Check(eap.Wait(nameof(IE87Callback.AccessStarted)), "Load 完成接着上报 AccessStarted");

// 机械手进站要载具在、Host 认可了槽图（CanPrepare）：接了 EAP、槽图还没被 Host 认定，Loaded 了也不让进（手动传片不经过 Job，也靠这一关挡住）
Check(!port.CanPrepare && port.PrepareTransfer() is null && port.State == LoadPortState.Loaded,
    "接了 EAP、槽图没认定：机械手不能进站");
port._carrier.UpdateStatus(null, CarrierSlotMapStatus.Verified);
Check(port.CanPrepare && port.PrepareTransfer() is not null && port.CancelTransfer() && port.State == LoadPortState.Loaded,
    "槽图认定后机械手能进站；准备再撤回应回到 Loaded");
port._carrier.UpdateStatus(null, CarrierSlotMapStatus.Read);

// 取放途中出错：载具算没干完，落 Stopped
var brokenUnload = new ProbeOperation();
Check(port.BeginAction(LoadPortAction.Unload, brokenUnload) is not null, "Loaded 状态应能发起 Unload");
brokenUnload.Reject();
port.Tick();
Check(port._carrier.Info!.AccessStatus == CarrierAccessStatus.Stopped, "取放途中出错应落 Stopped");
Check(eap.Wait(nameof(IE87Callback.PortError)), "动作失败应上报 PortError");

// 上层判完成：Complete 之后 Unload 不能把它改掉；Unload 好了端口自己判等取走
port.NoteState(ModuleState.Idle);
port._carrier.NoteComplete();
Check(port._carrier.Info!.AccessStatus == CarrierAccessStatus.Complete, "上层判完成后应转 Complete");
var reloadOp = new ProbeOperation();
port.BeginAction(LoadPortAction.Load, reloadOp);
reloadOp.Succeed();
port.Tick();
var lastUnload = new ProbeOperation();
port.BeginAction(LoadPortAction.Unload, lastUnload);
lastUnload.Succeed();
port.Tick();
Check(port._carrier.Info!.AccessStatus == CarrierAccessStatus.Complete,
    "已经判完成的载具，Unload 不该把它改掉");
Check(port.IsIdle && port.LocalTransferState == LoadPortTransferState.ReadyToUnload, "干完了、Unload 好了：端口自己判等取走");
port.Offline();

// Host 核对的进展写回设备侧（EAP 的 E87 用）：给了的那一项才改
port._carrier.UpdateStatus(null, CarrierSlotMapStatus.WaitingForHost);
Check(port._carrier.Info!.IdStatus == CarrierIdStatus.Verified && port._carrier.Info.SlotMapStatus == CarrierSlotMapStatus.WaitingForHost,
    "UpdateStatus 只改给了的槽图状态");

// 槽图认定状态只往前走：Host 已经认定的载具再 Map 一次（比如 Unload 之后又 Load），只更新槽图和账，不用重新等 Host
port._carrier.UpdateStatus(null, CarrierSlotMapStatus.Verified);
port.NoteMap([SlotState.CorrectlyOccupied, SlotState.Empty, SlotState.CorrectlyOccupied]);
Check(port._carrier.Info!.SlotMapStatus == CarrierSlotMapStatus.Verified, "已认定的槽图再 Map 一次，认定状态保持（不回 Read）");
Check(port._carrier.SlotMap.Count == 3 && port._carrier.SlotMap[2] == SlotState.CorrectlyOccupied, "再 Map 一次，槽图照常更新");
port._carrier.UpdateStatus(null, CarrierSlotMapStatus.Read);

// 载具状态要能出到 DTO，并且被 HasStateChanged 认出来
var carrierSnapshot = port.CreateStateDto();
Check(carrierSnapshot is not null && carrierSnapshot.HasCarrier && carrierSnapshot.CarrierId == "FOUP-777"
      && carrierSnapshot.CarrierIdStatus == CarrierIdStatus.Verified
      && carrierSnapshot.CarrierSlotMapStatus == CarrierSlotMapStatus.Read
      && carrierSnapshot.CarrierAccessStatus == CarrierAccessStatus.Complete,
    "状态快照应带出载具的三个状态");
Check(!port.CreateStateDto().HasStateChanged(carrierSnapshot), "载具没变时不该算变化");

// 取走：载具对象与这个端口的晶圆账一起清掉
port.NotePodPlaced(false);
port.Tick();
Check(port._carrier.Info is null, "FOUP 取走后载具对象应清掉");
Check(ledger.CountWafers(port.Name) == 0, "FOUP 取走后端口上的片也应从账上清掉，不能留幽灵片");
Check(port.CreateStateDto().HasStateChanged(carrierSnapshot), "载具走了应算状态变化");
Check(eap.Wait(nameof(IE87Callback.CarrierRemoved)), "FOUP 取走应上报 CarrierRemoved");

// 新放一个载具：取放过、没判完成就正常 Unload 了，也算中断（E87 CARRIER STOPPED）
port.NotePodPlaced(true);
port.Tick();
port.NoteState(ModuleState.Idle);
var secondLoad = new ProbeOperation();
port.BeginAction(LoadPortAction.Load, secondLoad);
secondLoad.Succeed();
port.Tick();
Check(port._carrier.Info!.AccessStatus == CarrierAccessStatus.InAccess, "第二个载具：Load 好了就在取放");
var secondUnload = new ProbeOperation();
port.BeginAction(LoadPortAction.Unload, secondUnload);
secondUnload.Succeed();
port.Tick();
Check(port._carrier.Info!.AccessStatus == CarrierAccessStatus.Stopped, "取放过、没判完成就 Unload，载具应落 Stopped");
port.NotePodPlaced(false);
port.Tick();

WaferManagerComponent.Current = null;
port.E87Callback = null;

// ── 机械手取放写晶圆账 ──────────────────────────────────────────────────
{
    var robotLedger = new WaferManagerComponent();
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
                            new ValueConfig { Name = "Y", Value = "20" },
                            new ValueConfig { Name = "Direction", Value = "South" },
                        ],
                    },
                ],
            },
        ],
    });
    Check(robot.TryGetStation("SmokeLP", out var lpStation) && lpStation.Number == 1
          && lpStation.Direction == RobotDirection.South && lpStation.Y == 20,
        "站点表应从 sc.xml 节点读进来");

    robotLedger.RegisterLocation("SmokeLP", 5);
    var carried = robotLedger.Create("SmokeLP", 3, WaferStatus.Normal, "FOUP-ROBOT")!;

    Check(robot.InitComponent(), "探针机械手的组件初始化应成功");
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

    // 状态推送带着账：手指上的片（片号、状态）跟着账走，界面不用另外通知
    var pickedState = robot.CreateStateDto();
    Check(pickedState.LedgerSlots.Count == robot.ArmCount
          && pickedState.LedgerSlots[0].Slot == 1 && pickedState.LedgerSlots[0].Wafer?.WaferId == carried.WaferId
          && pickedState.LedgerSlots[0].Wafer?.ProcessState == "Idle",
        "机械手状态推送应带上账上手指的片");
    var beforeLedgerChange = robot.CreateStateDto();
    robotLedger.SetWaferId(robot.Name, 1, "W-RENAMED");
    Check(robot.CreateStateDto().HasStateChanged(beforeLedgerChange), "账一变（改片号）状态推送就该算变化，下一拍推给界面");
    robotLedger.SetWaferId(robot.Name, 1, carried.WaferId);

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

    WaferManagerComponent.Current = null;
}

// ── 报警：LoadPort / _robot 出故障要报出来；报出去以后只能人工 Reset 清，源头恢复、动作成功都不清 ──────
{
    var alarms = new AlarmComponent();
    bool Active(ComponentBase source, string code) =>
        alarms.ActiveAlarms.Any(alarm => alarm.SourcePath == source.FullPath && alarm.AlarmCode == code);

    // LoadPort 设备报警：状态查询里的报警位亮就报；查不到、灭了都不清
    port.NoteStatus(new LoadPortStatus { IsDeviceAlarm = true });
    port.Tick();
    Check(Active(port, port.LoadPortDeviceAlarm) && port.HasAlarm, "状态查询里报警位亮，应报 LoadPort 设备报警");
    port.NoteStatus(null);
    port.Tick();
    Check(Active(port, port.LoadPortDeviceAlarm), "查不到状态不清");
    port.NoteStatus(new LoadPortStatus { IsDeviceAlarm = false });
    port.Tick();
    Check(Active(port, port.LoadPortDeviceAlarm), "报警位灭了也不自动清——报警只能人工 Reset 清");
    port.Reset();
    Check(!Active(port, port.LoadPortDeviceAlarm) && !port.HasAlarm, "人工 Reset 应清掉报警");

    // 报警位还亮着就 Reset：照样清，下个扫描周期又报出来
    port.NoteStatus(new LoadPortStatus { IsDeviceAlarm = true });
    port.Tick();
    port.Reset();
    Check(!Active(port, port.LoadPortDeviceAlarm), "条件还在也照样清");
    port.Tick();
    Check(Active(port, port.LoadPortDeviceAlarm), "条件还在，下个扫描周期应重新报出来");
    port.NoteStatus(new LoadPortStatus { IsDeviceAlarm = false });
    Check(alarms.Reset(port.FullPath) && !Active(port, port.LoadPortDeviceAlarm),
        "界面按来源复位应走到组件的 Reset、清掉报警");
    Check(!alarms.Reset("NoSuchSource"), "没报过报警的来源复位返回 false");

    // LoadPort 动作失败 → 报动作失败（先放上载具，没载具 Load 发不起来）
    port.NotePodPlaced(true);
    port.Tick();
    port.NoteState(ModuleState.Idle);
    var failedLoad = new ProbeOperation();
    Check(port.BeginAction(LoadPortAction.Load, failedLoad) is not null, "Idle 应能发起 Load");
    failedLoad.Reject();
    port.Tick();
    Check(Active(port, port.ControlledStopAlarm), "Load 失败应报动作失败");
    Check(!Active(port, port.InitTimeoutAlarm), "不是 Home 超时，不该报初始化超时");
    Check(alarms.ResetAll() == 1 && alarms.ActiveAlarms.Count == 0, "人工复位清掉动作失败");

    // Home 超时 → 只报初始化超时，不再另报动作失败（一次失败一条报警）
    var slowHome = new ProbeOperation();
    Check(port.BeginAction(LoadPortAction.Home, slowHome) is not null, "Error 状态应允许 Home");
    slowHome.TimeOut();
    port.Tick();
    Check(Active(port, port.InitTimeoutAlarm) && !Active(port, port.ControlledStopAlarm), "Home 超时只报初始化超时，不重复报动作失败");

    // Home 成功回到 Idle 也不清，等人工复位；全部复位清掉
    var goodHome = new ProbeOperation();
    port.BeginAction(LoadPortAction.Home, goodHome);
    goodHome.Succeed();
    port.Tick();
    Check(port.State == ModuleState.Idle && Active(port, port.InitTimeoutAlarm),
        "Home 成功回到 Idle 也不自动清，等人工复位");
    Check(alarms.ResetAll() == 1 && alarms.ActiveAlarms.Count == 0, "全部复位应清掉所有报警");

    // 每个动作超时都报自己那一条（跟 EC 里各动作的超时一一对应），不另报动作失败
    foreach (var (action, from, alarm) in new[]
             {
                 (LoadPortAction.Load, ModuleState.Idle, port.LoadTimeoutAlarm),
                 (LoadPortAction.Unload, LoadPortState.Loaded, port.UnloadTimeoutAlarm),
                 (LoadPortAction.Home, ModuleState.Idle, port.InitTimeoutAlarm),
                 (LoadPortAction.Clamp, ModuleState.Idle, port.ClampTimeoutAlarm),
                 (LoadPortAction.Unclamp, ModuleState.Idle, port.UnclampTimeoutAlarm),
                 (LoadPortAction.Reset, ModuleState.Idle, port.ResetTimeoutAlarm),
                 (LoadPortAction.Abort, ModuleState.Idle, port.AbortTimeoutAlarm),
             })
    {
        port.NoteState(from);
        var slow = new ProbeOperation();
        Check(port.BeginAction(action, slow) is not null, $"{from} 应能发起 {action}");
        slow.TimeOut();
        port.Tick();
        Check(Active(port, alarm) && !Active(port, port.ControlledStopAlarm), $"{action} 超时应报它自己的超时报警，不另报动作失败");
        Check(alarms.ResetAll() == 1 && alarms.ActiveAlarms.Count == 0, $"复位清掉 {action} 超时报警");
    }

    // 操作员急停顶掉的动作不报
    var interrupted = new ProbeOperation();
    var abort = new ProbeOperation();
    port.BeginAction(LoadPortAction.Load, interrupted);
    port.BeginAction(LoadPortAction.Abort, abort);
    abort.Succeed();
    port.Tick();
    Check(!Active(port, port.ControlledStopAlarm), "操作员急停顶掉的动作不该报动作失败");

    // _robot 设备报警：有报错就报；报错没了也不清
    var robot = new ProbeRobot();
    Check(robot.InitComponent(), "探针机械手的组件初始化应成功");
    robot.NoteDeviceError("40010006#Arm2 No Wafer When Put");
    robot.Tick();
    Check(Active(robot, robot.RobotDeviceAlarm), "设备报错应报 _robot 设备报警");
    robot.NoteDeviceError(null);
    robot.Tick();
    Check(Active(robot, robot.RobotDeviceAlarm), "报错没了也不自动清——报警只能人工 Reset 清");

    // _robot 的 Reset 重写了组件基类的 Reset：先清报警，再发设备复位清错，把操作交出去等
    robot.Next = new ProbeOperation();
    var robotReset = robot.Reset();
    Check(robotReset is not null && ReferenceEquals(robotReset, robot.Next) && !Active(robot, robot.RobotDeviceAlarm),
        "_robot Reset 应清掉报警并交出设备复位操作");
    robot.Next!.Succeed();
    robot.Tick();
    Check(robot.State == ModuleState.NotInit, "复位后是 NotInit（位置不可信，还得回原点）");

    robot.NoteDeviceError("stale");
    robot.Close();
    robot.Tick();
    Check(!Active(robot, robot.RobotDeviceAlarm), "没连上时 DeviceError 是旧值，不该据此报警");

    // _robot 动作失败 → 受控停止；Home 成功回到 Idle 也不清，只有人工复位清
    Check(robot.InitComponent(), "重新做一遍组件初始化");
    robot.NoteDeviceError(null);
    robot.NoteState(ModuleState.Idle);
    robot.Next = new ProbeOperation();
    robot.Home();
    robot.Next!.Reject();
    robot.Tick();
    Check(Active(robot, robot.ControlledStopAlarm), "_robot 动作失败应报受控停止");

    robot.Next = new ProbeOperation();
    robot.Home();
    robot.Next!.Succeed();
    robot.Tick();
    Check(robot.State == ModuleState.Idle && Active(robot, robot.ControlledStopAlarm),
        "Home 成功回到 Idle 也不自动清");

    // 界面按来源复位（这次不摆设备复位操作，只看报警）
    robot.Next = null;
    Check(alarms.Reset(robot.FullPath) && !Active(robot, robot.ControlledStopAlarm), "人工复位才清");

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
    Check(lp.InitComponent(), "带 E84 的端口的组件初始化应成功");
    lp.NoteState(ModuleState.Idle);

    lp.Tick();
    Check(e84.State == E84State.NotAvailable && e84.Outputs == default, "Manual + 下线时 E84 不可交接，输出全灭");
    lp.SetAutoMode(true);
    lp.Tick();
    Check(e84.State == E84State.NotAvailable && !e84.Outputs.HoAvbl, "只切 Auto、没上线（Out Of Service）也不交接");
    lp.Online();
    lp.Tick();
    var outputs = e84.Outputs;
    Check(e84.State == E84State.Available && outputs.HoAvbl && outputs.Es && !outputs.LReq && !outputs.UReq,
        "Auto + 上线 + 空闲 + 没载具：亮 HO_AVBL 等搬运车");
    Check(events.Wait("AvailabilityChanged:True"), "HO_AVBL 亮了应上报");

    // 送盒：CS_0+VALID → L_REQ；TR_REQ → READY（交接开始）；BUSY 时载具放上 → 撤 L_REQ；BUSY 撤；COMPT → 撤 READY；信号全撤 → 完成
    e84.Set(cs0: true, valid: true);
    lp.Tick();
    outputs = e84.Outputs;
    Check(e84.State == E84State.Requesting && outputs.LReq && !outputs.UReq && !outputs.Ready,
        "空端口被选中应亮 L_REQ");
    e84.Set(cs0: true, valid: true, trReq: true);
    lp.Tick();
    Check(e84.State == E84State.WaitBusy && e84.Outputs.Ready, "TR_REQ 来了应给 READY，等 BUSY");
    Check(events.Wait("HandoffStarted:True"), "送盒交接开始应上报");
    e84.Set(cs0: true, valid: true, trReq: true, busy: true);
    lp.Tick();
    outputs = e84.Outputs;
    Check(e84.State == E84State.Transferring && outputs.LReq && outputs.Ready,
        "BUSY 来了进搬运；载具还没放上，L_REQ 保持");
    lp.NotePodPlaced(true);
    lp.Tick();
    outputs = e84.Outputs;
    Check(e84.State == E84State.WaitComplete && !outputs.LReq && outputs.Ready,
        "载具放上应撤 L_REQ，等 BUSY 撤、COMPT 到");
    e84.Set(cs0: true, valid: true, trReq: true);
    lp.Tick();
    e84.Set(cs0: true, valid: true, trReq: true, compt: true);
    lp.Tick();
    Check(!e84.Outputs.Ready && e84.State == E84State.Releasing, "COMPT 来了应撤 READY，等信号全撤");
    e84.Set();
    lp.Tick();
    Check(events.Wait("HandoffCompleted:True"), "信号全撤应算送盒完成并上报");
    outputs = e84.Outputs;
    Check(e84.State == E84State.Available && outputs.HoAvbl && !outputs.LReq && !outputs.UReq,
        "送盒完成后回到可交接");

    // 盒子刚到、还没干完：搬运车再来也不给取
    e84.Set(cs0: true, valid: true);
    lp.Tick();
    Check(e84.State == E84State.Available && !e84.Outputs.UReq, "这一盒还没干完，不该亮 U_REQ");

    // 取盒：干完了才亮 U_REQ；载具被取走 → 撤 U_REQ；COMPT → 撤 READY；信号全撤 → 完成
    lp._carrier.NoteComplete();
    lp.Tick();
    outputs = e84.Outputs;
    Check(e84.State == E84State.Requesting && outputs.UReq && !outputs.LReq, "这一盒干完了应亮 U_REQ");
    e84.Set(cs0: true, valid: true, trReq: true);
    lp.Tick();
    outputs = e84.Outputs;
    Check(e84.State == E84State.WaitBusy && outputs.UReq && outputs.Ready, "取盒 TR_REQ 来了应给 READY");
    Check(events.Wait("HandoffStarted:False"), "取盒交接开始应上报");
    e84.Set(cs0: true, valid: true, trReq: true, busy: true);
    lp.Tick();
    Check(e84.Outputs.UReq, "载具还在，U_REQ 保持");
    lp.NotePodPlaced(false);
    lp.Tick();
    outputs = e84.Outputs;
    Check(!outputs.UReq && outputs.Ready, "载具取走应撤 U_REQ");
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
    Check(e84.State == E84State.WaitBusy, "又开始一次送盒");
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
    Check(offPort.InitComponent(), "E84 没开的端口组件初始化也应成功");
    offPort.NoteState(ModuleState.Idle);
    offPort.Online();
    offPort.SetAutoMode(true);
    offE84.Set(cs0: true, valid: true);
    offPort.Tick();
    Check(offE84.State == E84State.NotAvailable && offE84.Outputs == default, "E84 没开（EC）不该理搬运车");

    // 没装（SC IsEnable=False，本机没接搬运车）：端口当没有 E84——不打开、不每拍推、不读写 IO
    var noE84Port = new ProbePort("E84NotInstalledPort");
    var noE84 = new ProbeE84(noE84Port.Name) { IsEnable = false };
    noE84Port.AddChild(noE84);
    Check(noE84Port.E84 is null, "E84 没装时端口按没有 E84 处理（E84 为 null）");
    Check(noE84Port.InitComponent() && noE84.Writes == 0, "E84 没装时端口照常初始化，E84 自己拦住不做（一次 IO 都不写）");
    noE84Port.NoteState(ModuleState.Idle);
    noE84Port.Online();
    noE84Port.SetAutoMode(true);
    noE84.Set(cs0: true, valid: true);
    noE84Port.Tick();
    noE84Port.Tick();
    Check(noE84.Writes == 0 && noE84.State == E84State.NotAvailable && noE84.Inputs == default,
        "E84 没装时每拍也不推它：不读输入、不写输出");

    // 组件初始化直接对 E84：装了就把输出写一遍回初始；没装的自己拦住，一次 IO 都不写（端口的基类递归带着它调，端口拦不住）
    var directE84 = new ProbeE84("DirectE84Port");
    Check(directE84.InitComponent() && directE84.Writes == 1, "E84 的组件初始化：装了就写一遍输出回初始");
    var directOffE84 = new ProbeE84("DirectOffE84Port") { IsEnable = false };
    Check(directOffE84.InitComponent() && directOffE84.Writes == 0, "E84 没装：组件初始化什么都不写");

    // 超时：计时调短；TP1 没等到 TR_REQ → 锁住、撤输出、报警；Retry 解锁
    var tpPort = new ProbePort("E84TimeoutPort");
    var tpE84 = new ProbeE84(tpPort.Name, timeoutMs: 100);
    tpPort.AddChild(tpE84);
    var tpEvents = new RecordingE84Callback();
    tpPort.E84Callback = tpEvents;
    Check(tpPort.InitComponent(), "超时用的端口的组件初始化应成功");
    tpPort.NoteState(ModuleState.Idle);
    tpPort.Online();
    tpPort.SetAutoMode(true);
    tpE84.Set(cs0: true, valid: true);
    tpPort.Tick();
    Check(tpE84.State == E84State.Requesting, "亮了 L_REQ 等 TR_REQ");
    Thread.Sleep(150);
    tpPort.Tick();
    var tpOutputs = tpE84.Outputs;
    Check(tpE84.State == E84State.TimedOut && tpE84.TimedOutTimer == E84Timer.TP1
          && !tpOutputs.LReq && !tpOutputs.HoAvbl && tpOutputs.Es,
        "TP1 超时应锁住并撤 L_REQ/HO_AVBL（ES 保持）");
    Check(tpEvents.Wait("HandoffTimeout:True:TP1"), "TP1 超时应上报");
    Check(Active(tpE84, tpE84.E84TimeoutAlarm, alarms), "超时应报 E84 交接超时");
    Check(tpPort.HasAlarm, "E84 子组件的报警算在端口头上（按路径前缀）");
    tpE84.Set();
    tpPort.Tick();
    Check(tpE84.State == E84State.TimedOut, "搬运车撤了也不自动解锁，得人工恢复");
    Check(!tpE84.Complete(tpPort._carrier.IsArrived), "送盒没放上载具，Complete 应被拒");
    tpE84.Retry();
    tpPort.Tick();
    Check(tpE84.State == E84State.Available && tpE84.Outputs.HoAvbl && tpE84.TimedOutTimer is null,
        "Retry 后应重新可交接");
    Check(Active(tpE84, tpE84.E84TimeoutAlarm, alarms), "Retry 只恢复交接，不清报警——报警只能人工复位");
    Check(alarms.Reset(tpE84.FullPath) && !Active(tpE84, tpE84.E84TimeoutAlarm, alarms) && !tpPort.HasAlarm,
        "人工复位 E84 应清掉超时报警");

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
    tpPort.Tick();  // 在位下一拍扫描才判出来；锁住的交接这一拍不动
    Check(tpE84.State == E84State.TimedOut, "人工恢复之前交接一直锁着");
    Check(tpE84.Complete(tpPort._carrier.IsArrived), "载具确实放上了，Complete 应按完成收尾");
    tpPort.Tick();
    Check(tpEvents.Wait("HandoffCompleted:True"), "人工 Complete 应在下一拍随进展上报交接完成");
    Check(tpE84.State == E84State.Available && Active(tpE84, tpE84.E84TimeoutAlarm, alarms),
        "Complete 后回到可交接；报警还在，等人工复位");
    tpPort.Reset();
    Check(!Active(tpE84, tpE84.E84TimeoutAlarm, alarms), "复位端口连同 E84 子组件一起复位，清掉超时报警");

    AlarmComponent.Current = null;

    static bool Active(ComponentBase source, string code, AlarmComponent alarms) =>
        alarms.ActiveAlarms.Any(alarm => alarm.SourcePath == source.FullPath && alarm.AlarmCode == code);
}

// ── DI/AI 报警防抖：持续满 EC 防抖时间才报；公共组件的报警算在装它的模块头上；只能人工复位清 ─────
{
    var alarms = new AlarmComponent();
    bool Active(ComponentBase source, string code) =>
        alarms.ActiveAlarms.Any(alarm => alarm.SourcePath == source.FullPath && alarm.AlarmCode == code);

    var chamber = new ProbeModule();
    typeof(ComponentBase).GetProperty(nameof(ComponentBase.Name))!.SetValue(chamber, "SmokeChamber");
    typeof(ComponentBase).GetProperty(nameof(ComponentBase.FullPath))!.SetValue(chamber, "SmokeChamber");
    var di = new ProbeDi("SmokeChamber.Di1") { DebounceMs = 200 };
    var ai = new ProbeAi("SmokeChamber.Ai1");
    chamber.AddChild(di);
    chamber.AddChild(ai);

    // DI：IO 没接（读不到）不判
    di.Tick();
    Check(!di.IsTriggered && alarms.ActiveAlarms.Count == 0, "读不到 DI 时不判");

    // 到了报警电平但没持续满防抖时间就断了：计时重来，不报
    di.Level = true;
    di.Tick();
    di.Level = false;
    di.Tick();
    di.Level = true;
    di.Tick();
    Thread.Sleep(80);
    di.Tick();
    Check(!di.IsTriggered && !Active(di, di.SensorAlarm), "报警电平没持续满防抖时间不报（中间断过，计时重来）");

    // 持续满防抖时间：触发并报警，报警算在装它的模块头上
    Thread.Sleep(200);
    di.Tick();
    Check(di.IsTriggered && Active(di, di.SensorAlarm), "报警电平持续满防抖时间应触发并报警");
    Check(chamber.HasAlarm && di.HasAlarm && !ai.HasAlarm, "DI 的报警应算在装它的模块头上");

    // 离开报警电平：触发立刻撤，报警不清
    di.Level = false;
    di.Tick();
    Check(!di.IsTriggered && Active(di, di.SensorAlarm), "离开报警电平触发撤了，报警还在，等人工复位");

    // 复位模块连同子组件：清掉 DI 的报警
    chamber.Reset();
    Check(!Active(di, di.SensorAlarm) && !chamber.HasAlarm, "复位模块应连同 DI 一起清掉报警");

    // 低电平报警 + 关掉直接报警：只给触发状态，不报
    var quiet = new ProbeDi("SmokeChamber.Di2") { TriggerLevel = false, AlarmEnabled = false, DebounceMs = 0 };
    quiet.Level = false;
    quiet.Tick();
    Check(quiet.IsTriggered && !Active(quiet, quiet.SensorAlarm), "AlarmEnabled=False 只给触发状态，不报警");
    quiet.Level = true;
    quiet.Tick();
    Check(!quiet.IsTriggered, "TriggerLevel=false 在 DI=true 时不触发");

    foreach (var (text, expected) in new[] { ("true", true), ("false", false), ("True", true), ("False", false) })
    {
        var loaded = (DiSensorComponent)ComponentLoader.Load([new ModuleConfig
        {
            Name = "TriggerLevelSmoke", Type = typeof(DiSensorComponent).FullName,
            Values = [new() { Name = "TriggerLevel", Value = text }]
        }]).Single();
        Check(loaded.TriggerLevel == expected, $"DI 触发电平配置 {text} 应加载为 {expected}");
    }
    var defaultDi = (DiSensorComponent)ComponentLoader.Load([new ModuleConfig
    {
        Name = "DefaultTriggerSmoke", Type = typeof(DiSensorComponent).FullName
    }]).Single();
    Check(defaultDi.TriggerLevel, "未配置触发电平时仍默认高电平触发");
    // 只认 bool：旧的 High/Low 写法不再兼容，跟乱填的一样拒绝加载
    foreach (var invalid in new[] { "invalid", "High", "Low" })
    {
        var invalidTriggerRejected = false;
        try
        {
            ComponentLoader.Load([new ModuleConfig
            {
                Name = "InvalidTriggerSmoke", Type = typeof(DiSensorComponent).FullName,
                Values = [new() { Name = "TriggerLevel", Value = invalid }]
            }]);
        }
        catch (InvalidOperationException)
        {
            invalidTriggerRejected = true;
        }

        Check(invalidTriggerRejected, $"DI 触发电平 {invalid} 不是 bool，应拒绝加载");
    }

    // AI：没标定（上下限都为 0）不判
    ai.Reading = 1000;
    ai.Tick();
    Check(!ai.IsOutOfRange && !ai.IsInWarning && !ai.HasAlarm, "没标定的 AI 不判超限");

    ai.Min = 10;
    ai.Max = 90;
    ai.WarningMin = 20;
    ai.WarningMax = 80;
    ai.DurationMs = 0;
    Check(ai.Min == 10 && ai.WarningMax == 80, "AI 上下限是 EC，写完即生效");

    ai.Reading = 50;
    ai.Tick();
    Check(ai.Value == 50 && !ai.IsOutOfRange && !ai.IsInWarning, "在预警带内不报");

    ai.Reading = 85;
    ai.Tick();
    Check(ai.IsInWarning && !ai.IsOutOfRange && Active(ai, ai.AiSensorWarnAlarm) && !Active(ai, ai.AiSensorAlarm),
        "出了预警带、没超限：报预警");

    ai.Reading = 95;
    ai.Tick();
    Check(ai.IsOutOfRange && !ai.IsInWarning && Active(ai, ai.AiSensorAlarm), "超限：报超限，不再算预警");

    ai.Reading = 50;
    ai.Tick();
    Check(!ai.IsOutOfRange && Active(ai, ai.AiSensorAlarm) && Active(ai, ai.AiSensorWarnAlarm),
        "回到范围内状态撤了，报警还在，等人工复位");
    Check(alarms.Reset(ai.FullPath) && !ai.HasAlarm, "人工复位清掉 AI 的报警");

    // 超限要持续满 DurationMs 才报
    ai.DurationMs = 200;
    ai.Reading = 95;
    ai.Tick();
    Check(!ai.IsOutOfRange && !Active(ai, ai.AiSensorAlarm), "超限没持续满 DurationMs 不报");
    Thread.Sleep(250);
    ai.Tick();
    Check(ai.IsOutOfRange && Active(ai, ai.AiSensorAlarm), "超限持续满 DurationMs 应报");
    ai.Reset();

    // 监控使能 DO 门控：没开（或读不到）就不判
    var gated = new ProbeAi("SmokeChamber.Ai2") { MonitoringDoIndex = 3, MonitoringActiveHigh = true };
    gated.Min = 10;
    gated.Max = 90;
    gated.DurationMs = 0;
    gated.Reading = 95;
    gated.MonitoringDo = false;
    gated.Tick();
    Check(!gated.IsOutOfRange && !Active(gated, gated.AiSensorAlarm), "监控使能 DO 没开不判");
    gated.MonitoringDo = null;
    gated.Tick();
    Check(!gated.IsOutOfRange, "读不到监控使能 DO 当作没开");
    gated.MonitoringDo = true;
    gated.Tick();
    Check(gated.IsOutOfRange && Active(gated, gated.AiSensorAlarm), "监控使能 DO 开了才判超限");

    // 读不到 AI：不判，状态保持
    gated.Reading = null;
    gated.Tick();
    Check(gated.IsOutOfRange && gated.Value is null, "读不到 AI 时不判，状态保持");

    AlarmComponent.Current = null;
}

// ── EC 组件：属性读写即生效、缺项回退默认值；合并声明补默认、不动已有值；没装时写不生效；ec.xml 往返 ─────
{
    var ecPort = new ProbePort("EcSmokePort");
    Check(ecPort.LoadTimeout == 0 && ec.Get(ecPort.FullPath, nameof(ecPort.LoadTimeout)) == "0",
        "属性写应落进 EC 组件，读回即是新值");
    Check(ecPort.ClampTimeout == 10000 && ec.Get(ecPort.FullPath, nameof(ecPort.ClampTimeout)).Length == 0,
        "没摆过的项读回退 [VariableMark] 默认值");

    Check(ec.Merge([ecPort]), "合并声明应补建缺的项");
    Check(ec.Get(ecPort.FullPath, nameof(ecPort.ClampTimeout)) == "10000", "合并按声明的 Default 补建");
    Check(ecPort.LoadTimeout == 0, "合并不动已有值");
    Check(!ec.Merge([ecPort]), "再合并一次应没有变化");
    Check(ec.FilePath.Length == 0, "直接 new 的 EC 组件只在内存里，不落盘");

    ecPort.LoadTimeout = 1234;
    Check(ecPort.LoadTimeout == 1234, "改完即生效");
    ecPort.LoadTimeout = 0;

    EcComponent.Current = null;
    Check(ecPort.LoadTimeout == 30000, "EC 没装时读回退默认值");
    ecPort.LoadTimeout = 1;
    Check(ecPort.LoadTimeout == 30000, "EC 没装时写不生效");
    EcComponent.Current = ec;

    // ec.xml 往返：用临时文件，不碰真配置
    var file = Path.Combine(Path.GetTempPath(), $"ec-smoke-{Guid.NewGuid():N}.xml");
    try
    {
        var stored = new EcComponent();
        stored.Load(file);
        Check(stored.Merge([ecPort]) && File.Exists(file), "文件不存在时合并应补建并写出 ec.xml");
        Check(stored.Set(ecPort.FullPath, nameof(ecPort.HomeTimeout), "4321"), "改值应写回");

        var reloaded = new EcComponent();
        reloaded.Load(file);
        Check(reloaded.Get(ecPort.FullPath, nameof(ecPort.HomeTimeout)) == "4321"
              && reloaded.Get(ecPort.FullPath, nameof(ecPort.ClampTimeout)) == "10000",
            "重新读 ec.xml 应拿到改过的值和合并补建的项");
    }
    finally
    {
        File.Delete(file);
        EcComponent.Current = ec;
    }
}

// ── 组件初始化、模块初始化、中止：组件初始化（不动硬件）先处理子组件（按 InitOrder），基类默认什么都不做、不强制重写，
//    一个子组件没做成不耽误别的；模块初始化 InitModule = Home，不碰子组件；Abort = 子组件 + 设备中止，Abort 不清报警 ───────────
{
    var trace = new List<string>();
    var parent = new ProbeChild("Parent", trace);
    parent.AddChild(new ProbeChild("Late", trace, initOrder: 20));
    parent.AddChild(new ProbeChild("Early", trace, initOrder: 10));
    parent.AddChild(new PlainChild());
    Check(parent.InitComponent() && trace.SequenceEqual(new[] { "InitComponent:Early", "InitComponent:Late", "InitComponent:Parent" }),
        "组件初始化先按 InitOrder 从小到大初始化子组件，再初始化自己，都成功返回 true");

    // 子组件下面的子组件也跟着走：父组件不用点名
    trace.Clear();
    var tree = new ProbeChild("Root", trace);
    var branch = new ProbeChild("Branch", trace);
    branch.AddChild(new ProbeChild("Leaf", trace));
    tree.AddChild(branch);
    Check(tree.InitComponent() && trace.SequenceEqual(new[] { "InitComponent:Leaf", "InitComponent:Branch", "InitComponent:Root" }),
        "组件初始化一路递归下去：孙子先于儿子、儿子先于自己");

    // 一个子组件没做成：后面的子组件和自己照样初始化，汇总返回 false
    trace.Clear();
    var partlyBroken = new ProbeChild("Partly", trace);
    partlyBroken.AddChild(new ProbeChild("Broken", trace, initOrder: 10) { FailInit = true });
    partlyBroken.AddChild(new ProbeChild("Healthy", trace, initOrder: 20));
    Check(!partlyBroken.InitComponent()
          && trace.SequenceEqual(new[] { "InitComponent:Broken", "InitComponent:Healthy", "InitComponent:Partly" }),
        "一个子组件没做成不耽误别的：后面的子组件和自己照样初始化，汇总返回 false");

    trace.Clear();
    Check(parent.Abort() is null && trace.SequenceEqual(new[] { "Abort:Late", "Abort:Early", "Abort:Parent" }),
        "Abort 先按装配顺序中止子组件，再中止自己");
    var plain = new PlainChild();
    Check(plain.InitComponent() && plain.Abort() is null, "没重写 InitComponent/Abort 的组件照样能调，什么都不做");

    var alarms = new AlarmComponent();
    var initPort = new ProbePort("InitAbortPort");
    var child = new ProbeChild("Child", trace, fullPath: "InitAbortPort.Child");
    initPort.AddChild(child);
    trace.Clear();
    int calls = initPort.Calls;
    Check(initPort.InitComponent() && trace.SequenceEqual(new[] { "InitComponent:Child" }) && initPort.Calls == calls,
        "LoadPort 的组件初始化：驱动和子组件跟着基类递归走，不发 Home（不动硬件）");
    Check(initPort.Shell.IsConnected, "LoadPort 的组件初始化把品牌驱动连上了（连接写在驱动自己的 InitComponent 里，端口没点名）");
    trace.Clear();
    initPort.Next = new ProbeOperation();
    Check(ReferenceEquals(initPort.InitModule(), initPort.Next) && initPort.Calls == calls + 1 && trace.Count == 0,
        "LoadPort 的模块初始化 InitModule = Home，不碰子组件");

    var initRobot = new ProbeRobot();
    Check(initRobot.InitComponent(), "机械手的组件初始化把品牌驱动连上");
    initRobot.Next = new ProbeOperation();
    Check(ReferenceEquals(initRobot.InitModule(), initRobot.Next), "机械手的模块初始化 InitModule = Home");

    var bareModule = new ProbeAligner("InitAligner");
    Check(bareModule.InitModule() is null && bareModule.InitComponent(), "没有自己初始化动作的模块：InitModule 什么都不做，返回 null");

    child.Fault();
    Check(child.HasAlarm && initPort.HasAlarm, "子组件报警算在模块头上");
    trace.Clear();
    initPort.Next = new ProbeOperation();
    Check(ReferenceEquals(initPort.Abort(), initPort.Next) && trace.SequenceEqual(new[] { "Abort:Child" }),
        "LoadPort 的 Abort = 子组件中止 + 设备中止");
    Check(child.HasAlarm, "Abort 不清报警");
    initPort.Next = new ProbeOperation();
    initPort.Reset();
    Check(!child.HasAlarm && !initPort.HasAlarm, "报警只有 Reset 清");
    AlarmComponent.Current = null;
}

// ── 一趟搬运（TransferRoutine）出错时报给界面的错误码和参数：参数只放站点名、步号、毫秒数这类数据，
//    句子由界面按语言包写（不传中文阶段名，英文界面里才不会夹中文）─────────────────────────
{
    var transferRobot = new ProbeTransferRobot();

    TransferRoutine RunTransfer(ProbeStation source, ProbeStation target, int waitMilliseconds)
    {
        var routine = new TransferRoutine(TransferOrigin.Manual, transferRobot, source, 1, target, 1, 1, waitMilliseconds);
        for (int scan = 0; scan < 10 && !routine.IsTerminal; scan++)
        {
            routine.Scan();
        }

        return routine;
    }

    ModuleOperation Succeeded()
    {
        var operation = new ProbeOperation();
        operation.Succeed();
        return operation;
    }

    ModuleOperation Rejected()
    {
        var operation = new ProbeOperation();
        operation.Reject();
        return operation;
    }

    // 先抢目标：目标一直抢不到（第 1 步准备总被拒），等待上限 0 ms——报目标站点，片没动过，源站点碰都没碰
    var untouched = new ProbeStation("Src0") { First = Succeeded };
    var targetBusy = RunTransfer(untouched, new ProbeStation("Busy"), 0);
    Check(targetBusy.Code == ErrorCodes.StationBusy && targetBusy.ErrorArgs.SequenceEqual(new[] { "Busy", "0" })
          && !targetBusy.MotionStarted && untouched.Prepares == 0,
        "目标等不到：transfer.station_busy，参数是目标站点名和等待毫秒数，源站点不碰（放不下就不取）");

    // 源一直抢不到：目标已经抢到了，判负时把目标的环还回去（没动过手）
    var heldTarget = new ProbeStation("Target1") { First = Succeeded };
    var busy = RunTransfer(new ProbeStation("Busy"), heldTarget, 0);
    Check(busy.Code == ErrorCodes.StationBusy && busy.ErrorArgs.SequenceEqual(new[] { "Busy", "0" }) && heldTarget.Cancels == 1,
        "源等不到：transfer.station_busy 报源站点，已经抢到的目标环要还回去");

    var firstFailed = RunTransfer(new ProbeStation("Src1") { First = Rejected }, new ProbeStation("T1") { First = Succeeded }, 1000);
    Check(firstFailed.Code == ErrorCodes.StationPrepareFailed && firstFailed.ErrorArgs.SequenceEqual(new[] { "Src1", "1" }),
        "搬运第 1 步准备没做成：transfer.station_prepare_failed，参数是站点名和步号 1");

    var secondSource = new ProbeStation("Src2") { First = Succeeded };
    var secondTarget = new ProbeStation("T2") { First = Succeeded };
    var secondRejected = RunTransfer(secondSource, secondTarget, 1000);
    Check(secondRejected.Code == ErrorCodes.StationPrepareRejected && secondRejected.ErrorArgs.SequenceEqual(new[] { "Src2", "2" })
          && secondSource.Cancels == 1 && secondTarget.Cancels == 1 && !secondRejected.MotionStarted,
        "搬运第 2 步准备被拒：transfer.station_prepare_rejected，参数是站点名和步号 2；没动过手，两个环都还回去");

    var secondFailed = RunTransfer(new ProbeStation("Src3") { First = Succeeded, Second = Rejected }, new ProbeStation("T3") { First = Succeeded }, 1000);
    Check(secondFailed.Code == ErrorCodes.StationPrepareFailed && secondFailed.ErrorArgs.SequenceEqual(new[] { "Src3", "2" }),
        "搬运第 2 步准备没做成：参数是站点名和步号 2");

    // 落了"交互中"标记、机械手却发不出取片：算动过手，环不还（留着等人工确认）
    var pickSource = new ProbeStation("Src4") { First = Succeeded, Second = Succeeded };
    var pickTarget = new ProbeStation("T4") { First = Succeeded };
    var pickRejected = RunTransfer(pickSource, pickTarget, 1000);
    Check(pickRejected.Code == ErrorCodes.TransferRejected && pickRejected.ErrorArgs.SequenceEqual(new[] { "TransferRobot", "Pick" })
          && pickRejected.MotionStarted && pickSource.Cancels == 0 && pickTarget.Cancels == 0,
        "取片发不出去：transfer.rejected，参数是机械手和动作；落过交互中标记就算动过手，环不还");
}

// ── 主界面要的后端：系统设置带上 LoadPort / 机械手名单（右栏页签、默认调度图照它生成），机械手站点带上类型（调度图按它选卡片），
//    设备总状态带上模式，整机操作 Auto / Manual / Stop ─────────────────────────────────────────
{
    var previousTransfers = TransferManager.Current;
    var mainPort = new ProbePort("MainLP");
    var mainChamber = new ProbeChamber("MainPM");
    var mainAligner = new ProbeAligner("MainAligner");
    var mainRobot = new ProbeRobot();
    ModuleConfig StationNode(string name, string number, string direction) => new()
    {
        Name = name,
        Values =
        [
            new ValueConfig { Name = "Number", Value = number },
            new ValueConfig { Name = "Direction", Value = direction },
        ],
    };
    mainRobot.NoteSettings(new ModuleConfig
    {
        Name = "SmokeRobot",
        Children =
        [
            new ModuleConfig
            {
                Name = "Stations",
                Children =
                [
                    StationNode("MainLP", "1", "South"),
                    StationNode("MainPM", "2", "North"),
                    StationNode("MainAligner", "3", "East"),
                    StationNode("Nowhere", "4", "West"),
                ],
            },
        ],
    });
    BaseModule[] mainModules = [mainPort, mainChamber, mainAligner, mainRobot];

    // 搬运模块表没绑之前认不出是哪一类（跟槽数一样），一律 Other；绑了之后 LoadPort / 腔体 / 其他分得开，不在表里的还是 Other
    TransferManager.Current = null;
    Check(mainRobot.CreateStateDto().StationInfos.All(info => info.Kind == StationKind.Other),
        "搬运模块表还没绑时站点类型应一律是 Other");
    var mainTransfers = new TransferManager();
    mainTransfers.Bind(mainModules);
    var unbound = new RobotDto { Name = "SmokeRobot", StationInfos = [new RobotStationDto { Name = "MainLP", Number = 1 }] };
    var infos = mainRobot.CreateStateDto().StationInfos;
    StationKind KindOf(string name) => infos.First(info => info.Name == name).Kind;
    Check(KindOf("MainLP") == StationKind.LoadPort && KindOf("MainPM") == StationKind.Chamber
          && KindOf("MainAligner") == StationKind.Other && KindOf("Nowhere") == StationKind.Other,
        "站点类型应按搬运模块表认：LoadPort、腔体、其他，不在表里的算 Other");
    var bound = new RobotDto
    {
        Name = "SmokeRobot",
        StationInfos = [new RobotStationDto { Name = "MainLP", Number = 1, Kind = StationKind.LoadPort }],
    };
    Check(bound.HasStateChanged(unbound), "只有站点类型变了也要算状态变化（搬运模块表绑好后推给界面换卡片）");

    // 系统设置：LoadPort、机械手、腔体名单照装配的先后
    var system = new SystemService(mainModules);
    var settings = system.GetSettingsAsync(new RpcRequest()).Result.DeserializeData<SystemSettingsDto>();
    Check(settings.LoadPorts.SequenceEqual(["MainLP"]) && settings.Robots.SequenceEqual(["SmokeRobot"])
          && settings.Chambers.SequenceEqual(["MainPM"]),
        "系统设置应带上 LoadPort、机械手、腔体名单");

    // 模式：开机是 Manual；Auto / Manual 就是开、关自动派单，设备总状态跟着变
    var equipment = new EquipmentService(mainModules);
    Check(!EquipmentStatusPublisher.Snapshot(mainModules).IsAuto, "开机应是 Manual（自动派单关着）");
    Check(equipment.AutoAsync(new RpcRequest()).Result.Success && mainTransfers.IsAutoDispatch
          && EquipmentStatusPublisher.Snapshot(mainModules).IsAuto,
        "切 Auto 应开自动派单，设备总状态是 Auto");
    Check(equipment.ManualAsync(new RpcRequest()).Result.Success && !mainTransfers.IsAutoDispatch
          && !EquipmentStatusPublisher.Snapshot(mainModules).IsAuto,
        "切 Manual 应关自动派单");

    // 搬运管理停用（sc.xml IsEnable=False）：切不了 Auto
    mainTransfers.IsEnable = false;
    var disabled = equipment.AutoAsync(new RpcRequest()).Result;
    Check(!disabled.Success && disabled.Code == ErrorCodes.TransferDisabled && disabled.Args.Count == 0 && !mainTransfers.IsAutoDispatch,
        "搬运管理停用时切 Auto 应回 transfer.disabled");
    mainTransfers.IsEnable = true;

    // Stop：关自动派单，只给正在执行动作的模块发中止（闲着的不碰）；Data 是发了中止的个数
    Check(equipment.AutoAsync(new RpcRequest()).Result.Success, "再切回 Auto");
    mainPort.InitComponent();
    mainPort.NotePodPlaced(true);
    mainPort.Tick();
    mainPort.NoteState(ModuleState.Idle);
    var running = new ProbeOperation();
    Check(mainPort.BeginAction(LoadPortAction.Load, running) is not null && mainPort.CurrentOperation is not null,
        "摆一个正在做 Load 的 LoadPort");
    int callsBefore = mainPort.Calls;
    var stop = equipment.StopAsync(new RpcRequest()).Result;
    Check(stop.Success && stop.DeserializeData<int>() == 1 && mainPort.Calls == callsBefore + 1 && !mainTransfers.IsAutoDispatch,
        "Stop 应关自动派单，只给在做动作的那一个模块发中止");
    running.Succeed();
    mainPort.Tick();
    var idleStop = equipment.StopAsync(new RpcRequest()).Result;
    Check(idleStop.Success && idleStop.DeserializeData<int>() == 0 && mainPort.Calls == callsBefore + 1,
        "都闲着时 Stop 不发设备中止");

    // 没装搬运管理：切 Auto 回 transfer.not_installed，Manual 照样成功，模式一直是 Manual
    TransferManager.Current = null;
    var notInstalled = equipment.AutoAsync(new RpcRequest()).Result;
    Check(!notInstalled.Success && notInstalled.Code == ErrorCodes.TransferNotInstalled && notInstalled.Args.Count == 0,
        "没装搬运管理时切 Auto 应回 transfer.not_installed");
    Check(equipment.ManualAsync(new RpcRequest()).Result.Success && !EquipmentStatusPublisher.Snapshot(mainModules).IsAuto,
        "没装搬运管理时 Manual 照样成功，模式是 Manual");
    Check(equipment.StopAsync(new RpcRequest()).Result.Success, "没装搬运管理时 Stop 照样能用");

    TransferManager.Current = previousTransfers;
}

// ── LoadPort 在位来源（SC PresenceSource 二选一）和驱动恢复：状态查询超时作废重发、动作没做成作废在途指令、关连接作废、
//    断线按间隔重连、_rfid 连不上不连累 LoadPort、帧通讯重连时旧接收泵只停自己那一轮 ─────────────────────────
{
    var previousLedger = WaferManagerComponent.Current;
    var presenceLedger = new WaferManagerComponent();

    bool WaitUntil(Func<bool> condition, int timeoutMs = 3000)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < timeoutMs)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(10);
        }

        return false;
    }

    // 一拍一拍推扫描，直到条件成立（驱动的收发在后台任务上，要等）
    bool TickUntil(ProbePort target, Func<bool> condition, int timeoutMs = 3000)
    {
        return WaitUntil(() =>
        {
            target.Tick();
            return condition();
        }, timeoutMs);
    }

    // FCD 状态查询的回复：64 位状态串，第 1 位在位、第 2 位到位
    string StateReply(bool present, bool placed)
    {
        var bits = new string('0', 64).ToCharArray();
        bits[0] = present ? '1' : '0';
        bits[1] = placed ? '1' : '0';
        return "ACK:STATE/" + new string(bits);
    }

    // 1) Query：两位都亮算放好（开机时盒子已在端口上，第一次查到就认），一亮一灭、查不到都不算变化，两位都灭才算拿走；PODON/PODOF 不认
    var queryPort = new ProbePort("PresenceQueryPort", PodPresenceSource.Query);
    queryPort.InitComponent();
    queryPort.NoteState(ModuleState.Idle);
    queryPort.Tick();
    Check(!queryPort._carrier.IsArrived && queryPort._carrier.Info is null, "Query：还没查到状态时当没有载具");
    queryPort.NoteStatus(new LoadPortStatus { IsPresent = true, IsPlaced = true });
    queryPort.Tick();
    Check(queryPort._carrier.IsArrived && queryPort._carrier.Info is not null && queryPort.CreateStateDto().IsCarrierArrived,
        "Query：在位、到位都亮算放好（开机时已在端口上的也认），建载具对象，推给界面的在位跟着变");
    queryPort.NoteStatus(new LoadPortStatus { IsPresent = true, IsPlaced = false });
    queryPort.Tick();
    Check(queryPort._carrier.IsArrived && queryPort._carrier.Info is not null, "Query：一亮一灭不算拿走");
    queryPort.NoteStatus(null);
    queryPort.Tick();
    Check(queryPort._carrier.IsArrived, "Query：查不到状态保持原判断");
    queryPort.NotePodPlaced(false);
    queryPort.Tick();
    Check(queryPort._carrier.IsArrived, "Query：PODOF 事件不认");
    queryPort.NoteStatus(new LoadPortStatus { IsPresent = false, IsPlaced = false });
    queryPort.Tick();
    Check(!queryPort._carrier.IsArrived && queryPort._carrier.Info is null && !queryPort.CreateStateDto().IsCarrierArrived,
        "Query：在位、到位都灭算拿走，载具对象清掉");
    queryPort.NoteStatus(new LoadPortStatus { IsPresent = false, IsPlaced = true });
    queryPort.Tick();
    Check(!queryPort._carrier.IsArrived && queryPort._carrier.Info is null, "Query：一亮一灭不算放上");

    // 2) Event：只认 PODON / PODOF，状态查询说什么都不管
    var eventPort = new ProbePort("PresenceEventPort", PodPresenceSource.Event);
    eventPort.InitComponent();
    eventPort.NoteStatus(new LoadPortStatus { IsPresent = true, IsPlaced = true });
    eventPort.Tick();
    Check(!eventPort._carrier.IsArrived && eventPort._carrier.Info is null, "Event：状态查询说有盒也不认");
    eventPort.NotePodPlaced(true);
    eventPort.Tick();
    Check(eventPort._carrier.IsArrived && eventPort._carrier.Info is not null, "Event：PODON 算放上");
    eventPort.NoteStatus(new LoadPortStatus { IsPresent = false, IsPlaced = false });
    eventPort.Tick();
    Check(eventPort._carrier.IsArrived, "Event：状态查询说没盒也不认");
    eventPort.NotePodPlaced(false);
    eventPort.Tick();
    Check(!eventPort._carrier.IsArrived && eventPort._carrier.Info is null, "Event：PODOF 算拿走");

    // 3) 状态查询走真驱动：回来了接着发下一条；一条没回就超时作废、接着发（以前同名查询一直占着在途位，再也查不了）
    var pollPort = new ProbePort("PollPort", PodPresenceSource.Query);
    pollPort.QueryDataTimeOut = 100;
    var pollComm = pollPort.Shell.Comm;
    Check(pollPort.InitComponent(), "状态查询用的端口的组件初始化应成功");
    pollPort.Tick();
    Check(WaitUntil(() => pollComm.SentCount("GET:STATE") == 1), "连上后扫描一拍就发出第一条状态查询");
    pollComm.Push(StateReply(present: true, placed: true));
    Check(TickUntil(pollPort, () => pollPort._carrier.IsArrived && pollPort._carrier.Info is not null),
        "查询回来在位、到位都亮：判放上（盒子开机前就在端口上也认得）");
    Check(TickUntil(pollPort, () => pollComm.SentCount("GET:STATE") == 2), "上一条回来了，接着发下一条");
    Thread.Sleep(150);
    pollPort.Tick();
    Check(pollPort.Status is null && pollPort._carrier.IsArrived, "查询超时：Status 清空，在位保持原判断");
    Check(TickUntil(pollPort, () => pollComm.SentCount("GET:STATE") == 3), "超时的那一条作废了，同名查询还能接着发");
    pollComm.Push(StateReply(present: false, placed: false));
    Check(TickUntil(pollPort, () => !pollPort._carrier.IsArrived && pollPort._carrier.Info is null), "查询回来两位都灭：判拿走");

    // 4) 动作没做成（失败、超时、被顶替）：驱动上还在等回复的指令全部作废，同名指令能再发
    var stuckLoad = pollPort.Shell.Load();
    Check(stuckLoad is not null, "发一条 Load 指令（假通道不回它）");
    Check(pollPort.Shell.Load() is null, "同名指令还在途时再发被拒");
    // 端口上这时没载具（上面判拿走了），Load 发不起来，用 Home 当那个没做成的动作
    pollPort.NoteState(ModuleState.Idle);
    var failedHome = new ProbeOperation();
    Check(pollPort.BeginAction(LoadPortAction.Home, failedHome) is not null, "Idle 应能发起 Home");
    failedHome.TimeOut();
    pollPort.Tick();
    Check(stuckLoad!.IsCompleted && stuckLoad.Response is not null && !stuckLoad.Response.IsSuccess,
        "动作没做成：在途的指令作废，等它的人不会一直等");
    var retriedLoad = pollPort.Shell.Load();
    Check(retriedLoad is not null, "作废之后同名指令能再发");

    // 5) 关连接：在途的指令全部作废
    pollPort.Close();
    Check(retriedLoad!.IsCompleted && retriedLoad.Response is not null && !retriedLoad.Response.IsSuccess,
        "关连接：在途指令全部作废");

    // 6) 断线：按 EC 间隔在后台重连（先关后开），连上以后接着查状态
    var linkPort = new ProbePort("ReconnectPort", PodPresenceSource.Query);
    linkPort.Shell.ReconnectIntervalMs = 10;
    var linkComm = linkPort.Shell.Comm;
    Check(linkPort.InitComponent() && linkComm.Opens == 1, "重连用的端口的组件初始化应成功");
    linkPort.Tick();
    Check(WaitUntil(() => linkComm.SentCount("GET:STATE") == 1), "连着时照常查状态");
    linkComm.Drop();
    Check(TickUntil(linkPort, () => linkComm.Opens >= 2 && linkComm.IsConnected), "断了按间隔在后台重连上");

    // 断线前发过 1 条、断着的时候发不出去，所以到 2 条就是重连以后发的（重连上的那几拍里可能已经发了，不能从这时才开始数）
    Check(TickUntil(linkPort, () => linkComm.SentCount("GET:STATE") >= 2),
        "重连上以后接着查状态（断线前在途的那条已经作废，同名查询能再发）");
    linkPort.Close();
    int opensAfterClose = linkComm.Opens;
    linkComm.Drop();
    for (int tick = 0; tick < 5; tick++)
    {
        linkPort.Tick();
        Thread.Sleep(15);
    }

    Check(linkComm.Opens == opensAfterClose, "Close 以后不再重连");

    // 7) _rfid 读头连不上：LoadPort 照样开驱动、登记晶圆账槽位（以前整台 LoadPort 都不能动）；读头之后按间隔重连上
    var rfidPort = new ProbePort("RfidDownPort", PodPresenceSource.Query);
    var rfidShell = new ProbeRfidShell();
    typeof(ComponentBase).GetProperty(nameof(ComponentBase.Name))!.SetValue(rfidShell, "_rfid");
    typeof(ComponentBase).GetProperty(nameof(ComponentBase.FullPath))!.SetValue(rfidShell, "RfidDownPort._rfid");
    rfidShell.ReconnectIntervalMs = 10;
    rfidShell.Comm.FailOpen = true;
    rfidPort.AddChild(rfidShell);
    Check(!rfidPort.InitComponent(), "_rfid 连不上时组件初始化返回 false（开机日志看得到）");
    Check(rfidPort.Shell.IsConnected, "_rfid 连不上也照样打开 LoadPort 驱动");
    Check(presenceLedger.GetSlots(rfidPort.Name).Count == rfidPort.SlotCount, "_rfid 连不上也照样在晶圆账上登记槽位");
    rfidShell.Comm.FailOpen = false;
    Check(TickUntil(rfidPort, () => rfidShell.IsConnected), "读头能连了：按间隔在后台重连上");
    rfidPort.Close();

    // 7b) 到位后自动读码，读头这会儿没连上：以前读码没发起成功就这么算了，Host 干等一个永远不来的 ID。
    //     现在接着试，读头连上就发起读码；一直连不上，到读头的读码超时就按读码失败报给 E87
    ProbeRfidShell AddReader(ProbePort target, string path)
    {
        var reader = new ProbeRfidShell();
        typeof(ComponentBase).GetProperty(nameof(ComponentBase.Name))!.SetValue(reader, "RFID");
        typeof(ComponentBase).GetProperty(nameof(ComponentBase.FullPath))!.SetValue(reader, path);
        target.AddChild(reader);
        return reader;
    }

    var autoPort = new ProbePort("AutoReadPort", PodPresenceSource.Event);
    var autoReader = AddReader(autoPort, "AutoReadPort.RFID");
    autoReader.ReconnectIntervalMs = 10;
    autoReader.ReadCarrierIdTimeout = 60000;
    autoReader.Comm.FailOpen = true;
    var autoEvents = new RecordingE87Callback();
    autoPort.E87Callback = autoEvents;
    autoPort.InitComponent();
    autoPort.NotePodPlaced(true);
    autoPort.Tick();
    Check(autoPort._carrier.IsArrived && !autoReader.IsReading, "到位了、读头没连上：读码发不起来");
    autoPort.Tick();
    Check(!autoReader.IsReading && !autoEvents.Wait(nameof(IE87Callback.CarrierIdReadFailed), 100),
        "读头没连上、还没到读码超时：接着等，不报失败");
    autoReader.Comm.FailOpen = false;
    Check(TickUntil(autoPort, () => autoReader.IsReading), "读头连上以后自动读码接着发起（以前没发起成功就算了）");
    autoPort.Close();

    var failPort = new ProbePort("AutoReadFailPort", PodPresenceSource.Event);
    var failReader = AddReader(failPort, "AutoReadFailPort.RFID");
    failReader.ReconnectIntervalMs = 600000;
    failReader.ReadCarrierIdTimeout = 100;
    failReader.Comm.FailOpen = true;
    var failEvents = new RecordingE87Callback();
    failPort.E87Callback = failEvents;
    failPort.InitComponent();
    failPort.NotePodPlaced(true);
    failPort.Tick();
    Thread.Sleep(150);
    failPort.Tick();
    Check(failEvents.Wait(nameof(IE87Callback.CarrierIdReadFailed)) && failPort._carrier.Info!.IdStatus == CarrierIdStatus.ReadFailed,
        "读头一直连不上：到读码超时按读码失败报给 E87（Host 就能带端口号给号或取消）");
    failPort.Close();

    // 没配读头的端口本来就不读码，不算失败：到位后什么都不报
    var noReaderPort = new ProbePort("NoReaderPort", PodPresenceSource.Event);
    var noReaderEvents = new RecordingE87Callback();
    noReaderPort.E87Callback = noReaderEvents;
    noReaderPort.InitComponent();
    noReaderPort.NotePodPlaced(true);
    noReaderPort.Tick();
    noReaderPort.Tick();
    Check(noReaderPort._carrier.Info is not null && !noReaderEvents.Wait(nameof(IE87Callback.CarrierIdReadFailed), 100),
        "没配读头：本来就不读码，到位后不报读码失败");

    // 7c) 端口下没配 _carrier 子组件：装配错了，组件初始化要抛（开机就暴露），不让端口带着缺口跑
    var barePort = new BarePort("BarePort");
    bool bareThrown = false;
    try
    {
        barePort.InitComponent();
    }
    catch (InvalidOperationException exception)
    {
        bareThrown = exception.Message.Contains("_carrier", StringComparison.Ordinal);
    }

    Check(bareThrown, "端口下没配 _carrier 子组件：组件初始化抛 InvalidOperationException（开机就暴露）");

    // 8) 帧通讯重连：旧接收泵在新连接起来以后才出错，只停它自己那一轮，新的一轮照样收（以前一个全局标志会把新泵也停了）
    var transport = new GatedTransport();
    var codec = new FcdFrameCodec();
    var frameLink = new FrameCommunication(transport, new FcdFrameCodec());
    var received = new ConcurrentQueue<string>();
    frameLink.FrameReceived += received.Enqueue;
    Check(frameLink.Open() && WaitUntil(() => transport.Waiting == 1), "打开后第一轮接收泵在等数据");
    frameLink.Close();
    Check(WaitUntil(() => transport.Stale == 1), "关了以后旧泵醒了，卡在出错之前");
    Check(frameLink.Open() && WaitUntil(() => transport.Waiting == 1), "重新打开，新一轮接收泵在等数据");
    transport.ReleaseStale.Set();
    Check(WaitUntil(() => transport.Stale == 0), "旧泵这时才出错退出");
    Thread.Sleep(50);
    transport.Feed(codec.Wrap("INF:PODON"));
    Check(WaitUntil(() => received.Count == 1), "新一轮收到第一帧");
    transport.Feed(codec.Wrap("INF:PODOF"));
    Check(WaitUntil(() => received.Count == 2) && received.SequenceEqual(new[] { "INF:PODON", "INF:PODOF" }),
        "旧泵退出没把新的一轮停掉：第二帧照样收到");
    frameLink.Close();

    // 9) 平台默认动作：机型一个动作都不写也能用——一条驱动指令一个动作。Load 成功把 Mapping 落下来；设备回 NAK、等超时、没连上都判失败
    bool TickPlainUntil(PlainPort target, Func<bool> condition)
    {
        return WaitUntil(() =>
        {
            target.Tick();
            return condition();
        });
    }

    var plain = new PlainPort("PlainPort");
    var plainComm = plain.Shell.Comm;
    Check(plain.InitComponent(), "平台默认动作用的端口的组件初始化应成功");
    plain.NoteState(ModuleState.Idle);
    Check(plain.Load() is null && plain.State == ModuleState.Idle && plainComm.SentCount("MOV:CLOAD") == 0,
        "端口上没载具：平台默认 Load 不发");
    plain.NotePodPlaced(true);
    plain.Tick();
    var plainLoad = plain.Load();
    Check(plainLoad is not null && plain.State == LoadPortState.Loading, "平台默认 Load：空闲能发起，进 Loading");
    Check(TickPlainUntil(plain, () => plainComm.SentCount("MOV:CLOAD") == 1), "平台默认 Load 发的是 FCD 的 CLOAD");
    plainComm.Push("INF:CLOAD/PEC");
    Check(TickPlainUntil(plain, () => plainLoad!.IsTerminal) && plainLoad!.IsSuccess && plain.State == LoadPortState.Loaded
          && plain._carrier.SlotMap.Count == 3 && plain._carrier.SlotMap[0] == SlotState.CorrectlyOccupied && plain._carrier.SlotMap[1] == SlotState.Empty
          && plain._carrier.SlotMap[2] == SlotState.CrossSlotted,
        "平台默认 Load：设备回完成就落 Loaded，Mapping 结果落进 SlotMap");
    var plainUnload = plain.Unload();
    Check(plainUnload is not null && TickPlainUntil(plain, () => plainComm.SentCount("MOV:CULOD") == 1), "平台默认 Unload 发 CULOD");
    plainComm.Push("NAK:CULOD/BUSY");
    Check(TickPlainUntil(plain, () => plainUnload!.IsTerminal) && plainUnload!.Code == ErrorCodes.DeviceFailed
          && plain.State == ModuleState.Error,
        "设备回 NAK：动作判失败（device_failed），落 Error");
    plain.HomeTimeout = 100;
    var plainHome = plain.Home();
    Check(plainHome is not null && TickPlainUntil(plain, () => plainComm.SentCount("MOV:ORGSH") == 1), "出错后能 Home，发 ORGSH");
    Thread.Sleep(150);
    Check(TickPlainUntil(plain, () => plainHome!.IsTerminal) && plainHome!.Code == ErrorCodes.Timeout, "设备不回：超时判失败（timeout）");
    plainComm.Drop();
    var droppedHome = plain.Home();
    Check(droppedHome is not null && TickPlainUntil(plain, () => droppedHome!.IsTerminal) && droppedHome!.Code == ErrorCodes.CommandRejected,
        "没连上：指令发不出去，判被拒（command_rejected）");
    plain.Close();

    // 10) 复位只清错、中止只停，Idle 一律当"门关好、没 Load"：没初始化、出过错的复位完是 NotInit（要再 Home，照老 CTC），
    //     中止也绕不过复位和 Home；门开着没在动（Loaded、正被机械手取放）的复位 / 中止完按状态查询的门位落 Loaded / Idle，
    //     查不到、门在半路、载具不在，或者打断的是机构在动的动作（Load / Unload / Home / 夹紧），落 NotInit
    var resetPort = new ProbePort("ResetStatePort");
    Check(resetPort.InitComponent(), "复位用的端口的组件初始化应成功");
    var doorOpen = new LoadPortStatus { IsPresent = true, IsPlaced = true, IsDoorOpen = true };
    var doorClosed = new LoadPortStatus { IsPresent = true, IsPlaced = true, IsDoorClosed = true };

    // 从 from 状态发一个动作、做成，返回做完落的状态
    int StateAfter(int from, LoadPortAction action, LoadPortStatus? status)
    {
        resetPort.NoteState(from);
        resetPort.NoteStatus(status);
        var operation = new ProbeOperation();
        Check(resetPort.BeginAction(action, operation) is not null, $"状态 {from} 应能发起 {action}");
        operation.Succeed();
        resetPort.Tick();
        return resetPort.State;
    }

    resetPort.NoteState(ModuleState.Idle);
    Check(resetPort.BeginAction(LoadPortAction.Load, new ProbeOperation()) is null && resetPort.State == ModuleState.Idle,
        "端口上没载具：Load 发不起来（模块自己拦，机型重写了 Load 也走这一关）");
    resetPort.NotePodPlaced(true);
    resetPort.Tick();
    resetPort.BlockedAction = LoadPortAction.Load;
    Check(resetPort.BeginAction(LoadPortAction.Load, new ProbeOperation()) is null && resetPort.State == ModuleState.Idle,
        "机型重写了 Load 联锁（LoadInterlock）：载具在也不让 Load");
    resetPort.BlockedAction = null;

    // 其余动作的联锁：平台默认不拦；机型重写了哪个动作的联锁，就只拦那个动作（复位、中止不设联锁）
    foreach (var (action, from) in new[]
             {
                 (LoadPortAction.Unload, LoadPortState.Loaded),
                 (LoadPortAction.Home, ModuleState.Idle),
                 (LoadPortAction.Clamp, ModuleState.Idle),
                 (LoadPortAction.Unclamp, ModuleState.Idle),
             })
    {
        resetPort.NoteState(from);
        resetPort.BlockedAction = action;
        Check(resetPort.BeginAction(action, new ProbeOperation()) is null && resetPort.State == from,
            $"机型重写了 {action} 联锁：不让发，状态不动");
        resetPort.BlockedAction = null;
        var allowed = new ProbeOperation();
        Check(resetPort.BeginAction(action, allowed) is not null, $"{action} 联锁没拦（平台默认）：照常能发");
        allowed.Succeed();
        resetPort.Tick();
    }

    // 机械手进站要载具在：状态还是 Loaded、载具却不在了，不让进，免得往空口放片
    resetPort.NoteState(LoadPortState.Loaded);
    Check(resetPort.CanPrepare, "Loaded、载具在、没接 EAP：机械手能进站");
    resetPort.NotePodPlaced(false);
    resetPort.Tick();
    Check(!resetPort.CanPrepare && resetPort.PrepareTransfer() is null && resetPort.State == LoadPortState.Loaded,
        "Loaded 但载具不在：机械手不能进站");
    resetPort.NotePodPlaced(true);
    resetPort.Tick();

    Check(StateAfter(ModuleState.Error, LoadPortAction.Reset, doorOpen) == ModuleState.NotInit,
        "出过错的复位完是 NotInit，要再 Home（门开着也一样）");
    Check(StateAfter(ModuleState.NotInit, LoadPortAction.Reset, null) == ModuleState.NotInit,
        "没初始化的复位完还是没初始化：不能拿复位跳过 Home");
    Check(StateAfter(ModuleState.NotInit, LoadPortAction.Abort, null) == ModuleState.NotInit,
        "没初始化的中止完还是没初始化");
    Check(StateAfter(ModuleState.Error, LoadPortAction.Abort, doorOpen) == ModuleState.Error,
        "出过错的中止完还是出错：中止绕不过复位");
    Check(StateAfter(ModuleState.Idle, LoadPortAction.Reset, doorOpen) == ModuleState.Idle,
        "空闲的复位完还是空闲");
    Check(StateAfter(ModuleState.Idle, LoadPortAction.Abort, doorClosed) == ModuleState.Idle && resetPort.IsIdle,
        "空闲的中止完还是空闲");

    Check(StateAfter(LoadPortState.Loaded, LoadPortAction.Reset, doorOpen) == LoadPortState.Loaded && resetPort.IsLoaded,
        "Load 好了、门开着：复位完还是 Loaded（不掉成 Idle，机械手接着能进，不用再 Load 重建账）");
    Check(StateAfter(LoadPortState.Loaded, LoadPortAction.Reset, doorClosed) == ModuleState.Idle,
        "Load 好了但状态查询说门关着：复位完落 Idle");
    Check(StateAfter(LoadPortState.Loaded, LoadPortAction.Reset, null) == ModuleState.NotInit && !resetPort.IsIdle,
        "查不到门位：复位完落 NotInit，不当门关好了（Idle 会被判成能取走）");
    Check(StateAfter(LoadPortState.Loaded, LoadPortAction.Abort, doorOpen) == LoadPortState.Loaded,
        "Load 好了、门开着：中止完还是 Loaded");
    Check(StateAfter(LoadPortState.Loaded, LoadPortAction.Abort, new LoadPortStatus { IsPresent = true, IsPlaced = true })
          == ModuleState.NotInit,
        "门开、门关两位都不亮（停在半路）：中止完落 NotInit");
    Check(StateAfter(TransferModuleState.Transferring, LoadPortAction.Abort, doorOpen) == LoadPortState.Loaded
          && resetPort.CanPrepare,
        "机械手取片失败卡在取放中：人确认后中止，门开着回 Loaded，机械手又能来取");
    Check(StateAfter(TransferModuleState.Transferring, LoadPortAction.Abort, doorClosed) == ModuleState.Idle,
        "卡在取放中、门却关着：中止完落 Idle");

    // 打断的是机构在动的动作：门、夹爪可能停在半路，状态查询可能还是打断前的，一律 NotInit，要人 Home
    int StateAfterAbortDuring(int from, LoadPortAction moving)
    {
        resetPort.NoteState(from);
        resetPort.NoteStatus(doorOpen);
        var movingOperation = new ProbeOperation();
        var abort = new ProbeOperation();
        Check(resetPort.BeginAction(moving, movingOperation) is not null
              && resetPort.BeginAction(LoadPortAction.Abort, abort) is not null, $"{moving} 做到一半被中止顶替");
        abort.Succeed();
        resetPort.Tick();
        return resetPort.State;
    }

    Check(StateAfterAbortDuring(ModuleState.Idle, LoadPortAction.Load) == ModuleState.NotInit && !resetPort.IsIdle,
        "打断的是 Load：状态查询说门开着也落 NotInit，不当门关好了");
    Check(StateAfterAbortDuring(LoadPortState.Loaded, LoadPortAction.Unload) == ModuleState.NotInit,
        "打断的是 Unload：落 NotInit");
    Check(StateAfterAbortDuring(ModuleState.Idle, LoadPortAction.Clamp) == ModuleState.NotInit,
        "打断的是夹紧：落 NotInit");
    Check(StateAfterAbortDuring(ModuleState.Idle, LoadPortAction.Home) == ModuleState.NotInit,
        "Home 被中止打断：还是没初始化");

    resetPort.NotePodPlaced(false);
    resetPort.Tick();
    Check(StateAfter(LoadPortState.Loaded, LoadPortAction.Reset, doorOpen) == ModuleState.NotInit,
        "载具不在了：门开着也不回 Loaded，落 NotInit");
    resetPort.Close();

    WaferManagerComponent.Current = previousLedger;
}

Console.WriteLine($"PASS: {checks} operation wait checks (including 200 completion races, five device RPC actions, the online/offline and auto/manual mode switches, the EAP callback path, the carrier component: its lifecycle from arrival to removal, a Host-accepted slot map staying accepted across a re-map, the automatic carrier-id read retrying while the reader is down and reporting a read failure after the reader timeout, and a port without its _carrier node refusing to open,and robot pick/place writing the wafer ledger, LoadPort/_robot alarms raised and cleared only by a manual reset, the E84 handoff flow: load, unload, gating, abort, timeout and recovery, DI/AI alarm debounce with the module-level HasAlarm, and the EC component: live read/write, declaration merge, fallback when not installed and an ec.xml round trip, and the init/abort hooks: InitComponent (no hardware motion) recursing through every level of children by InitOrder with a failing child not holding back its siblings, optional overrides, InitModule = Home leaving the children alone, the E84 and driver components connecting themselves from InitComponent, and Abort without clearing alarms, and transfer routine failures reported with the station, the preparation step number and the wait time as error args, and the main page backend: LoadPort/robot lists in the system settings, station kinds for the dispatch map, the Auto/Manual mode in the equipment status and the equipment Auto/Manual/Stop service, and the LoadPort presence source: query (both bits) or event, status query timeout recovery, abandoning in-flight driver commands, LoadPort/_rfid reconnect, an _rfid outage not blocking the LoadPort and frame pump sessions across reconnects, and the LoadPort end states after Reset/Abort: NotInit after an error, an interrupted motion or an unknown door, Loaded/Idle by the door position, and the Load interlock).");

// 只为满足"驱动已连接"这个前置条件；真实帧收发不在本工具的范围内。
sealed class FakeFrameCommunication : IFrameCommunication
{
    private readonly ConcurrentQueue<string> _sent = new();
    private int _opens;
    private volatile bool _isConnected;

    public event Action<string>? FrameReceived;

    public bool IsConnected => _isConnected;

    /// <summary>为 true 时打不开（模拟设备没接、串口不存在）。</summary>
    public bool FailOpen { get; set; }

    /// <summary>打开过几次（重连也算）。</summary>
    public int Opens => Volatile.Read(ref _opens);

    public bool Open()
    {
        Interlocked.Increment(ref _opens);
        if (FailOpen)
        {
            return false;
        }

        _isConnected = true;
        return true;
    }

    public void Close() => _isConnected = false;

    /// <summary>模拟连接自己断了（传输出错自己关了）。</summary>
    public void Drop() => _isConnected = false;

    public void Send(string body) => _sent.Enqueue(body);

    public void Push(string body) => FrameReceived?.Invoke(body);

    /// <summary>发出去的帧里以 prefix 开头的有几条。</summary>
    public int SentCount(string prefix) => _sent.Count(body => body.StartsWith(prefix, StringComparison.Ordinal));
}

sealed class ProbeOperation() : ModuleOperation("Probe")
{
    public bool FinishOnScan { get; init; }
    public int AbortCount { get; private set; }
    public void Succeed() => Complete();
    public void Reject() => Fail(ErrorCodes.DeviceFailed, "device error", Name, "device error");
    public void TimeOut() => Fail(ErrorCodes.Timeout, "action timeout", Name, "1000");

    protected override void OnScan()
    {
        if (FinishOnScan)
        {
            Complete();
        }
    }

    protected override void OnAborted(string reason) => AbortCount++;
}

sealed class ProbeModule : BaseModule
{
    /// <summary>本探针只验操作挂载与终结，不跑状态表；状态码摆着不用。</summary>
    public override int State { get; protected set; } = ModuleState.NotInit;
    public ManualResetEventSlim? Completing { get; init; }
    public ManualResetEventSlim? ReleaseCompletion { get; init; }
    public bool CleanupDone { get; private set; }
    public bool StartOperation(ModuleOperation operation, bool replace = false) => Run(operation, replace);
    public void Tick() => OnScan();
    protected override void OnOperationCompleted(ModuleOperation operation)
    {
        Completing?.Set();
        if (ReleaseCompletion is not null && !ReleaseCompletion.Wait(2000))
        {
            throw new TimeoutException("Test completion callback was not released.");
        }

        CleanupDone = true;
    }
}

// 探针端口下挂的载具组件：生产里是 sc.xml 的 _carrier 节点，名字由装配器经 internal setter 设，这里反射设。
static class ProbeCarrier
{
    public static CarrierComponent Add(ComponentBase port, PodPresenceSource presence)
    {
        var carrier = new CarrierComponent { PresenceSource = presence };
        typeof(ComponentBase).GetProperty(nameof(ComponentBase.Name))!.SetValue(carrier, "_carrier");
        typeof(ComponentBase).GetProperty(nameof(ComponentBase.FullPath))!.SetValue(carrier, $"{port.FullPath}._carrier");
        port.AddChild(carrier);
        return carrier;
    }
}

sealed class ProbePort : BaseLoadPortModule
{
    public ProbeOperation? Next { get; set; }
    public int Calls { get; private set; }

    /// <summary>挂在下面的探针品牌壳：真 FCD 驱动，传输换成假通道。</summary>
    public ProbePortShell Shell { get; }

    public ProbePort(string name = "WaitSmokePort", PodPresenceSource presence = PodPresenceSource.Event)
    {
        // Production names are assigned by ComponentLoader through internal setters.
        typeof(ComponentBase).GetProperty(nameof(Name))!.SetValue(this, name);
        typeof(ComponentBase).GetProperty(nameof(FullPath))!.SetValue(this, Name);
        Shell = new ProbePortShell();
        typeof(ComponentBase).GetProperty(nameof(Name))!.SetValue(Shell, "_driver");
        typeof(ComponentBase).GetProperty(nameof(FullPath))!.SetValue(Shell, $"{name}._driver");
        AddChild(Shell);

        // 在位默认走设备上报（测试用 NotePodPlaced 摆）；测状态查询的传 Query，用 NoteStatus 或假通道回状态摆。
        ProbeCarrier.Add(this, presence);

        // Seed only the in-memory EC component created at the top; never load or flush a configuration file.
        LoadTimeout = 0;
        UnloadTimeout = 0;
        HomeTimeout = 0;
        ResetTimeout = 0;
        AbortTimeout = 0;

        // 假通道不回状态查询：超时给到最长，免得查询超时把测试摆的 Status 清掉。
        QueryDataTimeOut = 600000;
    }
    public void NoteMap(IReadOnlyList<SlotState> slotMap) => UpdateSlotMap(slotMap);

    /// <summary>顶替驱动的 PODON/PODOF 主动事件（生产里由驱动路由线程报），下一拍扫描生效。</summary>
    public void NotePodPlaced(bool placed) => SetDeviceReportedPlaced(placed);

    /// <summary>顶替扫描线程推一拍（本工具不跑扫描循环）。</summary>
    public void Tick() => OnScan();

    /// <summary>直接摆状态，省去为了进 Idle 先跑一遍 Home。</summary>
    public void NoteState(int state) => State = state;

    /// <summary>摆一个设备状态查询结果（生产里由机型扫描下发状态查询刷新）。</summary>
    public void NoteStatus(LoadPortStatus? status) => Status = status;

    /// <summary>走真路径发起动作（状态表 + 操作登记），不是 Load() 那种直接返回。</summary>
    public ModuleOperation? BeginAction(LoadPortAction action, ModuleOperation operation) => Begin(action, operation);

    /// <summary>顶替机型自己加的联锁条件（光幕、机械手缩回这类）：摆哪个动作就拦哪个动作，null 不拦。</summary>
    public LoadPortAction? BlockedAction { get; set; }

    protected override bool LoadInterlock() => base.LoadInterlock() && BlockedAction != LoadPortAction.Load;
    protected override bool UnloadInterlock() => base.UnloadInterlock() && BlockedAction != LoadPortAction.Unload;
    protected override bool HomeInterlock() => base.HomeInterlock() && BlockedAction != LoadPortAction.Home;
    protected override bool ClampInterlock() => base.ClampInterlock() && BlockedAction != LoadPortAction.Clamp;
    protected override bool UnclampInterlock() => base.UnclampInterlock() && BlockedAction != LoadPortAction.Unclamp;
    private ModuleOperation? Take() { Calls++; return Next; }
    public override ModuleOperation? Load() => Take();
    public override ModuleOperation? Unload() => Take();
    public override ModuleOperation? Home() => Take();
    protected override ModuleOperation? ResetDevice() => Take();
    protected override ModuleOperation? AbortDevice() => Take();
    public override ModuleOperation? Clamp() => Take();
    public override ModuleOperation? Unclamp() => Take();
}

// 没挂 _carrier 子组件的端口：验装配错了开机就暴露。
sealed class BarePort : BaseLoadPortModule
{
    public BarePort(string name)
    {
        typeof(ComponentBase).GetProperty(nameof(Name))!.SetValue(this, name);
        typeof(ComponentBase).GetProperty(nameof(FullPath))!.SetValue(this, name);
    }
}

// 平台默认动作的探针 LoadPort：一个动作都不重写，走 BaseLoadPortModule 自带的那一套（真 FCD 驱动 + 假通道）。
sealed class PlainPort : BaseLoadPortModule
{
    public ProbePortShell Shell { get; }

    public PlainPort(string name)
    {
        typeof(ComponentBase).GetProperty(nameof(Name))!.SetValue(this, name);
        typeof(ComponentBase).GetProperty(nameof(FullPath))!.SetValue(this, name);
        Shell = new ProbePortShell();
        typeof(ComponentBase).GetProperty(nameof(Name))!.SetValue(Shell, "Driver");
        typeof(ComponentBase).GetProperty(nameof(FullPath))!.SetValue(Shell, $"{name}.Driver");
        AddChild(Shell);
        ProbeCarrier.Add(this, PodPresenceSource.Event);

        // 假通道不回状态查询：超时给到最长，别让查询超时的日志掺和
        QueryDataTimeOut = 600000;
    }

    /// <summary>顶替扫描线程推一拍。</summary>
    public void Tick() => OnScan();

    /// <summary>直接摆状态，省去先跑一遍 Home。</summary>
    public void NoteState(int state) => State = state;

    /// <summary>顶替驱动的 PODON/PODOF 主动事件，下一拍扫描生效。</summary>
    public void NotePodPlaced(bool placed) => SetDeviceReportedPlaced(placed);
}

// 探针 LoadPort 品牌壳：只把传输换成假通道，编解码/驱动/指令都是生产代码。
sealed class ProbePortShell : FcdLoadPortComponent
{
    /// <summary>驱动底下的假通道：测试看发出去的帧、往里推回复、模拟断线。</summary>
    public FakeFrameCommunication Comm { get; } = new();

    protected override ILoadPortDriver CreateDriver() => new FcdLoadPortDriver(Comm);
}

// 探针 _rfid 品牌壳：真 FCD 读头驱动，传输换成假通道（测读头连不上、重连）。
sealed class ProbeRfidShell : FcdRfidComponent
{
    public FakeFrameCommunication Comm { get; } = new();

    protected override IRfidDriver CreateDriver() => new FcdRfidDriver(Comm);
}

// 字节级的假传输，专测帧通讯的接收泵：收包一直等到有数据或连接被关；等的时候被关了，要测试放行才出错，
// 模拟断线重连时旧泵在新连接起来以后才醒。
sealed class GatedTransport : ICommunication
{
    private readonly object _gate = new();
    private readonly Queue<byte[]> _incoming = new();
    private int _generation;
    private int _waiting;
    private int _stale;

    /// <summary>放行"等的时候连接被关了"的旧泵，让它出错退出。</summary>
    public ManualResetEventSlim ReleaseStale { get; } = new(false);

    public bool IsConnected { get; private set; }

    /// <summary>正在等数据的接收泵个数。</summary>
    public int Waiting
    {
        get
        {
            lock (_gate)
            {
                return _waiting;
            }
        }
    }

    /// <summary>等的时候连接被关了、在等放行的旧泵个数。</summary>
    public int Stale
    {
        get
        {
            lock (_gate)
            {
                return _stale;
            }
        }
    }

    public void Connect()
    {
        lock (_gate)
        {
            IsConnected = true;
        }
    }

    public void Close()
    {
        lock (_gate)
        {
            IsConnected = false;
            _generation++;
            Monitor.PulseAll(_gate);
        }
    }

    public void Send(byte[] data)
    {
    }

    /// <summary>往线上塞一段字节（已经按协议包好壳的帧）。</summary>
    public void Feed(string text)
    {
        lock (_gate)
        {
            _incoming.Enqueue(System.Text.Encoding.ASCII.GetBytes(text));
            Monitor.PulseAll(_gate);
        }
    }

    public byte[] Receive()
    {
        lock (_gate)
        {
            int generation = _generation;
            _waiting++;
            try
            {
                while (_generation == generation && _incoming.Count == 0)
                {
                    Monitor.Wait(_gate);
                }

                if (_generation == generation)
                {
                    return _incoming.Dequeue();
                }
            }
            finally
            {
                _waiting--;
            }

            _stale++;
        }

        ReleaseStale.Wait(5000);
        lock (_gate)
        {
            _stale--;
        }

        throw new IOException("连接已关");
    }

    public void Dispose()
    {
    }
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
        E84Enabled = enabled;
        Tp1Timeout = timeoutMs;
        Tp2Timeout = timeoutMs;
        Tp3Timeout = timeoutMs;
        Tp4Timeout = timeoutMs;
        Tp5Timeout = timeoutMs;
    }

    /// <summary>摆下一拍搬运车给的信号（没给的都当 OFF）。</summary>
    public void Set(bool cs0 = false, bool valid = false, bool trReq = false, bool busy = false, bool compt = false) =>
        _next = new E84Inputs { Cs0 = cs0, Valid = valid, TrReq = trReq, Busy = busy, Compt = compt };

    protected override E84Inputs ReadInputs() => _next;

    protected override bool WriteOutputs(E84Outputs outputs)
    {
        Writes++;
        return true;
    }
}

// 探针 DI/AI：IO 读写层还没接，读数由测试直接摆；EC 只在本进程内存里。
sealed class ProbeDi : DiSensorComponent
{
    public bool? Level { get; set; }

    public ProbeDi(string path)
    {
        typeof(ComponentBase).GetProperty(nameof(Name))!.SetValue(this, path[(path.LastIndexOf('.') + 1)..]);
        typeof(ComponentBase).GetProperty(nameof(FullPath))!.SetValue(this, path);
        DiIndex = 0;
    }

    /// <summary>顶替扫描线程推一拍。</summary>
    public void Tick() => OnScan();

    protected override bool? ReadDi() => Level;
}

sealed class ProbeAi : AiSensorComponent
{
    public double? Reading { get; set; }
    public bool? MonitoringDo { get; set; }

    public ProbeAi(string path)
    {
        typeof(ComponentBase).GetProperty(nameof(Name))!.SetValue(this, path[(path.LastIndexOf('.') + 1)..]);
        typeof(ComponentBase).GetProperty(nameof(FullPath))!.SetValue(this, path);
        AiIndex = 0;
    }

    /// <summary>顶替扫描线程推一拍。</summary>
    public void Tick() => OnScan();

    protected override double? ReadAi() => Reading;

    protected override bool? ReadMonitoringDo() => MonitoringDo;
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
            if (_received.ContainsKey(name))
            {
                return true;
            }

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
            if (_received.ContainsKey(name))
            {
                return true;
            }

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
        QueryDataTimeOut = 0;
        HomeTimeout = 0;
        PickTimeout = 0;
        PlaceTimeout = 0;
        ResetTimeout = 0;
        AbortTimeout = 0;
        PowerTimeout = 0;
        AddChild(new ProbeRobotShell());
    }

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
    protected override ModuleOperation? ResetDevice() => Take(RobotAction.Reset);
    protected override ModuleOperation? AbortDevice() => Take(RobotAction.Abort);
    public override ModuleOperation? PowerOn() => Take(RobotAction.PowerOn);
    public override ModuleOperation? PowerOff() => Take(RobotAction.PowerOff);
    protected override ModuleOperation CreatePickOperation(int arm, int stationNumber, int slot) => Supply();
    protected override ModuleOperation CreatePlaceOperation(int arm, int stationNumber, int slot) => Supply();

    private ModuleOperation Supply() =>
        Next ?? throw new InvalidOperationException("用例忘了给 ProbeRobot.Next 摆一个操作。");
}

// 探针腔体：只占个名字和类型（主界面那一节验站点类型、系统设置的腔体名单），不做动作。
sealed class ProbeChamber : BaseChamberModule
{
    public ProbeChamber(string name)
    {
        typeof(ComponentBase).GetProperty(nameof(Name))!.SetValue(this, name);
        typeof(ComponentBase).GetProperty(nameof(FullPath))!.SetValue(this, name);
    }

    public override ModuleOperation? Home() => null;

    protected override ModuleOperation? ResetDevice() => null;

    protected override ModuleOperation? AbortDevice() => null;

    protected override ModuleOperation? CreateProcessOperation(ProcessRequest request) => null;
}

// 探针普通站点（对中台这类）：能放片，既不是 LoadPort 也不是腔体。
sealed class ProbeAligner : BaseTransferStationModule
{
    public ProbeAligner(string name)
    {
        typeof(ComponentBase).GetProperty(nameof(Name))!.SetValue(this, name);
        typeof(ComponentBase).GetProperty(nameof(FullPath))!.SetValue(this, name);
    }

    public override int State { get; protected set; } = ModuleState.Idle;

    public override int SlotCount { get; set; } = 1;
}

// 探针品牌壳：驱动走假传输，只为把连接那道门打开（生产里真指令都被 ProbeOperation 顶替了）。
sealed class ProbeRobotShell : RejeRobotComponent
{
    protected override IRobotDriver CreateDriver() => new RejeRobotDriver(new FakeFrameCommunication());
}

// 探针子组件：记下 InitComponent/Abort 的先后，能报自己的一条报警；FailInit 为真时组件初始化返回 false（模拟没连上）。
sealed class ProbeChild : ComponentBase
{
    private readonly List<string> _trace;

    public ProbeChild(string name, List<string> trace, int initOrder = 10000, string? fullPath = null)
    {
        typeof(ComponentBase).GetProperty(nameof(Name))!.SetValue(this, name);
        typeof(ComponentBase).GetProperty(nameof(FullPath))!.SetValue(this, fullPath ?? name);
        typeof(ComponentBase).GetProperty(nameof(InitOrder))!.SetValue(this, initOrder);
        _trace = trace;
    }

    [xyz.Components.Attributes.Alarm("探针故障", xyz.Components.Enums.AlarmCategory.Other)]
    public string ProbeFault = nameof(ProbeFault);

    public bool FailInit { get; init; }

    public void Fault() => RaiseAlarm(ProbeFault);

    public override bool InitComponent()
    {
        bool childrenInitialized = base.InitComponent();
        _trace.Add($"InitComponent:{Name}");
        return childrenInitialized && !FailInit;
    }

    public override object? Abort()
    {
        base.Abort();
        _trace.Add($"Abort:{Name}");
        return null;
    }
}

// 什么都没重写的组件：InitComponent、Abort 照样能调。
sealed class PlainChild : ComponentBase
{
}

// 探针站点：两步准备各给什么由测试定（不给 = 被拒），交互标记一律落得下；只为验搬运出错时报的错误码和参数，
// 顺带记下准备一被抢了几次、环被还了几次。
sealed class ProbeStation(string name) : ITransferStation
{
    public Func<ModuleOperation?> First { get; init; } = () => null;
    public Func<ModuleOperation?> Second { get; init; } = () => null;
    public int Prepares { get; private set; }
    public int Cancels { get; private set; }
    public string Name => name;
    public int SlotCount => 1;
    public bool CanPrepare => true;

    public ModuleOperation? PrepareTransfer()
    {
        Prepares++;
        return First();
    }

    public ModuleOperation? PrepareTransfer2() => Second();
    public bool Transferring() => true;
    public bool TransferComplete() => true;

    public bool CancelTransfer()
    {
        Cancels++;
        return true;
    }

    public IReadOnlyList<string> SupportedTasks => StationTaskAction.PickPlace;

    public HandleResult CheckTask(StationTaskRequest request) => HandleResult.Fail(ErrorCodes.StationTaskUnsupported, Name, request.Kind);

    public ModuleOperation? StartTask(StationTaskRequest request) => null;
}

// 探针机械手（搬运用）：只占个名字，动作一律发不出去——验的几种出错都停在站点准备，走不到取放片。
sealed class ProbeTransferRobot : IRobot
{
    public string Name => "TransferRobot";
    public int State => 0;
    public bool? IsServoOn => null;
    public string? DeviceError => null;
    public IReadOnlyDictionary<string, RobotStation> Stations { get; } = new Dictionary<string, RobotStation>();
    public bool? HasWafer(int arm) => null;

    public bool TryGetStation(string station, [MaybeNullWhen(false)] out RobotStation config)
    {
        config = null;
        return false;
    }

    public ModuleOperation? Home() => null;
    public ModuleOperation? InitModule() => null;
    public ModuleOperation? Reset() => null;
    public ModuleOperation? Abort() => null;
    public ModuleOperation? Pick(int arm, string station, int slot) => null;
    public ModuleOperation? Place(int arm, string station, int slot) => null;
    public ModuleOperation? PowerOn() => null;
    public ModuleOperation? PowerOff() => null;
}
