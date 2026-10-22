using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using xyz.Components.Interfaces;
using xyz.Components.Models;
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
using xyz.Service.Systems;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Shared.Rpc;
using xyz.Shared.Services;
using xyz.Tools;

// No host, hardware connection, or configuration-file writes are used by these checks.
SmokeIoc.Reset();

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
SmokeIoc.Use<IEc>(ec);

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
    "_loadport failure must preserve its error code and arguments.");

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
    bool consistent = false;
    switch (operation.State)
    {
        case OperationState.Completed:
            consistent = operation.Code.Length == 0 && operation.ErrorArgs.Count == 0;
            break;
        case OperationState.Failed:
            consistent = operation.Code == ErrorCodes.DeviceFailed && operation.ErrorArgs.Count == 2;
            break;
        case OperationState.Aborted:
            consistent = operation.Code == ErrorCodes.Aborted && operation.ErrorArgs.Count == 1;
            break;
    }

    Check(consistent, "Concurrent terminal paths produced a mixed result.");
}

// 跟开机装配一样：晶圆账先装出来，再做组件初始化——端口和载具在组件初始化时取一次账本（_ledger），
// 载具组件也在这里挂到端口上
var ledger = new LedgerManagerComponent();
SmokeIoc.Use<ILedgerManager>(ledger);
var port = new ProbePort();
port.InitComponent();
ComponentLoader.BindAll([port]);
SmokeIoc.Tree(port);
var service = new LoadPortService();
// 服务回包（Unload、Home、复位、中止）：入口被拒原样回原因；发起了就等终态，成功 / 设备报错 / 超时 / 被顶替 / 等待超时都翻成对应的码。
// 走真路径（挂操作、落状态、扫描线程收尾才唤醒等的人），所以用单独的端口，动作挂上以后再收尾、推一拍。Load 见下面一节。
{
    var servicePort = new ProbePort("ServicePort");
    Check(servicePort.InitComponent(), "服务回包用的端口的组件初始化应成功");
    ComponentLoader.BindAll([servicePort]);
    servicePort.HomeTimeout = 5000;
    servicePort.UnloadTimeout = 5000;
    servicePort.ResetTimeout = 5000;
    servicePort.AbortTimeout = 5000;
    SmokeIoc.Tree(servicePort);
    var portService = new LoadPortService();

    bool Mounted(ModuleOperation operation)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < 3000)
        {
            if (ReferenceEquals(servicePort.CurrentOperation, operation))
            {
                return true;
            }

            Thread.Sleep(1);
        }

        return false;
    }

    // 发一个动作：摆一个探针动作，等它挂上了按 finish 收尾、推一拍，回服务的回包
    async Task<RpcResponse> Run(Func<string, Task<RpcResponse>> call, Action<ProbeOperation> finish)
    {
        var probe = new ProbeOperation();
        servicePort.Next = probe;
        var pending = Task.Run(() => call(servicePort.Name));
        Check(Mounted(probe), "动作应挂上");
        finish(probe);
        servicePort.Tick();
        return await pending;
    }

    foreach (var call in new Func<string, Task<RpcResponse>>[]
             {
                 portService.UnloadAsync, portService.HomeAsync, portService.ResetAsync, portService.AbortAsync,
             })
    {
        var missing = await call("missing");
        Check(!missing.Success && missing.Code == ErrorCodes.ModuleNotFound, "没有这个模块：module.not_found");
    }

    // 被拒原样回入口的原因
    servicePort.NoteState(ModuleState.Idle);
    var refused = await portService.UnloadAsync(servicePort.Name);
    Check(!refused.Success && refused.Code == ErrorCodes.ModuleStateNotAllowed
          && refused.Args.SequenceEqual(new[] { servicePort.Name, ModuleState.Idle.ToString() }),
        $"空闲（门关着）点 Unload：回状态不允许带状态码，实际 {refused.Code}");
    servicePort.NoteState(LoadPortState.Loading);
    refused = await portService.ResetAsync(servicePort.Name);
    Check(!refused.Success && refused.Code == ErrorCodes.ModuleStateNotAllowed
          && refused.Args.SequenceEqual(new[] { servicePort.Name, LoadPortState.Loading.ToString() }),
        $"复位：跟着组件基类回 null 的，服务再问原因（CheckReset），回状态不允许，实际 {refused.Code}");
    servicePort.BlockedAction = LoadPortAction.Home;
    servicePort.NoteState(ModuleState.Idle);
    refused = await portService.HomeAsync(servicePort.Name);
    Check(!refused.Success && refused.Code == ProbePort.BlockedCode, $"机型的 Home 联锁不过：回机型自己的原因，实际 {refused.Code}");
    servicePort.BlockedAction = null;

    // 发起了：终态翻成回包（翻法几个动作共用，拿 Home 走一遍）
    servicePort.NoteState(ModuleState.Idle);
    var response = await Run(portService.HomeAsync, probe => probe.Succeed());
    Check(response.Success && servicePort.State == ModuleState.Idle, "Home 做成：回成功，落 Idle");
    response = await Run(portService.HomeAsync, probe => probe.Reject());
    Check(!response.Success && response.Code == ErrorCodes.DeviceFailed && response.Args.Count == 2 && servicePort.State == ModuleState.Error,
        "设备报错：回 device_failed 带参数，落 Error");
    response = await Run(portService.HomeAsync, probe => probe.TimeOut());
    Check(!response.Success && response.Code == ErrorCodes.Timeout, "动作超时：回 timeout");
    response = await Run(portService.HomeAsync, probe => probe.AbortByHost("replacement"));
    Check(!response.Success && response.Code == ErrorCodes.Aborted, "被顶替：回 action_aborted");

    servicePort.HomeTimeout = 0;
    var waiting = new ProbeOperation();
    servicePort.Next = waiting;
    response = await portService.HomeAsync(servicePort.Name);
    Check(!response.Success && response.Code == ErrorCodes.WaitTimeout && response.Args.SequenceEqual(new[] { "Probe", "0" })
          && waiting.State == OperationState.Running,
        "等不到结果：回 wait_timeout，动作照样在跑");
    waiting.Succeed();
    servicePort.Tick();
    Check(waiting.Wait(0) && servicePort.State == ModuleState.Idle, "等待超时不耽误动作之后做完");

    // Unload（手动）、复位、中止：发起了照样等终态
    servicePort.NoteState(LoadPortState.Loaded);
    response = await Run(portService.UnloadAsync, probe => probe.Succeed());
    Check(response.Success && servicePort.State == ModuleState.Idle, "手动 Unload：Loaded 能发，做成落 Idle");
    response = await Run(portService.ResetAsync, probe => probe.Succeed());
    Check(response.Success, "复位：空闲能发，做成回成功");
    response = await Run(portService.AbortAsync, probe => probe.Succeed());
    Check(response.Success, "中止：做成回成功");

    // 中止不看连没连上：没连上也能把在途的动作顶掉，不让它一直等到超时
    servicePort.NoteState(ModuleState.Idle);
    var inflight = new ProbeOperation();
    servicePort.Next = inflight;
    Check(servicePort.Home().IsSuccess, "Home 发起、在途");
    servicePort.Shell.Close();
    var homeOffline = servicePort.Home();
    Check(!homeOffline.IsSuccess && homeOffline.ErrorMessage == ErrorCodes.ModuleNotConnected, "没连上：Home 当场被拒（回设备没连上）");
    var abortOperation = new ProbeOperation();
    servicePort.Next = abortOperation;
    Check(ReferenceEquals(servicePort.Abort(), abortOperation) && inflight.State == OperationState.Aborted,
        "没连上也能中止：顶掉在途的 Home");
}

// Load 入口：按 状态 → 资源 → 互锁 查，被拒照原样回入口给的错误码 + 参数（状态码由界面翻成状态字），端口不动；
// 三项都过才发起。机型（这里是探针端口）只重写设备动作 CreateLoadOperation，三项检查照样过。
{
    var loadPort = new ProbePort("ServiceLoadPort");
    Check(loadPort.InitComponent(), "Load 服务用的端口的组件初始化应成功");
    ComponentLoader.BindAll([loadPort]);
    SmokeIoc.Tree(loadPort);
    var loadService = new LoadPortService();
    var loadResponse = await loadService.LoadAsync("missing");
    Check(!loadResponse.Success && loadResponse.Code == ErrorCodes.ModuleNotFound, "Load：没有这个模块");

    loadResponse = await loadService.LoadAsync(loadPort.Name);
    Check(!loadResponse.Success && loadResponse.Code == ErrorCodes.ModuleStateNotAllowed
          && loadResponse.Args.SequenceEqual(new[] { loadPort.Name, ModuleState.NotInit.ToString() })
          && loadPort.State == ModuleState.NotInit && loadPort.Calls == 0,
        $"Load：没初始化，回状态不允许（模块名 + 状态码），不建设备动作，实际 {loadResponse.Code}");

    loadPort.NoteState(ModuleState.Idle);
    loadResponse = await loadService.LoadAsync(loadPort.Name);
    Check(!loadResponse.Success && loadResponse.Code == ErrorCodes.CarrierNotArrived
          && loadResponse.Args.SequenceEqual(new[] { loadPort.Name }) && loadPort.State == ModuleState.Idle,
        $"Load：端口上没载具，回联锁的原因，实际 {loadResponse.Code}");

    loadPort.NotePodPlaced(true);
    loadPort.Tick();
    loadPort.Next = new ProbeOperation();
    loadResponse = await loadService.LoadAsync(loadPort.Name);
    Check(!loadResponse.Success && loadResponse.Code == ErrorCodes.WaitTimeout && loadPort.State == LoadPortState.Loading
          && ReferenceEquals(loadPort.CurrentOperation, loadPort.Next),
        "Load：三项都过就发起、进 Loading（探针端口等结果的时间是 0，回等待超时，动作照样在跑）");
    loadPort.Next.Succeed();
    loadPort.Tick();
    Check(loadPort.State == LoadPortState.Loaded, "Load 做成落 Loaded");

    loadResponse = await loadService.LoadAsync(loadPort.Name);
    Check(!loadResponse.Success && loadResponse.Code == ErrorCodes.ModuleStateNotAllowed
          && loadResponse.Args.SequenceEqual(new[] { loadPort.Name, LoadPortState.Loaded.ToString() }),
        "已经 Load 好了再 Load：回状态不允许，带当前状态码");

    loadPort.Shell.Close();
    loadPort.NoteState(ModuleState.Idle);
    loadResponse = await loadService.LoadAsync(loadPort.Name);
    Check(!loadResponse.Success && loadResponse.Code == ErrorCodes.ModuleNotConnected && loadPort.State == ModuleState.Idle,
        $"驱动没连上：Load 当场被拒（不发出去再落 Error、报警），实际 {loadResponse.Code}");
}

// Online/Offline 只改模块模式 Mode，Auto/Manual 只改 LoadPort 的 Access Mode：
// 都不经设备协议，置位即成功，不产生操作、不等待，而且互不影响。
SmokeIoc.Tree(port);
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

// 机械手上线 / 下线：跟 LoadPort、腔体一样走服务基类 → 模块基类的 Online / Offline，只改 Mode；
// 按模块类型找，机械手的服务拉不动同名的 LoadPort。
{
    var modeRobot = new ProbeRobot();
    SmokeIoc.Tree(modeRobot, port);
    var robotService = new RobotService();
    var robotMode = await robotService.OnlineAsync("missing");
    Check(!robotMode.Success && robotMode.Code == ErrorCodes.ModuleNotFound, "机械手上线：找不到模块要回 module.not_found");
    robotMode = await robotService.OnlineAsync(port.Name);
    Check(!robotMode.Success && robotMode.Code == ErrorCodes.ModuleNotFound && port.Mode == ModuleMode.Offline,
        "机械手的服务只找机械手，不能把 LoadPort 拉上线");
    Check(modeRobot.Mode == ModuleMode.Offline, "机械手开机是下线");
    robotMode = await robotService.OnlineAsync(modeRobot.Name);
    Check(robotMode.Success && modeRobot.Mode == ModuleMode.Online && modeRobot.CreateStateDto().Mode == ModuleMode.Online,
        "机械手上线：Mode 变 Online，状态推送里带上");
    robotMode = await robotService.OfflineAsync(modeRobot.Name);
    Check(robotMode.Success && modeRobot.Mode == ModuleMode.Offline && modeRobot.CreateStateDto().Mode == ModuleMode.Offline,
        "机械手下线：Mode 变 Offline，状态推送里带上");
}

// EAP 口子：回调走 EAP 的派发组件（宿主里是 sc.xml Eap 下的 Notifier）；本工具不跑扫描循环，正好证明派发不依赖扫描。
SmokeIoc.Use<IEapNotifierComponent>(new EapNotifierComponent());
port.Bind(); // 端口开机 Bind 时取 EAP 的派发组件
var eap = new RecordingE87Callback();
port.E87Callback = eap;
port.SetAutoMode(true);
Check(eap.Wait(nameof(IE87Callback.AutoModeChanged)), "EAP callbacks must be delivered without a scan loop.");
port.SetAutoMode(false);

// 载具 ID 与 Mapping 结果要能进状态快照（DTO），否则出不了服务进程。
port.NoteMap([SlotState.Normal, SlotState.Empty, SlotState.Crossed]);
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
port.NoteMap([SlotState.Empty, SlotState.Empty, SlotState.Crossed]);
Check(port.CreateStateDto().HasStateChanged(snapshot), "A slot map change must count as a change.");

// ── 载具对象：放上到取走这一程 ──────────────────────────────────────────
// 载具 ID 是空的不等于没载具——这就是要有载具对象的原因。
Check(port._carrier.Info is null, "还没放 FOUP 时不该有载具对象");
port._carrier.SetId("GHOST");
Check(port._carrier.Info is null, "没有载具时改 ID 不该凭空造出一个载具对象");

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
port.NoteMap([SlotState.Normal, SlotState.Empty, SlotState.Crossed, SlotState.Undefined]);
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

// 机械手进站要载具在、Host 认可了槽图（CanPrepareTransfer）：接了 EAP、槽图还没被 Host 认定，Loaded 了也不让进（手动传片不经过 Job，也靠这一关挡住）
var portTransferContext = TransferContext.ForPick("WaitSmokeRobot", 1, 1);
Check(!port.CanPrepareTransfer && port.PrepareTransfer(portTransferContext) is null && port.State == LoadPortState.Loaded,
    "接了 EAP、槽图没认定：机械手不能进站");
port._carrier.UpdateStatus(null, CarrierSlotMapStatus.Verified);
Check(port.CanPrepareTransfer && port.PrepareTransfer(portTransferContext) is not null && port.CancelTransfer(portTransferContext) && port.State == LoadPortState.Loaded,
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
// Load 好的这一拍，载具已经判干完：EC AutoUnload（默认开）当场走 Unload 入口发起卸载，用的就是摆好的 lastUnload
var lastUnload = new ProbeOperation();
port.Next = lastUnload;
port.Tick();
Check(ReferenceEquals(port.CurrentOperation, lastUnload) && port.State == LoadPortState.Unloading,
    "干完的载具 Load 好了：自动 Unload 走入口发起，进 Unloading");
lastUnload.Succeed();
port.Tick();
port.Next = null;
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
port.NoteMap([SlotState.Normal, SlotState.Empty, SlotState.Normal]);
Check(port._carrier.Info!.SlotMapStatus == CarrierSlotMapStatus.Verified, "已认定的槽图再 Map 一次，认定状态保持（不回 Read）");
Check(port._carrier.SlotMap.Count == 3 && port._carrier.SlotMap[2] == SlotState.Normal, "再 Map 一次，槽图照常更新");
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

SmokeIoc.Use<ILedgerManager>(null);
port.E87Callback = null;

// ── 机械手取放写晶圆账 ──────────────────────────────────────────────────
{
    var robotLedger = new LedgerManagerComponent();
    SmokeIoc.Use<ILedgerManager>(robotLedger);
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
    ComponentLoader.BindAll([robot]);
    Check(robotLedger.GetSlots(robot.Name).Count == robot.Arms.Count,
        $"开机应把手指注册成账本槽位，实际 {robotLedger.GetSlots(robot.Name).Count} 个");
    Check(robot.Arms.Count == 1 && robot.Axes.Count == 4 && robot.Arms[0].Number == 1
          && !robot.Arms[0].IsWaferCheckEnabled,
        "轴/手指从组件树收出来；没有配置 DI 的默认手指不启用持片检查");

    robot.NoteState(ModuleState.Idle);

    // 入口按 状态 → 资源 → 互锁 查，被拒带原因（错误码 + 参数）、不起操作：资源这一步查站点、手指和晶圆账
    bool RejectedWith(HandleResult<ModuleOperation> started, string code, params string[] args)
    {
        return !started.IsSuccess && started.Result is null && started.ErrorMessage == code && started.Args.SequenceEqual(args)
            && robot.State == ModuleState.Idle;
    }

    Check(RejectedWith(robot.Pick(1, "NoSuchStation", 1), ErrorCodes.StationNotFound, "SmokeRobot", "NoSuchStation"),
        "没配在站点表里的站点：robot.station_not_found [机械手, 站点]");
    Check(RejectedWith(robot.Pick(2, "SmokeLP", 3), ErrorCodes.ArmNotAllowed, "SmokeRobot", "SmokeLP", "2"),
        "没有这只手（只配了 Arm1）：robot.arm_not_allowed [机械手, 站点, 手指号]");
    Check(RejectedWith(robot.Pick(1, "SmokeLP", 2), ErrorCodes.WaferNoWafer, "SmokeLP", "2"),
        "账上源槽没片：不去空槽取，wafer.no_wafer [站点, 槽号]（以前设备做完才发现账移不动）");
    Check(RejectedWith(robot.Place(1, "SmokeLP", 4), ErrorCodes.RobotArmNoWafer, "SmokeRobot", "1"),
        "账上手上没片：放不了，robot.arm_no_wafer [机械手, 手指号]");
    Check(robot.CheckPick(1, "SmokeLP", 3) is null && robot.CheckPlace(1, "SmokeLP", 4) is not null,
        "只查不动的 CheckPick / CheckPlace 跟入口查的是同一套");
    Check(!typeof(ITransferRobotStep).IsAssignableFrom(typeof(BaseRobotModule))
          && RejectedWith(robot.ExtendPick(1, "SmokeLP", 3), ErrorCodes.RobotHandoffUnsupported, "SmokeRobot")
          && RejectedWith(robot.RetractPick(1, "SmokeLP", 3), ErrorCodes.ModuleStateNotAllowed, "SmokeRobot", ModuleState.Idle.ToString()),
        "分段取放是可选能力：机械手基类不自带（类上声明 ITransferRobotStep 才算）；没给分段动作的伸臂当场拒（robot.handoff_unsupported），没伸着不收缩臂");

    // Pick 成功：片从花篮槽位挪到手指上
    robot.Next = new ProbeOperation();
    var beforePick = robot.CreateStateDto();
    Check(!beforePick.Arms[0].IsExtended, "没在取放时手指是缩着的");
    var pickOperation = robot.Pick(1, "SmokeLP", 3).Result;
    Check(pickOperation is not null && ReferenceEquals(robot.CurrentOperation, pickOperation)
          && !ReferenceEquals(pickOperation, robot.Next), "Idle 状态发起包含设备动作和记账的完整 Pick");
    var picking = robot.CreateStateDto();
    Check(picking.Arms[0].IsExtended && picking.HasStateChanged(beforePick), "发出取片指令：Arm1 伸出，推送算变化（界面画伸出）");
    robot.Next!.Succeed();
    Check(pickOperation is not null && !pickOperation.IsTerminal && !pickOperation.IsSettled
          && robotLedger.Get("SmokeLP", 3)?.Id == carried.Id,
        "设备动作刚成功但尚未记账：外层取片仍在执行，不提前唤醒等待方");
    robot.Tick();
    Check(pickOperation is not null && pickOperation.IsSuccess && pickOperation.IsSettled,
        "取片记账完成且模块收尾后，才向等待方确认成功");
    Check(!robot.CreateStateDto().Arms[0].IsExtended, "取片做完：Arm1 缩回");
    Check(robotLedger.Get("SmokeLP", 3) is null, "Pick 成功后原槽位应变空");
    Check(robotLedger.Get(robot.Name, 1)?.Id == carried.Id,
        "Pick 成功后片应在手指上，且还是同一片");

    // 状态推送带着账：手指上的片（片号、状态）跟着账走，界面不用另外通知
    var pickedState = robot.CreateStateDto();
    Check(pickedState.LedgerSlots.Count == robot.Arms.Count
          && pickedState.LedgerSlots[0].Slot == 1 && pickedState.LedgerSlots[0].Wafer?.WaferId == carried.WaferId
          && pickedState.LedgerSlots[0].Wafer?.ProcessState == "Idle",
        "机械手状态推送应带上账上手指的片");
    var beforeLedgerChange = robot.CreateStateDto();
    robotLedger.SetWaferId(robot.Name, 1, "W-RENAMED");
    Check(robot.CreateStateDto().HasStateChanged(beforeLedgerChange), "账一变（改片号）状态推送就该算变化，下一拍推给界面");
    robotLedger.SetWaferId(robot.Name, 1, carried.WaferId);

    // 手上有片：不能再取（拿着片去取会碰片）；往有片的槽上放也不行（会叠片）
    robotLedger.Create("SmokeLP", 4, WaferStatus.Normal, "FOUP-ROBOT");
    var occupiedBy = robotLedger.Get("SmokeLP", 4)!.WaferId;
    Check(RejectedWith(robot.Pick(1, "SmokeLP", 4), ErrorCodes.RobotArmHasWafer, "SmokeRobot", "1", carried.WaferId),
        "账上手上已有片：robot.arm_has_wafer [机械手, 手指号, 片号]");
    Check(RejectedWith(robot.Place(1, "SmokeLP", 4), ErrorCodes.WaferSlotOccupied, "SmokeLP", "4", occupiedBy),
        "账上目标槽有片：wafer.slot_occupied [站点, 槽号, 片号]");
    robotLedger.Delete("SmokeLP", 4);

    // Place 成功：片从手指放回另一个槽位
    robot.Next = new ProbeOperation();
    var placeOperation = robot.Place(1, "SmokeLP", 5).Result;
    Check(placeOperation is not null && ReferenceEquals(robot.CurrentOperation, placeOperation), "应能发起完整 Place");
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
    Check(!robot.CreateStateDto().Arms[0].IsExtended, "取片失败也缩回：指令做完了，手不该一直画成伸着");
    Check(robotLedger.Get("SmokeLP", 5)?.Id == carried.Id, "Pick 失败不该把片从账上挪走");
    Check(robotLedger.Get(robot.Name, 1) is null, "Pick 失败不该把片记到手指上");

    // 非取放动作终结时不碰账
    robot.NoteState(ModuleState.Idle);
    robot.Next = new ProbeOperation();
    robot.Home();
    robot.Next!.Succeed();
    robot.Tick();
    Check(robotLedger.Get("SmokeLP", 5)?.Id == carried.Id, "Home 不该动账");

    // 换片平台默认不支持：直接被拒，不起操作、不改状态、不动账
    robot.NoteState(ModuleState.Idle);
    Check(robot.Swap(1, 2, "SmokeLP", 5) is null && robot.State == ModuleState.Idle
          && robotLedger.Get("SmokeLP", 5)?.Id == carried.Id,
        "机械手默认不支持换片：Swap 返回 null，状态和账都不动");

    // 设备取片期间来源账被换成另一片（即使业务片号相同），不能误移新片的账。
    robot.Next = new ProbeOperation();
    var replacedPick = robot.Pick(1, "SmokeLP", 5).Result;
    Check(replacedPick is not null && robotLedger.Move("SmokeLP", 5, "SmokeLP", 4), "取片启动后模拟原来源账被移走");
    var pickReplacement = robotLedger.Create("SmokeLP", 5)!;
    robotLedger.SetWaferId("SmokeLP", 5, carried.WaferId);
    robot.Next!.Succeed();
    robot.Tick();
    Check(replacedPick is not null && replacedPick.IsSettled && replacedPick.Code == ErrorCodes.RobotLedgerMoveFailed
          && replacedPick.ErrorArgs.SequenceEqual([robot.Name, "SmokeLP", "5", robot.Name, "1"])
          && robot.State == ModuleState.Error && robot.Next.IsSuccess,
        "设备取片成功但来源已换片：整次操作记账失败并落 Error");
    Check(robotLedger.Get("SmokeLP", 5)?.Id == pickReplacement.Id && robotLedger.Get("SmokeLP", 4)?.Id == carried.Id
          && robotLedger.Get(robot.Name, 1) is null,
        "同号替换晶圆保留在来源，原晶圆账也保留，不把另一片记到机械手上");
    robot.Tick();
    Check(robot.CurrentOperation is null && robotLedger.Get("SmokeLP", 5)?.Id == pickReplacement.Id,
        "来源身份不符后不自动重试设备动作或记账");
    robotLedger.Delete("SmokeLP", 5);
    Check(robotLedger.Move("SmokeLP", 4, "SmokeLP", 5), "人工恢复取片用例的原晶圆账");
    robot.NoteState(ModuleState.Idle);

    // 模拟动作发出后账发生冲突：设备成功也不能掩盖记账失败，不重发动作、不覆盖目标片。
    robot.Next = new ProbeOperation();
    var ledgerFailedPick = robot.Pick(1, "SmokeLP", 5).Result;
    var armBlocker = robotLedger.Create(robot.Name, 1, WaferStatus.Normal, "FOUP-BLOCK-PICK")!;
    robot.Next!.Succeed();
    robot.Tick();
    Check(ledgerFailedPick is not null && ledgerFailedPick.IsSettled && !ledgerFailedPick.IsSuccess
          && ledgerFailedPick.Code == ErrorCodes.RobotLedgerMoveFailed && robot.State == ModuleState.Error
          && ledgerFailedPick.ErrorArgs.SequenceEqual([robot.Name, "SmokeLP", "5", robot.Name, "1"]),
        "设备取片成功但手指账被占用：完整操作失败，Robot 落 Error，错误带源和目标位置");
    Check(robotLedger.Get("SmokeLP", 5)?.Id == carried.Id && robotLedger.Get(robot.Name, 1)?.Id == armBlocker.Id,
        "记账失败保留原账，不覆盖占位晶圆，也不伪造成功片位");
    robot.Tick();
    Check(robotLedger.Get("SmokeLP", 5)?.Id == carried.Id && robot.CurrentOperation is null,
        "失败后下一拍不自动重试设备动作或记账");

    robotLedger.Delete(robot.Name, 1);
    robot.NoteState(ModuleState.Idle);
    robot.Next = new ProbeOperation();
    var recoveredPick = robot.Pick(1, "SmokeLP", 5).Result;
    robot.Next!.Succeed();
    robot.Tick();
    Check(recoveredPick?.IsSuccess == true && robotLedger.Get(robot.Name, 1)?.Id == carried.Id,
        "核对并清理账后，由调用方重新发起取片可以正常完成");

    robot.Next = new ProbeOperation();
    var replacedPlace = robot.Place(1, "SmokeLP", 3).Result;
    Check(replacedPlace is not null && robotLedger.Move(robot.Name, 1, "SmokeLP", 4), "放片启动后模拟手指来源账被移走");
    var placeReplacement = robotLedger.Create(robot.Name, 1)!;
    robotLedger.SetWaferId(robot.Name, 1, carried.WaferId);
    robot.Next!.Succeed();
    robot.Tick();
    Check(replacedPlace is not null && replacedPlace.Code == ErrorCodes.RobotLedgerMoveFailed
          && replacedPlace.ErrorArgs.SequenceEqual([robot.Name, robot.Name, "1", "SmokeLP", "3"])
          && robot.State == ModuleState.Error && robotLedger.Get(robot.Name, 1)?.Id == placeReplacement.Id
          && robotLedger.Get("SmokeLP", 4)?.Id == carried.Id && robotLedger.Get("SmokeLP", 3) is null,
        "放片完成时手指账被换片：不误移替换晶圆，不伪造目标片位");
    robotLedger.Delete(robot.Name, 1);
    Check(robotLedger.Move("SmokeLP", 4, robot.Name, 1), "人工恢复放片用例的原晶圆账");
    robot.NoteState(ModuleState.Idle);
    robot.Next = new ProbeOperation();
    var ledgerFailedPlace = robot.Place(1, "SmokeLP", 3).Result;
    var slotBlocker = robotLedger.Create("SmokeLP", 3, WaferStatus.Normal, "FOUP-BLOCK-PLACE")!;
    robot.Next!.Succeed();
    robot.Tick();
    Check(ledgerFailedPlace is not null && ledgerFailedPlace.IsSettled && !ledgerFailedPlace.IsSuccess
          && ledgerFailedPlace.Code == ErrorCodes.RobotLedgerMoveFailed && robot.State == ModuleState.Error
          && ledgerFailedPlace.ErrorArgs.SequenceEqual([robot.Name, robot.Name, "1", "SmokeLP", "3"]),
        "设备放片成功但目标账被占用：完整操作失败，错误按放片方向携带源和目标位置");
    Check(robotLedger.Get(robot.Name, 1)?.Id == carried.Id && robotLedger.Get("SmokeLP", 3)?.Id == slotBlocker.Id,
        "放片记账失败不删除手指上的原账，也不覆盖目标片");

    robot.NoteState(ModuleState.Idle);
    robot.Next = new ProbeOperation();
    var missingSource = robot.Place(1, "SmokeLP", 2).Result;
    robotLedger.Delete(robot.Name, 1);
    robot.Next!.Succeed();
    robot.Tick();
    Check(missingSource is not null && missingSource.Code == ErrorCodes.RobotLedgerMoveFailed
          && robot.State == ModuleState.Error && robotLedger.Get("SmokeLP", 2) is null,
        "设备动作完成时源账已丢失：报告记账失败，不凭空给目标补片");

    // 驱动没连上：动作不发（发出去也到不了设备，只会落 Error），module.not_connected；急停不看连接，照样能把在途的顶掉
    robot.NoteState(ModuleState.Idle);
    robot.Close();
    Check(RejectedWith(robot.Home(), ErrorCodes.ModuleNotConnected, "SmokeRobot"), "驱动没连上：Home 被拒，module.not_connected [机械手]");
    robot.Next = new ProbeOperation();
    Check(robot.Abort() == robot.Next, "驱动没连上照样能急停");

    SmokeIoc.Use<ILedgerManager>(null);
}

// ── 机械手手指在位传感器：Arm 下挂了 WaferSensor（DI）的手，取放前后、Home 前跟晶圆账对；没挂的手不查（上面那段） ──────
{
    var sensorLedger = new LedgerManagerComponent();
    SmokeIoc.Use<ILedgerManager>(sensorLedger);
    var robot = new ProbeRobot();
    var sensor = new ProbeDi("SmokeRobot.Arm1.WaferSensor") { AlarmEnabled = false, DebounceMs = 0 };
    var arm1 = robot.FindChild<RobotArmComponent>("Arm1")!;
    arm1.AddChild(sensor);
    robot.NoteSettings(new ModuleConfig
    {
        Name = "SmokeRobot",
        Children =
        [
            new ModuleConfig
            {
                Name = "Stations",
                Children = [new ModuleConfig { Name = "SmokeLP", Values = [new ValueConfig { Name = "Number", Value = "1" }] }],
            },
        ],
    });
    sensorLedger.RegisterLocation("SmokeLP", 5);
    var wafer = sensorLedger.Create("SmokeLP", 3, WaferStatus.Normal, "FOUP-SENSOR")!;
    Check(robot.InitComponent() && ReferenceEquals(robot.Arms[0].WaferSensor, sensor),
        "Arm1 下面名叫 WaferSensor 的 DI 按 IDiSensor 认成这只手的在位传感器");
    ComponentLoader.BindAll([robot]);
    Check(!robot.Arms[0].LedgerHasWafer, "取片前账上 Arm1 空着：LedgerHasWafer 为 false");
    robot.NoteState(ModuleState.Idle);
    arm1.WaferSensorTimeoutMs = 1000;

    bool SensorRejected(HandleResult<ModuleOperation> started, string code, params string[] args)
    {
        return !started.IsSuccess && started.Result is null && started.ErrorMessage == code && started.Args.SequenceEqual(args)
            && robot.State == ModuleState.Idle;
    }

    // 传感器还没读到：取放不敢伸手；Home 不碰片，不拦
    robot.Tick();
    Check(robot.Arms[0].HasWafer is null && robot.Arms[0].IsWaferCheckEnabled
          && SensorRejected(robot.Pick(1, "SmokeLP", 3), ErrorCodes.RobotArmSensorUnknown, "SmokeRobot", "1"),
        "传感器还没读到：取片被拒，robot.arm_sensor_unknown [机械手, 手指号]");
    robot.Next = new ProbeOperation();
    Check(robot.Home().Result == robot.Next, "传感器还没读到不拦 Home");
    robot.Next!.Succeed();
    robot.Tick();

    // 取片前、Home 前：传感器看到手上有片，账上手是空的
    sensor.Level = true;
    robot.Tick();
    Check(robot.Arms[0].HasWafer == true && robot.CreateStateDto().Arms[0].HasWafer,
        "挂了传感器的手，在位按传感器读（推送给界面的也是它）");
    Check(SensorRejected(robot.Pick(1, "SmokeLP", 3), ErrorCodes.RobotArmSensorHasWafer, "SmokeRobot", "1"),
        "取片前传感器看到手上有片、账上没有：robot.arm_sensor_has_wafer [机械手, 手指号]");
    Check(robot.CheckPick(1, "SmokeLP", 3)?.ErrorMessage == ErrorCodes.RobotArmSensorHasWafer, "只查不动的 CheckPick 也查传感器（搬运管理落标记前先问它）");
    Check(SensorRejected(robot.Home(), ErrorCodes.RobotArmSensorHasWafer, "SmokeRobot", "1"), "Home 前账实不符也拦：先按实物改账");

    // 取片：设备做完了还要等传感器看到片，才算做成、才记账
    sensor.Level = false;
    robot.Tick();
    robot.Next = new ProbeOperation();
    var picking = robot.Pick(1, "SmokeLP", 3).Result;
    Check(picking is not null && !ReferenceEquals(picking, robot.Next) && robot.State == RobotState.Picking,
        "挂了传感器的手：取片外面套一层等传感器的操作");
    robot.Next!.Succeed();
    robot.Tick();
    Check(picking is not null && !picking.IsTerminal && sensorLedger.Get("SmokeLP", 3)?.Id == wafer.Id,
        "设备说取完了、传感器还没看到片：不算做成、不记账");
    sensor.Level = true;
    robot.Tick();
    Check(picking is not null && picking.IsSuccess && robot.State == ModuleState.Idle && sensorLedger.Get(robot.Name, 1)?.Id == wafer.Id,
        "传感器跟上（有片）：取片做成，片记到 Arm1");
    Check(robot.Arms[0].LedgerHasWafer, "取片记账以后手指的 LedgerHasWafer 跟着账变成有片");
    sensorLedger.IsEnable = false;
    Check(!robot.Arms[0].LedgerHasWafer, "账没开：LedgerHasWafer 为 false");
    sensorLedger.IsEnable = true;

    // 放片前、Home 前：账上手上有片，传感器没看到
    sensor.Level = false;
    robot.Tick();
    Check(SensorRejected(robot.Place(1, "SmokeLP", 4), ErrorCodes.RobotArmSensorNoWafer, "SmokeRobot", "1"),
        "放片前传感器没看到片、账上有：robot.arm_sensor_no_wafer [机械手, 手指号]");
    Check(SensorRejected(robot.Home(), ErrorCodes.RobotArmSensorNoWafer, "SmokeRobot", "1"), "Home 前账上有片、传感器没看到：拦");

    // 放片做完、时限到了传感器还看到片：判失败、落 Error、不记账（片可能还挂在手上）
    sensor.Level = true;
    robot.Tick();
    arm1.WaferSensorTimeoutMs = 0;
    robot.Next = new ProbeOperation();
    var placing = robot.Place(1, "SmokeLP", 4).Result;
    robot.Next!.Succeed();
    robot.Tick();
    Check(placing is not null && !placing.IsSuccess && placing.Code == ErrorCodes.RobotArmSensorHasWafer
          && placing.ErrorArgs.SequenceEqual(["SmokeRobot", "1"]) && robot.State == ModuleState.Error,
        "放完传感器还看到片：时限到判失败 robot.arm_sensor_has_wafer，机械手落 Error");
    Check(sensorLedger.Get(robot.Name, 1)?.Id == wafer.Id && sensorLedger.Get("SmokeLP", 4) is null, "放片没做成不记账：片还记在手上");

    // 设备动作自己失败：原样带它的错误码，不等传感器
    robot.NoteState(ModuleState.Idle);
    robot.Next = new ProbeOperation();
    var failing = robot.Place(1, "SmokeLP", 4).Result;
    robot.Next!.Reject();
    robot.Tick();
    Check(failing is not null && failing.Code == ErrorCodes.DeviceFailed && failing.ErrorArgs.SequenceEqual(["Probe", "device error"]),
        "设备动作失败：原样带设备的错误码和参数");

    // 急停顶掉：等传感器那层和设备动作一起打断
    robot.NoteState(ModuleState.Idle);
    var motion = new ProbeOperation();
    robot.Next = motion;
    var abortedPlace = robot.Place(1, "SmokeLP", 4).Result;
    robot.Next = new ProbeOperation();
    robot.Abort();
    Check(abortedPlace is not null && abortedPlace.State == OperationState.Aborted && motion.State == OperationState.Aborted && motion.AbortCount == 1,
        "急停顶掉放片：外层和设备动作都落 Aborted");
    robot.Next!.Succeed();
    robot.Tick();

    SmokeIoc.Use<ILedgerManager>(null);
}

// ── 非 DI 持片反馈：手指组件重写统一契约后，公共 Home / 取放检查与完成确认不再依赖 WaferSensor 对象 ──────
{
    var feedbackLedger = new LedgerManagerComponent();
    SmokeIoc.Use<ILedgerManager>(feedbackLedger);
    var arm = new ProbeFeedbackArm { Number = 1 };
    var robot = new ProbeRobot(arm);
    robot.NoteSettings(new ModuleConfig
    {
        Name = "SmokeRobot",
        Children =
        [
            new ModuleConfig
            {
                Name = "Stations",
                Children = [new ModuleConfig { Name = "SmokeLP", Values = [new ValueConfig { Name = "Number", Value = "1" }] }],
            },
        ],
    });
    feedbackLedger.RegisterLocation("SmokeLP", 5);
    var wafer = feedbackLedger.Create("SmokeLP", 3, WaferStatus.Normal, "FOUP-FEEDBACK")!;
    Check(robot.InitComponent() && arm.WaferSensor is null && arm.IsWaferCheckEnabled,
        "非 DI 手指组件可以独立启用持片检查");
    ComponentLoader.BindAll([robot]);
    robot.NoteState(ModuleState.Idle);
    arm.WaferSensorTimeoutMs = 1000;

    Check(robot.CheckPick(1, "SmokeLP", 3)?.ErrorMessage == ErrorCodes.RobotArmSensorUnknown
          && !robot.Pick(1, "SmokeLP", 3).IsSuccess && robot.CurrentOperation is null,
        "非 DI 反馈未知时取片被拒，启用状态不随未知读数关闭");
    arm.Feedback = true;
    Check(robot.CheckPick(1, "SmokeLP", 3)?.ErrorMessage == ErrorCodes.RobotArmSensorHasWafer
          && robot.Home().ErrorMessage == ErrorCodes.RobotArmSensorHasWafer,
        "非 DI 反馈有片但账上为空：取片和 Home 都检查同一个 HasWafer");

    arm.Feedback = false;
    robot.Next = new ProbeOperation();
    var pick = robot.Pick(1, "SmokeLP", 3).Result;
    Check(pick is not null && robot.State == RobotState.Picking, "非 DI 反馈为空时允许取片");
    robot.Next!.Succeed();
    robot.Tick();
    Check(pick is not null && !pick.IsTerminal && feedbackLedger.Get("SmokeLP", 3)?.Id == wafer.Id,
        "非 DI 持片反馈未到：设备取片成功也继续等待，不提前记账");
    arm.Feedback = true;
    robot.Tick();
    Check(pick?.IsSuccess == true && feedbackLedger.Get(robot.Name, 1)?.Id == wafer.Id
          && robot.CreateStateDto().Arms[0].HasWafer,
        "非 DI 持片反馈到位后才完成取片、记账并更新界面反馈");

    arm.Feedback = false;
    Check(robot.CheckPlace(1, "SmokeLP", 4)?.ErrorMessage == ErrorCodes.RobotArmSensorNoWafer,
        "非 DI 反馈没片时，放片前检查同样拒绝");
    arm.Feedback = true;
    robot.Next = new ProbeOperation();
    var place = robot.Place(1, "SmokeLP", 4).Result;
    Check(place is not null, "非 DI 反馈有片时允许放片");
    robot.Next!.Succeed();
    robot.Tick();
    Check(place is not null && !place.IsTerminal && feedbackLedger.Get(robot.Name, 1)?.Id == wafer.Id,
        "非 DI 放片后仍有片：等待反馈，不提前把账移到目标");
    arm.Feedback = false;
    robot.Tick();
    Check(place?.IsSuccess == true && feedbackLedger.Get("SmokeLP", 4)?.Id == wafer.Id,
        "非 DI 放片后反馈为空，才完成放片并记账");

    arm.WaferSensorTimeoutMs = 0;
    robot.Next = new ProbeOperation();
    var unknownPick = robot.Pick(1, "SmokeLP", 4).Result;
    Check(unknownPick is not null, "反馈丢失用例：取片前反馈有效，动作已启动");
    arm.Feedback = null;
    robot.Next!.Succeed();
    robot.Tick();
    Check(unknownPick?.Code == ErrorCodes.RobotArmSensorUnknown && robot.State == ModuleState.Error
          && arm.IsWaferCheckEnabled && feedbackLedger.Get("SmokeLP", 4)?.Id == wafer.Id,
        "动作后非 DI 反馈失效仍按启用检查处理，超时报错，不跳过确认或提交晶圆账");

    SmokeIoc.Use<ILedgerManager>(null);
}

// ── 报警：LoadPort / _robot 出故障要报出来；报出去以后只能人工 Reset 清，源头恢复、动作成功都不清 ──────
{
    var alarms = new AlarmComponent();
    SmokeIoc.Use<IAlarm>(alarms);
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

    // 报警复位走到端口的 Reset()，发了设备复位（探针动作下一拍做完）：出错复位完落 NotInit，要再 Home
    port.Tick();
    Check(port.State == ModuleState.NotInit && port.CurrentOperation is null, "报警复位带的设备复位做完：出错复位完是 NotInit");

    // Home 超时 → 只报初始化超时，不再另报动作失败（一次失败一条报警）
    var slowHome = new ProbeOperation();
    Check(port.BeginAction(LoadPortAction.Home, slowHome) is not null, "NotInit 状态应允许 Home");
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
    port.Tick();

    // 每个动作超时都报自己那一条（跟 EC 里各动作的超时一一对应），不另报动作失败
    // （报警复位会走到端口的 Reset()、发设备复位，每次复位完推一拍让它做完）
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
        port.Tick();
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
    ComponentLoader.BindAll([robot]);
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

    // 界面按来源复位：机械手复位（2026-10-12 起）走入口、要挂一个设备复位操作，这里摆一个不收尾的，只看报警
    robot.Next = new ProbeOperation();
    Check(alarms.Reset(robot.FullPath) && !Active(robot, robot.ControlledStopAlarm), "人工复位才清");

    SmokeIoc.Use<IAlarm>(null);
}

// ── E84：按 CTC 时序走一遍送盒、取盒，闸门（没开/Manual/下线）、中途打断、超时锁住与人工恢复 ─────
{
    var alarms = new AlarmComponent();
    SmokeIoc.Use<IAlarm>(alarms);

    // 送盒/取盒整程：计时给足，不让超时掺和
    var lp = new ProbePort("E84SmokePort");
    var e84 = new ProbeE84(lp.Name, timeoutMs: 60000);
    lp.AddChild(e84);
    var events = new RecordingE84Callback();
    lp.E84Callback = events;
    Check(ReferenceEquals(lp.E84, e84), "LoadPort 应能按接口找到 E84 子组件");
    Check(lp.InitComponent(), "带 E84 的端口的组件初始化应成功");
    ComponentLoader.BindAll([lp]);
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
    var handoffLoad = lp.Load();
    Check(!handoffLoad.IsSuccess && handoffLoad.ErrorMessage == ErrorCodes.E84HandoffRunning
          && handoffLoad.Args.SequenceEqual(new[] { lp.Name }) && lp.State == ModuleState.Idle,
        $"载具放上了、搬运车交接还没走完：Load 被拒（loadport.handoff_running），端口不动，实际 {handoffLoad.ErrorMessage}");
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
    Check(!Active(e84, e84.UnexpectedCarrierAlarm, alarms), "正常送盒交接：载具放上不算意外");

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
    Check(!Active(e84, e84.UnexpectedCarrierAlarm, alarms), "正常取盒交接：载具拿走不算意外");

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

    // Auto 下没走交接就放上、拿走载具（人手动放拿）：报"意外放上 / 拿走载具"，交接不锁；Manual 下不管
    lp.NotePodPlaced(true);
    lp.Tick();
    Check(Active(e84, e84.UnexpectedCarrierAlarm, alarms) && e84.State == E84State.Available,
        "Auto 下没走交接放上载具：报意外放上，交接不锁");
    Check(alarms.Reset(e84.FullPath) && !Active(e84, e84.UnexpectedCarrierAlarm, alarms), "意外放上的报警人工复位能清");
    lp.NotePodPlaced(false);
    lp.Tick();
    Check(Active(e84, e84.UnexpectedCarrierAlarm, alarms), "Auto 下没走交接拿走载具：报意外拿走");
    alarms.Reset(e84.FullPath);
    lp.SetAutoMode(false);
    lp.Tick();
    lp.NotePodPlaced(true);
    lp.Tick();
    lp.NotePodPlaced(false);
    lp.Tick();
    Check(!Active(e84, e84.UnexpectedCarrierAlarm, alarms), "Manual 下人工放、拿载具不报");
    lp.SetAutoMode(true);
    lp.Tick();

    // 第一拍载具就在（端口已经是 Auto）：只记下来，不算意外放上
    var bootPort = new ProbePort("E84BootPort");
    var bootE84 = new ProbeE84(bootPort.Name, timeoutMs: 60000);
    bootPort.AddChild(bootE84);
    Check(bootPort.InitComponent(), "第一拍用的端口的组件初始化应成功");
    ComponentLoader.BindAll([bootPort]);
    bootPort.NoteState(ModuleState.Idle);
    bootPort.Online();
    bootPort.SetAutoMode(true);
    bootPort.NotePodPlaced(true);
    bootPort.Tick();
    bootPort.Tick();
    Check(!Active(bootE84, bootE84.UnexpectedCarrierAlarm, alarms), "E84 第一拍载具就在：不算意外放上");

    // EC 没开：什么都不亮
    var offPort = new ProbePort("E84OffPort");
    var offE84 = new ProbeE84(offPort.Name, enabled: false);
    offPort.AddChild(offE84);
    Check(offPort.InitComponent(), "E84 没开的端口组件初始化也应成功");
    ComponentLoader.BindAll([offPort]);
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
    ComponentLoader.BindAll([noE84Port]);
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
    ComponentLoader.BindAll([tpPort]);
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
    Check(!Active(tpE84, tpE84.UnexpectedCarrierAlarm, alarms), "交接超时锁住时载具到了：已经报过超时，不再报意外放上");
    Check(tpE84.Complete(tpPort._carrier.IsArrived), "载具确实放上了，Complete 应按完成收尾");
    tpPort.Tick();
    Check(tpEvents.Wait("HandoffCompleted:True"), "人工 Complete 应在下一拍随进展上报交接完成");
    Check(tpE84.State == E84State.Available && Active(tpE84, tpE84.E84TimeoutAlarm, alarms),
        "Complete 后回到可交接；报警还在，等人工复位");
    tpPort.Reset();
    Check(!Active(tpE84, tpE84.E84TimeoutAlarm, alarms), "复位端口连同 E84 子组件一起复位，清掉超时报警");

    SmokeIoc.Use<IAlarm>(null);

    static bool Active(ComponentBase source, string code, AlarmComponent alarms) =>
        alarms.ActiveAlarms.Any(alarm => alarm.SourcePath == source.FullPath && alarm.AlarmCode == code);
}

// ── DI/AI 报警防抖：持续满 EC 防抖时间才报；公共组件的报警算在装它的模块头上；只能人工复位清 ─────
{
    var alarms = new AlarmComponent();
    SmokeIoc.Use<IAlarm>(alarms);
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
    var quiet = new ProbeDi("SmokeChamber.Di2") { TriggerValue = false, AlarmEnabled = false, DebounceMs = 0 };
    quiet.Level = false;
    quiet.Tick();
    Check(quiet.IsTriggered && !Active(quiet, quiet.SensorAlarm), "AlarmEnabled=False 只给触发状态，不报警");
    quiet.Level = true;
    quiet.Tick();
    Check(!quiet.IsTriggered, "TriggerValue=false 在 DI=true 时不触发");

    // 给用的地方看的 IsActive（在位传感器这类）：触发、不触发两边都防抖，抖一下不认；读不到为 null
    var presence = new ProbeDi("SmokeChamber.Di3") { AlarmEnabled = false, DebounceMs = 200 };
    presence.Tick();
    Check(presence.IsActive is null, "IsActive：读不到为 null");
    presence.Level = true;
    presence.Tick();
    Check(presence.IsActive is null, "IsActive：刚读到触发、还没稳满防抖时间，不认（仍是 null）");
    Thread.Sleep(250);
    presence.Tick();
    Check(presence.IsActive == true, "IsActive：触发持续满防抖时间才认");
    presence.Level = false;
    presence.Tick();
    presence.Level = true;
    presence.Tick();
    Thread.Sleep(250);
    presence.Tick();
    Check(presence.IsActive == true, "IsActive：掉了一下马上又回来，不认没触发（报警用的 IsTriggered 是立刻撤）");
    presence.Level = false;
    presence.Tick();
    Check(presence.IsActive == true && !presence.IsTriggered, "IsActive：刚变成没触发、还没稳满，保持原来的触发；IsTriggered 已经立刻撤了");
    Thread.Sleep(250);
    presence.Tick();
    Check(presence.IsActive == false, "IsActive：没触发也要持续满防抖时间才认");
    presence.Level = null;
    presence.Tick();
    presence.Level = false;
    presence.Tick();
    Check(presence.IsActive is null, "IsActive：中间读不到过，认下来的作废，重新读到要重新稳满");

    foreach (var (text, expected) in new[] { ("true", true), ("false", false), ("True", true), ("False", false) })
    {
        var loaded = (DiSensorComponent)SmokeIoc.Load([new ModuleConfig
        {
            Name = "TriggerValueSmoke", Type = typeof(DiSensorComponent).FullName,
            Values = [new() { Name = "TriggerValue", Value = text }]
        }]).Single();
        Check(loaded.TriggerValue == expected, $"DI 触发电平配置 {text} 应加载为 {expected}");
    }
    var defaultDi = (DiSensorComponent)SmokeIoc.Load([new ModuleConfig
    {
        Name = "DefaultTriggerSmoke", Type = typeof(DiSensorComponent).FullName
    }]).Single();
    Check(defaultDi.TriggerValue, "未配置触发电平时仍默认高电平触发");
    // 只认 bool：旧的 H/L 写法不再兼容，跟乱填的一样拒绝加载
    foreach (var invalid in new[] { "invalid", "H", "L" })
    {
        var invalidTriggerRejected = false;
        try
        {
            SmokeIoc.Load([new ModuleConfig
            {
                Name = "InvalidTriggerSmoke", Type = typeof(DiSensorComponent).FullName,
                Values = [new() { Name = "TriggerValue", Value = invalid }]
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

    ai.AlarmMin = 10;
    ai.AlarmMax = 90;
    ai.WarnMin = 20;
    ai.WarnMax = 80;
    ai.DurationMs = 0;
    Check(ai.AlarmMin == 10 && ai.WarnMax == 80, "AI 上下限是 EC，写完即生效");

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
    gated.AlarmMin = 10;
    gated.AlarmMax = 90;
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

    // 开监控后延时：监控使能 DO 变有效后过了 MonitoringDelayMs 才判（开阀后流量要爬升），DO 一关重新计
    Check(gated.MonitoringDelayMs == 0, "开监控后延时默认 0：马上判");
    gated.MonitoringDelayMs = 200;
    gated.Reading = 95;
    gated.MonitoringDo = false;
    gated.Tick();
    alarms.Reset(gated.FullPath);
    gated.MonitoringDo = true;
    gated.Tick();
    Check(!gated.IsOutOfRange && !Active(gated, gated.AiSensorAlarm), "开了监控还没过延时不判");
    Thread.Sleep(250);
    gated.Tick();
    Check(gated.IsOutOfRange && Active(gated, gated.AiSensorAlarm), "过了延时才判超限");
    gated.MonitoringDo = false;
    gated.Tick();
    alarms.Reset(gated.FullPath);
    gated.MonitoringDo = true;
    gated.Tick();
    Check(!gated.IsOutOfRange && !Active(gated, gated.AiSensorAlarm), "DO 关了再开，延时重新计");

    // 按目标值的百分比判（IsPercentOfTarget，喷嘴流量计：工艺每步把配方流量给它当目标）：AlarmMin/AlarmMax/WarnMin/WarnMax 是百分比；没给目标（0）不判，哪边是 0 哪边不判
    var flow = new ProbeAi("SmokeChamber.Arm1.Nozzle_SC1.Flow") { MonitoringDoIndex = 7, IsPercentOfTarget = true };
    flow.AlarmMin = 10;
    flow.AlarmMax = 10;
    flow.WarnMin = 5;
    flow.WarnMax = 5;
    flow.DurationMs = 0;
    flow.MonitoringDo = true;
    flow.Reading = 5;
    flow.Tick();
    Check(flow.Target == 0 && !flow.IsOutOfRange && !flow.IsInWarning, "没给目标值不按百分比判（不在工艺里手动开阀不报）");
    flow.SetTarget(2);
    flow.Reading = 2.05;
    flow.Tick();
    Check(flow.Target == 2 && !flow.IsOutOfRange && !flow.IsInWarning, "目标 2：2.05 在 ±5% 预警带里");
    flow.Reading = 2.15;
    flow.Tick();
    Check(flow.IsInWarning && !flow.IsOutOfRange && Active(flow, flow.AiSensorWarnAlarm), "目标 2：2.15 出了 +5%、没出 +10%，报预警");
    flow.Reading = 1.75;
    flow.Tick();
    Check(flow.IsOutOfRange && Active(flow, flow.AiSensorAlarm), "目标 2：1.75 低于 -10%（1.8），报超限");
    alarms.Reset(flow.FullPath);
    flow.AlarmMax = 0;
    flow.WarnMax = 0;
    flow.Reading = 5;
    flow.Tick();
    Check(!flow.IsOutOfRange && !flow.IsInWarning, "按百分比时上限是 0：上边不判");
    flow.AlarmMax = 10;
    flow.WarnMax = 5;

    // 开着监控换了目标（同一个喷嘴连着两步、流量不一样）：延时重新计
    flow.MonitoringDelayMs = 200;
    flow.Reading = 2;
    flow.Tick();
    Thread.Sleep(250);
    flow.Tick();
    Check(!flow.IsOutOfRange, "过了延时、流量在目标上");
    flow.SetTarget(3);
    flow.Tick();
    Check(!flow.IsOutOfRange && !Active(flow, flow.AiSensorAlarm), "目标从 2 换成 3，流量还没爬上来：延时重新计，先不判");
    Thread.Sleep(250);
    flow.Tick();
    Check(flow.IsOutOfRange && Active(flow, flow.AiSensorAlarm), "新目标的延时过了还是 2：低于 3 的 -10%，报超限");
    alarms.Reset(flow.FullPath);

    SmokeIoc.Use<IAlarm>(null);
}

// ── 电脑：Program（本程序）、Total（整台电脑）两个子节点各取 CPU、内存、线程数；第一拍就取内存和线程数，
//    CPU 要隔一个取样周期跟上一次比才有值；没到周期电脑不扫子节点 ─────
{
    var computer = new ProbeComputer("SmokeComputer");
    var program = new ProgramUsageComponent();
    var total = new ComputerUsageComponent();
    computer.AddChild(program);
    computer.AddChild(total);
    computer.SampleIntervalMs = 200;
    computer.Tick();
    Check(program.MemoryUsage > 0 && program.ThreadCount > 0, "第一拍就取到本程序的内存占用（MB）和线程数");
    Check(total.MemoryUsage > 0 && total.MemoryUsage <= 100 && total.ThreadCount >= program.ThreadCount,
        "第一拍就取到整台电脑的内存占用率（0~100）和线程数（不少于本程序的）");
    Check(program.CpuUsage is null && total.CpuUsage is null, "第一拍还没有上一次可比，CPU 占用率是 null");

    computer.Tick();
    Check(program.CpuUsage is null && total.CpuUsage is null, "没到取样周期电脑不扫子节点");

    Thread.Sleep(250);
    computer.Tick();
    Check(program.CpuUsage >= 0 && program.CpuUsage <= 100 && total.CpuUsage >= 0 && total.CpuUsage <= 100,
        "隔了一个取样周期，本程序和整台电脑的 CPU 占用率都有值（0~100）");
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

    // 值一变 EC 组件自己推给界面（EcItemDto，键 = 组件全路径.参数名，不留存）：EC 页、按 EcKey 取范围的输入框跟着走；转 DTO 靠映射组件
    var previousMapper = IocHelper.GetService<IMapper>();
    SmokeIoc.Use<IMapper>(new MapperComponent());
    ec.Bind(); // EC 组件开机 Bind 时取映射组件
    var pushedEc = new List<EcItemDto>();
    using (EventBus.Register<EcItemDto>(EcItemDto.EventToken, dto => pushedEc.Add(dto)))
    {
        Check(ec.Set(ecPort.FullPath, nameof(ecPort.HomeTimeout), "4321"), "改 EC 值");
    }

    SmokeIoc.Use<IMapper>(previousMapper);
    ec.Bind();
    Check(pushedEc.Count == 1 && pushedEc[0].Key == $"{ecPort.FullPath}.{nameof(ecPort.HomeTimeout)}" && pushedEc[0].Value == "4321",
        "值一变推一条：键是组件全路径.参数名，带新值");
    Check(ecPort.LoadTimeout == 0, "合并不动已有值");
    Check(!ec.Merge([ecPort]), "再合并一次应没有变化");
    Check(ec.FilePath.Length == 0, "直接 new 的 EC 组件只在内存里，不落盘");

    ecPort.LoadTimeout = 1234;
    Check(ecPort.LoadTimeout == 1234, "改完即生效");
    ecPort.LoadTimeout = 0;

    SmokeIoc.Use<IEc>(null);
    Check(ecPort.LoadTimeout == 30000, "EC 没装时读回退默认值");
    ecPort.LoadTimeout = 1;
    Check(ecPort.LoadTimeout == 30000, "EC 没装时写不生效");
    SmokeIoc.Use<IEc>(ec);

    // ec.xml 往返：用临时文件，不碰真配置
    var file = Path.Combine(Path.GetTempPath(), $"ec-smoke-{Guid.NewGuid():N}.xml");
    try
    {
        var stored = new EcComponent();
        SmokeIoc.Use<IEc>(stored);
        stored.Load(file);
        Check(stored.Merge([ecPort]) && File.Exists(file), "文件不存在时合并应补建并写出 ec.xml");
        Check(stored.Set(ecPort.FullPath, nameof(ecPort.HomeTimeout), "4321"), "改值应写回");

        var reloaded = new EcComponent();
        SmokeIoc.Use<IEc>(reloaded);
        reloaded.Load(file);
        Check(reloaded.Get(ecPort.FullPath, nameof(ecPort.HomeTimeout)) == "4321"
              && reloaded.Get(ecPort.FullPath, nameof(ecPort.ClampTimeout)) == "10000",
            "重新读 ec.xml 应拿到改过的值和合并补建的项");
    }
    finally
    {
        File.Delete(file);
        SmokeIoc.Use<IEc>(ec);
    }

    // 分组节点（不写 Type）不生成组件，但名字进组件全路径：EC 的键、EC 页的树都跟 sc.xml 的层级一样
    var grouped = SmokeIoc.Load([new ModuleConfig
    {
        Name = "EcGroupSmoke",
        Children = [new ModuleConfig { Name = "EcGroupedDi", Type = typeof(DiSensorComponent).FullName }]
    }]);
    Check(grouped.Count == 1 && grouped[0] is DiSensorComponent && grouped[0].FullPath == "EcGroupSmoke.EcGroupedDi",
        "分组节点不生成组件，名字进组件全路径");
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
    SmokeIoc.Use<IAlarm>(alarms);
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
    SmokeIoc.Use<IAlarm>(null);
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

    // 机械手这时候不能取片（没连上、正忙、账不对……）：落"交互中"标记之前先问它（CheckPick），不能就算没动手——
    // 带上机械手给的原因，两个环都还回去，不留着等人工（以前先落标记再发，被拒就算动过手、站点卡在交互中）
    var pickSource = new ProbeStation("Src4") { First = Succeeded, Second = Succeeded };
    var pickTarget = new ProbeStation("T4") { First = Succeeded };
    var pickRejected = RunTransfer(pickSource, pickTarget, 1000);
    Check(pickRejected.Code == ErrorCodes.ActionRejected && pickRejected.ErrorArgs.SequenceEqual(new[] { "TransferRobot", "0" })
          && !pickRejected.MotionStarted && pickSource.Cancels == 1 && pickTarget.Cancels == 1,
        "取片被机械手拒：落交互中标记之前就问过，带机械手给的原因（module.action_rejected），没动过手，两个环都还回去");

    var cancelSource = new ProbeStation("CancelSource") { First = Succeeded, Cancel = () => false };
    var cancelTarget = new ProbeStation("CancelTarget") { First = Succeeded };
    var cancelRejected = RunTransfer(cancelSource, cancelTarget, 1000);
    Check(cancelRejected.Code == ErrorCodes.StationPrepareRejected && !cancelRejected.MotionStarted
          && cancelRejected.NeedsRecovery && cancelSource.Cancels == 1 && cancelTarget.Cancels == 1,
        "来源撤销被拒仍尝试撤销目标，保留占用和原始失败，尚未运动也要求恢复");

    var throwSource = new ProbeStation("ThrowSource")
    {
        First = Succeeded,
        Cancel = () => throw new InvalidOperationException("smoke cancel fault"),
    };
    var afterThrowTarget = new ProbeStation("AfterThrowTarget") { First = Succeeded };
    var cancelFault = RunTransfer(throwSource, afterThrowTarget, 1000);
    Check(cancelFault.Code == ErrorCodes.StationPrepareRejected && cancelFault.NeedsRecovery
          && !cancelFault.MotionStarted && throwSource.Cancels == 1 && afterThrowTarget.Cancels == 1,
        "来源撤销抛异常不阻止目标撤销，不吞掉原始失败，异常站点保留占用");

    var sameStation = new ProbeStation("SameStation") { First = Succeeded };
    var sameCancel = new TransferRoutine(TransferOrigin.Manual, transferRobot, sameStation, 1, sameStation, 2, 1, 1000);
    sameCancel.Scan();
    sameCancel.AbortByHost("smoke same-station cancel");
    Check(sameCancel.State == OperationState.Aborted && !sameCancel.NeedsRecovery
          && sameStation.Prepares == 1 && sameStation.Cancels == 1,
        "同站点换槽中止只撤销一次，成功撤销后无需保留资源");

    var sameRefused = new ProbeStation("SameRefused") { First = Succeeded, Cancel = () => false };
    var sameCancelFailed = new TransferRoutine(TransferOrigin.Manual, transferRobot, sameRefused, 1, sameRefused, 2, 1, 1000);
    sameCancelFailed.Scan();
    sameCancelFailed.AbortByHost("smoke same-station cancel refused");
    Check(sameCancelFailed.State == OperationState.Aborted && !sameCancelFailed.MotionStarted
          && sameCancelFailed.NeedsRecovery && sameRefused.Prepares == 1 && sameRefused.Cancels == 1,
        "同站点换槽撤销被拒只调用一次并保留占用");
}

// ── 主界面要的后端：系统设置带上 LoadPort / 机械手名单（右栏页签、默认调度图照它生成），机械手站点带上类型（调度图按它选卡片），
//    设备总状态带上模式，整机操作 Auto / Manual / Stop ─────────────────────────────────────────
{
    var previousTransfers = IocHelper.GetService<ITransferManager>();
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
    SmokeIoc.Use<ITransferManager>(null);
    mainRobot.Bind(); // 机械手开机 Bind 时取搬运管理：这时没有
    Check(mainRobot.CreateStateDto().StationInfos.All(info => info.Kind == StationKind.Other),
        "搬运模块表还没绑时站点类型应一律是 Other");
    var mainTransfers = new TransferManager();
    SmokeIoc.Use<ITransferManager>(mainTransfers);
    mainTransfers.Bind(mainModules);
    mainRobot.Bind();
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
    SmokeIoc.Tree(mainModules);
    var system = new SystemService();
    var settings = system.GetSettingsAsync(new RpcRequest()).Result.DeserializeData<SystemSettingsDto>();
    Check(settings.LoadPorts.SequenceEqual(["MainLP"]) && settings.Robots.SequenceEqual(["SmokeRobot"])
          && settings.Chambers.SequenceEqual(["MainPM"]),
        "系统设置应带上 LoadPort、机械手、腔体名单");

    // 整机（系统模块）：开机未初始化；全部初始化先机械手、再其余模块，都做成进空闲，被拒 / 没做成回未初始化；
    // Auto / Manual 拨搬运管理的自动派单；Stop 进停止中，等停的那一刻手上的动作都收完尾，回到停之前该在的地方；全部回片回不去的记下来。
    // 真搬片的全部回片在 JobSmoke 里跟着整机停止一起跑。
    var previousSystem = IocHelper.GetService<ISystemModule>();
    var previousJobs = IocHelper.GetService<IJobManager>();
    var previousSystemLedger = IocHelper.GetService<ILedgerManager>();
    SmokeIoc.Use<IJobManager>(null);
    SmokeIoc.Use<ILedgerManager>(null);
    var sysRobot = new SystemProbeRobot("SysRobot");
    var sysPort = new SystemProbeModule("SysLP");
    var sysChamber = new SystemProbeModule("SysPM");
    var machine = new SmokeSystem();
    SmokeIoc.Use<ISystemModule>(machine);
    BaseModule[] sysModules = [machine, sysRobot, sysPort, sysChamber];
    var sysTransfers = new TransferManager();
    SmokeIoc.Use<ITransferManager>(sysTransfers);
    sysTransfers.Bind(sysModules);
    machine.Bind(sysModules); // 整机开机绑模块表时取搬运管理、Job 管理、晶圆账
    SmokeIoc.Tree(sysModules);
    var equipment = new EquipmentService();
    bool StateRefused(RpcResponse response, int state)
    {
        return !response.Success && response.Code == ErrorCodes.ModuleStateNotAllowed
            && response.Args.SequenceEqual(["System", state.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
    }

    // 机械手先回原点，做完了 LoadPort、腔体再一起初始化
    void InitAllOk()
    {
        var robotOk = new ProbeOperation();
        var portOk = new ProbeOperation();
        var chamberOk = new ProbeOperation();
        sysRobot.NextInit = robotOk;
        sysPort.NextInit = portOk;
        sysChamber.NextInit = chamberOk;
        Check(machine.InitAll().IsSuccess, "全部初始化应开始");
        machine.Tick();
        robotOk.Succeed();
        sysRobot.Tick();
        machine.Tick();
        machine.Tick();
        portOk.Succeed();
        chamberOk.Succeed();
        sysPort.Tick();
        sysChamber.Tick();
        machine.Tick();
        Check(machine.State == ModuleState.Idle, "全部初始化做成：空闲");
    }

    var sysSettings = new SystemService().GetSettingsAsync(new RpcRequest()).Result.DeserializeData<SystemSettingsDto>();
    Check(sysSettings.Modules.SequenceEqual(["SysRobot", "SysLP", "SysPM"]), "整机模块没有 IO，不进系统设置的模块名单");
    Check(machine.State == ModuleState.NotInit && !machine.IsAuto, "开机未初始化（Manual）");
    Check(StateRefused(equipment.AutoAsync(new RpcRequest()).Result, ModuleState.NotInit) && !sysTransfers.IsAutoDispatch,
        "没初始化切不了 Auto（module.state_not_allowed 带整机名和状态码）");
    Check(StateRefused(equipment.ReturnAllAsync(new RpcRequest()).Result, ModuleState.NotInit), "没初始化不能全部回片");

    // 整机自己推顶栏的设备总状态（EquipmentStatusDto，留存）：每拍算一次，变了才推（在做动作就是运行中）
    var statusPushes = new List<EquipmentStatusDto>();
    using var statusSubscription = EventBus.Register<EquipmentStatusDto>(EquipmentStatusDto.EventToken, dto => statusPushes.Add(dto));
    statusPushes.Clear();
    machine.Tick();
    machine.Tick();
    Check(statusPushes.Count == 1 && !statusPushes[0].IsRunning, "整机自己推设备总状态：第一拍推一条，没变化不再推");

    // 整机推各模块的状态和 Auto / Manual（ModuleStatusListDto，留存，顶栏模块行用）：不含整机自己、先后同模块表，有一个变了才推整张表
    var modulePushes = new List<ModuleStatusListDto>();
    using var moduleSubscription = EventBus.Register<ModuleStatusListDto>(ModuleStatusListDto.EventToken, dto => modulePushes.Add(dto));
    Check(modulePushes.Count == 1 && modulePushes[0].Modules.Select(module => module.Name).SequenceEqual(["SysRobot", "SysLP", "SysPM"])
          && modulePushes[0].Modules.Select(module => module.Kind).SequenceEqual([sysRobot.Kind, sysPort.Kind, sysChamber.Kind])
          && modulePushes[0].Modules.All(module => module.State == ModuleState.NotInit && module.Mode == ModuleMode.Offline),
        "整机推模块状态（留存，订上就补发）：不含整机自己、先后同模块表，带类别、状态码、Auto / Manual");
    modulePushes.Clear();
    machine.Tick();
    Check(modulePushes.Count == 0, "模块都没变：不再推");
    sysPort.Online();
    machine.Tick();
    machine.Tick();
    Check(modulePushes.Count == 1 && modulePushes[0].Modules[1].Mode == ModuleMode.Online
          && modulePushes[0].Modules[0].Mode == ModuleMode.Offline, "一个模块上线：推一张新表（只推一次）");
    sysPort.Offline();
    machine.Tick();
    Check(modulePushes.Count == 2 && modulePushes[1].Modules[1].Mode == ModuleMode.Offline, "下线了：再推一张");

    // 全部初始化：先只初始化机械手，它收完尾才轮到别的，都做成进空闲
    var robotHome = new ProbeOperation();
    var portHome = new ProbeOperation();
    var chamberHome = new ProbeOperation();
    sysRobot.NextInit = robotHome;
    sysPort.NextInit = portHome;
    sysChamber.NextInit = chamberHome;
    Check(equipment.InitAsync(new RpcRequest()).Result.Success && machine.State == ModuleState.Initing, "全部初始化开了就回：初始化中");
    machine.Tick();
    Check(sysRobot.InitCalls == 1 && sysPort.InitCalls == 0 && sysChamber.InitCalls == 0, "先只初始化机械手");
    Check(statusPushes.Count == 2 && statusPushes[1].IsRunning, "整机在做动作：设备总状态变成运行中，推一条");
    robotHome.Succeed();
    machine.Tick();
    machine.Tick();
    Check(sysPort.InitCalls == 0, "机械手的初始化到了终态、还没收完尾：别的先不动");
    sysRobot.Tick();
    machine.Tick();
    machine.Tick();
    Check(sysPort.InitCalls == 1 && sysChamber.InitCalls == 1 && machine.State == ModuleState.Initing, "机械手做完：LoadPort、腔体一起初始化");
    portHome.Succeed();
    sysPort.Tick();
    machine.Tick();
    Check(machine.State == ModuleState.Initing, "还有一个没做完：还在初始化中");
    chamberHome.Succeed();
    sysChamber.Tick();
    machine.Tick();
    Check(machine.State == ModuleState.Idle, "都做成了：空闲");
    Check(statusPushes.Count == 3 && !statusPushes[2].IsRunning, "做完了：推回不在运行");

    // 没做成、被拒：整趟没做成，回未初始化（报警逻辑还没接，不进 Error）
    var badHome = new ProbeOperation();
    sysRobot.NextInit = badHome;
    var failing = machine.InitAll();
    machine.Tick();
    badHome.Reject();
    sysRobot.Tick();
    machine.Tick();
    var initFailed = failing.Result;
    Check(initFailed is not null && initFailed.Code == ErrorCodes.SystemInitFailed && initFailed.ErrorArgs.SequenceEqual(["SysRobot"])
          && machine.State == ModuleState.NotInit && sysPort.InitCalls == 1,
        "机械手初始化没做成：system.init_failed 带模块名，回未初始化，别的模块不动");
    var refused = machine.InitAll();
    machine.Tick();
    Check(refused.Result is not null && refused.Result.Code == ErrorCodes.SystemInitRejected
          && refused.Result.ErrorArgs.SequenceEqual(["SysRobot"]) && machine.State == ModuleState.NotInit,
        "模块的初始化被拒：system.init_rejected 带模块名，回未初始化");

    // Auto / Manual：当场切，拨自动派单；Auto 下没有在跑的 Job 是自动空闲
    InitAllOk();
    Check(equipment.AutoAsync(new RpcRequest()).Result.Success && machine.State == SystemState.AutoIdle && machine.IsAuto
          && sysTransfers.IsAutoDispatch, "切 Auto：自动空闲，开自动派单");
    Check(StateRefused(equipment.AutoAsync(new RpcRequest()).Result, SystemState.AutoIdle), "已经是 Auto 再切不收");
    machine.Tick();
    Check(machine.State == SystemState.AutoIdle, "没有在跑的 Job：一直是自动空闲");
    Check(StateRefused(equipment.ReturnAllAsync(new RpcRequest()).Result, SystemState.AutoIdle), "Auto 下不能全部回片");
    Check(equipment.ManualAsync(new RpcRequest()).Result.Success && machine.State == ModuleState.Idle && !sysTransfers.IsAutoDispatch,
        "切 Manual：空闲，关自动派单");
    Check(StateRefused(equipment.ManualAsync(new RpcRequest()).Result, ModuleState.Idle), "已经是 Manual 再切不收");
    sysTransfers.IsEnable = false;
    var disabled = equipment.AutoAsync(new RpcRequest()).Result;
    Check(!disabled.Success && disabled.Code == ErrorCodes.TransferDisabled && disabled.Args.Count == 0
          && machine.State == ModuleState.Idle && !sysTransfers.IsAutoDispatch, "搬运管理停用：切 Auto 回 transfer.disabled，还是空闲");
    sysTransfers.IsEnable = true;
    SmokeIoc.Use<ITransferManager>(null);
    machine.Bind(sysModules); // 整机开机绑模块表时取单例：换了要重新 Bind
    var notInstalled = equipment.AutoAsync(new RpcRequest()).Result;
    Check(!notInstalled.Success && notInstalled.Code == ErrorCodes.TransferNotInstalled && notInstalled.Args.Count == 0
          && machine.State == ModuleState.Idle, "没装搬运管理：切 Auto 回 transfer.not_installed");
    SmokeIoc.Use<ITransferManager>(sysTransfers);
    machine.Bind(sysModules);

    // Stop：进停止中、关自动派单，只给在做动作的模块发中止；那个动作收完尾才算停下来，Auto 停完回空闲
    Check(equipment.AutoAsync(new RpcRequest()).Result.Success, "再切回 Auto");
    var manualMove = new ProbeOperation();
    sysPort.StartManual(manualMove);
    var stop = equipment.StopAsync(new RpcRequest()).Result;
    Check(stop.Success && machine.State == ModuleState.Aborting && !sysTransfers.IsAutoDispatch
          && sysPort.AbortCalls == 1 && sysRobot.AbortCalls == 0 && sysChamber.AbortCalls == 0,
        "Stop：停止中，关自动派单，只给在做动作的那一个模块发中止");
    Check(StateRefused(equipment.StopAsync(new RpcRequest()).Result, ModuleState.Aborting), "停止中再按 Stop 不收");
    machine.Tick();
    Check(machine.State == ModuleState.Aborting, "被中止的动作还没收完尾：还在停止中");
    sysPort.Tick();
    machine.Tick();
    Check(machine.State == ModuleState.Idle, "停下来了：Auto 停完回空闲（Manual）");

    // 初始化中 Stop：顶掉全部初始化，正在回原点的机械手发中止，停完回未初始化；未初始化时也能停
    var homing = new ProbeOperation();
    sysRobot.NextInit = homing;
    Check(machine.InitAll().IsSuccess, "再开一次全部初始化");
    machine.Tick();
    Check(equipment.StopAsync(new RpcRequest()).Result.Success && sysRobot.AbortCalls == 1 && machine.State == ModuleState.Aborting,
        "初始化中 Stop：顶掉初始化，给正在回原点的机械手发中止");
    sysRobot.Tick();
    machine.Tick();
    Check(machine.State == ModuleState.NotInit, "初始化被停：回未初始化");
    Check(equipment.StopAsync(new RpcRequest()).Result.Success && machine.State == ModuleState.Aborting, "未初始化时也能 Stop");
    machine.Tick();
    Check(machine.State == ModuleState.NotInit, "没有在做的动作：停完还是未初始化");

    // 全部回片：要晶圆账；机内的片回不去（不知道从哪个 LoadPort 来）记下来、回空闲；机内没片直接做完
    InitAllOk();
    var noLedger = equipment.ReturnAllAsync(new RpcRequest()).Result;
    Check(!noLedger.Success && noLedger.Code == ErrorCodes.WaferLedgerDisabled && machine.State == ModuleState.Idle,
        "没装晶圆账：全部回片回 wafer.ledger_disabled");
    var sysLedger = new LedgerManagerComponent();
    SmokeIoc.Use<ILedgerManager>(sysLedger);
    machine.Bind(sysModules);
    sysLedger.RegisterLocation("SysRobot", 1);
    Check(sysLedger.Create("SysRobot", 1) is not null, "机械手手上摆一片补账的片（不知道从哪个 LoadPort 来）");
    var returning = machine.ReturnAll();
    Check(returning.IsSuccess && machine.State == SystemState.Returning, "全部回片开始：回片中");
    machine.Tick();
    machine.Tick();
    var unreturned = returning.Result;
    Check(unreturned is not null && unreturned.Code == ErrorCodes.SystemReturnIncomplete && unreturned.ErrorArgs.SequenceEqual(["1"])
          && machine.State == ModuleState.Idle && sysLedger.Get("SysRobot", 1) is not null,
        "回不去的片：system.return_incomplete 带片数，片不动，回空闲");
    Check(sysLedger.Delete("SysRobot", 1), "删掉那片");
    var empty = machine.ReturnAll();
    machine.Tick();
    machine.Tick();
    Check(empty.Result is not null && empty.Result.IsSuccess && machine.State == ModuleState.Idle, "机内没片：直接做完，回空闲");

    SmokeIoc.Use<ISystemModule>(null);
    equipment = new EquipmentService(); // 服务在构造时取单例（宿主每次调用都新建一个），换了单例要重新建
    Check(equipment.InitAsync(new RpcRequest()).Result.Code == ErrorCodes.SystemNotInstalled
          && equipment.StopAsync(new RpcRequest()).Result.Code == ErrorCodes.SystemNotInstalled,
        "没配整机：整机操作回 system.not_installed");
    SmokeIoc.Use<ISystemModule>(previousSystem);
    SmokeIoc.Use<IJobManager>(previousJobs);
    SmokeIoc.Use<ILedgerManager>(previousSystemLedger);

    // 晶圆尺寸：整机没初始化（启用的模块都是未初始化）、机台里（LoadPort 以外）没片才让切；尺寸不对、没配晶圆尺寸各回各的码。
    // 这里没有 sc.xml，过了这几道检查就是写回失败（不换尺寸）；写成功、写回的样子在 ProcessRecipeSmoke 里用真 sc.xml 查。
    var previousWaferSize = IocHelper.GetService<IWaferSize>();
    var previousSizeLedger = IocHelper.GetService<ILedgerManager>();
    SmokeIoc.Use<IWaferSize>(null);
    SmokeIoc.Use<ILedgerManager>(null);
    machine.Bind(sysModules);
    var sizePort = new ProbePort("SizeLP");
    var sizeChamber = new ProbeChamber("SizePM");
    SmokeIoc.Tree(sizePort, sizeChamber);
    RpcRequest SizeRequest(string size)
    {
        var request = new RpcRequest();
        request.Parameters[IEquipmentService.WaferSizeKey] = size;
        return request;
    }

    RpcResponse SwitchSize(string size)
    {
        // 跟宿主一样每次调用新建服务：构造时取的是当时登记的晶圆尺寸、晶圆账
        return new EquipmentService().SetWaferSizeAsync(SizeRequest(size)).Result;
    }

    var noSize = SwitchSize("8");
    Check(!noSize.Success && noSize.Code == ErrorCodes.WaferSizeNotInstalled && noSize.Args.Count == 0
          && machine.CreateEquipmentStatus().WaferSize == 12,
        "没配晶圆尺寸：切不了，设备总状态按 12 寸");
    var waferSize = new WaferSizeComponent();
    SmokeIoc.Use<IWaferSize>(waferSize);
    machine.Bind(sysModules);
    var badSize = SwitchSize("6");
    Check(!badSize.Success && badSize.Code == ErrorCodes.WaferSizeInvalid && badSize.Args.SequenceEqual(["6"]), "尺寸只能是 8 或 12");
    Check(!SwitchSize("x").Success && SwitchSize(string.Empty).Code == ErrorCodes.WaferSizeInvalid, "不是数、没给都算尺寸不对");
    sizePort.NoteState(ModuleState.Idle);
    Check(SwitchSize("12").Success, "跟现在一样直接成功，不查别的");
    var initialized = SwitchSize("8");
    Check(!initialized.Success && initialized.Code == ErrorCodes.WaferSizeInitialized && initialized.Args.SequenceEqual(["SizeLP"]) && waferSize.Size == 12,
        "有模块初始化过（不是未初始化）：不让切，尺寸不变");
    sizePort.NoteState(ModuleState.NotInit);
    var sizeLedger = new LedgerManagerComponent();
    SmokeIoc.Use<ILedgerManager>(sizeLedger);
    sizeLedger.RegisterLoadPort("SizeLP", 25);
    sizeLedger.RegisterLocation("SizePM", 1);
    Check(sizeLedger.Create("SizePM", 1) is not null && sizeLedger.Create("SizeLP", 3) is not null, "腔体里一片、LoadPort 上一片");
    var hasWafer = SwitchSize("8");
    Check(!hasWafer.Success && hasWafer.Code == ErrorCodes.WaferSizeHasWafer && hasWafer.Args.SequenceEqual(["SizePM"]) && waferSize.Size == 12,
        "机台里（腔体）有片：不让切");
    Check(sizeLedger.Delete("SizePM", 1), "把腔体里的片删掉");
    var notSaved = SwitchSize("8");
    Check(!notSaved.Success && notSaved.Code == ErrorCodes.WaferSizeSaveFailed && notSaved.Args.Count == 1 && waferSize.Size == 12
          && machine.CreateEquipmentStatus().WaferSize == 12,
        "LoadPort 上的片不挡；写不了 sc.xml 就不换尺寸");
    waferSize.Size = 8;
    Check(machine.CreateEquipmentStatus().WaferSize == 8, "设备总状态带上现在的尺寸");
    SmokeIoc.Use<IWaferSize>(previousWaferSize);
    SmokeIoc.Use<ILedgerManager>(previousSizeLedger);

    SmokeIoc.Use<ITransferManager>(previousTransfers);
}

// ── LoadPort 在位来源（SC PresenceSource 二选一）和驱动恢复：状态查询超时作废重发、动作没做成作废在途指令、关连接作废、
//    断线按间隔重连、_rfid 连不上不连累 LoadPort、帧通讯重连时旧接收泵只停自己那一轮 ─────────────────────────
{
    var previousLedger = IocHelper.GetService<ILedgerManager>();
    var presenceLedger = new LedgerManagerComponent();
    SmokeIoc.Use<ILedgerManager>(presenceLedger);

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
    ComponentLoader.BindAll([queryPort]);
    queryPort.NoteState(ModuleState.Idle);
    queryPort.Tick();
    Check(!queryPort._carrier.IsArrived && queryPort._carrier.Info is null, "Query：还没查到状态时当没有载具");
    queryPort.NoteStatus(new LoadPortStatus { IsPresent = true, IsPlaced = true });
    queryPort.Tick();
    Check(queryPort._carrier.IsArrived && queryPort._carrier.Info is not null,
        "Query：在位、到位都亮算放好（开机时已在端口上的也认），建载具对象");
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
    Check(!queryPort._carrier.IsArrived && queryPort._carrier.Info is null,
        "Query：在位、到位都灭算拿走，载具对象清掉");
    queryPort.NoteStatus(new LoadPortStatus { IsPresent = false, IsPlaced = true });
    queryPort.Tick();
    Check(!queryPort._carrier.IsArrived && queryPort._carrier.Info is null, "Query：一亮一灭不算放上");

    // 2) Event：只认 PODON / PODOF，状态查询说什么都不管
    var eventPort = new ProbePort("PresenceEventPort", PodPresenceSource.Event);
    eventPort.InitComponent();
    ComponentLoader.BindAll([eventPort]);
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

    // 3) 状态查询走真驱动：按 EC 周期一条条发（回来了也要等满一个周期）；一条没回就超时作废、接着发（以前同名查询一直占着在途位，再也查不了）
    var pollPort = new ProbePort("PollPort", PodPresenceSource.Query);
    pollPort.QueryDataTimeOut = 100;
    pollPort.QueryStatusIntervalMs = 500;
    var pollComm = pollPort.Shell.Comm;
    Check(pollPort.InitComponent(), "状态查询用的端口的组件初始化应成功");
    ComponentLoader.BindAll([pollPort]);
    pollPort.Tick();
    Check(WaitUntil(() => pollComm.SentCount("GET:STATE") == 1), "连上后扫描一拍就发出第一条状态查询");
    pollComm.Push(StateReply(present: true, placed: true));
    Check(TickUntil(pollPort, () => pollPort._carrier.IsArrived && pollPort._carrier.Info is not null),
        "查询回来在位、到位都亮：判放上（盒子开机前就在端口上也认得）");
    pollPort.Tick();
    Thread.Sleep(50);
    Check(pollComm.SentCount("GET:STATE") == 1, "上一条回来了，但离它发出去不满一个周期（EC QueryStatusIntervalMs）：这一拍不发");
    Check(TickUntil(pollPort, () => pollComm.SentCount("GET:STATE") == 2), "满一个周期发下一条");
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
    ComponentLoader.BindAll([linkPort]);
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

    // 6b) 重连满 EC ReconnectMaxCount 次还连不上：停下、报"已停止重连"，不再敲；人工复位以后从头再连（PLC 也是这一套）
    var previousAlarms = IocHelper.GetService<IAlarm>();
    var reconnectAlarms = new AlarmComponent();
    SmokeIoc.Use<IAlarm>(reconnectAlarms);
    var giveUpPort = new ProbePort("GiveUpPort", PodPresenceSource.Query);
    giveUpPort.Shell.ReconnectIntervalMs = 10;
    giveUpPort.Shell.ReconnectMaxCount = 2;
    var giveUpComm = giveUpPort.Shell.Comm;
    Check(giveUpPort.InitComponent() && giveUpComm.Opens == 1, "停重连用的端口开机连上");
    ComponentLoader.BindAll([giveUpPort]);
    bool GaveUp() => reconnectAlarms.ActiveAlarms.Any(alarm =>
        alarm.SourcePath == giveUpPort.Shell.FullPath && alarm.AlarmCode == giveUpPort.Shell.ReconnectFailedAlarm);
    giveUpComm.FailOpen = true;
    giveUpComm.Drop();
    Check(TickUntil(giveUpPort, GaveUp) && giveUpComm.Opens == 1 + 2, "断了重连正好 EC 给的 2 次，还连不上就报已停止重连");
    for (int tick = 0; tick < 5; tick++)
    {
        giveUpPort.Tick();
        Thread.Sleep(15);
    }

    Check(giveUpComm.Opens == 3, "停下以后不再重连");
    giveUpComm.FailOpen = false;
    giveUpPort.Shell.Reset();
    Check(!GaveUp(), "人工复位清掉已停止重连的报警");
    Check(TickUntil(giveUpPort, () => giveUpComm.IsConnected), "人工复位以后从头再连，连上了");
    giveUpPort.Close();

    // 连上一下又断（串口打开就算连上，设备不在要等发东西失败才断）：没连满一个间隔不算恢复，照样算一次重连，次数用完照样停
    var flapPort = new ProbePort("FlapPort", PodPresenceSource.Query);
    flapPort.Shell.ReconnectIntervalMs = 200;
    flapPort.Shell.ReconnectMaxCount = 2;
    var flapComm = flapPort.Shell.Comm;
    flapPort.InitComponent();
    ComponentLoader.BindAll([flapPort]);
    bool FlapGaveUp() => reconnectAlarms.ActiveAlarms.Any(alarm =>
        alarm.SourcePath == flapPort.Shell.FullPath && alarm.AlarmCode == flapPort.Shell.ReconnectFailedAlarm);
    flapComm.Drop();
    Check(TickUntil(flapPort, () =>
    {
        if (flapComm.IsConnected)
        {
            flapPort.Tick();
            flapComm.Drop();
        }

        return FlapGaveUp();
    }) && flapComm.Opens == 1 + 2, "连上一下又断的照样算重连，次数用完停下、报警（以前次数一直清零，永远停不下来）");
    flapPort.Close();

    var previousPlcIo = IocHelper.GetService<IPlcIo>();
    var previousPlcBlock = IocHelper.GetService<IPlcBlock>();
    var giveUpPlc = new ProbePlc();
    SmokeIoc.Use<IPlcIo>(giveUpPlc);
    SmokeIoc.Use<IPlcBlock>(giveUpPlc);
    giveUpPlc.ReconnectIntervalMs = 10;
    giveUpPlc.ReconnectMaxCount = 2;
    Check(!giveUpPlc.Open() && giveUpPlc.Connects == 1, "PLC 开机连不上");
    Check(WaitUntil(() =>
    {
        giveUpPlc.Tick();
        return giveUpPlc.Connects == 3;
    }), "PLC 断着按间隔重连");
    for (int tick = 0; tick < 5; tick++)
    {
        giveUpPlc.Tick();
        Thread.Sleep(15);
    }

    Check(giveUpPlc.Connects == 3, "PLC 重连满 EC 给的 2 次就停下");
    giveUpPlc.FailConnect = false;
    giveUpPlc.Reset();
    Check(WaitUntil(() =>
    {
        giveUpPlc.Tick();
        return giveUpPlc.IsConnected;
    }), "PLC 人工复位以后从头再连，连上了");
    giveUpPlc.Close();
    SmokeIoc.Use<IPlcIo>(previousPlcIo);
    SmokeIoc.Use<IPlcBlock>(previousPlcBlock);
    SmokeIoc.Use<IAlarm>(previousAlarms);

    // 7) _rfid 读头连不上：LoadPort 照样开驱动、登记晶圆账槽位（以前整台 LoadPort 都不能动）；读头之后按间隔重连上
    var rfidPort = new ProbePort("RfidDownPort", PodPresenceSource.Query);
    var rfidShell = new ProbeRfidShell();
    typeof(ComponentBase).GetProperty(nameof(ComponentBase.Name))!.SetValue(rfidShell, "_rfid");
    typeof(ComponentBase).GetProperty(nameof(ComponentBase.FullPath))!.SetValue(rfidShell, "RfidDownPort._rfid");
    rfidShell.ReconnectIntervalMs = 10;
    rfidShell.Comm.FailOpen = true;
    rfidPort.AddChild(rfidShell);
    Check(!rfidPort.InitComponent(), "_rfid 连不上时组件初始化返回 false（开机日志看得到）");
    ComponentLoader.BindAll([rfidPort]);
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
    ComponentLoader.BindAll([autoPort]);
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
    ComponentLoader.BindAll([failPort]);
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
    ComponentLoader.BindAll([noReaderPort]);
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
        ComponentLoader.BindAll([barePort]);
    }
    catch (InvalidOperationException exception)
    {
        bareThrown = exception.Message.Contains("_carrier", StringComparison.Ordinal);
    }

    Check(bareThrown, "端口下没配 _carrier 子组件：组件初始化抛 InvalidOperationException（开机就暴露）");

    // 7d) 装机停用（SC IsEnable=False）的端口：载具照样挂上——停用的口照样扫描、推状态，Job 按载具号找端口也会查它的载具；
    //     不连驱动、不发动作
    var disabledPort = new PlainPort("DisabledPort") { IsEnable = false };
    Check(disabledPort.InitComponent(), "停用的端口：组件初始化直接算成功");
    ComponentLoader.BindAll([disabledPort]);
    Check(disabledPort._carrier is not null && !disabledPort._carrier.IsArrived, "停用的端口：载具照样挂上，上面没载具");
    bool disabledScanned = true;
    try
    {
        disabledPort.Tick();
        disabledPort.Tick();
    }
    catch (NullReferenceException)
    {
        disabledScanned = false;
    }

    Check(disabledScanned, "停用的端口：扫描不抛空引用");
    var disabledDto = disabledPort.CreateStateDto();
    Check(!disabledDto.HasCarrier && !disabledPort.CanUseCarrierForJob
          && disabledPort.LocalTransferState == LoadPortTransferState.OutOfService,
        "停用的端口：状态推得出来，没载具、不能分给 Job、搬运状态是 Out Of Service");
    var disabledLoad = disabledPort.Load();
    Check(!disabledLoad.IsSuccess && disabledLoad.ErrorMessage == ErrorCodes.ModuleDisabled && !disabledPort.Shell.IsConnected,
        "停用的端口：不连驱动、不发动作（Load 回模块停用）");

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
    var noCarrierLoad = plain.Load();
    Check(!noCarrierLoad.IsSuccess && noCarrierLoad.ErrorMessage == ErrorCodes.CarrierNotArrived
          && plain.State == ModuleState.Idle && plainComm.SentCount("MOV:CLOAD") == 0,
        "端口上没载具：平台默认 Load 不发，回载具没到位");
    plain.NotePodPlaced(true);
    plain.Tick();
    var plainLoad = plain.Load().Result;
    Check(plainLoad is not null && plain.State == LoadPortState.Loading, "平台默认 Load：空闲能发起，进 Loading");
    Check(TickPlainUntil(plain, () => plainComm.SentCount("MOV:CLOAD") == 1), "平台默认 Load 发的是 FCD 的 CLOAD");
    plainComm.Push($"INF:CLOAD/PE{new string('E', 23)}");
    Check(TickPlainUntil(plain, () => plainLoad!.IsTerminal) && plainLoad!.IsSuccess && plain.State == LoadPortState.Loaded
          && plain._carrier.SlotMap.Count == 25 && plain._carrier.SlotMap[0] == SlotState.Normal && plain._carrier.SlotMap[1] == SlotState.Empty,
        "平台默认 Load：设备回完成就落 Loaded，Mapping 结果落进 SlotMap");
    var plainUnload = plain.Unload().Result;
    Check(plainUnload is not null && TickPlainUntil(plain, () => plainComm.SentCount("MOV:CULOD") == 1), "平台默认 Unload 发 CULOD");
    plainComm.Push("NAK:CULOD/BUSY");
    Check(TickPlainUntil(plain, () => plainUnload!.IsTerminal) && plainUnload!.Code == ErrorCodes.DeviceFailed
          && plain.State == ModuleState.Error,
        "设备回 NAK：动作判失败（device_failed），落 Error");
    plain.HomeTimeout = 100;
    var plainHome = plain.Home().Result;
    Check(plainHome is not null && TickPlainUntil(plain, () => plainComm.SentCount("MOV:ORGSH") == 1), "出错后能 Home，发 ORGSH");
    Thread.Sleep(150);
    Check(TickPlainUntil(plain, () => plainHome!.IsTerminal) && plainHome!.Code == ErrorCodes.Timeout, "设备不回：超时判失败（timeout）");
    plainComm.Drop();
    int stateBeforeDrop = plain.State;
    var droppedHome = plain.Home();
    Check(!droppedHome.IsSuccess && droppedHome.ErrorMessage == ErrorCodes.ModuleNotConnected && plain.State == stateBeforeDrop
          && plainComm.SentCount("MOV:ORGSH") == 1,
        "没连上：Home 当场被拒（module.not_connected），不发指令、状态不动（以前是发出去判 command_rejected、落 Error）");
    plain.Close();

    // 9b) Load 回来的 Mapping 不能用：槽数跟 sc 的 SlotCount 对不上（不落账），或者有交叉片、叠片、认不出的槽（账照落）——
    //     Load 判失败、落 Error，机械手进不来，报"Mapping 异常"（不报笼统的动作失败）
    var mapAlarms = new AlarmComponent();
    SmokeIoc.Use<IAlarm>(mapAlarms);
    bool MapAlarmActive(BaseLoadPortModule port, string code) =>
        mapAlarms.ActiveAlarms.Any(alarm => alarm.SourcePath == port.FullPath && alarm.AlarmCode == code);

    ModuleOperation LoadWithMap(PlainPort port, string mapData)
    {
        Check(port.InitComponent(), $"{port.Name} 的组件初始化应成功");
        ComponentLoader.BindAll([port]);
        port.NoteState(ModuleState.Idle);
        port.NotePodPlaced(true);
        port.Tick();
        var load = port.Load().Result;
        Check(load is not null && TickPlainUntil(port, () => port.Shell.Comm.SentCount("MOV:CLOAD") == 1), $"{port.Name} 发了 CLOAD");
        port.Shell.Comm.Push($"INF:CLOAD/{mapData}");
        Check(TickPlainUntil(port, () => load!.IsTerminal), $"{port.Name} 的 Load 做完了");
        return load!;
    }

    var shortPort = new PlainPort("ShortMapPort");
    var shortLoad = LoadWithMap(shortPort, new string('P', 24));
    Check(shortLoad.Code == ErrorCodes.SlotMapLengthMismatch && shortLoad.ErrorArgs.SequenceEqual(new[] { "ShortMapPort", "24", "25" })
          && shortPort.State == ModuleState.Error,
        "设备回了 24 槽、sc 配的 25 槽：Load 判失败（slot_map_length_mismatch），落 Error");
    Check(shortPort._carrier.SlotMap.Count == 0 && presenceLedger.CountWafers("ShortMapPort") == 0, "槽数对不上：不落账（载具没槽图，账上没片）");
    Check(MapAlarmActive(shortPort, shortPort.SlotMapAlarm) && !MapAlarmActive(shortPort, shortPort.ControlledStopAlarm),
        "槽数对不上：报 Mapping 异常，不报笼统的动作失败");
    shortPort.Close();

    var crossPort = new PlainPort("CrossMapPort");
    var crossLoad = LoadWithMap(crossPort, $"PPCPD{new string('E', 20)}");
    Check(crossLoad.Code == ErrorCodes.SlotMapAbnormal && crossLoad.ErrorArgs.SequenceEqual(new[] { "CrossMapPort", "3,5" })
          && crossPort.State == ModuleState.Error && !crossPort.CanPrepareTransfer,
        "3 槽交叉片、5 槽叠片：Load 判失败（slot_map_abnormal），落 Error，机械手进不来");
    Check(crossPort._carrier.SlotMap.Count == 25 && crossPort._carrier.SlotMap[2] == SlotState.Crossed
          && presenceLedger.Get("CrossMapPort", 3)?.Status == WaferStatus.Crossed && presenceLedger.Get("CrossMapPort", 5)?.Status == WaferStatus.Double,
        "交叉片、叠片：账照落，看得到是哪几槽");
    Check(MapAlarmActive(crossPort, crossPort.SlotMapAlarm), "交叉片、叠片：报 Mapping 异常");
    crossPort.Close();
    SmokeIoc.Use<IAlarm>(null);

    // 9c) Job 做完自动 Unload（EC AutoUnload，接不接 EAP 都一样）：载具刚干完就卸；机械手还在取放就等它回 Loaded；
    //     只认刚干完这一下（之后还在 Loaded，比如人又手动 Load 起来，不再卸）；从这个口取出去的片没回齐先不卸；关着不卸；
    //     等着卸的时候在线把 EC 关掉，就改成只对账、不卸
    ProbePort CompletedCarrierPort(string name, bool autoUnload)
    {
        var port = new ProbePort(name) { AutoUnload = autoUnload, Next = new ProbeOperation() };
        Check(port.InitComponent(), $"{name} 的组件初始化应成功");
        ComponentLoader.BindAll([port]);
        port.NotePodPlaced(true);
        port.Tick();
        port.NoteState(LoadPortState.Loaded);
        return port;
    }

    var unloadPort = CompletedCarrierPort("AutoUnloadPort", autoUnload: true);
    unloadPort.NoteState(TransferModuleState.PreTransfer);
    int unloadCalls = unloadPort.Calls;
    unloadPort._carrier.NoteComplete();
    unloadPort.Tick();
    Check(unloadPort.Calls == unloadCalls, "载具干完了、机械手还在取放：先不卸");
    unloadPort.NoteState(LoadPortState.Loaded);
    unloadPort.Tick();
    Check(unloadPort.Calls == unloadCalls + 1, "机械手回到 Loaded：自动 Unload");
    unloadPort.Tick();
    unloadPort.Tick();
    Check(unloadPort.Calls == unloadCalls + 1, "只认刚干完这一下：之后还在 Loaded（人又手动 Load 起来）也不再卸");
    unloadPort.Close();

    var outPort = CompletedCarrierPort("AutoUnloadOutPort", autoUnload: true);
    presenceLedger.RegisterLocation("AutoUnloadChamber", 1);
    Check(presenceLedger.Create("AutoUnloadOutPort", 1) is not null && presenceLedger.Move("AutoUnloadOutPort", 1, "AutoUnloadChamber", 1),
        "摆一片从这个口取出去、还在腔体上的片");
    int outCalls = outPort.Calls;
    outPort._carrier.NoteComplete();
    outPort.Tick();
    Check(outPort.Calls == outCalls, "从这个口取出去的片还在腔体上（Job 中止会有）：先不卸，门一关片就回不来了");

    // 关门的动作在入口里也查这一条（手动、Host 放行都拦）：手动 Unload、门开着的 Home 当场被拒，带片数，端口不动
    var outUnload = outPort.UnloadManually();
    Check(!outUnload.IsSuccess && outUnload.ErrorMessage == ErrorCodes.PortWafersOutside
          && outUnload.Args.SequenceEqual(new[] { "AutoUnloadOutPort", "1" }) && outPort.State == LoadPortState.Loaded && outPort.Calls == outCalls,
        $"片还在腔体上：手动 Unload 被拒（loadport.wafers_outside，带片数），端口不动，实际 {outUnload.ErrorMessage}");
    var outHome = outPort.Home();
    Check(!outHome.IsSuccess && outHome.ErrorMessage == ErrorCodes.PortWafersOutside && outPort.State == LoadPortState.Loaded,
        $"片还在腔体上、门开着：Home 也被拒（回原点会关门），实际 {outHome.ErrorMessage}");
    Check(outPort.CheckUnload()?.ErrorMessage == ErrorCodes.PortWafersOutside, "只查不动的 CheckUnload 给的是同一个原因（E87 回 Host 前用）");

    Check(presenceLedger.Move("AutoUnloadChamber", 1, "AutoUnloadOutPort", 1), "片回到口上");
    outPort.Tick();
    Check(outPort.Calls == outCalls + 1, "片回齐了：自动 Unload");
    outPort.Close();

    // 门本来就关着、或者位置不明（出错复位后没初始化）出发的 Home 不查片：拦住就卡死了——端口回不到 Loaded，机械手进不来，片也回不来
    var lostPort = CompletedCarrierPort("HomeNotInitPort", autoUnload: false);
    presenceLedger.RegisterLocation("HomeNotInitChamber", 1);
    Check(presenceLedger.Create("HomeNotInitPort", 1) is not null && presenceLedger.Move("HomeNotInitPort", 1, "HomeNotInitChamber", 1),
        "摆一片从这个口取出去、还在腔体上的片");
    lostPort.NoteState(ModuleState.NotInit);
    var lostHome = lostPort.Home();
    Check(lostHome.IsSuccess && lostPort.State == LoadPortState.Homing,
        $"片还在外面、端口没初始化：Home 不查片，照常能发，实际 {lostHome.ErrorMessage}");
    lostPort.Close();

    var offPort = CompletedCarrierPort("AutoUnloadOffPort", autoUnload: false);
    int offCalls = offPort.Calls;
    offPort._carrier.NoteComplete();
    offPort.Tick();
    offPort.Tick();
    Check(offPort.Calls == offCalls, "EC AutoUnload 关着：干完不自动卸，等操作员或 Host CarrierRelease");
    offPort.Close();

    var turnOffPort = CompletedCarrierPort("AutoUnloadTurnOffPort", autoUnload: true);
    Check(ec.Get(turnOffPort.FullPath, nameof(turnOffPort.AutoUnload)) == "True" && turnOffPort.AutoUnload, "AutoUnload 是 EC：值放在 EC 组件里");
    presenceLedger.RegisterLocation("AutoUnloadTurnOffChamber", 1);
    Check(presenceLedger.Create("AutoUnloadTurnOffPort", 1) is not null
          && presenceLedger.Move("AutoUnloadTurnOffPort", 1, "AutoUnloadTurnOffChamber", 1),
        "摆一片从这个口取出去、还在腔体上的片");
    int turnOffCalls = turnOffPort.Calls;
    turnOffPort._carrier.NoteComplete();
    turnOffPort.Tick();
    Check(turnOffPort.Calls == turnOffCalls, "片没回齐：先等着");
    Check(ec.Set(turnOffPort.FullPath, nameof(turnOffPort.AutoUnload), "False") && !turnOffPort.AutoUnload, "在线把 EC 关掉，下一拍就生效");
    Check(presenceLedger.Move("AutoUnloadTurnOffChamber", 1, "AutoUnloadTurnOffPort", 1), "片回到口上");
    turnOffPort.Tick();
    Check(turnOffPort.Calls == turnOffCalls, "等着卸的时候 EC 关了：片回齐也不卸，改成只对账");
    Check(ec.Set(turnOffPort.FullPath, nameof(turnOffPort.AutoUnload), "True"), "再把 EC 打开");
    turnOffPort.Tick();
    Check(turnOffPort.Calls == turnOffCalls, "收过尾的不再补卸：只认刚干完那一下");
    turnOffPort.Close();

    // 9c-2) EC 关着时干完对账（照 CTC 参数关着时的干完对账）：不卸、不发设备指令，拿 Load 时的槽图跟晶圆账按片的来处逐槽对——
    //       片按流程配方回到别的 LoadPort、别的 LoadPort 的片回到这盒都不算错；片在账上换了槽报 Mapping 异常，只报警：端口不落 Error、账不改
    var completeAlarms = new AlarmComponent();
    SmokeIoc.Use<IAlarm>(completeAlarms);
    bool CompleteAlarmActive(BaseLoadPortModule port) =>
        completeAlarms.ActiveAlarms.Any(alarm => alarm.SourcePath == port.FullPath && alarm.AlarmCode == port.SlotMapAlarm);

    var completeMap = Enumerable.Range(1, 25).Select(slot => slot <= 3 ? SlotState.Normal : SlotState.Empty).ToArray();
    presenceLedger.RegisterLoadPort("CompleteOtherPort", 25);

    var settledPort = CompletedCarrierPort("CompleteSettledPort", autoUnload: false);
    settledPort.NoteMap(completeMap);
    Check(presenceLedger.CountWafers("CompleteSettledPort") == 3, "Load 时 1~3 槽有片，落了账");
    Check(presenceLedger.Move("CompleteSettledPort", 2, "CompleteOtherPort", 2), "第 2 槽的片按流程配方回到了别的 LoadPort");
    Check(presenceLedger.Create("CompleteOtherPort", 10) is not null && presenceLedger.Move("CompleteOtherPort", 10, "CompleteSettledPort", 10),
        "别的 LoadPort 的片回到这盒原来空着的第 10 槽");
    int settledCalls = settledPort.Calls;
    settledPort._carrier.NoteComplete();
    settledPort.Tick();
    Check(settledPort.Calls == settledCalls && !CompleteAlarmActive(settledPort),
        "EC 关着：干完不卸；片回别的口、别的口的片回到这盒都不算对不上，不报警");
    settledPort.Close();

    var movedPort = CompletedCarrierPort("CompleteMovedPort", autoUnload: false);
    movedPort.NoteMap(completeMap);
    Check(presenceLedger.Move("CompleteMovedPort", 3, "CompleteMovedPort", 5), "第 3 槽的片在账上跑到了 Load 时空着的第 5 槽");
    int movedCalls = movedPort.Calls;
    movedPort._carrier.NoteComplete();
    movedPort.Tick();
    Check(movedPort.Calls == movedCalls && CompleteAlarmActive(movedPort),
        "Load 时的槽图跟账对不上（第 3 槽找不到原来那片、第 5 槽多了片）：报 Mapping 异常，不卸");
    Check(movedPort.State == LoadPortState.Loaded && presenceLedger.HasWafer("CompleteMovedPort", 5) && !presenceLedger.HasWafer("CompleteMovedPort", 3),
        "干完对账只报警：端口还在 Loaded（不落 Error），账不改");
    movedPort.Close();
    SmokeIoc.Use<IAlarm>(null);

    // 9d) 自动跑货的 Unload 带 Mapping 对账（SC AutoRunMapOnUnload，照老 CTC 的卸载对账）：FCD 没有带图卸载的指令，先发 CULOD 关门，
    //     关好了再主动发 CLDMP 扫一遍；扫到的跟账一致 Unload 成功；少片、多片、交叉片都判失败（unload_slot_map_mismatch）、落 Error，
    //     报 Mapping 异常和晶圆账的账实不符，账不动；关门没成就不扫；扫图没成 Unload 判失败；SC 关着只发 CULOD（第 9 节）
    var unloadMapAlarms = new AlarmComponent();
    SmokeIoc.Use<IAlarm>(unloadMapAlarms);
    PlainPort LoadedMapPort(string name)
    {
        var port = new PlainPort(name) { AutoRunMapOnUnload = true };
        Check(port.InitComponent(), $"{name} 的组件初始化应成功");
        ComponentLoader.BindAll([port]);
        port.NoteState(ModuleState.Idle);
        port.NotePodPlaced(true);
        port.Tick();
        var load = port.Load().Result;
        Check(load is not null && TickPlainUntil(port, () => port.Shell.Comm.SentCount("MOV:CLOAD") == 1), $"{name} 发了 CLOAD");
        port.Shell.Comm.Push($"INF:CLOAD/PPE{new string('E', 22)}");
        Check(TickPlainUntil(port, () => load!.IsTerminal) && load!.IsSuccess && port.State == LoadPortState.Loaded, $"{name} Load 好了，1、2 槽有片");
        return port;
    }

    ModuleOperation UnloadThenMap(PlainPort port, params string[] mapReplies)
    {
        var comm = port.Shell.Comm;
        var unload = port.Unload().Result;
        Check(unload is not null && TickPlainUntil(port, () => comm.SentCount("MOV:CULOD") == 1) && comm.SentCount("MOV:CLDMP") == 0,
            $"{port.Name}：AutoRunMapOnUnload 开着，Unload 先发普通的 CULOD 关门，门没关好不扫");
        comm.Push("INF:CULOD");
        Check(TickPlainUntil(port, () => comm.SentCount("MOV:CLDMP") == 1), $"{port.Name}：门关好了，再主动发 CLDMP 扫一遍");
        foreach (string reply in mapReplies)
        {
            comm.Push(reply);
        }

        Check(TickPlainUntil(port, () => unload!.IsTerminal), $"{port.Name} 的 Unload 做完了");
        return unload!;
    }

    var matchPort = LoadedMapPort("UnloadMapOkPort");
    var matchUnload = UnloadThenMap(matchPort, $"INF:CLDMP/PPE{new string('E', 22)}");
    Check(matchUnload.IsSuccess && matchPort.State == ModuleState.Idle && !matchPort.HasAlarm, "关门后扫到的跟账一致：Unload 成功、落 Idle，不报警");
    matchPort.Close();

    // 1 槽账上有、没扫到（少片），3 槽扫到、账上没有（多片），4 槽交叉片；槽图走单独的 MAPDT 帧、终结帧不带数据也收
    var mismatchPort = LoadedMapPort("UnloadMapBadPort");
    var mismatchUnload = UnloadThenMap(mismatchPort, $"INF:MAPDT/EPPC{new string('E', 21)}", "INF:CLDMP");
    Check(mismatchUnload.Code == ErrorCodes.UnloadSlotMapMismatch && mismatchUnload.ErrorArgs.SequenceEqual(new[] { "UnloadMapBadPort", "1,3,4" })
          && mismatchPort.State == ModuleState.Error,
        "少片、多片、交叉片：Unload 判失败（unload_slot_map_mismatch，带槽号），落 Error，不能就这么取走");
    Check(MapAlarmActiveIn(unloadMapAlarms, mismatchPort, mismatchPort.SlotMapAlarm)
          && unloadMapAlarms.ActiveAlarms.Any(alarm => alarm.AlarmCode == presenceLedger.WaferLedgerAlarm),
        "Unload 对账不符：报 Mapping 异常，晶圆账报账实不符");
    Check(presenceLedger.Get("UnloadMapBadPort", 1) is not null && presenceLedger.Get("UnloadMapBadPort", 3) is null,
        "Unload 对账不符：账不动，等人按实物在账单调整页改");
    mismatchPort.Close();

    // 关门没成（设备回 ABS）：Unload 判失败、落 Error，不再发 CLDMP 扫图
    var closeFailPort = LoadedMapPort("UnloadMapCloseFailPort");
    var closeFailComm = closeFailPort.Shell.Comm;
    var closeFailUnload = closeFailPort.Unload().Result;
    Check(closeFailUnload is not null && TickPlainUntil(closeFailPort, () => closeFailComm.SentCount("MOV:CULOD") == 1),
        "关门没成用的端口：Unload 先发了 CULOD");
    closeFailComm.Push("ABS:CULOD/0301");
    Check(TickPlainUntil(closeFailPort, () => closeFailUnload!.IsTerminal) && closeFailUnload!.Code == ErrorCodes.DeviceFailed
          && closeFailPort.State == ModuleState.Error && closeFailComm.SentCount("MOV:CLDMP") == 0,
        "关门没成：Unload 判失败（device_failed）、落 Error，不再发 CLDMP 扫图");
    closeFailPort.Close();

    // 门关好了、扫图没成（设备回 NAK）：Unload 判失败、落 Error——没扫成就对不了账，不能当卸好了让人取走
    var scanFailPort = LoadedMapPort("UnloadMapScanFailPort");
    var scanFailUnload = UnloadThenMap(scanFailPort, "NAK:CLDMP/0103");
    Check(scanFailUnload.Code == ErrorCodes.DeviceFailed && scanFailPort.State == ModuleState.Error,
        "门关好了、扫图没成：Unload 判失败（device_failed）、落 Error，不当卸好了");
    scanFailPort.Close();

    // 9e) 手动 Unload 不吃 SC AutoRunMapOnUnload：SC 开着（自动跑货要关门后扫图对账）时，手动卸载照样只发普通 CULOD，关好门也不扫、不对账——
    //     账跟实物对不上也得能让操作员把盒子放出去（跟 CTC 的手动卸载一致，SC 只管自动跑货的 Unload）
    var manualPort = LoadedMapPort("ManualUnloadPort");
    var manualUnload = manualPort.UnloadManually().Result;
    Check(manualUnload is not null && TickPlainUntil(manualPort, () => manualPort.Shell.Comm.SentCount("MOV:CULOD") == 1),
        "手动 Unload：SC AutoRunMapOnUnload 开着也发普通的 CULOD");
    manualPort.Shell.Comm.Push("INF:CULOD");
    Check(TickPlainUntil(manualPort, () => manualUnload!.IsTerminal) && manualUnload!.IsSuccess
          && manualPort.State == ModuleState.Idle && !manualPort.HasAlarm && manualPort.Shell.Comm.SentCount("MOV:CLDMP") == 0,
        "手动 Unload：关好门不扫、不对账——账上 1、2 槽有片也不拦，卸成功、落 Idle、不报警");
    Check(presenceLedger.Get("ManualUnloadPort", 1) is not null && presenceLedger.Get("ManualUnloadPort", 2) is not null,
        "手动 Unload：账不动");
    manualPort.Close();
    SmokeIoc.Use<IAlarm>(null);

    static bool MapAlarmActiveIn(AlarmComponent alarms, BaseLoadPortModule port, string code) =>
        alarms.ActiveAlarms.Any(alarm => alarm.SourcePath == port.FullPath && alarm.AlarmCode == code);

    // 10) 复位只清错、中止只停，Idle 一律当"门关好、没 Load"：没初始化、出过错的复位完是 NotInit（要再 Home，照老 CTC），
    //     中止也绕不过复位和 Home；门开着没在动（Loaded、正被机械手取放）的复位 / 中止完按状态查询的门位落 Loaded / Idle，
    //     查不到、门在半路、载具不在，或者打断的是机构在动的动作（Load / Unload / Home / 夹紧），落 NotInit
    var resetPort = new ProbePort("ResetStatePort");
    Check(resetPort.InitComponent(), "复位用的端口的组件初始化应成功");
    ComponentLoader.BindAll([resetPort]);
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

    // Load 入口的互锁：平台默认要载具在；机型加的条件重写 LoadInterlock（先调 base），不过时回机型自己的原因，端口不动
    resetPort.NoteState(ModuleState.Idle);
    var noCarrier = resetPort.Load();
    Check(!noCarrier.IsSuccess && noCarrier.ErrorMessage == ErrorCodes.CarrierNotArrived && resetPort.State == ModuleState.Idle,
        "端口上没载具：Load 被拒，回载具没到位（机型换了 Load 的设备动作也走这一关）");
    resetPort.NotePodPlaced(true);
    resetPort.Tick();
    resetPort.BlockedAction = LoadPortAction.Load;
    var blocked = resetPort.Load();
    Check(!blocked.IsSuccess && blocked.ErrorMessage == ProbePort.BlockedCode && blocked.Args.SequenceEqual(new[] { resetPort.Name })
          && resetPort.State == ModuleState.Idle,
        "机型重写了 Load 联锁（LoadInterlock）：载具在也不让 Load，回机型自己的原因");
    resetPort.BlockedAction = null;

    // 其余动作的联锁（在各自的入口里查）：平台默认不拦；机型重写了哪个动作的联锁，就只拦那个动作，回机型自己的原因（复位、中止不设联锁）
    foreach (var (action, from, start) in new (LoadPortAction, int, Func<HandleResult<ModuleOperation>>)[]
             {
                 (LoadPortAction.Unload, LoadPortState.Loaded, resetPort.Unload),
                 (LoadPortAction.Home, ModuleState.Idle, resetPort.Home),
                 (LoadPortAction.Clamp, ModuleState.Idle, resetPort.Clamp),
                 (LoadPortAction.Unclamp, ModuleState.Idle, resetPort.Unclamp),
             })
    {
        resetPort.NoteState(from);
        resetPort.BlockedAction = action;
        var blockedStart = start();
        Check(!blockedStart.IsSuccess && blockedStart.ErrorMessage == ProbePort.BlockedCode && resetPort.State == from,
            $"机型重写了 {action} 联锁：不让发，回机型自己的原因，状态不动");
        resetPort.BlockedAction = null;
        var allowed = new ProbeOperation();
        resetPort.Next = allowed;
        Check(start().IsSuccess && ReferenceEquals(resetPort.CurrentOperation, allowed), $"{action} 联锁没拦（平台默认）：照常能发");
        allowed.Succeed();
        resetPort.Tick();
    }

    resetPort.Next = null;

    // 机械手进站要载具在：状态还是 Loaded、载具却不在了，不让进，免得往空口放片
    resetPort.NoteState(LoadPortState.Loaded);
    Check(resetPort.CanPrepareTransfer, "Loaded、载具在、没接 EAP：机械手能进站");
    resetPort.NotePodPlaced(false);
    resetPort.Tick();
    Check(!resetPort.CanPrepareTransfer && resetPort.PrepareTransfer(TransferContext.ForPick("Robot", 1, 1)) is null && resetPort.State == LoadPortState.Loaded,
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
          && resetPort.CanPrepareTransfer,
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

    SmokeIoc.Use<ILedgerManager>(previousLedger);
}

// ── 机械手设备状态查询：一圈从上往下查（没订上每圈开头订一次，订上了不再订）；一条没回就超时作废，同名查询还能再发；
//    回来了写到查的那根轴 / 那个状态上、接着查下一条；迟到的"没有报错"回复不当报错推送；关连接作废在途指令 ─────────
{
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

    // 探针机械手的查询超时是 0：没回的查询下一拍就作废、同一拍接着发下一条，一条一拍；一圈查完空一拍，下一拍重开一圈。
    // 探针有 X / Z / Theta / Arm1 四根轴：没订上时一圈 8 条（订阅、报错、伺服、速度、4 根轴）+ 空一拍 = 9 拍
    var pollRobot = new ProbeRobot();
    var robotComm = pollRobot.Shell.Comm;
    Check(pollRobot.InitComponent(), "状态查询用的机械手的组件初始化应成功");
    ComponentLoader.BindAll([pollRobot]);

    // 1) 假通道一条都不回：每条都超时作废，下一圈同名查询照样发得出去
    //    （以前超时只丢句柄，驱动里同名查询一直占着在途位，这一种再也查不了）
    for (int tick = 0; tick < 18; tick++)
    {
        pollRobot.Tick();
    }

    var oneRound = new[] { "SubWafer1", "Error", "QEnable", "QSpeed", "XPos", "ZPos", "ThetaPos", "Arm1Pos" };
    var expected = oneRound.Concat(oneRound).ToArray();
    Check(WaitUntil(() => robotComm.Sent.Count == expected.Length),
        $"18 拍应发出两圈 16 条查询（超时的作废了，同名的还能再发），实际 {robotComm.Sent.Count} 条");
    Check(robotComm.Sent.SequenceEqual(expected),
        "一圈的顺序：订阅 → 报错 → 伺服 → 速度 → 每根轴一条（按 sc.xml 轴节点先后），没订上每圈都订一次；实际 "
        + string.Join(",", robotComm.Sent));

    // 2) 回来了就写到查的那根轴 / 那个状态上，接着查下一条（超时拉长，免得等回复时被作废）
    bool TickUntil(Func<bool> condition)
    {
        return WaitUntil(() =>
        {
            pollRobot.Tick();
            return condition();
        });
    }

    bool LastSent(int count, string frame)
    {
        return robotComm.Sent.Count == count && robotComm.Sent.Last() == frame;
    }

    pollRobot.QueryDataTimeOut = 600000;
    Check(TickUntil(() => LastSent(17, "SubWafer1")), "一圈查完从头来：还没订上，先订阅");
    robotComm.Push(">00000000#@SubWafer1");
    Check(TickUntil(() => LastSent(18, "Error")), "订阅回来了，接着查报错");
    robotComm.Push(">40010006#Arm2 No Wafer When Put@Error");
    Check(TickUntil(() => pollRobot.DeviceError == "40010006#Arm2 No Wafer When Put"), "查报错回来了：设备报错写到模块上");
    Check(TickUntil(() => LastSent(19, "QEnable")), "上一条回来了，接着查伺服");
    robotComm.Push(">00000000#YES@QEnable");
    Check(TickUntil(() => pollRobot.IsServoOn == true), "伺服回来了：写到模块上");
    Check(TickUntil(() => LastSent(20, "QSpeed")), "接着查速度");
    robotComm.Push(">00000000#80@QSpeed");
    Check(TickUntil(() => pollRobot.Speed == 80), "速度回来了：写到模块上");
    Check(TickUntil(() => LastSent(21, "XPos")), "接着查第一根轴 X 的坐标");
    robotComm.Push(">00000000#1@XPos");
    Check(TickUntil(() => LastSent(22, "ZPos")), "接着查 Z 的坐标");
    robotComm.Push(">00000000#2@ZPos");
    var theta = pollRobot.Axes.First(axis => axis.Name == "Theta");
    Check(TickUntil(() => LastSent(23, "ThetaPos")), "接着查 Theta 的坐标");
    robotComm.Push(">00000000#12.5@ThetaPos");
    Check(TickUntil(() => theta.Position == 12.5), "轴位回来了：坐标写到查的那根轴（Theta）上");
    Check(TickUntil(() => LastSent(24, "Arm1Pos")), "接着查 Arm1 的坐标");
    robotComm.Push(">00000000#-3@Arm1Pos");
    Check(TickUntil(() => pollRobot.Arms[0].Position == -3), "手指也是轴：Arm1 的坐标写到手指上");
    Check(TickUntil(() => LastSent(25, "Error")), "一圈查完从头来：已经订上了，第一条直接查报错");

    // 订上以后不再订：一圈 7 条 + 空一拍 = 8 拍。在途的那条报错先作废，再走 15 拍：
    // 这一圈剩下 6 条 + 空一拍，下一圈 7 条 + 空一拍，正好停在两圈之间（手上没有在途的查询，下面第 3 段要用）
    pollRobot.QueryDataTimeOut = 0;
    for (int tick = 0; tick < 15; tick++)
    {
        pollRobot.Tick();
    }

    Check(WaitUntil(() => robotComm.Sent.Count == 38), $"订上以后 15 拍应再发 13 条查询，实际共 {robotComm.Sent.Count} 条");
    Check(!robotComm.Sent.Skip(18).Contains("SubWafer1"), "订上了就不再订阅；实际 " + string.Join(",", robotComm.Sent.Skip(18)));

    // 3) 没有查报错在途时来了一帧成功码的 Error（作废以后迟到的"没有报错"回复）：不当报错推送，设备报错不变
    //    后面跟一帧手指在位推送，它落下来了，说明前面那帧也处理过了（同一条路由队列，按先后处理）
    robotComm.Push(">00000000#@Error");
    robotComm.Push(">00000000#SubWaferEx,1,0@Event");
    Check(WaitUntil(() => pollRobot.Arms[0].HasWafer == true), "手指在位推送应落到 Arm1 上");
    Check(pollRobot.DeviceError == "40010006#Arm2 No Wafer When Put",
        $"成功码的 Error 帧不是报错推送，设备报错不该被它改掉，实际 {pollRobot.DeviceError}");

    // 4) 关连接：在途的指令全部作废（等它的人不会一直等）；重新连上以后同名指令能再发
    var stuckHome = pollRobot.Shell.Home();
    Check(stuckHome is not null, "发一条 Home 指令（假通道不回它）");
    Check(pollRobot.Shell.Home() is null, "同名指令还在途时再发被拒");
    pollRobot.Close();
    Check(stuckHome!.IsCompleted && stuckHome.Response is not null && !stuckHome.Response.IsSuccess,
        "关连接：在途指令全部作废");
    Check(pollRobot.InitComponent() && pollRobot.Shell.Home() is not null, "重新连上以后同名指令能再发");
    pollRobot.Close();

    // 5) 这台机械手不带手指在位推送（SC WaferEventEnabled = False）：一圈不订阅，一条订阅都不发
    var plainRobot = new ProbeRobot { WaferEventEnabled = false };
    var plainComm = plainRobot.Shell.Comm;
    Check(plainRobot.InitComponent(), "不带手指在位推送的机械手组件初始化应成功");
    ComponentLoader.BindAll([plainRobot]);
    for (int tick = 0; tick < 16; tick++)
    {
        plainRobot.Tick();
    }

    Check(WaitUntil(() => plainComm.Sent.Count == 14), $"16 拍应发出两圈 14 条查询（一圈 7 条 + 空一拍），实际 {plainComm.Sent.Count} 条");
    Check(!plainComm.Sent.Contains("SubWafer1") && plainComm.Sent[0] == "Error",
        "SC 关了就不订阅：第一条直接查报错；实际 " + string.Join(",", plainComm.Sent));
    plainRobot.Close();

    // 6) 查询周期（EC QueryStatusIntervalMs）管的是"圈"：一圈里的各条照旧一拍一条，一圈查完要离这一圈开始满一个周期才开下一圈
    var paceRobot = new ProbeRobot { WaferEventEnabled = false, QueryStatusIntervalMs = 500 };
    var paceComm = paceRobot.Shell.Comm;
    Check(paceRobot.InitComponent(), "测查询周期的机械手组件初始化应成功");
    ComponentLoader.BindAll([paceRobot]);
    for (int tick = 0; tick < 12; tick++)
    {
        paceRobot.Tick();
    }

    Check(WaitUntil(() => paceComm.Sent.Count == 7), $"一圈 7 条照旧一拍一条发完，实际 {paceComm.Sent.Count} 条");
    Thread.Sleep(100);
    Check(paceComm.Sent.Count == 7, $"一圈查完、离这一圈开始不满一个周期：后面几拍不开下一圈，实际 {paceComm.Sent.Count} 条");
    // 发送在后台任务上，推一拍以后不一定马上数得到：等到"至少 8 条"再看第 8 条（等"正好 8 条"会被下一拍越过去，机器一忙就失败）
    Check(WaitUntil(() =>
        {
            paceRobot.Tick();
            return paceComm.Sent.Count >= 8;
        }) && paceComm.Sent[7] == "Error",
        "满一个周期开下一圈：第一条查报错；实际 " + string.Join(",", paceComm.Sent));
    paceRobot.Close();

    // 7) 断线：按 EC 间隔在后台重连（先关后开），连上以后从头查、重新订手指在位推送；Close 以后不再重连
    //    （以前机械手不重连：控制器断电重启、比后端晚上电，只能重启后端）
    var linkRobot = new ProbeRobot { QueryDataTimeOut = 600000 };
    linkRobot.Shell.ReconnectIntervalMs = 10;
    var linkComm = linkRobot.Shell.Comm;
    Check(linkRobot.InitComponent() && linkComm.Opens == 1, "重连用的机械手组件初始化应成功");
    ComponentLoader.BindAll([linkRobot]);
    linkRobot.Tick();
    Check(WaitUntil(() => linkComm.SentCount("SubWafer1") == 1), "连着时照常查状态：先订手指在位推送");
    linkComm.Push(">00000000#@SubWafer1");
    Check(WaitUntil(() =>
        {
            linkRobot.Tick();
            return linkComm.SentCount("Error") == 1;
        }), "订上了，接着查报错");
    linkComm.Drop();
    Check(WaitUntil(() =>
        {
            linkRobot.Tick();
            return linkComm.Opens >= 2 && linkComm.IsConnected;
        }), "断了按间隔在后台重连上");
    Check(WaitUntil(() =>
        {
            linkRobot.Tick();
            return linkComm.SentCount("SubWafer1") == 2;
        }), "重连上以后从头查：订阅跟着连接走，重新订手指在位推送；实际 " + string.Join(",", linkComm.Sent));
    linkRobot.Close();
    int robotOpensAfterClose = linkComm.Opens;
    linkComm.Drop();
    for (int tick = 0; tick < 5; tick++)
    {
        linkRobot.Tick();
        Thread.Sleep(15);
    }

    Check(linkComm.Opens == robotOpensAfterClose, "Close 以后不再重连");

    // 8) 动作等回复超时：那条指令作废、让出在途位，复位、回零以后同一条指令（再取同一槽）还能发
    //    （以前超时只判失败，锐洁按整条指令认在途，回复丢了就一直占着，只能急停或断线才放开）
    var stuckRobot = new ProbeRobot { WaferEventEnabled = false };
    Check(stuckRobot.InitComponent(), "测动作超时的机械手组件初始化应成功");
    ComponentLoader.BindAll([stuckRobot]);
    var stuckPick = new RobotCommandOperation("Pick", () => stuckRobot.Shell.Pick(1, 3, 5), () => 0);
    stuckPick.Scan();
    Check(!stuckPick.IsTerminal && stuckRobot.Shell.Pick(1, 3, 5) is null, "取片指令发出去了（假通道不回它），同一条还在途时再发被拒");
    Thread.Sleep(5);
    stuckPick.Scan();
    Check(stuckPick.IsTerminal && stuckPick.Code == ErrorCodes.Timeout, "等回复超时：动作判失败（超时）");
    Check(stuckRobot.Shell.Pick(1, 3, 5) is not null, "超时的那条作废了：同一条取片指令能再发");
    stuckRobot.Close();
}

// 本站交接上下文：对象身份隔离轮次，覆盖正常/延迟收尾、撤销、恢复及同站换槽。
{
    Throws<ArgumentException>(() => TransferContext.ForPick(" ", 1, 1));
    Throws<ArgumentOutOfRangeException>(() => TransferContext.ForPick("Robot1", 0, 1));
    Throws<ArgumentOutOfRangeException>(() => TransferContext.ForPlace("Robot1", 1, 0));
    Throws<ArgumentOutOfRangeException>(() => TransferContext.ForPickThenPlace("Robot1", 1, 1, 2, 0));
    var context = TransferContext.ForPick("Robot1", 4, 5);
    var identical = TransferContext.ForPick("Robot1", 4, 5);
    Check(context != identical && !context.Equals(identical), "内容相同的两个 class 上下文仍是不同轮次；手臂编号不限定为 1/2");

    var station = new ContextProbeStation();
    Throws<ArgumentNullException>(() => station.PrepareTransfer(null!));
    Check(station.PrepareTransfer(context) is not null, "本站上下文取得传片环");
    Check(station.PrepareTransfer2(identical) is null && !station.Transferring(identical)
          && !station.TransferComplete(identical) && station.SecondPrepares == 0,
        "同内容的另一轮不能准备、开始交互或完成，也不会调用设备准备扩展点");
    Check(station.CancelTransfer(identical) && station.State == TransferModuleState.PreTransfer,
        "撤销不属于本站的轮次直接成功，不动实际占用");
    Check(station.PrepareTransfer2(context) is not null && ReferenceEquals(station.PreparedContext, context),
        "准备二扩展点取得准备一的同一份上下文");
    Check(station.Transferring(context) && station.Transferring(context)
          && !station.CancelTransfer(context), "同轮交互标记可复核，已经交互时不能普通撤销");
    station.DelayCleanup = true;
    Check(station.TransferComplete(context) && station.State == TransferModuleState.TransferComplete,
        "接受完成通知后允许站点继续异步收尾");
    Check(station.PrepareTransfer(identical) is null && !station.TransferComplete(identical)
          && station.CancelTransfer(identical) && station.State == TransferModuleState.TransferComplete,
        "收尾期间挡住下一轮，旧轮撤销不影响正在收尾的站点");
    Check(station.FinishCleanup() && station.State == ModuleState.Idle
          && !station.Transferring(context) && !station.TransferComplete(context),
        "实际回待命后旧轮不能继续交互或完成");
    Check(station.PrepareTransfer(identical) is not null && station.CancelTransfer(context)
          && station.State == TransferModuleState.PreTransfer,
        "下一轮即使内容相同，上一轮撤销仍不能释放它");
    Check(station.CancelTransfer(identical) && station.State == TransferModuleState.PreTransfer
          && station.PrepareTransfer(TransferContext.ForPick("Robot1", 4, 5)) is null,
        "撤销的延迟机构收尾没完成时也不能接下一轮");
    Check(station.FinishCleanup() && station.CanPrepareTransfer, "撤销收尾实际结束后站点恢复可用");

    // 换段准备：只认本轮、取放两段都有、已经在交互中；站点没有换段动作时给空操作，之后照常放片收尾
    station.DelayCleanup = false;
    var legs = TransferContext.ForPickThenPlace("Robot1", 1, 5, 2, 5);
    Throws<ArgumentNullException>(() => station.PrepareNextLeg(null!));
    Check(station.PrepareTransfer(legs) is not null && station.PrepareNextLeg(legs) is null,
        "两段的轮次还没开始交互不能换段");
    Check(station.PrepareTransfer2(legs) is not null && station.Transferring(legs)
          && station.PrepareNextLeg(TransferContext.ForPickThenPlace("Robot1", 1, 5, 2, 5)) is null,
        "别的轮次即使内容相同也不能换段");
    var nextLeg = station.PrepareNextLeg(legs);
    Check(nextLeg is not null && nextLeg.IsSuccess && station.State == TransferModuleState.Transferring,
        "本轮交互中换段：默认空操作直接成功，站点仍停在交互中");
    Check(station.Transferring(legs) && station.TransferComplete(legs) && station.State == ModuleState.Idle
          && station.PrepareNextLeg(legs) is null,
        "换段以后照常放片收尾回待命，旧轮不能再换段");
    var single = TransferContext.ForPick("Robot1", 1, 5);
    Check(station.PrepareTransfer(single) is not null && station.PrepareTransfer2(single) is not null && station.Transferring(single)
          && station.PrepareNextLeg(single) is null,
        "只有一段的轮次不能换段");
    Check(station.TransferComplete(single) && station.State == ModuleState.Idle, "单段轮次照常收尾回待命");

    // 中途交接：只认配了开关、本轮带这一段的上下文、已经在交互中；交接完站点还停在交互中，照常收尾
    var pickRound = TransferContext.ForPick("Robot1", 1, 5);
    Throws<ArgumentNullException>(() => station.HandoffPick(null!));
    Check(station.PrepareTransfer(pickRound) is not null && station.HandoffPick(pickRound) is null,
        "还没开始交互不能交接");
    Check(station.PrepareTransfer2(pickRound) is not null && station.Transferring(pickRound)
          && station.HandoffPick(pickRound) is null && station.Handoffs == 0,
        "没配 HandoffOnPick 的站点不受理取片交接");
    station.HandoffOnPick = true;
    station.HandoffOnPlace = true;
    Check(station.HandoffPlace(pickRound) is null && station.HandoffPick(TransferContext.ForPick("Robot1", 1, 5)) is null
          && station.Handoffs == 0,
        "只取片的轮次不能放片交接，别的轮次（内容相同）也不能交接");
    var handoff = station.HandoffPick(pickRound);
    Check(handoff is not null && handoff.IsSuccess && station.Handoffs == 1 && station.State == TransferModuleState.Transferring,
        "本轮交互中取片交接：交给站点的扩展点，站点还停在交互中");
    Check(station.TransferComplete(pickRound) && station.State == ModuleState.Idle && station.HandoffPick(pickRound) is null,
        "交接以后照常收尾回待命，旧轮不能再交接");
    var placeRound = TransferContext.ForPlace("Robot1", 1, 5);
    Check(station.PrepareTransfer(placeRound) is not null && station.PrepareTransfer2(placeRound) is not null
          && station.Transferring(placeRound) && station.HandoffPick(placeRound) is null
          && station.HandoffPlace(placeRound) is not null && station.Handoffs == 2
          && station.TransferComplete(placeRound) && station.State == ModuleState.Idle,
        "放片交接一样：只放片的轮次不收取片交接");
    station.HandoffOnPick = false;
    station.HandoffOnPlace = false;
    var plainStation = new ProbeAligner("PlainHandoff") { HandoffOnPick = true };
    var plainRound = TransferContext.ForPick("Robot1", 1, 1);
    Check(plainStation.PrepareTransfer(plainRound) is not null && plainStation.PrepareTransfer2(plainRound) is not null
          && plainStation.Transferring(plainRound) && plainStation.HandoffPick(plainRound) is null,
        "站点没重写交接扩展点：配了开关也被拒（默认没有交接动作）");

    // 真实腔体状态表：Reset 执行态仍保留传片状态，操作收尾之前不能靠旧上下文推进。
    foreach (int phase in new[] { TransferModuleState.PreTransfer, TransferModuleState.TransferReady, TransferModuleState.Transferring })
    {
        var chamber = new ProbeChamber("ContextResetChamber");
        chamber.NoteState(ModuleState.Idle);
        var beforeReset = TransferContext.ForPlace("Robot1", 1, 1);
        var afterReset = TransferContext.ForPlace("Robot1", 1, 1);
        Check(chamber.PrepareTransfer(beforeReset) is not null, "复位用例占用腔体");
        if (phase != TransferModuleState.PreTransfer)
        {
            Check(chamber.PrepareTransfer2(beforeReset) is not null, "复位用例进入准备好状态");
        }

        if (phase == TransferModuleState.Transferring)
        {
            Check(chamber.Transferring(beforeReset), "复位用例进入交互中状态");
        }

        var resetting = new ProbeOperation();
        chamber.NextReset = resetting;
        Check(ReferenceEquals(chamber.Reset(), resetting) && chamber.State == phase,
            "腔体真实状态表在复位执行中保留原来的传片状态");
        Check(chamber.PrepareTransfer2(beforeReset) is null && !chamber.Transferring(beforeReset)
              && !chamber.TransferComplete(beforeReset) && !chamber.CancelTransfer(beforeReset)
              && chamber.PrepareTransfer(afterReset) is null && chamber.State == phase,
            "复位执行中：上下文和状态即使仍匹配，也不能推进或撤销传片环");
        resetting.Succeed();
        Check(!resetting.IsSettled && !chamber.Transferring(beforeReset), "复位已达终态但模块尚未收尾时仍拒绝旧交互");
        chamber.Tick();
        Check(chamber.State == ModuleState.Idle && chamber.CancelTransfer(beforeReset)
              && chamber.PrepareTransfer2(beforeReset) is null && !chamber.TransferComplete(beforeReset),
            "复位收尾后旧轮无事可撤，旧准备和完成不能推进");
        Check(chamber.PrepareTransfer(afterReset) is not null && chamber.CancelTransfer(beforeReset)
              && chamber.State == TransferModuleState.PreTransfer && chamber.CancelTransfer(afterReset),
            "复位后准备一覆盖残留上下文，旧撤销不影响新一轮");
    }

    var idleReset = new ProbeChamber("ContextIdleReset");
    idleReset.NoteState(ModuleState.Idle);
    idleReset.NextReset = new ProbeOperation();
    Check(idleReset.Reset() is not null && !idleReset.CanPrepareTransfer
          && idleReset.CheckPrepareTransfer()?.ErrorMessage == ErrorCodes.ModuleBusy
          && idleReset.PrepareTransfer(context) is null, "待命状态执行复位时，查询和准备入口也都拒绝占用");
    idleReset.NextReset.Succeed();
    idleReset.Tick();
    Check(idleReset.CanPrepareTransfer, "复位真正收尾后允许申请交接");

    // 真实 LoadPort 恢复路径使用 Abort，而非给传片状态补 Reset。
    var contextPort = new ProbePort("ContextAbortPort");
    Check(contextPort.InitComponent(), "上下文中止用例端口初始化");
    ComponentLoader.BindAll([contextPort]);
    contextPort.NotePodPlaced(true);
    contextPort.NoteStatus(new LoadPortStatus { IsPresent = true, IsPlaced = true, IsDoorOpen = true });
    contextPort.Tick();
    contextPort._carrier.SetId("CONTEXT-CARRIER");
    contextPort._carrier.UpdateStatus(CarrierIdStatus.Verified, CarrierSlotMapStatus.Verified);
    foreach (int phase in new[] { TransferModuleState.PreTransfer, TransferModuleState.TransferReady, TransferModuleState.Transferring })
    {
        contextPort.NoteState(LoadPortState.Loaded);
        var beforeAbort = TransferContext.ForPick("Robot1", 1, 1);
        var afterAbort = TransferContext.ForPick("Robot1", 1, 1);
        Check(contextPort.PrepareTransfer(beforeAbort) is not null, "中止用例占用端口");
        if (phase != TransferModuleState.PreTransfer)
        {
            Check(contextPort.PrepareTransfer2(beforeAbort) is not null, "中止用例端口准备好");
        }

        if (phase == TransferModuleState.Transferring)
        {
            Check(contextPort.Transferring(beforeAbort), "中止用例端口交互中");
        }

        Check(contextPort.Reset() is null && contextPort.State == phase, "LoadPort 传片状态不通过 Reset 退出");
        var aborting = new ProbeOperation();
        contextPort.Next = aborting;
        Check(ReferenceEquals(contextPort.Abort(), aborting) && contextPort.State == ModuleState.Aborting
              && !contextPort.Transferring(beforeAbort), "LoadPort 中止执行中旧轮不能交互");
        aborting.Succeed();
        contextPort.Tick();
        Check(contextPort.State == LoadPortState.Loaded && contextPort.CancelTransfer(beforeAbort), "中止完成按门位恢复 Loaded，旧轮可以无操作撤销");
        Check(contextPort.PrepareTransfer(afterAbort) is not null && contextPort.CancelTransfer(beforeAbort)
              && contextPort.State == TransferModuleState.PreTransfer && contextPort.CancelTransfer(afterAbort),
            "LoadPort 中止后新轮覆盖旧上下文，旧撤销不会导致新轮丢失占用");
    }

    contextPort.Close();

    // 搬运层确实传递本站信息；同站换槽共用同一上下文，准备一次、最后收尾一次。
    var robot = new ProbeTransferRobot { AllowMotion = true };
    TransferRoutine Run(ITransferStation? source, int pickSlot, ITransferStation? target, int placeSlot)
    {
        var routine = new TransferRoutine(TransferOrigin.Manual, robot, source, pickSlot, target, placeSlot, 4, 1000);
        for (int scan = 0; scan < 32 && !routine.IsTerminal; scan++)
        {
            routine.Scan();
        }

        Check(routine.IsSuccess, "上下文贯通用例搬运完成");
        return routine;
    }

    ProbeStation Station(string name) => new(name)
    {
        First = () => new NoOpOperation("Prepare"),
        Second = () => new NoOpOperation("Prepare2"),
    };

    void CheckCalls(ProbeStation station, int? pickSlot, int? placeSlot, params string[] steps)
    {
        var first = station.TransferCalls[0].Context;
        Check(station.TransferCalls.Select(call => call.Step).SequenceEqual(steps)
              && station.TransferCalls.All(call => ReferenceEquals(call.Context, first))
              && first.Robot == robot.Name && (first.PickArm == 4) == (pickSlot is not null) && (first.PlaceArm == 4) == (placeSlot is not null)
              && first.PickSlot == pickSlot && first.PlaceSlot == placeSlot,
            "站点每个入口取得同一实例，且只描述本站的取片段（手、槽）和放片段（手、槽）");
    }

    var source = Station("ContextLP");
    var target = Station("ContextPM");
    Run(source, 5, target, 1);
    CheckCalls(source, 5, null, "Prepare", "Prepare2", "Transferring", "Complete");
    CheckCalls(target, null, 1, "Prepare", "Prepare2", "Transferring", "Complete");
    Check(!ReferenceEquals(source.TransferCalls[0].Context, target.TransferCalls[0].Context), "跨站搬运的两个站点各有自己的上下文");
    var onlyPick = Station("ContextPickOnly");
    Run(onlyPick, 3, null, 4);
    CheckCalls(onlyPick, 3, null, "Prepare", "Prepare2", "Transferring", "Complete");
    var onlyPlace = Station("ContextPlaceOnly");
    Run(null, 4, onlyPlace, 2);
    CheckCalls(onlyPlace, null, 2, "Prepare", "Prepare2", "Transferring", "Complete");
    var relocate = Station("ContextRelocate");
    Run(relocate, 5, relocate, 8);
    CheckCalls(relocate, 5, 8, "Prepare", "Prepare2", "Transferring", "NextLeg", "Transferring", "Complete");

    // 分段取放：站点配了中途交接，机械手伸到位停下，站点交接完才让它接着缩臂；按站点、按取放各看各的开关
    var stepRobot = new ProbeStepRobot { AllowMotion = true };
    TransferExecution RunStepped(TransferExecution execution)
    {
        for (int scan = 0; scan < 32 && !execution.IsTerminal; scan++)
        {
            execution.Scan();
        }

        return execution;
    }

    ProbeStation HandoffStation(string name, bool pick, bool place) => new(name)
    {
        First = () => new NoOpOperation("Prepare"),
        Second = () => new NoOpOperation("Prepare2"),
        HandoffOnPick = pick,
        HandoffOnPlace = place,
    };

    var handoffSource = HandoffStation("HandoffLP", true, false);
    var handoffTarget = HandoffStation("HandoffPM", false, true);
    Check(RunStepped(new TransferRoutine(TransferOrigin.Manual, stepRobot, handoffSource, 5, handoffTarget, 1, 4, 1000)).IsSuccess
          && stepRobot.Calls.SequenceEqual(["ExtendPick", "RetractPick", "ExtendPlace", "RetractPlace"]),
        "两头都配了交接：机械手分段取、分段放，各停一下等站点交接再继续");
    CheckCalls(handoffSource, 5, null, "Prepare", "Prepare2", "Transferring", "HandoffPick", "Complete");
    CheckCalls(handoffTarget, null, 1, "Prepare", "Prepare2", "Transferring", "HandoffPlace", "Complete");

    stepRobot.Calls.Clear();
    var plainSource = Station("PlainLP");
    var placeHandoffTarget = HandoffStation("HandoffPM2", false, true);
    Check(RunStepped(new TransferRoutine(TransferOrigin.Manual, stepRobot, plainSource, 5, placeHandoffTarget, 1, 4, 1000)).IsSuccess
          && stepRobot.Calls.SequenceEqual(["Pick", "ExtendPlace", "RetractPlace"]),
        "只看这一头的开关：来源没配就一条指令取完，目标配了放片才分段");
    CheckCalls(plainSource, 5, null, "Prepare", "Prepare2", "Transferring", "Complete");

    stepRobot.Calls.Clear();
    var handoffRelocate = HandoffStation("HandoffRelocate", true, true);
    Check(RunStepped(new TransferRoutine(TransferOrigin.Manual, stepRobot, handoffRelocate, 5, handoffRelocate, 8, 4, 1000)).IsSuccess
          && stepRobot.Calls.SequenceEqual(["ExtendPick", "RetractPick", "ExtendPlace", "RetractPlace"]),
        "同站换槽两段各分段一次");
    CheckCalls(handoffRelocate, 5, 8, "Prepare", "Prepare2", "Transferring", "HandoffPick", "NextLeg", "Transferring", "HandoffPlace", "Complete");

    stepRobot.Calls.Clear();
    var handoffSwapStation = HandoffStation("HandoffSwap", true, true);
    var steppedSwap = new SwapRoutine(new SwapRequest
    {
        Robot = stepRobot.Name,
        Station = handoffSwapStation.Name,
        Slot = 1,
        PickArm = 4,
        PlaceArm = 5,
        PickWafer = Guid.NewGuid(),
        PlaceWafer = Guid.NewGuid(),
    }, stepRobot, handoffSwapStation, "A", "B", 1000);
    Check(RunStepped(steppedSwap).IsSuccess
          && steppedSwap.Wafers[0].Progress == TransferWaferProgress.Picked && steppedSwap.Wafers[1].Progress == TransferWaferProgress.Placed
          && stepRobot.Calls.SequenceEqual(["ExtendPick", "RetractPick", "ExtendPlace", "RetractPlace"])
          && handoffSwapStation.TransferCalls.Select(call => call.Step)
              .SequenceEqual(["Prepare", "Prepare2", "Transferring", "HandoffPick", "NextLeg", "Transferring", "HandoffPlace", "Complete"]),
        "交换两段都分段：取旧片交接一次，换段后放新片再交接一次");

    // 交接被拒、没做成：机械手停着不继续（就地中止），整趟失败挂起等人工，站点不收尾
    stepRobot.Calls.Clear();
    var refusingStation = new ProbeStation("HandoffRefused")
    {
        First = () => new NoOpOperation("Prepare"),
        Second = () => new NoOpOperation("Prepare2"),
        HandoffOnPick = true,
        Handoff = () => null,
    };
    var handoffRefused = RunStepped(new TransferRoutine(TransferOrigin.Manual, stepRobot, refusingStation, 5, null, 4, 4, 1000));
    Check(handoffRefused.Code == ErrorCodes.StationHandoffRejected && handoffRefused.ErrorArgs.SequenceEqual(["HandoffRefused"])
          && handoffRefused.MotionStarted && handoffRefused.NeedsRecovery && stepRobot.Calls.SequenceEqual(["ExtendPick"])
          && refusingStation.TransferCalls.Select(call => call.Step).SequenceEqual(["Prepare", "Prepare2", "Transferring", "HandoffPick"]),
        "交接被拒：transfer.station_handoff_rejected [站点]，机械手不继续，整趟挂起等人工，站点不收尾");

    stepRobot.Calls.Clear();
    var failingStation = new ProbeStation("HandoffFailed")
    {
        First = () => new NoOpOperation("Prepare"),
        Second = () => new NoOpOperation("Prepare2"),
        HandoffOnPlace = true,
        Handoff = () =>
        {
            var failed = new ProbeOperation();
            failed.Reject();
            return failed;
        },
    };
    var handoffFailed = RunStepped(new TransferRoutine(TransferOrigin.Manual, stepRobot, null, 4, failingStation, 1, 4, 1000));
    Check(handoffFailed.Code == ErrorCodes.StationHandoffFailed && handoffFailed.ErrorArgs.SequenceEqual(["HandoffFailed"])
          && handoffFailed.NeedsRecovery && stepRobot.Calls.SequenceEqual(["ExtendPlace"])
          && failingStation.TransferCalls.All(call => call.Step != "Complete"),
        "交接没做成：transfer.station_handoff_failed [站点]，同样停着不继续、整趟挂起");

    var noStepRobot = new ProbeTransferRobot { AllowMotion = true };
    var noStepSource = HandoffStation("HandoffNoStep", true, false);
    var noStepTarget = Station("HandoffNoStepTarget");
    var unsupported = RunStepped(new TransferRoutine(TransferOrigin.Manual, noStepRobot, noStepSource, 5, noStepTarget, 1, 4, 1000));
    Check(unsupported.Code == ErrorCodes.RobotHandoffUnsupported && unsupported.ErrorArgs.SequenceEqual(["TransferRobot"])
          && !unsupported.MotionStarted && !unsupported.NeedsRecovery && noStepSource.Cancels == 1 && noStepTarget.Cancels == 1
          && noStepRobot.Calls.Count == 0,
        "机械手没有分段指令：落交互中标记之前就拒（robot.handoff_unsupported），没动过手，两头的环都还回去");

    // 站点交接还没做完就被中止：交接跟着中止，机械手停在站里，动过手，整趟挂起等人工
    stepRobot.Calls.Clear();
    var slowHandoff = new ProbeOperation();
    var slowStation = new ProbeStation("HandoffSlow")
    {
        First = () => new NoOpOperation("Prepare"),
        Second = () => new NoOpOperation("Prepare2"),
        HandoffOnPick = true,
        Handoff = () => slowHandoff,
    };
    var slowRoutine = new TransferRoutine(TransferOrigin.Manual, stepRobot, slowStation, 5, null, 4, 4, 1000);
    for (int scan = 0; scan < 10; scan++)
    {
        slowRoutine.Scan();
    }

    Check(!slowRoutine.IsTerminal && stepRobot.Calls.SequenceEqual(["ExtendPick"]) && slowStation.TransferCalls[^1].Step == "HandoffPick",
        "站点交接没做完：机械手一直停着等");
    slowRoutine.AbortByHost("冒烟中止");
    Check(slowRoutine.State == OperationState.Aborted && slowRoutine.NeedsRecovery
          && slowHandoff.State == OperationState.Aborted && slowHandoff.AbortCount == 1,
        "停着等交接时被中止：站点的交接跟着中止，整趟挂起等人工");

    var cancelled = new ContextProbeStation();
    var oldRoutine = new TransferRoutine(TransferOrigin.Manual, robot, null, 4, cancelled, 1, 4, 1000);
    oldRoutine.Scan();
    cancelled.NoteState(ModuleState.Idle); // 模拟恢复完成，状态与上下文身份的真实覆盖已在上面的 Reset / Abort 用例验证。
    var replacementContext = TransferContext.ForPlace(robot.Name, 4, 1);
    Check(cancelled.PrepareTransfer(replacementContext) is not null, "恢复后的另一轮取得站点");
    oldRoutine.AbortByHost("old round cancelled after recovery");
    Check(!oldRoutine.NeedsRecovery && cancelled.State == TransferModuleState.PreTransfer
          && cancelled.CancelTransfer(replacementContext), "旧轮撤销不会改新轮状态，也不会制造不必要的搬运挂起");
}

// LP-1：载具供 Job 使用与开始一次搬运准备是两种能力；查询只返回原因，实际发起准备仍须重查并独占传片环。
{
    var accessPort = new ProbePort("CarrierAccessPort");
    Check(accessPort.InitComponent(), "载具准入测试端口初始化成功");
    ComponentLoader.BindAll([accessPort]);
    IJobAccess jobAccess = accessPort;
    ICarrierAccess carrierAccess = jobAccess.Carrier;
    ITransferStation transferAccess = accessPort;
    var accessContext = TransferContext.ForPick("AccessRobot", 1, 1);

    Check(!carrierAccess.IsArrived && carrierAccess.Info is null && carrierAccess.CarrierId is null && carrierAccess.SlotMap.Count == 0,
        "Job 经 ICarrierAccess 读取空端口，无需取得载具维护接口");

    void RefusesAccess(HandleResult? error, string code, string message)
    {
        Check(error is not null && error.ErrorMessage == code && error.Args.SequenceEqual(new[] { accessPort.Name }), message);
    }

    accessPort.NoteState(LoadPortState.Loaded);
    RefusesAccess(jobAccess.CheckCarrierForJob(), ErrorCodes.CarrierNotArrived, "Job 查询：载具未到位，返回端口名和具体原因");
    RefusesAccess(transferAccess.CheckPrepareTransfer(), ErrorCodes.CarrierNotArrived, "搬运查询：Loaded 但载具未到位，同样返回具体原因");
    Check(!jobAccess.CanUseCarrierForJob && !transferAccess.CanPrepareTransfer && accessPort.PrepareTransfer(accessContext) is null
          && accessPort.State == LoadPortState.Loaded && accessPort.CurrentOperation is null,
        "载具不在：查询与动作都拒绝，状态和操作不变");

    accessPort.NoteState(ModuleState.Idle);
    accessPort.NotePodPlaced(true);
    accessPort.Tick();
    var arrivedCarrier = carrierAccess.Info;
    Check(carrierAccess.IsArrived && arrivedCarrier?.Location == accessPort.Name && arrivedCarrier.Capacity == accessPort.SlotCount,
        "事先取得的载具读取入口能观察到后来到位的载具");
    RefusesAccess(jobAccess.CheckCarrierForJob(), ErrorCodes.CarrierNotLoaded, "载具到了但未装载：Job 查询返回未装载");
    var notStandby = transferAccess.CheckPrepareTransfer();
    Check(!jobAccess.CanUseCarrierForJob && !transferAccess.CanPrepareTransfer
          && notStandby?.ErrorMessage == ErrorCodes.ModuleStateNotAllowed
          && notStandby.Args.SequenceEqual(new[] { accessPort.Name, ModuleState.Idle.ToString() })
          && accessPort.State == ModuleState.Idle && accessPort.Calls == 0,
        "查询不发装载指令、不改变状态；搬运查询保留状态码参数");

    accessPort.NoteState(LoadPortState.Loaded);
    RefusesAccess(jobAccess.CheckCarrierForJob(), ErrorCodes.CarrierIdRequired, "已装载但载具号未取得：Job 准入返回具体原因");
    Check(!jobAccess.CanUseCarrierForJob && transferAccess.CanPrepareTransfer,
        "Job 需要有效载具号，机械手手动搬运的准备条件继续独立判断");
    accessPort._carrier.SetId("ACCESS-A");
    accessPort._carrier.UpdateStatus(CarrierIdStatus.Read, null);
    Check(accessPort.Mode == ModuleMode.Offline && carrierAccess.Info?.SlotMapStatus == CarrierSlotMapStatus.NotRead
          && jobAccess.CanUseCarrierForJob && jobAccess.CheckCarrierForJob() is null
          && transferAccess.CanPrepareTransfer && transferAccess.CheckPrepareTransfer() is null,
        "RFID 已读到有效载具号：未接 EAP 无需额外认定，查询也不增加自动调度的 Online 限制");

    Check(accessPort.PrepareTransfer(accessContext) is not null && accessPort.State == TransferModuleState.PreTransfer,
        "通过查询后仍由 PrepareTransfer 正式占用传片环");
    var preparing = transferAccess.CheckPrepareTransfer();
    Check(!transferAccess.CanPrepareTransfer && preparing?.ErrorMessage == ErrorCodes.ModuleStateNotAllowed
          && preparing.Args.SequenceEqual(new[] { accessPort.Name, TransferModuleState.PreTransfer.ToString() })
          && jobAccess.CanUseCarrierForJob && jobAccess.CheckCarrierForJob() is null,
        "端口已在准备：拒绝另一轮搬运准备，载具供 Job 使用的条件仍成立");
    Check(accessPort.PrepareTransfer(accessContext) is null && accessPort.State == TransferModuleState.PreTransfer,
        "重复启动搬运准备被拒，已经占用的传片环不变");
    Check(accessPort.PrepareTransfer2(accessContext) is not null && accessPort.State == TransferModuleState.TransferReady
          && jobAccess.CanUseCarrierForJob && !transferAccess.CanPrepareTransfer,
        "准备二沿用本轮交互，载具准入与下一轮搬运准备分别判断");
    Check(accessPort.Transferring(accessContext) && jobAccess.CanUseCarrierForJob && !transferAccess.CanPrepareTransfer,
        "正在取放：载具仍具备 Job 准入条件，不能开始另一轮搬运准备");
    Check(accessPort.TransferComplete(accessContext) && accessPort.State == LoadPortState.Loaded && transferAccess.CanPrepareTransfer,
        "本轮完成恢复 Loaded 后，允许下一轮搬运准备");

    var deviceMap = Enumerable.Repeat(SlotState.Empty, accessPort.SlotCount).ToArray();
    deviceMap[0] = SlotState.Normal;
    accessPort.NoteMap(deviceMap);
    var publishedMap = carrierAccess.SlotMap;
    deviceMap[0] = SlotState.Empty;
    Check(publishedMap.Count == accessPort.SlotCount && publishedMap[0] == SlotState.Normal,
        "载具读取入口返回 Mapping 快照，调用方修改原始设备数据不影响发布的槽图");
    if (publishedMap is IList<SlotState> mutableMap)
    {
        Throws<NotSupportedException>(() => mutableMap[0] = SlotState.Empty);
    }
    Check(carrierAccess.SlotMap[0] == SlotState.Normal,
        "读取方不能通过集合写接口更改已发布的槽图");

    // 查询成功不代表预留了许可：Host 状态随后变化，真正发起时必须拒绝。
    var accessEvents = new RecordingE87Callback();
    accessPort.E87Callback = accessEvents;
    accessPort._carrier.UpdateStatus(null, CarrierSlotMapStatus.Verified);
    Check(jobAccess.CanUseCarrierForJob && transferAccess.CanPrepareTransfer
          && carrierAccess.Info?.IdStatus == CarrierIdStatus.Read,
        "保持 EAP 原规则：槽图通过即可，未新增载具 ID 必须认定的限制");
    foreach (var mapStatus in new[] { CarrierSlotMapStatus.Read, CarrierSlotMapStatus.WaitingForHost, CarrierSlotMapStatus.VerifyFailed })
    {
        accessPort._carrier.UpdateStatus(null, mapStatus);
        Check(!carrierAccess.IsAccepted && carrierAccess.Info?.SlotMapStatus == mapStatus
              && arrivedCarrier?.SlotMapStatus == CarrierSlotMapStatus.NotRead,
            $"槽图 {mapStatus}：读取入口反映当前认定状态，先前取得的载具快照保持不变");
        RefusesAccess(jobAccess.CheckCarrierForJob(), ErrorCodes.CarrierNotAccepted, $"槽图 {mapStatus}：Job 查询返回载具未被认可");
        RefusesAccess(transferAccess.CheckPrepareTransfer(), ErrorCodes.CarrierNotAccepted, $"槽图 {mapStatus}：搬运查询返回相同的认可原因");
        Check(!jobAccess.CanUseCarrierForJob && !transferAccess.CanPrepareTransfer && accessPort.PrepareTransfer(accessContext) is null
              && accessPort.State == LoadPortState.Loaded && accessPort.CurrentOperation is null,
            $"槽图 {mapStatus}：布尔属性与拒绝原因一致，之前查询成功也不能绕过动作入口重查");
    }

    accessPort._carrier.UpdateStatus(null, CarrierSlotMapStatus.Verified);
    typeof(ComponentBase).GetProperty(nameof(ComponentBase.IsEnabled))!.SetValue(accessPort, false);
    RefusesAccess(jobAccess.CheckCarrierForJob(), ErrorCodes.ModuleDisabled, "组件停用：载具 Job 查询返回模块停用");
    Check(!jobAccess.CanUseCarrierForJob, "组件停用：Job 布尔属性与查询一致");
    typeof(ComponentBase).GetProperty(nameof(ComponentBase.IsEnabled))!.SetValue(accessPort, true);

    RefusesAccess(jobAccess.NotifyJobFinished(" "), ErrorCodes.CarrierIdRequired, "作业结束通知必须携带 Job 保存的载具号");
    var wrongCarrier = jobAccess.NotifyJobFinished("ACCESS-B");
    Check(!wrongCarrier.IsSuccess && wrongCarrier.ErrorMessage == ErrorCodes.CarrierIdMismatch
          && wrongCarrier.Args.SequenceEqual(new[] { accessPort.Name, "ACCESS-B", "ACCESS-A" })
          && carrierAccess.Info?.AccessStatus != CarrierAccessStatus.Complete,
        "载具号不匹配：返回期望与实际载具号，不改变当前载具的访问状态");
    accessPort.NoteState(ModuleState.Error);
    Check(jobAccess.NotifyJobFinished("access-a").IsSuccess && accessPort.State == ModuleState.Error
          && carrierAccess.Info?.AccessStatus == CarrierAccessStatus.Complete
          && accessEvents.Wait(nameof(IE87Callback.CarrierComplete)),
        "完成或中止收尾按 CarrierId 核对，不要求端口在 Loaded，也不改变端口状态");
    var completedCarrier = carrierAccess.Info;
    Check(jobAccess.NotifyJobFinished("ACCESS-A").IsSuccess && ReferenceEquals(completedCarrier, carrierAccess.Info),
        "相同载具重复通知成功，不重复更新完成状态");
    accessPort.SetAutoMode(true);
    Check(accessEvents.Wait(nameof(IE87Callback.AutoModeChanged)) && accessEvents.Count(nameof(IE87Callback.CarrierComplete)) == 1,
        "同一上报队列的后续通知已处理：重复结束没有重复上报 E87 CarrierComplete");
    accessPort.NoteState(LoadPortState.Loaded);
    accessPort.Shell.Comm.Drop();
    Check(jobAccess.CanUseCarrierForJob && transferAccess.CanPrepareTransfer,
        "保持原查询条件：查询不增加设备连接限制，设备动作及自动调度继续各自检查");
    accessPort.NotePodPlaced(false);
    accessPort.Tick();
    Check(!carrierAccess.IsArrived && carrierAccess.Info is null && carrierAccess.CarrierId is null && carrierAccess.SlotMap.Count == 0
          && publishedMap[0] == SlotState.Normal,
        "载具移走后，同一读取入口反映空端口，调用方已取得的槽图快照仍保持不变");
    RefusesAccess(jobAccess.NotifyJobFinished("ACCESS-A"), ErrorCodes.CarrierNotArrived, "载具已取走：结束通知拒绝，不凭空完成载具");
    accessPort.NotePodPlaced(true);
    accessPort.Tick();
    accessPort._carrier.SetId("ACCESS-B");
    var oldNotification = jobAccess.NotifyJobFinished("ACCESS-A");
    Check(oldNotification.ErrorMessage == ErrorCodes.CarrierIdMismatch
          && carrierAccess.CarrierId == "ACCESS-B" && carrierAccess.Info?.AccessStatus != CarrierAccessStatus.Complete,
        "端口换成另一载具：旧 Job 的 CarrierId 通知不能完成新载具");
    accessPort.Close();

    // 普通站点只实现搬运能力即可；并发发起仍只有一个调用能取得传片环。
    var station = new ProbeAligner("TransferAccessStation");
    ITransferStation stationAccess = station;
    Check(stationAccess.CheckPrepareTransfer() is null && stationAccess.CanPrepareTransfer
          && (object)station is not IJobAccess,
        "普通站点复用搬运查询，无需实现载具 Job 能力");
    var attempts = new ModuleOperation?[8];
    var attemptContexts = Enumerable.Range(1, attempts.Length)
        .Select(arm => TransferContext.ForPlace("ParallelRobot", arm, 1)).ToArray();
    Parallel.For(0, attempts.Length, index => attempts[index] = station.PrepareTransfer(attemptContexts[index]));
    Check(attempts.Count(operation => operation is not null) == 1 && !stationAccess.CanPrepareTransfer
          && stationAccess.CheckPrepareTransfer()?.ErrorMessage == ErrorCodes.ModuleStateNotAllowed,
        "八次并发准备只有一次成功；查询不会代替锁内占用");
    var winner = Array.FindIndex(attempts, operation => operation is not null);
    Check(station.CancelTransfer(attemptContexts[winner]) && stationAccess.CanPrepareTransfer && stationAccess.CheckPrepareTransfer() is null,
        "普通站点撤回准备后恢复可用");
}

Console.WriteLine($"PASS: {checks} operation wait checks (including 200 completion races, the LoadPort action entries over RPC (every action checks state, resource and interlock and returns the reason; reset/abort asked for the reason after a refusal; terminal results translated; a disconnected driver refused up front while abort still replaces the in-flight action), the online/offline and auto/manual mode switches, the EAP callback path, the carrier component: its lifecycle from arrival to removal, a Host-accepted slot map staying accepted across a re-map, the automatic carrier-id read retrying while the reader is down and reporting a read failure after the reader timeout, and a port without its _carrier node refusing to open, a disabled port still attaching its carrier so scanning and the state push work, the Load mapping check (a slot count mismatch or crossed/double/unrecognized slots failing the Load with the mapping alarm), the auto unload once the carrier completes (waiting for the robot and for wafers still out; with the AutoUnload EC off, a ledger check against the load-time slot map instead, tolerating wafers returned to another LoadPort), the E84 alarm for a carrier placed or removed without a handoff in Auto, the unload mapping check against the wafer ledger (when AutoRunMapOnUnload is on: CULOD first, then an active CLDMP scan once the door is closed; missing, extra or crossed wafers failing the Unload with the mapping alarm and leaving the ledger alone, no scan after a failed close, a failed scan failing the Unload, while the manual unload ignores that switch: plain CULOD, no scan, no check, no ledger change), and robot pick/place writing the wafer ledger, LoadPort/_robot alarms raised and cleared only by a manual reset, the E84 handoff flow: load, unload, gating, abort, timeout and recovery, DI/AI alarm debounce with the module-level HasAlarm, the program's and the whole computer's CPU/memory usage and thread count sampled once per EC interval, and the EC component: live read/write, declaration merge, fallback when not installed and an ec.xml round trip, and the init/abort hooks: InitComponent (no hardware motion) recursing through every level of children by InitOrder with a failing child not holding back its siblings, optional overrides, InitModule = Home leaving the children alone, the E84 and driver components connecting themselves from InitComponent, and Abort without clearing alarms, and transfer routine failures reported with the station, the preparation step number and the wait time as error args, and the main page backend: LoadPort/robot lists in the system settings, station kinds for the dispatch map, the system module behind the equipment service (init all: robots first, then the rest, idle when all succeed and back to not initialized on a refusal or failure; Auto/Manual switching on the spot and driving the dispatch switch; Stop waiting for the actions in flight to settle and going back to where it stopped from; return all recording the wafers it cannot return), the system module kept out of the IO module list, and the LoadPort presence source: query (both bits) or event, status queries paced by the EC interval with timeout recovery, abandoning in-flight driver commands, LoadPort/_rfid reconnect capped at the EC count (then stopping with an alarm until a manual reset starts over, the PLC too), an _rfid outage not blocking the LoadPort and frame pump sessions across reconnects, the robot status query (one round in order: subscription retried at the start of each round until it succeeds, then error, servo, speed and every axis, replies landing on the axis or state they asked about, a lost reply abandoned on timeout so the same query goes out again, a late no-error reply not taken for an error push, closing the connection abandoning in-flight commands, and no wafer-event subscription when the SC switch is off, the EC interval pacing whole rounds, reconnecting in the background after a drop and subscribing again, and a timed-out action abandoning its command so the same command can go out again), and the LoadPort end states after Reset/Abort: NotInit after an error, an interrupted motion or an unknown door, Loaded/Idle by the door position, and the entry checks: no Load without a carrier or while an E84 handoff is running, no Unload or door-open Home while wafers are still out, Home from NotInit not blocked by wafers out, machine-specific interlocks with their own reasons on Load/Unload/Home/Clamp/Unclamp).");

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

    /// <summary>发出去的帧，按发出的先后。</summary>
    public IReadOnlyList<string> Sent => _sent.ToArray();
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

    /// <summary>机型自己加的联锁不过时回的原因（机型用自己的错误码）。</summary>
    public const string BlockedCode = "smoke.blocked";

    private HandleResult? Blocked(LoadPortAction action)
    {
        return BlockedAction == action ? HandleResult.Fail(BlockedCode, Name) : null;
    }

    protected override HandleResult? LoadInterlock() => base.LoadInterlock() ?? Blocked(LoadPortAction.Load);
    protected override HandleResult? UnloadInterlock() => base.UnloadInterlock() ?? Blocked(LoadPortAction.Unload);
    protected override HandleResult? HomeInterlock() => base.HomeInterlock() ?? Blocked(LoadPortAction.Home);
    protected override HandleResult? ClampInterlock() => base.ClampInterlock() ?? Blocked(LoadPortAction.Clamp);
    protected override HandleResult? UnclampInterlock() => base.UnclampInterlock() ?? Blocked(LoadPortAction.Unclamp);

    /// <summary>
    /// 建设备动作：记一次，给摆好的 Next；没摆就给一个下一拍就做完的探针动作（像一台马上做完的设备——
    /// 复位、报警复位这类测试不关心动作本身，别让它一直挂着挡住后面的动作）。
    /// </summary>
    private ModuleOperation Take()
    {
        Calls++;
        return Next ?? new ProbeOperation { FinishOnScan = true };
    }

    // 动作入口固定（三项检查都在里面），机型只能换设备动作
    protected override ModuleOperation CreateLoadOperation() => Take();
    protected override ModuleOperation CreateUnloadOperation(bool mapAndVerify) => Take();
    protected override ModuleOperation CreateHomeOperation() => Take();
    protected override ModuleOperation CreateResetOperation() => Take();
    protected override ModuleOperation CreateAbortOperation() => Take();
    protected override ModuleOperation CreateClampOperation() => Take();
    protected override ModuleOperation CreateUnclampOperation() => Take();
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

// 探针 PLC：连接由测试摆（连不上 / 能连），数连了几次；扫描换成测试推。
sealed class ProbePlc : PlcComponent
{
    private int _connects;

    public ProbePlc()
    {
        Host = "ProbePlc";
    }

    public volatile bool FailConnect = true;

    public int Connects => Volatile.Read(ref _connects);

    /// <summary>顶替扫描线程推一拍。</summary>
    public void Tick() => OnScan();

    protected override bool ConnectDevice()
    {
        Interlocked.Increment(ref _connects);
        return !FailConnect;
    }
}

// 探针电脑组件：子节点读的是真的 CPU、内存、线程数，只把扫描换成测试推。
sealed class ProbeComputer : ComputerComponent
{
    public ProbeComputer(string path)
    {
        typeof(ComponentBase).GetProperty(nameof(Name))!.SetValue(this, path);
        typeof(ComponentBase).GetProperty(nameof(FullPath))!.SetValue(this, path);
    }

    /// <summary>顶替扫描线程推一拍。</summary>
    public void Tick() => OnScan();
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

    public void HandoffStarted(ILoadPortAccess port, bool isLoad) => _received[$"HandoffStarted:{isLoad}"] = true;
    public void HandoffCompleted(ILoadPortAccess port, bool isLoad) => _received[$"HandoffCompleted:{isLoad}"] = true;
    public void HandoffTimeout(ILoadPortAccess port, bool isLoad, E84Timer timer) => _received[$"HandoffTimeout:{isLoad}:{timer}"] = true;
    public void HandoffAborted(ILoadPortAccess port, bool isLoad, string reason) => _received[$"HandoffAborted:{isLoad}"] = true;
    public void AvailabilityChanged(ILoadPortAccess port, bool available) => _received[$"AvailabilityChanged:{available}"] = true;
}

// 只记下收到了哪些回调：派发在别的线程上，等到为止。
sealed class RecordingE87Callback : IE87Callback
{
    private readonly ConcurrentDictionary<string, int> _received = new();

    public int Count(string name) => _received.GetValueOrDefault(name);

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

    private void Note([CallerMemberName] string? name = null) => _received.AddOrUpdate(name!, 1, (_, count) => count + 1);

    public void CarrierArrived(ILoadPortAccess port) => Note();
    public void CarrierRemoved(ILoadPortAccess port, string? carrierId) => Note();
    public void CarrierIdRead(ILoadPortAccess port, string carrierId) => Note();
    public void CarrierIdReadFailed(ILoadPortAccess port) => Note();
    public void SlotMapRead(ILoadPortAccess port, IReadOnlyList<SlotState> slotMap) => Note();
    public void LoadCompleted(ILoadPortAccess port) => Note();
    public void UnloadCompleted(ILoadPortAccess port) => Note();
    public void AutoModeChanged(ILoadPortAccess port, bool autoMode) => Note();
    public void AccessStarted(ILoadPortAccess port) => Note();
    public void AccessStopped(ILoadPortAccess port) => Note();
    public void CarrierComplete(ILoadPortAccess port) => Note();
    public void PortError(ILoadPortAccess port, string error) => Note();
}

// 探针机械手：动作体不连设备，只为验"取放成功之后账有没有跟着动"。
sealed class ProbeRobot : BaseRobotModule
{
    public ProbeOperation? Next { get; set; }

    /// <summary>品牌壳（真锐洁驱动 + 假通道）：测试看发出去的帧、往里推回复。</summary>
    public ProbeRobotShell Shell { get; } = new();

    public ProbeRobot() : this(new RobotArmComponent { Number = 1 })
    {
    }

    public ProbeRobot(RobotArmComponent arm)
    {
        typeof(ComponentBase).GetProperty(nameof(Name))!.SetValue(this, "SmokeRobot");
        typeof(ComponentBase).GetProperty(nameof(FullPath))!.SetValue(this, Name);
        QueryDataTimeOut = 0;
        // 查询周期 0：一圈查完下一拍就开下一圈，状态查询那段按拍数数条数；周期本身在那段最后单独测
        QueryStatusIntervalMs = 0;
        HomeTimeout = 0;
        PickTimeout = 0;
        PlaceTimeout = 0;
        ResetTimeout = 0;
        AbortTimeout = 0;
        PowerTimeout = 0;
        AddChild(Shell);
        // sc.xml 的轴节点：本冒烟装一指（生产里由 ComponentLoader 建，这里直接挂）。
        AddChild(Named(new RobotAxisComponent(), "X"));
        AddChild(Named(new RobotAxisComponent(), "Z"));
        AddChild(Named(new RobotAxisComponent(), "Theta"));
        AddChild(Named(arm, "Arm1"));
    }

    private static T Named<T>(T component, string name) where T : ComponentBase
    {
        typeof(ComponentBase).GetProperty(nameof(ComponentBase.Name))!.SetValue(component, name);
        typeof(ComponentBase).GetProperty(nameof(ComponentBase.FullPath))!.SetValue(component, name);
        return component;
    }

    /// <summary>直接摆状态，省去为了进 Idle 先跑一遍 Home。</summary>
    public void NoteState(int state) => State = state;

    /// <summary>顶替扫描线程推一拍。</summary>
    public void Tick() => OnScan();

    /// <summary>灌站点表（生产里由 ComponentLoader 把 sc.xml 节点交给 OnSettingLoaded）。</summary>
    public void NoteSettings(ModuleConfig setting) => OnSettingLoaded(setting);

    /// <summary>摆一个设备报错（生产里由查询或主动推送刷新）。</summary>
    public void NoteDeviceError(string? error) => DeviceError = error;

    // 入口检查（状态 → 资源 → 互锁）走平台的；过了以后挂上去的设备动作换成用例摆的 Next
    protected override ModuleOperation CreateHomeOperation() => Supply();
    protected override ModuleOperation CreateResetOperation() => Supply();
    protected override ModuleOperation CreateAbortOperation() => Supply();
    protected override ModuleOperation CreatePowerOnOperation() => Supply();
    protected override ModuleOperation CreatePowerOffOperation() => Supply();
    protected override ModuleOperation CreatePickOperation(int arm, int stationNumber, int slot) => Supply();
    protected override ModuleOperation CreatePlaceOperation(int arm, int stationNumber, int slot) => Supply();

    private ModuleOperation Supply() =>
        Next ?? throw new InvalidOperationException("用例忘了给 ProbeRobot.Next 摆一个操作。");
}

// 探针手指：模拟机型自己提供的有效持片反馈，不挂 DI 传感器。
sealed class ProbeFeedbackArm : RobotArmComponent
{
    public bool? Feedback { get; set; }
    public override bool IsWaferCheckEnabled => true;
    public override bool? HasWafer => Feedback;
}

// 探针腔体：只占个名字和类型（主界面那一节验站点类型、系统设置的腔体名单）；复位的设备动作由冒烟摆（NextReset），一拍一拍地推。
sealed class ProbeChamber : BaseChamberModule
{
    public ProbeChamber(string name)
    {
        typeof(ComponentBase).GetProperty(nameof(Name))!.SetValue(this, name);
        typeof(ComponentBase).GetProperty(nameof(FullPath))!.SetValue(this, name);
    }

    public ProbeOperation? NextReset { get; set; }

    public void NoteState(int state) => State = state;

    public void Tick() => OnScan();

    protected override ModuleOperation CreateResetOperation() => NextReset ?? new ProbeOperation();
}

// 探针普通站点（对中台这类）：能放片，既不是 LoadPort 也不是腔体。
sealed class ContextProbeStation : BaseTransferStationModule
{
    private int? _cleanupFrom;
    public override int State { get; protected set; } = ModuleState.Idle;
    public override int SlotCount { get; set; } = 25;
    public bool DelayCleanup { get; set; }
    public int SecondPrepares { get; private set; }
    public TransferContext? PreparedContext { get; private set; }

    public void NoteState(int state) => State = state;

    public bool FinishCleanup()
    {
        return _cleanupFrom is int from && TransferStep(from, StandbyState);
    }

    protected override ModuleOperation? PrepareTransfer2Core(TransferContext context)
    {
        SecondPrepares++;
        PreparedContext = context;
        return base.PrepareTransfer2Core(context);
    }

    /// <summary>
    /// 受理了几次中途交接（取片、放片合计）
    /// </summary>
    public int Handoffs { get; private set; }

    protected override ModuleOperation? HandoffPickCore(TransferContext context)
    {
        Handoffs++;
        return new NoOpOperation("HandoffPick");
    }

    protected override ModuleOperation? HandoffPlaceCore(TransferContext context)
    {
        Handoffs++;
        return new NoOpOperation("HandoffPlace");
    }

    protected override void OnTransferFinished(TransferContext context)
    {
        if (DelayCleanup)
        {
            _cleanupFrom = TransferModuleState.TransferComplete;
            return;
        }

        base.OnTransferFinished(context);
    }

    protected override void OnTransferCancelled(TransferContext context, int from)
    {
        if (DelayCleanup)
        {
            _cleanupFrom = from;
            return;
        }

        base.OnTransferCancelled(context, from);
    }
}

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

// 探针品牌壳：真锐洁驱动，传输换成假通道（动作被 ProbeOperation 顶替了，状态查询走的是真驱动）。
sealed class ProbeRobotShell : RejeRobotComponent
{
    /// <summary>驱动底下的假通道：测试看发出去的帧、往里推回复。</summary>
    public FakeFrameCommunication Comm { get; } = new();

    protected override IRobotDriver CreateDriver() => new RejeRobotDriver(Comm);
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

    [xyz.Components.Attributes.Alarm("探针故障", xyz.Components.Enums.AlarmCategory.OtherCategories)]
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
sealed class ProbeStation(string name) : ITransferStation, IStationTaskExecutor
{
    public List<(string Step, TransferContext Context)> TransferCalls { get; } = [];
    public Func<ModuleOperation?> First { get; init; } = () => null;
    public Func<ModuleOperation?> Second { get; init; } = () => null;
    public Func<bool> Cancel { get; init; } = () => true;
    public int Prepares { get; private set; }
    public int Cancels { get; private set; }
    public string Name => name;
    public string FullPath => Name;
    public int State => ModuleState.Idle;
    public ModuleMode Mode { get; private set; } = ModuleMode.Online;
    public bool IsEnabled => true;
    public ModuleOperation? CurrentOperation => null;
    public void Online() => Mode = ModuleMode.Online;
    public void Offline() => Mode = ModuleMode.Offline;
    public ModuleOperation? InitModule() => null;
    public ModuleOperation? Reset() => null;
    public ModuleOperation? Abort() => null;
    public int SlotCount => 1;
    public StationKind Kind => StationKind.Other;
    public bool CanPrepareTransfer => CheckPrepareTransfer() is null;
    public HandleResult? CheckPrepareTransfer() => null;

    public ModuleOperation? PrepareTransfer(TransferContext context)
    {
        TransferCalls.Add(("Prepare", context));
        Prepares++;
        return First();
    }

    public ModuleOperation? PrepareTransfer2(TransferContext context)
    {
        TransferCalls.Add(("Prepare2", context));
        return Second();
    }

    public bool Transferring(TransferContext context)
    {
        TransferCalls.Add(("Transferring", context));
        return true;
    }

    public Func<ModuleOperation?> NextLeg { get; init; } = () => new NoOpOperation("PrepareNextLeg");

    public ModuleOperation? PrepareNextLeg(TransferContext context)
    {
        TransferCalls.Add(("NextLeg", context));
        return NextLeg();
    }

    public bool HandoffOnPick { get; init; }

    public bool HandoffOnPlace { get; init; }

    /// <summary>
    /// 中途交接给什么（null = 被拒）
    /// </summary>
    public Func<ModuleOperation?> Handoff { get; init; } = () => new NoOpOperation("Handoff");

    public ModuleOperation? HandoffPick(TransferContext context)
    {
        TransferCalls.Add(("HandoffPick", context));
        return Handoff();
    }

    public ModuleOperation? HandoffPlace(TransferContext context)
    {
        TransferCalls.Add(("HandoffPlace", context));
        return Handoff();
    }

    public bool TransferComplete(TransferContext context)
    {
        TransferCalls.Add(("Complete", context));
        return true;
    }

    public bool CancelTransfer(TransferContext context)
    {
        TransferCalls.Add(("Cancel", context));
        Cancels++;
        return Cancel();
    }

    public IReadOnlyList<string> SupportedTasks => StationTaskAction.PickPlace;

    public HandleResult CheckTask(StationTaskRequest request) => HandleResult.Fail(ErrorCodes.StationTaskUnsupported, Name, request.Kind);

    public ModuleOperation? StartTask(StationTaskRequest request) => null;
}

// 探针机械手：默认拒绝动作；上下文贯通用例可开启即时完成的取放。收到的取放按先后记在 Calls。
class ProbeTransferRobot : ITransferRobot
{
    public bool AllowMotion { get; init; }

    public List<string> Calls { get; } = [];

    public string Name => "TransferRobot";
    public string FullPath => Name;
    public ModuleMode Mode => ModuleMode.Online;
    public StationKind Kind => StationKind.Robot;

    public bool IsEnabled => true;
    public ModuleOperation? CurrentOperation => null;

    public void Online()
    {
    }

    public void Offline()
    {
    }

    public int State => 0;
    public IReadOnlyDictionary<string, RobotStation> Stations { get; } = new Dictionary<string, RobotStation>();

    public bool TryGetStation(string station, [MaybeNullWhen(false)] out RobotStation config)
    {
        config = null;
        return false;
    }

    public ModuleOperation? InitModule() => null;
    public ModuleOperation? Reset() => null;
    public ModuleOperation? Abort() => null;
    public HandleResult<ModuleOperation> Pick(int arm, string station, int slot) => Move("Pick");
    public HandleResult<ModuleOperation> Place(int arm, string station, int slot) => Move("Place");
    public HandleResult? CheckPick(int arm, string station, int slot) => CheckMotion();
    public HandleResult? CheckPlace(int arm, string station, int slot) => CheckMotion();
    public ModuleOperation? Swap(int pickArm, int placeArm, string station, int slot) => null;

    private HandleResult? CheckMotion()
    {
        if (!AllowMotion)
        {
            return HandleResult.Fail(ErrorCodes.ActionRejected, Name, "0");
        }

        return null;
    }

    /// <summary>
    /// 记下这一下、当场做完；没开动作的被拒
    /// </summary>
    protected HandleResult<ModuleOperation> Move(string action)
    {
        var refused = CheckMotion();
        if (refused is not null)
        {
            return HandleResult<ModuleOperation>.Fail(refused.ErrorMessage, [.. refused.Args]);
        }

        Calls.Add(action);
        return HandleResult<ModuleOperation>.Success(new NoOpOperation(action));
    }
}

// 能分段取放的探针机械手：声明 ITransferRobotStep，伸臂、缩臂当场做完，跟取放一起按先后记在 Calls。
sealed class ProbeStepRobot : ProbeTransferRobot, ITransferRobotStep
{
    public HandleResult<ModuleOperation> ExtendPick(int arm, string station, int slot) => Move("ExtendPick");
    public HandleResult<ModuleOperation> RetractPick(int arm, string station, int slot) => Move("RetractPick");
    public HandleResult<ModuleOperation> ExtendPlace(int arm, string station, int slot) => Move("ExtendPlace");
    public HandleResult<ModuleOperation> RetractPlace(int arm, string station, int slot) => Move("RetractPlace");
}

// 整机冒烟用的探针模块：初始化挂用例摆的操作（没摆就是被拒），能挂一个手动动作，中止把在做的动作当场打断（下一拍收尾）。
class SystemProbeModule : BaseModule
{
    public SystemProbeModule(string name)
    {
        typeof(ComponentBase).GetProperty(nameof(Name))!.SetValue(this, name);
        typeof(ComponentBase).GetProperty(nameof(FullPath))!.SetValue(this, name);
    }

    public override int State { get; protected set; } = ModuleState.NotInit;

    /// <summary>下一次初始化挂的操作；null = 初始化被拒。挂上就清掉。</summary>
    public ProbeOperation? NextInit { get; set; }

    public int InitCalls { get; private set; }

    public int AbortCalls { get; private set; }

    public override ModuleOperation? InitModule()
    {
        InitCalls++;
        var operation = NextInit;
        NextInit = null;
        if (operation is null || !Run(operation))
        {
            return null;
        }

        return operation;
    }

    public void StartManual(ModuleOperation operation) => Run(operation);

    public override ModuleOperation? Abort()
    {
        AbortCalls++;
        CurrentOperation?.AbortByHost("整机停止");
        return null;
    }

    /// <summary>顶替扫描线程推一拍。</summary>
    public void Tick() => OnScan();
}

// 整机冒烟用的探针机械手：全部初始化要先认出机械手，别的动作一律不收。
sealed class SystemProbeRobot(string name) : SystemProbeModule(name), IRobot
{
    public int HomeTimeout => 0;
    public int ResetTimeout => 0;
    public int AbortTimeout => 0;
    public int PowerTimeout => 0;
    public HandleResult? CheckReset() => null;
    public HandleResult? CheckAbort() => null;
    public RobotDto CreateStateDto() => new() { Name = Name };
    public bool? IsServoOn => null;
    public string? DeviceError => null;
    public IReadOnlyDictionary<string, RobotStation> Stations { get; } = new Dictionary<string, RobotStation>();

    public bool TryGetStation(string station, [MaybeNullWhen(false)] out RobotStation config)
    {
        config = null;
        return false;
    }

    private HandleResult<ModuleOperation> Refused() => HandleResult<ModuleOperation>.Fail(ErrorCodes.ActionRejected, Name, "0");

    public HandleResult<ModuleOperation> Home() => Refused();
    public override ModuleOperation? Reset() => null;
    public HandleResult<ModuleOperation> Pick(int arm, string station, int slot) => Refused();
    public HandleResult<ModuleOperation> Place(int arm, string station, int slot) => Refused();
    public HandleResult? CheckPick(int arm, string station, int slot) => HandleResult.Fail(ErrorCodes.ActionRejected, Name, "0");
    public HandleResult? CheckPlace(int arm, string station, int slot) => HandleResult.Fail(ErrorCodes.ActionRejected, Name, "0");
    public ModuleOperation? Swap(int pickArm, int placeArm, string station, int slot) => null;
    public HandleResult<ModuleOperation> PowerOn() => Refused();
    public HandleResult<ModuleOperation> PowerOff() => Refused();
}

// 整机冒烟用的系统模块：名字跟 sc.xml 一样叫 System，测试代替扫描线程推一拍。
sealed class SmokeSystem : SystemModule
{
    public SmokeSystem()
    {
        typeof(ComponentBase).GetProperty(nameof(Name))!.SetValue(this, "System");
        typeof(ComponentBase).GetProperty(nameof(FullPath))!.SetValue(this, "System");
    }

    public void Tick() => OnScan();
}
