using ProtoBuf.Grpc;
using xyz.Components;
using xyz.Configs;
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
var actions = new Func<string, CallContext, Task<RpcResponse>>[]
{
    service.LoadAsync, service.UnloadAsync, service.HomeAsync, service.ResetAsync, service.AbortAsync,
    service.OnlineAsync, service.OfflineAsync
};
foreach (var action in actions)
{
    var response = await action("missing", default);
    Check(!response.Success && response.Code == ErrorCodes.ModuleNotFound, "Missing module response changed.");

    port.Next = null;
    response = await action(port.Name, default);
    Check(!response.Success && response.Code == ErrorCodes.ActionRejected, "Rejected action response changed.");

    port.Next = new ProbeOperation();
    port.Next.Succeed();
    response = await action(port.Name, default);
    Check(response.Success, "RPC did not return a successful operation.");

    port.Next = new ProbeOperation();
    port.Next.Reject();
    response = await action(port.Name, default);
    Check(!response.Success && response.Code == ErrorCodes.DeviceFailed && response.Args.Count == 2,
        "RPC lost device failure details.");

    port.Next = new ProbeOperation();
    port.Next.TimeOut();
    response = await action(port.Name, default);
    Check(!response.Success && response.Code == ErrorCodes.Timeout, "RPC lost action timeout details.");

    port.Next = new ProbeOperation();
    port.Next.AbortByHost("replacement");
    response = await action(port.Name, default);
    Check(!response.Success && response.Code == ErrorCodes.Aborted, "RPC returned an empty Abort error.");

    port.Next = new ProbeOperation();
    response = await action(port.Name, default);
    Check(!response.Success && response.Code == ErrorCodes.WaitTimeout
        && response.Args.SequenceEqual(new[] { "Probe", "0" })
        && port.Next.State == OperationState.Running,
        "RPC must report wait timeout without changing device operation state.");
    port.Next.Succeed();
    Check(port.Next.Wait(0), "RPC timeout prevented later completion.");

    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();
    var callsBefore = port.Calls;
    Throws<OperationCanceledException>(() => action(port.Name, cancellation.Token).GetAwaiter().GetResult());
    Check(port.Calls == callsBefore, "A request canceled before admission must not start a device action.");
}

Console.WriteLine($"PASS: {checks} operation wait checks (including 200 completion races and all seven RPC actions).");

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
                     nameof(AbortTimeout), nameof(OnlineTimeout), nameof(OfflineTimeout)
                 })
        {
            // Seed only this process's EC memory; never load or flush a configuration file.
            EC.UpsertValueMetadata(Name, key, "0", "Int", "ms", null, null, null, null, null);
        }
    }
    private ModuleOperation? Take() { Calls++; return Next; }
    public override ModuleOperation? Load() => Take();
    public override ModuleOperation? Unload() => Take();
    public override ModuleOperation? Home() => Take();
    public override ModuleOperation? Reset() => Take();
    public override ModuleOperation? Abort() => Take();
    public override ModuleOperation? Online() => Take();
    public override ModuleOperation? Offline() => Take();
}
