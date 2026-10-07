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
using xyz.Database;
using xyz.Database.DbProvider;
using xyz.Database.Jobs;
using xyz.Modules.Enums;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Tools;

namespace xyz.Modules;

[Component(description: "Job 管理：里面有 CJ 管理（E94）、PJ 管理（E40），下面挂任务组件（任务表）和调度引擎")]
public class JobManager : ComponentBase, IJobManager
{
    public static JobManager? Current { get; set; }

    private readonly object _gate = new();

    private IReadOnlyDictionary<string, BaseModule> _modules = new Dictionary<string, BaseModule>(StringComparer.OrdinalIgnoreCase);
    private BaseTaskComponent? _tasks;
    private SchedulerComponent? _scheduler;

    /// <summary>有改动，这一拍要发布。开机先算有改动：第一拍就发一份（空的也发），界面重连拿到的是这次开机的全貌。</summary>
    private bool _dirty = true;

    private long _version;
    private long _publishedTasks = -1;
    private volatile JobListDto _snapshot = new();

    private readonly IPjManager _processJobs;
    private readonly ICjManager _controlJobs;
    public JobManager()
    {
        Current = this;
        _processJobs = new PjManager();
        _controlJobs = new CjManager(_processJobs);
        _processJobs.StateChanged += OnProcessJobStateChanged;
        _controlJobs.StateChanged += OnControlJobStateChanged;
    }

    #region SC

    [SCEditor("True", "Job", "是否启用 Job 管理（False = 不收 Job 命令、不按 Job 调度）")]
    public bool IsEnable { get; set; } = true;

    [SCEditor("True", "Job", "是否把 Job 记进库（CJ、PJ 各一行，每片的任务明细跟着 PJ）：开机把上次没做完的记成中止（重启后不接着跑）")]
    public bool IsPersistent { get; set; } = true;

    [SCEditor("Default", "Job", "Job 记录落哪个库（sc.xml 的 Database 节点名）")]
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

    /// <summary>E94（CJ）上报口；null 表示没接 EAP。上报都放进 EAP 的派发组件（EapNotifierComponent），按发生先后发。</summary>
    public IE94Callback? E94Callback { get; set; }

    #endregion

    #region 装配

    /// <summary>
    /// 绑定模块表（装配完、模块和搬运管理起来之后调一次）：sc.xml Job 节点下的 Task（机型的任务组件）必须配，
    /// Scheduler 没配用默认策略。开了存库的先把库里上次没做完的 Job 记成中止（重启后不接着跑），再起写库线程。
    /// </summary>
    public void Bind(IEnumerable<BaseModule> modules)
    {
        var tasks = FindChild<BaseTaskComponent>()
            ?? throw new InvalidOperationException($"{Name}：sc.xml 的 Job 节点下没配 Task 子节点（机型的任务组件，继承 BaseTaskComponent）");
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
                CloseOutLastRun();
                _ = Task.Run(StoreLoopAsync);
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
        return Execute(() => CreateProcessJob(spec));
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

            return _controlJobs.Create(spec);
        });
    }

    public Task<HandleResult> ExecuteControlJobCommandAsync(string id, ControlJobCommand command, ControlJobAction action)
    {
        return Execute(() =>
        {
            // 启动要 Auto：Manual 下不派动作，启动了也跑不起来
            if (command == ControlJobCommand.Start && !IsAuto && _controlJobs.Find(id.Trim()) is not null)
            {
                return HandleResult.Fail(ErrorCodes.JobNotAuto);
            }

            return _controlJobs.Execute(id, command, action);
        });
    }

    public Task<HandleResult> ExecuteProcessJobCommandAsync(string id, ProcessJobCommand command)
    {
        return Execute(() =>
        {
            if (command == ProcessJobCommand.Start && !IsAuto && _processJobs.Find(id.Trim()) is not null)
            {
                return HandleResult.Fail(ErrorCodes.JobNotAuto);
            }

            return _processJobs.Execute(id, command);
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
    /// 建 PJ（本地、Host 一样）：名字合规、没人用；给了 LoadPort 按它找，没给按载具号找，载具要能取片；每一片查过
    /// （有片、正常、没做过、不归别的 PJ）、回片槽定好；流程配方取快照；任务组件给每片生成一行任务。有一项不过回原因，什么都不留。
    /// 都过了挂进任务表、进 PJ 队列（报 E40 #1），回 PJ 名。
    /// </summary>
    private HandleResult CreateProcessJob(ProcessJobSpec spec)
    {
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

        var ledger = WaferManagerComponent.Current;
        if (ledger is null || !ledger.IsEnable)
        {
            return HandleResult.Fail(ErrorCodes.WaferLedgerDisabled);
        }

        var sequenceError = TryTakeSequence(spec.Sequence.Trim(), loadPort, out var sequence);
        if (sequenceError is not null)
        {
            return sequenceError;
        }

        var job = new ProcessJob
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

            job.Rows.Add(new TaskRow
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
        var taskError = Tasks.Build(job);
        if (taskError is not null)
        {
            return taskError;
        }

        Tasks.Add(job);
        _processJobs.Add(job);
        return HandleResult.Success(job.Id);
    }

    /// <summary>
    /// 新名字：E39 的 ObjID（1~80 个 ASCII 可见字符和空格，不能有 ? * ~ &gt; :），而且没结束的 CJ、PJ 里没人用。
    /// </summary>
    private HandleResult? CheckNewId(string id)
    {
        bool valid = id.Length >= 1 && id.Length <= 80
            && id.All(ch => ch >= ' ' && ch <= '~' && !"?*~>:".Contains(ch));
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
        if (target is null || slot > target.SlotCount || WaferManagerComponent.Current?.Get(port, slot) is not null || claimed)
        {
            return HandleResult.Fail(ErrorCodes.JobReturnSlotUnavailable, port, slotText);
        }

        returnPort = target.Name;
        return null;
    }

    #endregion

    #region CJ / PJ 转了之后（牵扯别处的事）

    /// <summary>
    /// PJ 转了：刚进 ABORTING（只有 #13 / #14 / #15 转到它）给设备发中止（<see cref="AbortDevices"/>）；结束了它的行从任务表拿掉、
    /// 写最后一遍库（不归 CJ 的结束了就不在全貌里了）；往 EAP 报（E40，派发线程上按先后发，PJ 结束一定先于 CJ 完成）。
    /// </summary>
    private void OnProcessJobStateChanged(ProcessJob job, int number)
    {
        if (job.State == ProcessJobState.Aborting)
        {
            AbortDevices(job);
        }

        if (job.IsEnded)
        {
            Tasks.Close(job);
            Store(job, JobDtos.Of(job));
        }

        var callback = E40Callback;
        if (callback is not null)
        {
            var dto = JobDtos.Of(job);
            EapNotifierComponent.Current?.Post(() => callback.ProcessJobStateChanged(dto, number));
        }

        _dirty = true;
    }

    /// <summary>
    /// PJ 进 ABORTING：任务组件把它的行标上中止，中止这个 PJ 正在执行的搬运（手臂在动的由搬运管理发设备中止），
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

    /// <summary>
    /// CJ 转了：刚完成（#10 / #11 / #12 转进 COMPLETED，还没删）告诉 LoadPort 这个载具的活干完了（转成 E87 的 CarrierComplete）；
    /// 删掉了（#2 / #13）写最后一遍库（之后就不在全貌里了）；往 EAP 报（E94）。
    /// </summary>
    private void OnControlJobStateChanged(ControlJob job, int number)
    {
        if (job.State == ControlJobState.Completed && !job.IsEnded)
        {
            LoadPort(job.LoadPort)?.NoteCarrierComplete();
        }

        if (job.IsEnded)
        {
            Store(job, JobDtos.Of(job));
        }

        var callback = E94Callback;
        if (callback is not null)
        {
            var dto = JobDtos.Of(job);
            EapNotifierComponent.Current?.Post(() => callback.ControlJobStateChanged(dto, number));
        }

        _dirty = true;
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
    /// 自动转换都是往前走（不会自己转回去），转几轮就停。CJ 要知道的载具好没好、拿没拿走，这里查设备给它。
    /// </summary>
    private void Advance()
    {
        bool changed;
        do
        {
            changed = _processJobs.Advance();
            changed |= _controlJobs.Advance(IsCarrierReady, IsCarrierGone);
        }
        while (changed);

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
    /// ⑤ 有变化才发布：换一份新的全貌（版本加 1）——没删的 CJ、界面要看的 PJ（没结束的，加上没删的 CJ 下面已经结束的）；
    /// 推给界面（留存，重连就能拿到最新的）；里面的每个 CJ、PJ 交给写库线程更新库里那一行（任务进度也跟着进库）。
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
        var controlJobs = _controlJobs.Jobs.ToList();
        var processJobs = controlJobs.SelectMany(control => control.ProcessJobs)
            .Concat(_processJobs.Jobs).Distinct().ToList();

        var snapshot = new JobListDto
        {
            Version = ++_version,
            ControlJobs = controlJobs.Select(JobDtos.Of).ToList(),
            ProcessJobs = processJobs.Select(JobDtos.Of).ToList(),
        };
        _snapshot = snapshot;
        for (int index = 0; index < controlJobs.Count; index++)
        {
            Store(controlJobs[index], snapshot.ControlJobs[index]);
        }

        for (int index = 0; index < processJobs.Count; index++)
        {
            Store(processJobs[index], snapshot.ProcessJobs[index]);
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

    #region 存库

    // CJ、PJ 各一张表，一个 Job 一行（control_job、process_job）：第一次写插一行、记下行号（RowId），之后按行号更新。
    // 每次发布把全貌里的 CJ、PJ 交给一条写库线程，同一个 Job 只写最新的一份；删掉的 CJ、结束的 PJ 由转换那里单独交最后一次。
    // 写库不在扫描线程上做——磁盘一卡，扫描就卡。写不进去只记日志，Job 照跑，下一次变化再写。

    private readonly Channel<bool> _storeSignal = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    /// <summary>等着写的：每个 CJ / PJ 只留最新一份。扫描线程放、写库线程取，换手用 <see cref="_pendingGate"/>。</summary>
    private readonly object _pendingGate = new();
    private Dictionary<ControlJob, ControlJobDto> _pendingControlJobs = [];
    private Dictionary<ProcessJob, (ControlJob? Owner, ProcessJobDto Dto)> _pendingProcessJobs = [];

    private bool _storeStarted;
    private bool _storeTableReady;
    private bool _storeFailing;

    /// <summary>
    /// 重启收场（开机时，写库线程起来之前）：库里还没删的 CJ、没结束的 PJ 就是上次没做完的。不接着跑——重启前在途的搬运、工艺做没做完说不准，
    /// 接着派只会把错放大：CJ 记成中止结束（E94 #12，标着重启；完成了还没删的只补上删掉），PJ 记成中止（E40 #16）。
    /// 机内的片由人确认片位后收回，再重新建 Job（行业里也是这么收场：回片、记异常结束、由 MES / 工程师决定返工还是重做）。读写不了只记日志。
    /// </summary>
    private void CloseOutLastRun()
    {
        try
        {
            using var db = XyzDb.Create(Database);
            EnsureStoreTables(db);
            var now = DateTime.Now;
            var interrupted = new List<string>();
            var controls = db.Queryable<ControlJobEntity>().Where(row => row.EndedBy == 0).ToList();
            foreach (var row in controls)
            {
                if (row.State != (int)ControlJobState.Completed)
                {
                    row.State = (int)ControlJobState.Completed;
                    row.CompletedBy = 12; // E94 #12：中止做完进 COMPLETED
                    row.Ending = ControlJobEnding.Abort.ToString();
                    row.CompletedAt = now;
                    row.Restarted = true;
                    interrupted.Add($"{row.Name}（{row.LoadPort}）");
                }

                row.EndedBy = 13; // E94 #13：完成后删掉
                row.EndedAt = now;
                row.UpdatedTime = now;
            }

            var processes = db.Queryable<ProcessJobEntity>().Where(row => row.EndedBy == 0).ToList();
            foreach (var row in processes)
            {
                row.State = (int)ProcessJobState.Aborted;
                row.EndedBy = 16; // E40 #16：中止做完
                row.EndedAt = now;
                row.UpdatedTime = now;
            }

            if (controls.Count > 0)
            {
                db.Updateable(controls).ExecuteCommand();
            }

            if (processes.Count > 0)
            {
                db.Updateable(processes).ExecuteCommand();
            }

            if (interrupted.Count > 0)
            {
                LogHelper.Warn(Name, $"上次有 {interrupted.Count} 个 Job 没做完就重启了：{string.Join("、", interrupted)}。重启后不接着跑，库里已记成中止；"
                    + "机内的片到现场确认片位后收回，再重新建 Job");
            }
        }
        catch (Exception exception)
        {
            LogHelper.Error(Name, $"重启收场写库失败（上次没做完的 Job 在库里还是原来的状态）：{exception.Message}");
        }
    }

    /// <summary>交一个 CJ 去写库（同一个 CJ 只留最新的一份）。在 Job 的锁里调。</summary>
    private void Store(ControlJob job, ControlJobDto dto)
    {
        if (!_storeStarted)
        {
            return;
        }

        lock (_pendingGate)
        {
            _pendingControlJobs[job] = dto;
        }

        _storeSignal.Writer.TryWrite(true);
    }

    /// <summary>交一个 PJ 去写库（同一个 PJ 只留最新的一份），连同它现在归的 CJ（写的时候要那个 CJ 的行号）。在 Job 的锁里调。</summary>
    private void Store(ProcessJob job, ProcessJobDto dto)
    {
        if (!_storeStarted)
        {
            return;
        }

        lock (_pendingGate)
        {
            _pendingProcessJobs[job] = (job.ControlJob, dto);
        }

        _storeSignal.Writer.TryWrite(true);
    }

    private async Task StoreLoopAsync()
    {
        while (await _storeSignal.Reader.WaitToReadAsync().ConfigureAwait(false))
        {
            _storeSignal.Reader.TryRead(out _);
            Dictionary<ControlJob, ControlJobDto> controls;
            Dictionary<ProcessJob, (ControlJob? Owner, ProcessJobDto Dto)> processes;
            lock (_pendingGate)
            {
                controls = _pendingControlJobs;
                processes = _pendingProcessJobs;
                _pendingControlJobs = [];
                _pendingProcessJobs = [];
            }

            if (controls.Count > 0 || processes.Count > 0)
            {
                WriteRows(controls, processes);
            }
        }
    }

    /// <summary>写一批：先写 CJ（PJ 要记它的行号），没写过的插一行、记下行号，写过的按行号更新。</summary>
    private void WriteRows(Dictionary<ControlJob, ControlJobDto> controls, Dictionary<ProcessJob, (ControlJob? Owner, ProcessJobDto Dto)> processes)
    {
        try
        {
            using var db = XyzDb.Create(Database);
            EnsureStoreTables(db);
            foreach (var (job, dto) in controls)
            {
                var row = new ControlJobEntity
                {
                    Name = dto.Id,
                    LoadPort = dto.LoadPort,
                    CarrierId = dto.CarrierId,
                    LotId = dto.LotId,
                    State = dto.State,
                    AutoStart = dto.AutoStart,
                    Ending = dto.Ending,
                    CreatedTime = dto.CreatedAt,
                    StartedAt = dto.StartedAt,
                    CompletedAt = dto.CompletedAt,
                    CompletedBy = dto.CompletedBy,
                    EndedBy = dto.EndedBy,
                    EndedAt = dto.EndedAt,
                };
                job.RowId = SaveRow(db, row, job.RowId);
            }

            foreach (var (job, (owner, dto)) in processes)
            {
                var row = new ProcessJobEntity
                {
                    Name = dto.Id,
                    ControlJob = dto.ControlJob,
                    ControlJobRowId = owner?.RowId ?? 0,
                    CarrierId = dto.CarrierId,
                    Sequence = dto.Sequence,
                    SequenceRevision = dto.SequenceRevision,
                    State = dto.State,
                    AutoStart = dto.AutoStart,
                    CreatedTime = dto.CreatedAt,
                    StartedAt = dto.StartedAt,
                    EndedAt = dto.EndedAt,
                    EndedBy = dto.EndedBy,
                    Wafers = JsonHelper.Serialize(dto.Wafers),
                };
                job.RowId = SaveRow(db, row, job.RowId);
            }
        }
        catch (Exception exception)
        {
            if (!_storeFailing)
            {
                _storeFailing = true;
                LogHelper.Warn(Name, $"Job 写库失败（Job 照跑，下一次变化再写）：{exception.Message}");
            }

            return;
        }

        if (_storeFailing)
        {
            _storeFailing = false;
            LogHelper.Info(Name, "Job 写库恢复正常");
        }
    }

    /// <summary>没写过（行号 0）插一行、回新行号；写过的按行号整行更新。</summary>
    private static long SaveRow<TEntity>(ISqlSugarClient db, TEntity row, long rowId) where TEntity : BaseEntity, new()
    {
        row.UpdatedTime = DateTime.Now;
        if (rowId == 0)
        {
            return db.Insertable(row).ExecuteReturnBigIdentity();
        }

        row.Id = rowId;
        db.Updateable(row).ExecuteCommand();
        return rowId;
    }

    /// <summary>表第一次用到时建（已有就只补列）。</summary>
    private void EnsureStoreTables(ISqlSugarClient db)
    {
        if (_storeTableReady)
        {
            return;
        }

        db.CodeFirst.InitTables<ControlJobEntity, ProcessJobEntity>();
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
