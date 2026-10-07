using System.Globalization;
using System.Threading.Channels;
using SqlSugar;
using xyz.Common.Log;
using xyz.Components.Interfaces;
using xyz.Components.Models;
using xyz.Components;
using xyz.Components.Attributes;
using xyz.Components.Components;
using xyz.Components.Enums;
using xyz.Database.DbProvider;
using xyz.Database.Jobs;
using xyz.Modules.Enums;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Tools;

namespace xyz.Modules;

/// <summary>
/// Job 管理（sc.xml 顶层 Job 节点，自己一条扫描线程）：里面有 CJ 管理（<see cref="ICjManager"/>，E94）和 PJ 管理（<see cref="IPjManager"/>，E40），
/// 它们各管自己的队列、状态机、命令，转了经事件告诉这里；牵扯别处的事都在这里做——建 Job（查设备、片、配方，任务组件建任务表）、
/// PJ 中止时给设备发中止、PJ 结束时任务表收场、CJ 完成时告诉 LoadPort、往 EAP 报。下面挂任务组件（Task）和调度引擎（Scheduler）。
/// 本地界面和 EAP 的命令（<see cref="IJobManager"/>）当场执行，跟扫描线程用同一把锁；每拍收结果、核对片位、转状态、派任务，有变化才发布、存盘。
/// </summary>
[Component(description: "Job 管理：里面有 CJ 管理（E94）、PJ 管理（E40），下面挂任务组件（任务表）和调度引擎")]
public class JobManager : ComponentBase, IJobManager
{
    /// <summary>
    /// 当前 Job 管理；sc.xml 里装出来即生效。冒烟与测试可以直接换成自己的实例。
    /// </summary>
    public static JobManager? Current { get; set; }

    /// <summary>每拍自动转换最多转几轮：转了再问一遍直到不再转，这个数只是防配错了的死循环。</summary>
    private const int MaxAdvancePasses = 16;

    /// <summary>E39 ObjID 最长 80 个字符。</summary>
    private const int MaxIdLength = 80;

    /// <summary>E39 ObjID 不能用的字符。</summary>
    private const string ForbiddenIdChars = "?*~>:";

    /// <summary>命令和扫描线程共用的锁：CJ / PJ 管理、任务表同一时刻只有一个在改。</summary>
    private readonly object _gate = new();

    private readonly IPjManager _processJobs;
    private readonly ICjManager _controlJobs;

    /// <summary>EAP 派发线程：E40、E94 的上报按发生先后在这一条线程上发，不占扫描线程。</summary>
    private readonly EapNotifier _notifier;

    private IReadOnlyDictionary<string, BaseModule> _modules = new Dictionary<string, BaseModule>(StringComparer.OrdinalIgnoreCase);
    private BaseTaskComponent? _tasks;
    private SchedulerComponent? _scheduler;

    /// <summary>有改动，这一拍要发布。开机先算有改动：第一拍就发一份（空的也发），界面重连拿到的是这次开机的全貌。</summary>
    private bool _dirty = true;

    private long _version;
    private long _publishedTasks = -1;
    private volatile JobListDto _snapshot = new();

    public JobManager()
    {
        Current = this;
        _notifier = new EapNotifier(() => Name);
        _processJobs = new PjManager();
        _controlJobs = new CjManager(_processJobs);
        _processJobs.Transitioned += OnProcessJobTransitioned;
        _controlJobs.Transitioned += OnControlJobTransitioned;
    }

    #region SC

    [SCEditor("True", "Job", "是否启用 Job 管理（False = 不收 Job 命令、不按 Job 调度）")]
    public bool IsEnable { get; set; } = true;

    [SCEditor("True", "Job", "是否把 Job 存盘：开机把上次没结束的 Job 记成中止进历史（重启后不接着跑），历史接着留")]
    public bool IsPersistent { get; set; } = true;

    [SCEditor("Default", "Job", "存盘落哪个库（sc.xml 的 Database 节点名）")]
    public string Database { get; set; } = XyzDb.DefaultName;

    #endregion

    #region EC

    [VariableMark(VariableType.EC, ValueFormat.Int, unit: "ms", min: "100", max: "60000",
        @default: "5000", description: "命令等 Job 管理空出来的上限（扫描线程一拍就做完，等不到说明线程卡了）")]
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
    /// 绑定模块表（装配完、模块和搬运管理起来之后调一次）：sc.xml Job 节点下的 Task（机型的任务组件）必须配，
    /// Scheduler 没配用默认策略。开了存盘的读回上次的 Job：不接着跑，没结束的记成中止进历史。
    /// </summary>
    public void Bind(IEnumerable<BaseModule> modules)
    {
        var tasks = FindChild<BaseTaskComponent>()
            ?? throw new InvalidOperationException($"{Name}：sc.xml 的 Job 节点下没配 Task 子节点（机型的任务组件，继承 BaseTaskComponent）");
        tasks.StationTaskBegan -= OnStationTaskBegan;
        tasks.StationTaskBegan += OnStationTaskBegan;
        tasks.StationTaskFinished -= OnStationTaskFinished;
        tasks.StationTaskFinished += OnStationTaskFinished;

        var table = new Dictionary<string, BaseModule>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in modules)
        {
            table[module.Name] = module;
        }

        lock (_gate)
        {
            _modules = table;
            _tasks = tasks;
            _scheduler = FindChild<SchedulerComponent>() ?? new SchedulerComponent();
            if (IsPersistent && !_storeStarted)
            {
                _storeStarted = true;
                _ = Task.Run(StoreLoopAsync);
                _controlJobs.CloseOutLastRun(LoadStored());
            }

            _dirty = true;
            Publish();
        }

        LogHelper.Info(Name, $"Job 管理已绑定：任务组件 {tasks.GetType().Name}，调度 {_scheduler.GetType().Name}");
    }

    #endregion

    #region 命令（IJobManager，加上本地界面才有的出错任务处理）

    public JobListDto Snapshot => _snapshot;

    /// <summary>按载具号找 CJ：从当前全貌里找（任意线程可调，不占锁）。</summary>
    public ControlJobDto? FindControlJobByCarrier(string carrierId)
    {
        string wanted = carrierId.Trim();
        if (wanted.Length == 0)
        {
            return null;
        }

        return _snapshot.ControlJobs.FirstOrDefault(job => string.Equals(job.CarrierId, wanted, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>按载具号找 PJ：从当前全貌里找（任意线程可调，不占锁）。</summary>
    public IReadOnlyList<ProcessJobDto> FindProcessJobsByCarrier(string carrierId)
    {
        string wanted = carrierId.Trim();
        if (wanted.Length == 0)
        {
            return [];
        }

        return _snapshot.ProcessJobs.Where(job => string.Equals(job.CarrierId, wanted, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>这一片现在归哪个 PJ；不归任何没结束的 PJ 返回 null。任意线程可调（搬运管理受理手动单时问）。</summary>
    public string? OwnerOf(Guid waferId)
    {
        return _processJobs.OwnerOf(waferId);
    }

    public Task<HandleResult> CreateProcessJobAsync(ProcessJobSpec spec)
    {
        return Execute(() =>
        {
            var rejected = TryBuildProcessJob(spec, source, out var job);
            if (rejected is not null)
            {
                return rejected;
            }

            Tasks.Add(job);
            _processJobs.Add(job);
            return HandleResult.Success(job.Id);
        });
    }

    public Task<HandleResult> CreateControlJobAsync(ControlJobSpec spec)
    {
        return Execute(() =>
        {
            var idError = CheckNewId(spec.Id.Trim());
            if (idError is not null)
            {
                return idError;
            }

            var rejected = _controlJobs.TryBuild(spec, source, out var job, out var processes);
            if (rejected is not null)
            {
                return rejected;
            }

            _controlJobs.Add(job, processes);
            return HandleResult.Success(job.Id);
        });
    }

    public Task<HandleResult> CommandControlJobAsync(string id, CtrlJobCommand command, CtrlJobAction action)
    {
        return Execute(() =>
        {
            // 启动要 Auto：Manual 下不派动作，启动了也跑不起来
            if (command == CtrlJobCommand.Start && !IsAuto && _controlJobs.Find(id.Trim()) is not null)
            {
                return HandleResult.Fail(ErrorCodes.JobNotAuto);
            }

            return _controlJobs.Command(id, command, action);
        });
    }

    public Task<HandleResult> CommandProcessJobAsync(string id, PrJobCommand command)
    {
        return Execute(() =>
        {
            if (command == PrJobCommand.Start && !IsAuto && _processJobs.Find(id.Trim()) is not null)
            {
                return HandleResult.Fail(ErrorCodes.JobNotAuto);
            }

            return _processJobs.Command(id, command);
        });
    }

    /// <summary>
    /// 整机停止：所有没结束的 Job 走中止（等设备确认、核对片位），不直接给模块发中止。
    /// </summary>
    public Task<HandleResult> AbortAllAsync()
    {
        return Execute(() =>
        {
            _controlJobs.AbortAll();
            _processJobs.AbortLoose();
            return HandleResult.Success(string.Empty);
        });
    }

    /// <summary>
    /// 出错的任务重做（本地界面用，Host 不碰）：任务退回等着做，调度按片现在在哪重新派。片按 PJ 和来源槽认，任务按这一行里的序号（从 0 开始）。
    /// </summary>
    public Task<HandleResult> RetryTaskAsync(string processJob, int slot, int task)
    {
        return Execute(() => WithProcessJob(processJob, job => Tasks.Retry(job, slot, task)));
    }

    /// <summary>
    /// 出错的任务标记完成（本地界面用，Host 不碰）：人已经把这一步做完了，接着走。取放要片在账上正好在这一步做完该在的地方。
    /// </summary>
    public Task<HandleResult> CompleteTaskAsync(string processJob, int slot, int task)
    {
        return Execute(() => WithProcessJob(processJob, job => Tasks.Complete(job, slot, task)));
    }

    /// <summary>
    /// 执行一条命令：在调用方的线程上直接做，跟扫描线程用同一把锁；锁等不到（扫描线程卡了）回超时。
    /// 做成了当场发布（界面、调用方马上看得到结果）。出异常回错误，不往外抛。
    /// </summary>
    private Task<HandleResult> Execute(Func<HandleResult> command)
    {
        if (!IsEnable || _tasks is null)
        {
            return Task.FromResult(HandleResult.Fail(ErrorCodes.JobDisabled));
        }

        int timeout = CommandTimeoutMs;
        if (!Monitor.TryEnter(_gate, timeout))
        {
            return Task.FromResult(HandleResult.Fail(ErrorCodes.JobCommandTimeout, timeout.ToString(CultureInfo.InvariantCulture)));
        }

        try
        {
            var result = command();
            if (result.IsSuccess)
            {
                _dirty = true;
            }

            Publish();
            return Task.FromResult(result);
        }
        catch (Exception exception)
        {
            LogHelper.Error(Name, $"Job 命令执行出错：{exception.Message}");
            return Task.FromResult(HandleResult.Fail(ErrorCodes.OperationFaulted, Name, exception.Message));
        }
        finally
        {
            Monitor.Exit(_gate);
        }
    }

    private HandleResult WithProcessJob(string id, Func<ProcessJob, HandleResult> run)
    {
        var job = _processJobs.Find(id.Trim());
        return job is null ? HandleResult.Fail(ErrorCodes.JobNotFound, id.Trim()) : run(job);
    }

    #endregion

    #region 建 PJ

    /// <summary>
    /// 造一个 PJ（本地、Host 一样）：名字合规、没人用；给了 LoadPort 按它找，没给按载具号找，载具要能取片；每一片查过
    /// （有片、正常、没做过、不归别的 PJ）、回片槽定好；流程配方取快照；任务组件给每片生成一行任务。有一项不过回原因，什么都不留。
    /// 造出来还没进队列。
    /// </summary>
    private HandleResult? TryBuildProcessJob(ProcessJobSpec spec, out ProcessJob job)
    {
        job = null!;
        string id = spec.Id.Trim();
        var idError = CheckNewId(id);
        if (idError is not null)
        {
            return idError;
        }

        var portError = FindLoadPort(spec, out var port);
        if (portError is not null)
        {
            return portError;
        }

        string loadPort = port.Name;
        var slots = spec.Slots.Distinct().ToList();
        if (slots.Count == 0)
        {
            return HandleResult.Fail(ErrorCodes.JobNoWafers, loadPort);
        }

        var ledger = WaferManager.Current;
        if (ledger is null || !ledger.IsEnable)
        {
            return HandleResult.Fail(ErrorCodes.WaferLedgerDisabled);
        }

        var sequenceError = TryTakeSequence(spec.Sequence.Trim(), loadPort, out var sequence);
        if (sequenceError is not null)
        {
            return sequenceError;
        }

        var built = new ProcessJob
        {
            Id = id,
            Sequence = sequence,
            CarrierId = port.CarrierId,
            CarrierInstance = port.Carrier?.Id,
            AutoStart = spec.AutoStart,
        };

        foreach (int slot in slots)
        {
            string slotText = slot.ToString(CultureInfo.InvariantCulture);
            var wafer = ledger.Get(loadPort, slot);
            if (wafer is null)
            {
                return HandleResult.Fail(ErrorCodes.JobSlotEmpty, loadPort, slotText);
            }

            if (wafer.Status != WaferStatus.Normal)
            {
                return HandleResult.Fail(ErrorCodes.JobWaferNotNormal, loadPort, slotText, wafer.WaferId, wafer.Status.ToString());
            }

            if (wafer.ProcessState != WaferProcessState.Idle)
            {
                return HandleResult.Fail(ErrorCodes.JobWaferProcessed, loadPort, slotText, wafer.WaferId, wafer.ProcessState.ToString());
            }

            string? owner = _processJobs.OwnerOf(wafer.Id);
            if (owner is not null)
            {
                return HandleResult.Fail(ErrorCodes.JobWaferOwned, wafer.WaferId, owner);
            }

            var returnError = PickReturnSlot(sequence, loadPort, slot, out string returnPort);
            if (returnError is not null)
            {
                return returnError;
            }

            built.Rows.Add(new TaskRow
            {
                Owner = id,
                WaferId = wafer.Id,
                WaferName = wafer.WaferId,
                SourcePort = loadPort,
                SourceSlot = slot,
                ReturnPort = returnPort,
                ReturnSlot = slot,
            });
        }

        // 每片的任务行照流程配方生成（任务组件管）
        var taskError = Tasks.Build(built);
        if (taskError is not null)
        {
            return taskError;
        }

        job = built;
        return null;
    }

    /// <summary>
    /// 新名字：E39 的 ObjID（1~80 个 ASCII 可见字符和空格，不能有 ? * ~ &gt; :），而且没结束的 CJ、PJ 里没人用。
    /// </summary>
    private HandleResult? CheckNewId(string id)
    {
        bool valid = id.Length >= 1 && id.Length <= MaxIdLength
            && id.All(ch => ch >= ' ' && ch <= '~' && !ForbiddenIdChars.Contains(ch));
        if (!valid)
        {
            return HandleResult.Fail(ErrorCodes.JobIdInvalid, id);
        }

        bool inUse = _controlJobs.Find(id) is not null || _processJobs.Find(id) is not null;
        return inUse ? HandleResult.Fail(ErrorCodes.JobIdDuplicate, id) : null;
    }

    /// <summary>
    /// 料在哪个 LoadPort：给了 LoadPort 就按它（本地建，载具号可能没读到），没给按载具号找（Host 建，载具要已经在 LoadPort 上）；
    /// 载具要能取片（放着、Load 好）。
    /// </summary>
    private HandleResult? FindLoadPort(ProcessJobSpec spec, out BaseLoadPortModule port)
    {
        port = null!;
        string name = (spec.LoadPort ?? string.Empty).Trim();
        BaseLoadPortModule? found;
        if (name.Length > 0)
        {
            found = LoadPort(name);
            if (found is null)
            {
                return HandleResult.Fail(ErrorCodes.JobLoadPortNotFound, name);
            }
        }
        else
        {
            string carrier = spec.CarrierId.Trim();
            found = carrier.Length == 0
                ? null
                : LoadPorts.FirstOrDefault(item => string.Equals(item.CarrierId, carrier, StringComparison.OrdinalIgnoreCase) && item.IsCarrierArrived);
            if (found is null)
            {
                return HandleResult.Fail(ErrorCodes.JobCarrierNotFound, carrier);
            }
        }

        port = found;
        return IsCarrierReady(found.Name) ? null : HandleResult.Fail(ErrorCodes.JobCarrierNotReady, found.Name);
    }

    /// <summary>
    /// 取流程配方快照（库里的副本）：要在库里、至少三步；第 1 步勾了来源 LoadPort；最后一步有回片的 LoadPort。
    /// 中间每一站的工艺配方、能去的站点由任务组件生成任务表时查。
    /// </summary>
    private HandleResult? TryTakeSequence(string name, string loadPort, out SequenceData sequence)
    {
        sequence = null!;
        var found = SequenceComponent.Current?.Find(name);
        if (found is null || found.Steps.Count < 3)
        {
            return HandleResult.Fail(ErrorCodes.JobSequenceNotFound, name);
        }

        if (!found.Steps[0].Stations.Contains(loadPort, StringComparer.OrdinalIgnoreCase))
        {
            return HandleResult.Fail(ErrorCodes.JobSequenceSourceMismatch, found.Name, loadPort);
        }

        if (ReturnPorts(found).Count == 0)
        {
            return HandleResult.Fail(ErrorCodes.JobSequenceNoReturn, found.Name);
        }

        sequence = found;
        return null;
    }

    /// <summary>流程配方最后一步勾的、装了的 LoadPort：回片从这里定。</summary>
    private List<string> ReturnPorts(SequenceData sequence)
    {
        return sequence.Steps[^1].Stations.Where(name => LoadPort(name) is not null).ToList();
    }

    /// <summary>
    /// 定回片槽（流程配方页定下的规则）：来源 LoadPort 在最后一步里就回原槽；不在就放到最后一步第一个有载具的 LoadPort 的同号槽，
    /// 那个槽要空着，也不能是别的没结束的片要回的槽（同一个 PJ 里槽号不重复，回的槽也就不会撞）。
    /// </summary>
    private HandleResult? PickReturnSlot(SequenceData sequence, string loadPort, int slot, out string returnPort)
    {
        returnPort = loadPort;
        string slotText = slot.ToString(CultureInfo.InvariantCulture);
        var returnPorts = ReturnPorts(sequence);
        if (returnPorts.Contains(loadPort, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        string? port = returnPorts.FirstOrDefault(IsCarrierReady);
        if (port is null)
        {
            return HandleResult.Fail(ErrorCodes.JobReturnSlotUnavailable, returnPorts[0], slotText);
        }

        var target = LoadPort(port);
        bool claimed = _processJobs.Jobs.Any(job => job.Rows.Any(row =>
            !row.IsReturned && string.Equals(row.ReturnPort, port, StringComparison.OrdinalIgnoreCase) && row.ReturnSlot == slot));
        if (target is null || slot > target.SlotCount || WaferManager.Current?.Get(port, slot) is not null || claimed)
        {
            return HandleResult.Fail(ErrorCodes.JobReturnSlotUnavailable, port, slotText);
        }

        returnPort = target.Name;
        return null;
    }

    #endregion

    #region CJ / PJ 转了之后（牵扯别处的事）

    /// <summary>
    /// PJ 转了：进 ABORTING 给设备发中止（<see cref="AbortDevices"/>）；结束了它的行从任务表拿掉；往 EAP 报（E40，派发线程上按先后发，
    /// PJ 结束一定先于 CJ 完成）。
    /// </summary>
    private void OnProcessJobTransitioned(ProcessJob job, int number, PrJobState? from, PrJobState? to)
    {
        if (to == PrJobState.Aborting && from != PrJobState.Aborting)
        {
            AbortDevices(job);
        }

        if (job.IsEnded)
        {
            Tasks.Close(job);
        }

        var callback = E40Callback;
        if (callback is not null)
        {
            var dto = JobDtos.Of(job);
            _notifier.Post(() => callback.ProcessJobTransitioned(dto, number, from, to));
        }

        _dirty = true;
    }

    /// <summary>
    /// PJ 进 ABORTING：任务组件把它的行标上中止，撤这个 PJ 还没开始的搬运单、中止它在跑的搬运（手臂在动的由搬运管理发设备中止），
    /// 再给正在给它做站内任务的站点发中止，记下发出去的中止动作。设备中止做完、在途的都结束、片位都确定，PJ 管理才转成结束（#16）。
    /// </summary>
    private void AbortDevices(ProcessJob job)
    {
        Tasks.Abort(job);
        TransferManager.Current?.CancelOwner(job.Id, $"PJ {job.Id} 中止");
        var stations = job.Rows
            .SelectMany(row => row.Tasks)
            .Where(task => task.State == WaferTaskState.Running && !task.IsRobotTask && task.Station is not null)
            .Select(task => task.Station!)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (string station in stations)
        {
            // 发不出去（状态不允许）的不等：站内任务照常做完，结束了照样往下走
            if (Module(station)?.Abort() is ModuleOperation abort)
            {
                job.DeviceAborts.Add(abort);
            }
        }
    }

    /// <summary>CJ 转了：完成了告诉 LoadPort 这个载具的活干完了（转成 E87 的 CarrierComplete）；往 EAP 报（E94）。</summary>
    private void OnControlJobTransitioned(ControlJob job, int number, CtrlJobState? from, CtrlJobState? to)
    {
        if (to == CtrlJobState.Completed && from != CtrlJobState.Completed)
        {
            LoadPort(job.LoadPort)?.NoteCarrierComplete();
        }

        var callback = E94Callback;
        if (callback is not null)
        {
            var dto = JobDtos.Of(job);
            _notifier.Post(() => callback.ControlJobTransitioned(dto, number, from, to));
        }

        _dirty = true;
    }

    /// <summary>站内任务开始：是工艺的记日志、往 EAP 报片开始加工（E40）。</summary>
    private void OnStationTaskBegan(TaskRow row, WaferTask task)
    {
        var job = _processJobs.Find(row.Owner);
        if (task.Kind != StationTaskAction.Process || job is null)
        {
            return;
        }

        string station = task.Station ?? string.Empty;
        LogHelper.Info(Name, $"PJ {job.Id} {row.WaferName} 在 {station} 开始加工（第 {task.Step + 1} 站，{task.RecipeName}）");
        var callback = E40Callback;
        if (callback is not null)
        {
            var jobDto = JobDtos.Of(job);
            var waferDto = JobDtos.Of(row);
            _notifier.Post(() => callback.WaferProcessStarted(jobDto, waferDto, station));
        }
    }

    /// <summary>站内任务结束：是工艺的记日志、往 EAP 报片加工结束（成没成看任务状态）。</summary>
    private void OnStationTaskFinished(TaskRow row, WaferTask task)
    {
        var job = _processJobs.Find(row.Owner);
        if (task.Kind != StationTaskAction.Process || job is null)
        {
            return;
        }

        string station = task.Station ?? string.Empty;
        bool success = task.State == WaferTaskState.Done;
        if (success)
        {
            LogHelper.Info(Name, $"PJ {job.Id} {row.WaferName} 在 {station} 加工完成");
        }
        else
        {
            LogHelper.Warn(Name, $"PJ {job.Id} {row.WaferName} 在 {station} 加工没做成：这一步停住等人处理，别的片照常走");
        }

        var callback = E40Callback;
        if (callback is not null)
        {
            var jobDto = JobDtos.Of(job);
            var waferDto = JobDtos.Of(row);
            _notifier.Post(() => callback.WaferProcessEnded(jobDto, waferDto, station, success));
        }
    }

    #endregion

    #region 扫描

    /// <summary>
    /// 每拍固定五步：① 调度引擎收做完的（搬运、站内任务），结果记回任务表 → ② 任务组件核对片位 → ③ PJ、CJ 管理按任务进度自动转状态、定许可 →
    /// ④ 调度引擎派新任务 → ⑤ 有变化才发布。整拍拿着命令那把锁，命令和扫描不会交叉着改。
    /// </summary>
    protected override void OnScan()
    {
        base.OnScan();

        var tasks = _tasks;
        var scheduler = _scheduler;
        if (!IsEnable || tasks is null || scheduler is null)
        {
            return;
        }

        lock (_gate)
        {
            scheduler.Collect(tasks);
            tasks.CheckPositions();
            Advance();
            Dispatch(scheduler, tasks);
            Publish();
        }
    }

    /// <summary>
    /// ③ 自动转换：先 PJ 后 CJ（PJ 结束先报，CJ 完成后报），转了就再问一遍，直到不再转；最后按 PJ 状态给它的行定许可。
    /// CJ 要知道的载具好没好、拿没拿走，这里查设备给它。
    /// </summary>
    private void Advance()
    {
        for (int pass = 0; pass < MaxAdvancePasses; pass++)
        {
            bool changed = _processJobs.Advance();
            changed |= _controlJobs.Advance(IsCarrierReady, IsCarrierGone);
            if (!changed)
            {
                break;
            }
        }

        _processJobs.UpdatePermissions();
    }

    /// <summary>
    /// ④ 派任务：Manual 下不派新动作（在途的照常做完）。Auto 下把 CJ 里没结束的 PJ 的行按优先级（CJ 队列先后、PJ 在 CJ 里的先后、
    /// PJ 里的投片顺序）交给调度引擎；能派什么看每一行的许可（PJ 状态定的，暂停、停止就体现在这上面）。不归 CJ 的 PJ 没开始，不派。
    /// </summary>
    private void Dispatch(SchedulerComponent scheduler, BaseTaskComponent tasks)
    {
        if (!IsAuto)
        {
            return;
        }

        var rows = _controlJobs.Jobs
            .SelectMany(control => control.ProcessJobs)
            .Where(process => !process.IsEnded)
            .SelectMany(process => process.Rows)
            .ToList();
        scheduler.Dispatch(rows, tasks);
    }

    /// <summary>
    /// ⑤ 有变化才发布：换一份新的全貌（版本加 1）——没删的 CJ、界面要看的 PJ（没结束的，加上没删的 CJ 下面已经结束的）、
    /// 历史（本次的在前，上次开机留下的在后，按 EC HistoryKeepCount 留）；推给界面（留存，重连就能拿到最新的），交给存盘。
    /// </summary>
    private void Publish()
    {
        long taskVersion = _tasks?.Version ?? 0;
        if (taskVersion != _publishedTasks)
        {
            _publishedTasks = taskVersion;
            _dirty = true;
        }

        if (!_dirty)
        {
            return;
        }

        _dirty = false;
        _controlJobs.TrimHistory(HistoryKeepCount);
        var processJobs = new List<ProcessJob>();
        foreach (var control in _controlJobs.Jobs)
        {
            processJobs.AddRange(control.ProcessJobs);
        }

        foreach (var process in _processJobs.Jobs)
        {
            if (!processJobs.Contains(process))
            {
                processJobs.Add(process);
            }
        }

        var snapshot = new JobListDto
        {
            Version = ++_version,
            ControlJobs = _controlJobs.Jobs.Select(JobDtos.Of).ToList(),
            ProcessJobs = processJobs.Select(JobDtos.Of).ToList(),
            History = _controlJobs.History.Select(JobDtos.Of).Concat(_controlJobs.Restored).ToList(),
        };
        _snapshot = snapshot;
        if (_storeStarted)
        {
            Store(snapshot);
        }

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

    #region 存盘

    // 每次发布的全貌交给一条写库线程，只写最新的一份（来不及写的旧版本直接跳过）；开机读回上一份交给 CJ 管理收场。
    // 写库不在扫描线程上做——磁盘一卡，扫描就卡。写不进去只记日志，Job 照跑。

    private readonly Channel<bool> _storeSignal = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    private JobListDto? _pendingSnapshot;
    private bool _storeStarted;
    private bool _storeTableReady;
    private bool _storeFailing;

    /// <summary>读回上一份全貌；没存过返回 null，读不出来也返回 null（记日志，当没有上次的 Job）。</summary>
    private JobListDto? LoadStored()
    {
        try
        {
            using var db = XyzDb.Create(Database);
            EnsureStoreTable(db);
            var row = db.Queryable<JobSnapshotEntity>().InSingle(1);
            if (row is null || string.IsNullOrWhiteSpace(row.Json))
            {
                return null;
            }

            return JsonHelper.Deserialize<JobListDto>(row.Json);
        }
        catch (Exception exception)
        {
            LogHelper.Error(Name, $"读上次的 Job 存盘失败（当没有上次的 Job）：{exception.Message}");
            return null;
        }
    }

    /// <summary>交一份全貌去存：只留最新的，写库线程空了就写。</summary>
    private void Store(JobListDto snapshot)
    {
        Volatile.Write(ref _pendingSnapshot, snapshot);
        _storeSignal.Writer.TryWrite(true);
    }

    private async Task StoreLoopAsync()
    {
        while (await _storeSignal.Reader.WaitToReadAsync().ConfigureAwait(false))
        {
            _storeSignal.Reader.TryRead(out _);
            var snapshot = Interlocked.Exchange(ref _pendingSnapshot, null);
            if (snapshot is not null)
            {
                WriteStored(snapshot);
            }
        }
    }

    private void WriteStored(JobListDto snapshot)
    {
        try
        {
            using var db = XyzDb.Create(Database);
            EnsureStoreTable(db);
            var row = new JobSnapshotEntity
            {
                Id = 1,
                Version = snapshot.Version,
                Json = JsonHelper.Serialize(snapshot),
                SavedAt = DateTime.Now,
            };
            db.Storageable(row).ExecuteCommand();
        }
        catch (Exception exception)
        {
            if (!_storeFailing)
            {
                _storeFailing = true;
                LogHelper.Warn(Name, $"Job 存盘失败（Job 照跑，下一次变化再存）：{exception.Message}");
            }

            return;
        }

        if (_storeFailing)
        {
            _storeFailing = false;
            LogHelper.Info(Name, "Job 存盘恢复正常");
        }
    }

    /// <summary>表第一次用到时建（已有就只补列）。</summary>
    private void EnsureStoreTable(ISqlSugarClient db)
    {
        if (_storeTableReady)
        {
            return;
        }

        db.CodeFirst.InitTables<JobSnapshotEntity>();
        _storeTableReady = true;
    }

    #endregion

    #region 设备

    /// <summary>任务组件（装配时从 sc.xml 的 Task 子节点取）。</summary>
    private BaseTaskComponent Tasks => _tasks ?? throw new InvalidOperationException($"{Name}：还没装配，没有任务组件");

    /// <summary>Auto 模式（自动派单开着）：Manual 下不派新动作，启动 Job 也要 Auto。</summary>
    private static bool IsAuto => TransferManager.Current?.IsAutoDispatch == true;

    /// <summary>装了的 LoadPort（按 sc.xml 先后）。</summary>
    private IEnumerable<BaseLoadPortModule> LoadPorts => _modules.Values.OfType<BaseLoadPortModule>();

    private BaseModule? Module(string name)
    {
        return _modules.TryGetValue(name.Trim(), out var module) ? module : null;
    }

    private BaseLoadPortModule? LoadPort(string name)
    {
        return Module(name) as BaseLoadPortModule;
    }

    /// <summary>
    /// LoadPort 上有能取片的载具：放着、Load 好了（在待命，或正在被机械手服务）。
    /// </summary>
    private bool IsCarrierReady(string loadPort)
    {
        var port = LoadPort(loadPort);
        if (port is null || !port.IsEnabled || !port.IsCarrierArrived)
        {
            return false;
        }

        int state = port.State;
        return state == LoadPortState.Loaded
            || (state >= TransferModuleState.PreTransfer && state <= TransferModuleState.TransferComplete);
    }

    /// <summary>CJ 的载具从 LoadPort 拿走了（或换了一个）：完成的 CJ 可以删了（#13）。</summary>
    private bool IsCarrierGone(ControlJob job)
    {
        var port = LoadPort(job.LoadPort);
        var carrier = port?.Carrier;
        bool sameCarrier = port is not null && port.IsCarrierArrived && carrier is not null
            && (job.CarrierInstance is null || carrier.Id == job.CarrierInstance.Value);
        return !sameCarrier;
    }

    /// <summary>这个腔体正在给 Job 做工艺（整机停止时由 Job 的中止去停它，不直接发中止）。</summary>
    public static bool IsJobProcess(BaseModule module)
    {
        return module is IProcessStation process && process.CurrentProcess?.Origin == ProcessOrigin.Job;
    }

    #endregion
}
