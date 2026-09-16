using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using xyz.Components;
using xyz.Components.Components;
using xyz.Components.Wafers;
using xyz.Configs;
using xyz.Drivers.Communication;
using xyz.Drivers.Loadport;
using xyz.Drivers.Loadport.FCD;
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

// Online/Offline 是内部模式位（不经设备协议）：置位即成功，不产生操作、不等待。
var modeResponse = await service.OnlineAsync("missing");
Check(!modeResponse.Success && modeResponse.Code == ErrorCodes.ModuleNotFound,
    "Online must report a missing module.");

Check(!port.IsAutoMode, "Probe port must start in manual mode.");
modeResponse = await service.OnlineAsync(port.Name);
Check(modeResponse.Success && port.IsAutoMode, "Online must set auto mode.");

var callsBeforeMode = port.Calls;
modeResponse = await service.OfflineAsync(port.Name);
Check(modeResponse.Success && !port.IsAutoMode && port.Calls == callsBeforeMode,
    "Offline must clear auto mode without starting a device action.");

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

Console.WriteLine($"PASS: {checks} operation wait checks (including 200 completion races, five device RPC actions, the online/offline mode switch, the EAP callback path, and the carrier lifecycle from arrival to removal with its wafer-ledger side effects).");

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
    public ProbePort()
    {
        // Production names are assigned by ComponentLoader through internal setters.
        typeof(ComponentBase).GetProperty(nameof(Name))!.SetValue(this, "WaitSmokePort");
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
    protected override LoadPortDriverBase CreateDriver() => new FcdLoadPortDriver(new FakeFrameCommunication());

    /// <summary>直接摆状态，省去为了进 Idle 先跑一遍 Home。</summary>
    public void NoteState(int state) => State = state;

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
