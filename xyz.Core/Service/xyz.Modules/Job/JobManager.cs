using System.Collections.Concurrent;
using System.Globalization;
using xyz.Common.Log;
using xyz.Components.Interfaces;
using xyz.Components.Models;
using xyz.Components;
using xyz.Components.Attributes;
using xyz.Components.Components;
using xyz.Components.Enums;
using xyz.Database.DbProvider;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Tools;

namespace xyz.Modules;

/// <summary>
/// Job 管理（sc.xml 顶层 Job 节点，自己一条扫描线程）：CJ（SEMI E94）/ PJ（SEMI E40）的建、控、跑，按 Job 自动调度。
/// 只有它改 Job 数据：外面（本地服务、以后的 EAP）的命令进队列（<see cref="IJobManager"/>），扫描线程里按顺序执行。
/// 每拍固定六步，每一步交给各自的小类：
/// ① 执行命令（JobCommandHandler，查 E94 / E40 转换表）→ ② 收搬运、工艺的结果（JobProgress）→ ③ 核对片位 →
/// ④ 跑自动转换规则（Rules）→ ⑤ 调度出计划（Scheduler 子组件）并提交（JobDispatcher）→ ⑥ 有变化才发布（推送 + 上报口）。
/// 暂停、停止这些不在调度里写判断：状态 → 闸门（JobGates）→ 调度许可，机内没片了由规则转到位。
/// </summary>
[Component(description: "Job 管理：CJ（SEMI E94）/ PJ（SEMI E40）的建、控、跑，按 Job 自动调度")]
public class JobManager : ComponentBase, IJobManager, IWaferOwnership
{
    /// <summary>
    /// 当前 Job 管理；sc.xml 里装出来即生效。冒烟与测试可以直接换成自己的实例。
    /// </summary>
    public static JobManager? Current { get; set; }

    /// <summary>每拍规则最多转几轮：转了再问一遍直到不再转，这个数只是防配错了的死循环。</summary>
    private const int MaxRulePasses = 16;

    private readonly ConcurrentQueue<JobCommand> _commands = new();
    private readonly Dictionary<string, JobCommandResult> _requests = new(StringComparer.Ordinal);
    private readonly Queue<string> _requestOrder = new();
    private readonly JobBook _book = new();
    private readonly EapNotifier _notifier;

    private readonly IReadOnlyList<IProcessJobRule> _processRules =
    [
        new SetupRule(),
        new SetupDoneRule(),
        new ProcessDoneRule(),
        new MaterialOutRule(),
        new PauseDoneRule(),
        new StopDoneRule(),
        new AbortDoneRule(),
    ];

    private readonly IReadOnlyList<IControlJobRule> _controlRules =
    [
        new SelectRule(),
        new MaterialReadyRule(),
        new AllDoneRule(),
        new EndingDoneRule(),
        new DeleteRule(),
    ];

    private JobRuntime? _runtime;
    private JobEngine? _engine;
    private JobCommandHandler? _handler;
    private JobProgress? _progress;
    private JobDispatcher? _dispatcher;
    private SchedulerComponent? _scheduler;
    private JobStore? _store;
    private volatile JobListDto _snapshot = new();

    public JobManager()
    {
        Current = this;
        _notifier = new EapNotifier(() => Name);
    }

    #region SC

    [SCEditor("True", "Job", "是否启用 Job 管理（False = 不收 Job 命令、不按 Job 调度）")]
    public bool IsEnable { get; set; } = true;

    [SCEditor("1", "Job", "同时最多几个 CJ 在跑（选中到暂停都算）；第一版一个")]
    public int MaxActiveControlJobs { get; set; } = 1;

    [SCEditor("10", "Job", "没删的 CJ 最多几个（含排队的、完成待删的）")]
    public int ControlJobCapacity { get; set; } = 10;

    [SCEditor("50", "Job", "没结束的 PJ 最多几个")]
    public int ProcessJobCapacity { get; set; } = 50;

    [SCEditor("True", "Job", "是否把 Job 存盘：开机把上次没结束的 Job 记成中止进历史（重启后不接着跑），历史接着留")]
    public bool IsPersistent { get; set; } = true;

    [SCEditor("Default", "Job", "存盘落哪个库（sc.xml 的 Database 节点名）")]
    public string Database { get; set; } = XyzDb.DefaultName;

    #endregion

    #region EC

    [VariableMark(VariableType.EC, ValueFormat.Int, unit: "ms", min: "100", max: "60000",
        @default: "5000", description: "命令等 Job 管理受理的上限（扫描线程一拍就处理，等不到说明线程卡了）")]
    public int CommandTimeoutMs
    {
        get { return GetEcInt(nameof(CommandTimeoutMs)); }
        set { SetEcInt(nameof(CommandTimeoutMs), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, min: "0", max: "1000",
        @default: "20", description: "最近结束的 CJ 留几个给界面看")]
    public int HistoryKeepCount
    {
        get { return GetEcInt(nameof(HistoryKeepCount)); }
        set { SetEcInt(nameof(HistoryKeepCount), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, min: "10", max: "10000",
        @default: "200", description: "记住最近多少个请求号的结果（同一个请求重发回同一个结果）")]
    public int RequestKeepCount
    {
        get { return GetEcInt(nameof(RequestKeepCount)); }
        set { SetEcInt(nameof(RequestKeepCount), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, unit: "ms", min: "10", max: "5000",
        @default: "200", description: "单周期慢扫描警告阈值")]
    public int SlowScanWarnMs
    {
        get { return GetEcInt(nameof(SlowScanWarnMs)); }
        set { SetEcInt(nameof(SlowScanWarnMs), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, unit: "ms", min: "10", max: "5000",
        @default: "300", description: "单周期慢扫描报警阈值")]
    public int SlowScanAlarmMs
    {
        get { return GetEcInt(nameof(SlowScanAlarmMs)); }
        set { SetEcInt(nameof(SlowScanAlarmMs), value); }
    }

    protected override int SlowScanWarnMilliseconds => SlowScanWarnMs;

    protected override int SlowScanAlarmMilliseconds => SlowScanAlarmMs;

    #endregion

    #region EAP 口子（上报；命令走 IJobManager）

    /// <summary>E40（PJ）上报口；null 表示没接 EAP，照常跑。装配时由 EAP 侧挂上。</summary>
    public IE40Callback? E40Callback { get; set; }

    /// <summary>E94（CJ）上报口；null 表示没接 EAP。跟 E40 共用一条派发线程。</summary>
    public IE94Callback? E94Callback { get; set; }

    #endregion

    #region 装配

    /// <summary>
    /// 绑定模块表（装配完、模块和搬运管理起来之后调一次）：挂到搬运管理上当晶圆归属口、订搬运单结束事件。
    /// sc.xml 里 Job 下没配 Scheduler 子节点就用默认策略。开了存盘的读回上次的 Job：不接着跑，没结束的记成中止进历史。
    /// </summary>
    public void Bind(IEnumerable<BaseModule> modules)
    {
        var environment = new JobEnvironment(modules);
        var events = new JobEvents(_notifier, () => E40Callback, () => E94Callback);
        var limits = new JobLimits(() => HistoryKeepCount, () => MaxActiveControlJobs, () => ControlJobCapacity, () => ProcessJobCapacity);
        var runtime = new JobRuntime(_book, environment, events, limits);
        var engine = new JobEngine(runtime,
            [new AbortProcessJobEffect(), new EndProcessJobEffect()],
            [new CarrierCompleteEffect(), new ArchiveControlJobEffect()]);

        _runtime = runtime;
        _engine = engine;
        _handler = new JobCommandHandler(runtime, engine);
        _progress = new JobProgress(runtime);
        _dispatcher = new JobDispatcher(runtime);
        _scheduler = FindChild<SchedulerComponent>() ?? new SchedulerComponent();

        var transfers = TransferManager.Current;
        if (transfers is not null)
        {
            transfers.Ownership = this;
            transfers.TransferFinished -= OnTransferFinished;
            transfers.TransferFinished += OnTransferFinished;
        }

        if (IsPersistent && _store is null)
        {
            var store = new JobStore(Database, () => Name);
            _store = store;
            var interrupted = JobRestart.CloseOut(store.Load(), _book, HistoryKeepCount, DateTime.Now);
            if (interrupted.Count > 0)
            {
                string jobs = string.Join("、", interrupted.Select(job => $"{job.Id}（{job.LoadPort}）"));
                LogHelper.Warn(Name, $"上次有 {interrupted.Count} 个 Job 没做完就重启了：{jobs}。重启后不接着跑，已记成中止进历史；"
                    + "机内的片到现场确认片位后全部回片，再重新建 Job");
            }
        }

        _book.Touch();
        Publish();
        LogHelper.Info(Name, $"Job 管理已绑定：调度 {_scheduler.GetType().Name}，同时最多 {MaxActiveControlJobs} 个 CJ 在跑");
    }

    private void OnTransferFinished(TransferResult result)
    {
        _progress?.Enqueue(result);
    }

    #endregion

    #region IJobManager / IWaferOwnership

    public JobListDto Snapshot => _snapshot;

    public int ProcessJobSpace => Math.Max(0, ProcessJobCapacity - _snapshot.ProcessJobs.Count);

    /// <summary>出过执行故障、自动派单暂停中。</summary>
    public bool IsHeld => _runtime?.Hold is not null;

    public string? OwnerOf(Guid waferId)
    {
        return _book.OwnerOf(waferId);
    }

    public Task<JobCommandResult> CreateLocalJobAsync(LocalJobRequest request)
    {
        return Enqueue(request.RequestId, handler => handler.CreateLocal(request));
    }

    public Task<JobCommandResult> CreateProcessJobAsync(ProcessJobSpec spec, JobCommandSource source)
    {
        return Enqueue(spec.RequestId, handler => handler.CreateProcessJob(spec, source));
    }

    public Task<JobCommandResult> CreateControlJobAsync(ControlJobSpec spec, JobCommandSource source)
    {
        return Enqueue(spec.RequestId, handler => handler.CreateControlJob(spec, source));
    }

    public Task<JobCommandResult> CommandControlJobAsync(string id, CtrlJobCommand command, CtrlJobAction action, JobCommandSource source,
        string? requestId = null)
    {
        return Enqueue(requestId, handler => handler.CommandControlJob(id, command, action));
    }

    public Task<JobCommandResult> CommandProcessJobAsync(string id, PrJobCommand command, JobCommandSource source, string? requestId = null)
    {
        return Enqueue(requestId, handler => handler.CommandProcessJob(id, command));
    }

    public Task<JobCommandResult> RecoverAsync(JobCommandSource source)
    {
        return Enqueue(null, handler => handler.Recover());
    }

    /// <summary>
    /// 整机停止：所有没结束的 Job 走中止（等设备确认、核对片位），不直接给模块发中止。
    /// </summary>
    public Task<JobCommandResult> AbortAllAsync(JobCommandSource source)
    {
        return Enqueue(null, handler => handler.AbortAll());
    }

    /// <summary>
    /// 命令进队列等扫描线程执行；当场回受理结果，等不到（扫描线程卡了）回超时——命令稍后可能还会执行。
    /// </summary>
    private async Task<JobCommandResult> Enqueue(string? requestId, Func<JobCommandHandler, JobCommandResult> run)
    {
        if (!IsEnable || _handler is null)
        {
            return JobCommandResult.Reject(ErrorCodes.JobDisabled);
        }

        var command = new JobCommand(string.IsNullOrWhiteSpace(requestId) ? null : requestId.Trim(), run);
        _commands.Enqueue(command);

        int timeout = CommandTimeoutMs;
        var finished = await Task.WhenAny(command.Completion.Task, Task.Delay(timeout)).ConfigureAwait(false);
        if (finished != command.Completion.Task)
        {
            return JobCommandResult.Reject(ErrorCodes.JobCommandTimeout, timeout.ToString(CultureInfo.InvariantCulture));
        }

        return await command.Completion.Task.ConfigureAwait(false);
    }

    #endregion

    #region 扫描

    protected override void OnScan()
    {
        base.OnScan();

        var runtime = _runtime;
        var progress = _progress;
        if (!IsEnable || runtime is null || progress is null)
        {
            return;
        }

        runtime.Environment.Refresh();
        RunCommands();
        progress.Collect();
        progress.RefreshPositions();
        RunRules();
        Dispatch(runtime);
        Publish();
    }

    /// <summary>① 执行排着的命令：请求号见过的回原来的结果，没见过的执行后记下来。单条出异常回错误、不连累别的命令。</summary>
    private void RunCommands()
    {
        var handler = _handler;
        if (handler is null)
        {
            return;
        }

        while (_commands.TryDequeue(out var command))
        {
            string? requestId = command.RequestId;
            if (requestId is not null && _requests.TryGetValue(requestId, out var seen))
            {
                command.Completion.TrySetResult(seen);
                continue;
            }

            JobCommandResult result;
            try
            {
                result = command.Run(handler);
            }
            catch (Exception exception)
            {
                LogHelper.Error(Name, $"Job 命令执行出错：{exception.Message}");
                result = JobCommandResult.Reject(ErrorCodes.OperationFaulted, Name, exception.Message);
            }

            if (requestId is not null)
            {
                Remember(requestId, result);
            }

            command.Completion.TrySetResult(result);
        }
    }

    private void Remember(string requestId, JobCommandResult result)
    {
        _requests[requestId] = result;
        _requestOrder.Enqueue(requestId);
        int keep = Math.Max(1, RequestKeepCount);
        while (_requestOrder.Count > keep)
        {
            _requests.Remove(_requestOrder.Dequeue());
        }
    }

    /// <summary>④ 自动转换：先 PJ 后 CJ（PJ 结束先报，CJ 完成后报），转了就再问一遍，直到不再转。</summary>
    private void RunRules()
    {
        var runtime = _runtime;
        var engine = _engine;
        if (runtime is null || engine is null)
        {
            return;
        }

        for (int pass = 0; pass < MaxRulePasses; pass++)
        {
            bool changed = false;
            foreach (var job in _book.ProcessJobs.ToList())
            {
                foreach (var rule in _processRules)
                {
                    var trigger = rule.Evaluate(job, runtime);
                    if (trigger is not null && engine.Fire(job, trigger.Value))
                    {
                        changed = true;
                        break;
                    }
                }
            }

            foreach (var job in _book.ControlJobs.ToList())
            {
                foreach (var rule in _controlRules)
                {
                    var trigger = rule.Evaluate(job, runtime);
                    if (trigger is not null && engine.Fire(job, trigger.Value))
                    {
                        changed = true;
                        break;
                    }
                }
            }

            if (!changed)
            {
                return;
            }
        }
    }

    /// <summary>
    /// ⑤ 调度并提交。Manual 模式、出过故障暂停派单时不派新动作（在途的照常做完），片的等待原因写成为什么不派。
    /// 不投新片的 PJ（暂停中、停止中、CJ 没在执行）里还在来源槽的片写成"没在投片"。
    /// </summary>
    private void Dispatch(JobRuntime runtime)
    {
        var dispatcher = _dispatcher;
        var scheduler = _scheduler;
        if (dispatcher is null || scheduler is null)
        {
            return;
        }

        var jobs = OrderedProcessJobs();
        JobWait? blocked = !runtime.Environment.IsAuto
            ? JobWait.Of(ErrorCodes.JobWaitManual)
            : runtime.Hold is not null ? JobWait.Of(ErrorCodes.JobWaitHeld) : null;

        var plan = blocked is null ? scheduler.Plan(jobs, runtime.Environment) : new JobPlan();
        foreach (var job in jobs)
        {
            var gate = JobGates.Of(job);
            foreach (var wafer in job.Wafers)
            {
                if (wafer.HasInFlight || wafer.Phase is JobWaferPhase.Done or JobWaferPhase.Lost)
                {
                    dispatcher.SetWait(wafer, null);
                    continue;
                }

                bool idle = wafer.Phase == JobWaferPhase.Waiting ? (gate & JobDispatch.Feed) == 0 : (gate & JobDispatch.Advance) == 0;
                if (blocked is not null)
                {
                    dispatcher.SetWait(wafer, idle ? null : blocked);
                }
                else if (idle && wafer.Phase == JobWaferPhase.Waiting && job.State != PrJobState.QueuedPooled)
                {
                    dispatcher.SetWait(wafer, JobWait.Of(ErrorCodes.JobWaitNotFeeding, job.Id));
                }
                else if (!plan.Waits.ContainsKey(wafer.Id))
                {
                    dispatcher.SetWait(wafer, null);
                }
            }
        }

        if (blocked is null)
        {
            dispatcher.Apply(plan);
        }
    }

    /// <summary>调度看的 PJ 顺序：CJ 的队列先后、PJ 在 CJ 里的先后；不归 CJ 的排后面（它们不会被调度，只为写等待原因）。</summary>
    private List<ProcessJob> OrderedProcessJobs()
    {
        var ordered = new List<ProcessJob>();
        foreach (var control in _book.ControlJobs)
        {
            foreach (var process in control.ProcessJobs)
            {
                if (!process.IsEnded)
                {
                    ordered.Add(process);
                }
            }
        }

        foreach (var process in _book.ProcessJobs)
        {
            if (!ordered.Contains(process))
            {
                ordered.Add(process);
            }
        }

        return ordered;
    }

    /// <summary>⑥ 有变化才发布：换一份新的快照（版本加 1），推给界面（留存，重连就能拿到最新的）。</summary>
    private void Publish()
    {
        if (!_book.IsDirty)
        {
            return;
        }

        long version = _book.NextVersion();
        var snapshot = JobDtos.Of(_book, version, _runtime?.Hold);
        _snapshot = snapshot;
        _store?.Save(snapshot);
        try
        {
            EventBus.Send(snapshot, JobListDto.EventToken);
        }
        catch (Exception exception)
        {
            LogHelper.Warn(Name, $"Job 推送失败：{exception.Message}");
        }
    }

    #endregion

    #region 整机停止要知道的

    /// <summary>这个腔体正在给 Job 做工艺（整机停止时由 Job 的中止去停它，不直接发中止）。</summary>
    public static bool IsJobProcess(BaseModule module)
    {
        return module is IProcessStation process && process.CurrentProcess?.Origin == ProcessOrigin.Job;
    }

    #endregion

    /// <summary>
    /// 排队的一条命令：执行体、请求号、受理结果（续体异步跑，免得在扫描线程上执行调用方的代码）。
    /// </summary>
    private sealed class JobCommand
    {
        public JobCommand(string? requestId, Func<JobCommandHandler, JobCommandResult> run)
        {
            RequestId = requestId;
            Run = run;
        }

        public string? RequestId { get; }

        public Func<JobCommandHandler, JobCommandResult> Run { get; }

        public TaskCompletionSource<JobCommandResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
