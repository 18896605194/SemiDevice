using SqlSugar;
using System.Globalization;
using System.Threading.Channels;
using xyz.Common.Log;
using xyz.Components;
using xyz.Components.Attributes;
using xyz.Components.Components;
using xyz.Components.Enums;
using xyz.Components.Interfaces;
using xyz.Database;
using xyz.Database.DbProvider;
using xyz.Database.Jobs;
using xyz.Drivers.Loadport;
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

    #region 任务+调度 组件
    private BaseTaskComponent? _tasks;
    private SchedulerComponent? _scheduler;
    #endregion

    #region CJ / PJ 管理
    private readonly IPjManager _processJobs;
    private readonly ICjManager _controlJobs;

    #endregion

    #region EAP 上报口

    /// <summary>E40（PJ）上报口；null 表示没接 EAP，照常跑。装配时由 EAP 侧挂上。</summary>
    public IE40Callback? E40Callback { get; set; }

    /// <summary>E94（CJ）上报口；null 表示没接 EAP。上报放进 EAP 的派发组件（EapNotifierComponent），按发生先后发。</summary>
    public IE94Callback? E94Callback { get; set; }

    #endregion

    public JobManager()
    {
        Current = this;
        _processJobs = new PjManager();
        _controlJobs = new CjManager();
        _processJobs.StateChanged += OnProcessJobStateChanged;
        _controlJobs.StateChanged += OnControlJobStateChanged;
    }

    #region SC

    [SCEditor("True", "Job", "是否启用 Job 管理（False = 不收 Job 命令、不按 Job 调度）")]
    public bool IsEnable { get; set; } = true;

    [SCEditor("True", "Job", "PJ 准备好后直接开始；False = 等待 PJ Start 命令")]
    public bool ProcessJobAutoStart { get; set; } = true;

    [SCEditor("False", "Job", "CJ 的载具准备好后直接开始；False = 等待 CJ Start 命令")]
    public bool ControlJobAutoStart { get; set; }

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

    protected override int SlowScanWarnMilliseconds
    {
        get { return SlowScanWarnMs; }
    }

    protected override int SlowScanAlarmMilliseconds
    {
        get { return SlowScanAlarmMs; }
    }

    #endregion

    #region 报警

    [Alarm("PJ 等的载具到了，但定不了片", AlarmCategory.ProcessError,
        AlarmLevel = AlarmLevel.Alarm1,
        Description = "Host 先建的 PJ 等的载具到了 LoadPort、也能取片了，但片用不了：槽里没片、片不正常或已经做过、片归了别的 PJ、"
            + "流程配方第 1 步没勾这个 LoadPort、回片槽用不了、这个口被别的 Job 占着等。PJ 留在排队不动，哪个 PJ、什么原因看 Job 的日志",
        Solution = "核对载具和 PJ 要做的槽：不做了让 Host 停止 / 中止它的 CJ（排队的 PJ 一起删掉），没归 CJ 的取消这个 PJ；"
            + "要做就这样收场后按实际的片重新建")]
    public string MaterialUnusableAlarm = nameof(MaterialUnusableAlarm);

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

            Publish();
        }

        LogHelper.Info(Name, $"Job 管理已绑定：任务组件 {tasks.GetType().Name}，调度 {_scheduler.GetType().Name}");
    }

    #endregion

    #region Job 查询

    /// <summary>在 Job 锁内读取当前 CJ、PJ，生成独立的查询结果。</summary>
    public JobListDto Snapshot
    {
        get
        {
            lock (_gate)
            {
                var controlJobs = _controlJobs.ControlJobs;
                var processJobs = controlJobs.SelectMany(control => control.ProcessJobs)
                    .Concat(_processJobs.ProcessJobs).Distinct().ToList();

                return new JobListDto
                {
                    ControlJobs = controlJobs.Select(job => JobDtos.Of(job, ControlJobAutoStart)).ToList(),
                    ProcessJobs = processJobs.Select(job => JobDtos.Of(job, ProcessJobAutoStart)).ToList(),
                };
            }
        }
    }

    /// <summary>按载具号从 CJ 队列查找，在 Job 锁内读取并返回 DTO 副本。</summary>
    public ControlJobDto? FindControlJobByCarrier(string carrierId)
    {
        string wanted = carrierId.Trim();
        if (wanted.Length == 0)
        {
            return null;
        }

        lock (_gate)
        {
            var job = _controlJobs.FindByCarrier(wanted);
            if (job is null)
            {
                return null;
            }

            return JobDtos.Of(job, ControlJobAutoStart);
        }
    }

    /// <summary>按载具号找 PJ，在 Job 锁内读取并返回 DTO 副本；包含保留在 CJ 下的已结束 PJ。</summary>
    public IReadOnlyList<ProcessJobDto> FindProcessJobsByCarrier(string carrierId)
    {
        string wanted = carrierId.Trim();
        if (wanted.Length == 0)
        {
            return [];
        }

        lock (_gate)
        {
            var processJobs = _controlJobs.ControlJobs.SelectMany(control => control.ProcessJobs)
                .Concat(_processJobs.ProcessJobs).Distinct();

            return processJobs
                .Where(job => string.Equals(job.CarrierId, wanted, StringComparison.OrdinalIgnoreCase))
                .Select(job => JobDtos.Of(job, ProcessJobAutoStart)).ToList();
        }
    }

    /// <summary>这一片现在归哪个 PJ；不归任何没结束的 PJ 返回 null。任意线程可调（搬运管理受理手动单时问）。</summary>
    public string? OwnerOf(Guid waferId)
    {
        return _processJobs.OwnerOf(waferId);
    }



    #endregion

    #region 创建 PJ、CJ 和 Job

    public Task<HandleResult> CreateProcessJobAsync(string? loadPort, string pjName, IReadOnlyList<int> slots, string sequence, string? lotId, string? carrierId = null)
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
            string id = pjName.Trim();
            var idError = CheckNewId(id);
            if (idError is not null)
            {
                return Task.FromResult(idError);
            }

            var portError = FindLoadPort(loadPort, carrierId, out var port);
            if (portError is not null)
            {
                return Task.FromResult(portError);
            }

            // 本地建（给了 LoadPort）要挑片；Host 建（按载具号）没给槽号 = 料到了取载具上全部有片的槽
            bool byCarrier = string.IsNullOrWhiteSpace(loadPort);
            var selectedSlots = slots.Distinct().ToList();
            if (!byCarrier && selectedSlots.Count == 0)
            {
                return Task.FromResult(HandleResult.Fail(ErrorCodes.JobNoWafers, port!.Name));
            }

            var ledger = WaferManagerComponent.Current;
            if (ledger is null || !ledger.IsEnable)
            {
                return Task.FromResult(HandleResult.Fail(ErrorCodes.WaferLedgerDisabled));
            }

            var sequenceError = TryTakeSequence(sequence.Trim(), out var sequenceSnapshot);
            if (sequenceError is not null)
            {
                return Task.FromResult(sequenceError);
            }

            string? carrier = port is not null ? port._carrier.CarrierId : carrierId!.Trim();
            if (byCarrier)
            {
                var claimError = CheckSlotsFree(carrier!, selectedSlots);
                if (claimError is not null)
                {
                    return Task.FromResult(claimError);
                }
            }

            var job = new ProcessJob
            {
                Id = id,
                Sequence = sequenceSnapshot,
                CarrierId = carrier,
                Slots = selectedSlots,
                LotId = string.IsNullOrWhiteSpace(lotId) ? null : lotId.Trim(),
            };

            // 料已经在、能取片了就当场定片（查片、定回片槽、生成任务行），不行整个不建；料没到先建着，扫描里等料到了再定
            if (port is not null && port.IsCarrierReady)
            {
                var assignError = AssignWafers(job, port);
                if (assignError is not null)
                {
                    return Task.FromResult(assignError);
                }
            }

            _processJobs.Add(job);
            try
            {
                Tasks.Add(job);
                var queued = _processJobs.Queue(job);
                if (!queued.IsSuccess)
                {
                    _processJobs.Remove(job);
                    Tasks.Close(job);
                    return Task.FromResult(queued);
                }
            }
            catch
            {
                _processJobs.Remove(job);
                Tasks.Close(job);
                throw;
            }

            if (job.IsWaitingForMaterial)
            {
                LogHelper.Info(Name, $"PJ {id} 建好了，等载具 {carrier} 到 LoadPort、能取片（Load 好，接了 EAP 时槽图认定）再定片");
            }

            Publish();
            return Task.FromResult(HandleResult.Success(job.Id));
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

    public Task<HandleResult> CreateControlJobAsync(string? loadPort, IReadOnlyList<string> processJobs, string? cjName = null, string? lotId = null)
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
            string name = cjName?.Trim() ?? string.Empty;
            string? lot = lotId?.Trim();
            if (string.IsNullOrEmpty(lot))
            {
                lot = null;
            }

            if (name.Length == 0)
            {
                if (lot is not null)
                {
                    name = lot;
                }
                else
                {
                    name = $"CJ-{loadPort?.Trim()}-{DateTime.Now.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture)}";
                }
            }

            var idError = CheckNewId(name);
            if (idError is not null)
            {
                return Task.FromResult(idError);
            }

            // 下面的 PJ 要是同一个载具的：定了片的在同一个 LoadPort；料没到的按载具号认，口等它们定片时再填。
            // 指定了 LoadPort（本地建）的只收已经定了片的 PJ，免得口对不上。
            var processes = new List<ProcessJob>();
            string id = name;
            string? givenPort = loadPort?.Trim();
            if (string.IsNullOrEmpty(givenPort))
            {
                givenPort = null;
            }

            string? portName = givenPort;
            string? carrier = null;
            foreach (string processId in processJobs)
            {
                var process = _processJobs.Get(processId.Trim());
                if (process is null || process.ControlJob is not null || process.State != ProcessJobState.QueuedPooled
                    || processes.Contains(process))
                {
                    return Task.FromResult(HandleResult.Fail(ErrorCodes.JobProcessJobUnavailable, processId.Trim()));
                }

                if (process.IsWaitingForMaterial)
                {
                    if (givenPort is not null)
                    {
                        return Task.FromResult(HandleResult.Fail(ErrorCodes.JobProcessJobUnavailable, processId.Trim()));
                    }
                }
                else
                {
                    string port = process.Rows[0].SourcePort;
                    if (portName is not null && !string.Equals(portName, port, StringComparison.OrdinalIgnoreCase))
                    {
                        return Task.FromResult(HandleResult.Fail(ErrorCodes.JobProcessJobUnavailable, processId.Trim()));
                    }

                    portName = port;
                }

                if (!string.IsNullOrEmpty(process.CarrierId))
                {
                    if (carrier is null)
                    {
                        carrier = process.CarrierId;
                    }
                    else if (!string.Equals(carrier, process.CarrierId, StringComparison.OrdinalIgnoreCase))
                    {
                        return Task.FromResult(HandleResult.Fail(ErrorCodes.JobProcessJobUnavailable, processId.Trim()));
                    }
                }

                processes.Add(process);
            }

            if (processes.Count == 0)
            {
                return Task.FromResult(HandleResult.Fail(ErrorCodes.JobNoWafers, id));
            }

            if (portName is not null)
            {
                var busy = _controlJobs.FindByLoadPort(portName);
                if (busy is not null)
                {
                    return Task.FromResult(HandleResult.Fail(ErrorCodes.JobLoadPortBusy, portName, busy.Id));
                }
            }
            else
            {
                // 料都没到：同一个载具已经有没删的 CJ 就不收（到了也只能有一个 CJ 占那个口）
                var busy = _controlJobs.FindByCarrier(carrier!);
                if (busy is not null)
                {
                    return Task.FromResult(HandleResult.Fail(ErrorCodes.JobCarrierBusy, carrier!, busy.Id));
                }
            }

            // 载具跟着 PJ：本地建的是 PJ 建的时候 LoadPort 上那个载具，Host 建的是 Host 给的载具号
            var job = new ControlJob
            {
                Id = id,
                LoadPort = portName ?? string.Empty,
                CarrierId = carrier,
                LotId = lot ?? processes[0].LotId,
            };

            _controlJobs.Add(job);
            try
            {
                // 收下已创建的 PJ，按传入名称的顺序关联。
                foreach (var process in processes)
                {
                    process.ControlJob = job;
                    job.ProcessJobs.Add(process);
                }

                var queued = _controlJobs.Queue(job);
                if (!queued.IsSuccess)
                {
                    foreach (var process in processes)
                    {
                        process.ControlJob = null;
                    }

                    job.ProcessJobs.Clear();
                    _controlJobs.Remove(job);
                    return Task.FromResult(queued);
                }
            }
            catch
            {
                foreach (var process in processes)
                {
                    process.ControlJob = null;
                }

                job.ProcessJobs.Clear();
                _controlJobs.Remove(job);
                throw;
            }


            Publish();
            return Task.FromResult(HandleResult.Success(job.Id));
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

    public async Task<HandleResult> CreateJobAsync(string? loadPort, IReadOnlyList<string> processJobs, string? cjName = null, string? lotId = null)
    {
        var result = await CreateControlJobAsync(loadPort, processJobs, cjName, lotId).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return result;
        }

        return HandleResult.Success(new JobCreatedDto
        {
            ControlJob = (string)result.Result!,
            ProcessJobs = processJobs.Select(id => id.Trim()).ToList(),
        });
    }

    #endregion

    #region PJ 命令

    public Task<HandleResult> StartProcessJobAsync(string id)
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
            id = id.Trim();
            var processJob = _processJobs.Get(id);
            if (processJob is null)
            {
                return Task.FromResult(HandleResult.Fail(ErrorCodes.JobNotFound, id));
            }

            if (!IsAuto)
            {
                return Task.FromResult(HandleResult.Fail(ErrorCodes.JobNotAuto));
            }

            var result = _processJobs.Start(processJob);

            if (result.ErrorMessage == ErrorCodes.JobCommandNotAllowed)
            {
                result.Args = [id, JobNames.Of(ProcessJobCommand.Start), JobNames.Of(processJob.State)];
            }

            if (result.IsSuccess)
            {
                Publish();
            }

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

    public Task<HandleResult> PauseProcessJobAsync(string id)
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
            id = id.Trim();
            var processJob = _processJobs.Get(id);
            if (processJob is null)
            {
                return Task.FromResult(HandleResult.Fail(ErrorCodes.JobNotFound, id));
            }

            var result = _processJobs.Pause(processJob);

            if (result.ErrorMessage == ErrorCodes.JobCommandNotAllowed)
            {
                result.Args = [id, JobNames.Of(ProcessJobCommand.Pause), JobNames.Of(processJob.State)];
            }

            if (result.IsSuccess)
            {
                Publish();
            }

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

    public Task<HandleResult> ResumeProcessJobAsync(string id)
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
            id = id.Trim();
            var processJob = _processJobs.Get(id);
            if (processJob is null)
            {
                return Task.FromResult(HandleResult.Fail(ErrorCodes.JobNotFound, id));
            }

            var result = _processJobs.Resume(processJob);

            if (result.ErrorMessage == ErrorCodes.JobCommandNotAllowed)
            {
                result.Args = [id, JobNames.Of(ProcessJobCommand.Resume), JobNames.Of(processJob.State)];
            }

            if (result.IsSuccess)
            {
                Publish();
            }

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

    public Task<HandleResult> StopProcessJobAsync(string id)
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
            id = id.Trim();
            var processJob = _processJobs.Get(id);
            if (processJob is null)
            {
                return Task.FromResult(HandleResult.Fail(ErrorCodes.JobNotFound, id));
            }

            HandleResult result;
            if (processJob.State == ProcessJobState.QueuedPooled)
            {
                result = _processJobs.Dequeue(processJob);
            }
            else
            {
                result = _processJobs.Stop(processJob);
            }

            if (result.ErrorMessage == ErrorCodes.JobCommandNotAllowed)
            {
                result.Args = [id, JobNames.Of(ProcessJobCommand.Stop), JobNames.Of(processJob.State)];
            }

            if (result.IsSuccess)
            {
                Publish();
            }

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

    public Task<HandleResult> AbortProcessJobAsync(string id)
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
            id = id.Trim();
            var processJob = _processJobs.Get(id);
            if (processJob is null)
            {
                return Task.FromResult(HandleResult.Fail(ErrorCodes.JobNotFound, id));
            }

            HandleResult result;
            if (processJob.State == ProcessJobState.QueuedPooled)
            {
                result = _processJobs.Dequeue(processJob);
            }
            else
            {
                result = _processJobs.Abort(processJob);
            }

            if (result.ErrorMessage == ErrorCodes.JobCommandNotAllowed)
            {
                result.Args = [id, JobNames.Of(ProcessJobCommand.Abort), JobNames.Of(processJob.State)];
            }

            if (result.IsSuccess)
            {
                Publish();
            }

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

    public Task<HandleResult> CancelProcessJobAsync(string id)
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
            id = id.Trim();
            var processJob = _processJobs.Get(id);
            if (processJob is null)
            {
                return Task.FromResult(HandleResult.Fail(ErrorCodes.JobNotFound, id));
            }

            var result = _processJobs.Dequeue(processJob);

            if (result.ErrorMessage == ErrorCodes.JobCommandNotAllowed)
            {
                result.Args = [id, JobNames.Of(ProcessJobCommand.Cancel), JobNames.Of(processJob.State)];
            }

            if (result.IsSuccess)
            {
                Publish();
            }

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

    #endregion

    #region CJ 命令

    public Task<HandleResult> StartControlJobAsync(string id)
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
            id = id.Trim();
            var controlJob = _controlJobs.Get(id);
            if (controlJob is null)
            {
                return Task.FromResult(HandleResult.Fail(ErrorCodes.JobNotFound, id));
            }

            if (!IsAuto)
            {
                return Task.FromResult(HandleResult.Fail(ErrorCodes.JobNotAuto));
            }

            if (controlJob.Ending != ControlJobEnding.None)
            {
                return Task.FromResult(HandleResult.Fail(ErrorCodes.JobEnding, id, JobNames.Of(ControlJobCommand.Start)));
            }

            if (controlJob.State != ControlJobState.WaitingForStart)
            {
                return Task.FromResult(HandleResult.Fail(ErrorCodes.JobCommandNotAllowed,
                    id, JobNames.Of(ControlJobCommand.Start), JobNames.Of(controlJob.State)));
            }

            var result = _controlJobs.Activate(controlJob);

            if (result.ErrorMessage == ErrorCodes.JobCommandNotAllowed)
            {
                result.Args = [id, JobNames.Of(ControlJobCommand.Start), JobNames.Of(controlJob.State)];
            }

            if (result.IsSuccess)
            {
                Publish();
            }

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

    public Task<HandleResult> PauseControlJobAsync(string id)
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
            id = id.Trim();
            var controlJob = _controlJobs.Get(id);
            if (controlJob is null)
            {
                return Task.FromResult(HandleResult.Fail(ErrorCodes.JobNotFound, id));
            }

            if (controlJob.Ending != ControlJobEnding.None)
            {
                return Task.FromResult(HandleResult.Fail(ErrorCodes.JobEnding, id, JobNames.Of(ControlJobCommand.Pause)));
            }

            var result = _controlJobs.Pause(controlJob);

            if (result.ErrorMessage == ErrorCodes.JobCommandNotAllowed)
            {
                result.Args = [id, JobNames.Of(ControlJobCommand.Pause), JobNames.Of(controlJob.State)];
            }

            if (result.IsSuccess)
            {
                Publish();
            }

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

    public Task<HandleResult> ResumeControlJobAsync(string id)
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
            id = id.Trim();
            var controlJob = _controlJobs.Get(id);
            if (controlJob is null)
            {
                return Task.FromResult(HandleResult.Fail(ErrorCodes.JobNotFound, id));
            }

            if (controlJob.Ending != ControlJobEnding.None)
            {
                return Task.FromResult(HandleResult.Fail(ErrorCodes.JobEnding, id, JobNames.Of(ControlJobCommand.Resume)));
            }

            var result = _controlJobs.Resume(controlJob);

            if (result.ErrorMessage == ErrorCodes.JobCommandNotAllowed)
            {
                result.Args = [id, JobNames.Of(ControlJobCommand.Resume), JobNames.Of(controlJob.State)];
            }

            if (result.IsSuccess)
            {
                Publish();
            }

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

    public Task<HandleResult> StopControlJobAsync(string id, ControlJobAction action = ControlJobAction.RemoveJobs)
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
            id = id.Trim();
            var controlJob = _controlJobs.Get(id);
            if (controlJob is null)
            {
                return Task.FromResult(HandleResult.Fail(ErrorCodes.JobNotFound, id));
            }

            HandleResult result;
            if (controlJob.State == ControlJobState.Queued)
            {
                EndControlJobProcesses(controlJob, ControlJobCommand.Stop, action);
                result = _controlJobs.Dequeue(controlJob);
            }
            else if (!controlJob.IsActive)
            {
                result = HandleResult.Fail(ErrorCodes.JobCommandNotAllowed,
                    id, JobNames.Of(ControlJobCommand.Stop), JobNames.Of(controlJob.State));
            }
            else
            {
                if (controlJob.Ending == ControlJobEnding.None)
                {
                    controlJob.Ending = ControlJobEnding.Stop;
                    EndControlJobProcesses(controlJob, ControlJobCommand.Stop, action);
                }

                result = HandleResult.Success(id);
            }

            if (result.ErrorMessage == ErrorCodes.JobCommandNotAllowed)
            {
                result.Args = [id, JobNames.Of(ControlJobCommand.Stop), JobNames.Of(controlJob.State)];
            }

            if (result.IsSuccess)
            {
                Publish();
            }

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

    public Task<HandleResult> AbortControlJobAsync(string id, ControlJobAction action = ControlJobAction.RemoveJobs)
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
            id = id.Trim();
            var controlJob = _controlJobs.Get(id);
            if (controlJob is null)
            {
                return Task.FromResult(HandleResult.Fail(ErrorCodes.JobNotFound, id));
            }

            HandleResult result;
            if (controlJob.State == ControlJobState.Queued)
            {
                EndControlJobProcesses(controlJob, ControlJobCommand.Abort, action);
                result = _controlJobs.Dequeue(controlJob);
            }
            else if (controlJob.State == ControlJobState.Aborting)
            {
                result = HandleResult.Success(id);
            }
            else
            {
                result = _controlJobs.Abort(controlJob);
                if (result.IsSuccess)
                {
                    EndControlJobProcesses(controlJob, ControlJobCommand.Abort, action);
                }
            }

            if (result.ErrorMessage == ErrorCodes.JobCommandNotAllowed)
            {
                result.Args = [id, JobNames.Of(ControlJobCommand.Abort), JobNames.Of(controlJob.State)];
            }

            if (result.IsSuccess)
            {
                Publish();
            }

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

    public Task<HandleResult> CancelControlJobAsync(string id, ControlJobAction action = ControlJobAction.RemoveJobs)
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
            id = id.Trim();
            var controlJob = _controlJobs.Get(id);
            if (controlJob is null)
            {
                return Task.FromResult(HandleResult.Fail(ErrorCodes.JobNotFound, id));
            }

            if (controlJob.Ending != ControlJobEnding.None)
            {
                return Task.FromResult(HandleResult.Fail(ErrorCodes.JobEnding, id, JobNames.Of(ControlJobCommand.Cancel)));
            }

            if (controlJob.State != ControlJobState.Queued)
            {
                return Task.FromResult(HandleResult.Fail(ErrorCodes.JobCommandNotAllowed,
                    id, JobNames.Of(ControlJobCommand.Cancel), JobNames.Of(controlJob.State)));
            }

            EndControlJobProcesses(controlJob, ControlJobCommand.Cancel, action);
            var result = _controlJobs.Dequeue(controlJob);

            if (result.ErrorMessage == ErrorCodes.JobCommandNotAllowed)
            {
                result.Args = [id, JobNames.Of(ControlJobCommand.Cancel), JobNames.Of(controlJob.State)];
            }

            if (result.IsSuccess)
            {
                Publish();
            }

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

    public Task<HandleResult> DeselectControlJobAsync(string id)
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
            id = id.Trim();
            var controlJob = _controlJobs.Get(id);
            if (controlJob is null)
            {
                return Task.FromResult(HandleResult.Fail(ErrorCodes.JobNotFound, id));
            }

            if (controlJob.Ending != ControlJobEnding.None)
            {
                return Task.FromResult(HandleResult.Fail(ErrorCodes.JobEnding, id, JobNames.Of(ControlJobCommand.Deselect)));
            }

            var result = _controlJobs.Deselect(controlJob);

            if (result.ErrorMessage == ErrorCodes.JobCommandNotAllowed)
            {
                result.Args = [id, JobNames.Of(ControlJobCommand.Deselect), JobNames.Of(controlJob.State)];
            }

            if (result.IsSuccess)
            {
                Publish();
            }

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

    #endregion

    #region EAP 命令编号适配

    public Task<HandleResult> ExecuteControlJobCommandAsync(string id, ControlJobCommand command, ControlJobAction action)
    {
        switch (command)
        {
            case ControlJobCommand.Start:
                return StartControlJobAsync(id);
            case ControlJobCommand.Pause:
                return PauseControlJobAsync(id);
            case ControlJobCommand.Resume:
                return ResumeControlJobAsync(id);
            case ControlJobCommand.Cancel:
                return CancelControlJobAsync(id, action);
            case ControlJobCommand.Deselect:
                return DeselectControlJobAsync(id);
            case ControlJobCommand.Stop:
                return StopControlJobAsync(id, action);
            case ControlJobCommand.Abort:
                return AbortControlJobAsync(id, action);
        }

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
            id = id.Trim();
            var job = _controlJobs.Get(id);
            if (job is null)
            {
                return Task.FromResult(HandleResult.Fail(ErrorCodes.JobNotFound, id));
            }

            if (job.Ending != ControlJobEnding.None)
            {
                return Task.FromResult(HandleResult.Fail(ErrorCodes.JobEnding, id, JobNames.Of(command)));
            }

            return Task.FromResult(HandleResult.Fail(ErrorCodes.JobCommandNotAllowed,
                id, JobNames.Of(command), JobNames.Of(job.State)));
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

    public Task<HandleResult> ExecuteProcessJobCommandAsync(string id, ProcessJobCommand command)
    {
        switch (command)
        {
            case ProcessJobCommand.Start:
                return StartProcessJobAsync(id);
            case ProcessJobCommand.Pause:
                return PauseProcessJobAsync(id);
            case ProcessJobCommand.Resume:
                return ResumeProcessJobAsync(id);
            case ProcessJobCommand.Stop:
                return StopProcessJobAsync(id);
            case ProcessJobCommand.Abort:
                return AbortProcessJobAsync(id);
            case ProcessJobCommand.Cancel:
                return CancelProcessJobAsync(id);
        }

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
            id = id.Trim();
            var job = _processJobs.Get(id);
            if (job is null)
            {
                return Task.FromResult(HandleResult.Fail(ErrorCodes.JobNotFound, id));
            }

            return Task.FromResult(HandleResult.Fail(ErrorCodes.JobCommandNotAllowed,
                id, JobNames.Of(command), JobNames.Of(job.State)));
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

    #endregion

    #region 整机中止与任务处理

    /// <summary>
    /// 整机停止：所有没结束的 Job 走中止（等设备确认、核对片位），不直接给模块发中止。
    /// </summary>
    public Task<HandleResult> AbortAllAsync()
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
            foreach (var controlJob in _controlJobs.ControlJobs)
            {
                if (controlJob.IsEnded || controlJob.State is ControlJobState.Completed or ControlJobState.Aborted)
                {
                    continue;
                }

                if (controlJob.State == ControlJobState.Queued)
                {
                    EndControlJobProcesses(controlJob, ControlJobCommand.Abort, ControlJobAction.RemoveJobs);
                    _controlJobs.Dequeue(controlJob);
                }
                else
                {
                    if (controlJob.State != ControlJobState.Aborting)
                    {
                        _controlJobs.Abort(controlJob);
                    }

                    EndControlJobProcesses(controlJob, ControlJobCommand.Abort, ControlJobAction.RemoveJobs);
                }
            }
            foreach (var processJob in _processJobs.ProcessJobs)
            {
                if (processJob.ControlJob is not null)
                {
                    continue;
                }

                if (processJob.State == ProcessJobState.QueuedPooled)
                {
                    _processJobs.Dequeue(processJob);
                }
                else
                {
                    _processJobs.Abort(processJob);
                }
            }
            Publish();
            return Task.FromResult(HandleResult.Success(string.Empty));
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

    /// <summary>
    /// 出错的任务重做（本地界面用，Host 不碰）：任务退回等着做，调度按片现在在哪重新派。片按 PJ 和来源槽认，任务按这一行里的序号（从 0 开始）。
    /// </summary>
    public Task<HandleResult> RetryTaskAsync(string processJob, int slot, int task)
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
            var job = _processJobs.Get(processJob.Trim());
            if (job is null)
            {
                Publish();
                return Task.FromResult(HandleResult.Fail(ErrorCodes.JobNotFound, processJob.Trim()));
            }

            var result = Tasks.Retry(job, slot, task);
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

    /// <summary>
    /// 出错的任务标记完成（本地界面用，Host 不碰）：人已经把这一步做完了，接着走。取放要片在账上正好在这一步做完该在的地方。
    /// </summary>
    public Task<HandleResult> CompleteTaskAsync(string processJob, int slot, int task)
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
            var job = _processJobs.Get(processJob.Trim());
            if (job is null)
            {
                Publish();
                return Task.FromResult(HandleResult.Fail(ErrorCodes.JobNotFound, processJob.Trim()));
            }

            var result = Tasks.Complete(job, slot, task);
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

    #endregion

    #region Job 创建检查

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

        bool inUse = _controlJobs.Get(id) is not null || _processJobs.Get(id) is not null;
        if (inUse)
        {
            return HandleResult.Fail(ErrorCodes.JobIdDuplicate, id);
        }

        return null;
    }

    /// <summary>
    /// 料在哪个 LoadPort：给了 LoadPort 就按它（本地建，载具号可能没读到），载具要能取片（放着、Load 好，接了 EAP 时槽图认定）；
    /// 没给按载具号找（Host 建）：载具在哪个口上就是哪个，还没到为 null（先建着，料到了再定片）。
    /// </summary>
    private HandleResult? FindLoadPort(string? loadPort, string? carrierId, out BaseLoadPortModule? port)
    {
        port = null;
        string name = (loadPort ?? string.Empty).Trim();
        if (name.Length > 0)
        {
            var found = LoadPort(name);
            if (found is null)
            {
                return HandleResult.Fail(ErrorCodes.JobLoadPortNotFound, name);
            }

            if (!found.IsCarrierReady)
            {
                return HandleResult.Fail(ErrorCodes.JobCarrierNotReady, found.Name);
            }

            port = found;
            return null;
        }

        string carrier = (carrierId ?? string.Empty).Trim();
        if (carrier.Length == 0)
        {
            return HandleResult.Fail(ErrorCodes.JobCarrierNotFound, carrier);
        }

        port = FindCarrierPort(carrier);
        return null;
    }

    /// <summary>这个载具在哪个 LoadPort 上（载具号不分大小写）；不在任何口上为 null。</summary>
    private BaseLoadPortModule? FindCarrierPort(string carrierId)
    {
        return LoadPorts.FirstOrDefault(item =>
        {
            var carrier = item._carrier;
            return carrier.IsArrived && string.Equals(carrier.CarrierId, carrierId, StringComparison.OrdinalIgnoreCase);
        });
    }

    /// <summary>
    /// 取流程配方快照（库里的副本）：要在库里、至少三步；最后一步有回片的 LoadPort。
    /// 第 1 步勾没勾来源 LoadPort 等定片时查（Host 先建的 PJ 这时候还不知道料在哪个口）；中间每一站的工艺配方、能去的站点由任务组件生成任务表时查。
    /// </summary>
    private HandleResult? TryTakeSequence(string name, out SequenceData sequence)
    {
        sequence = null!;
        var found = SequenceComponent.Current?.Find(name);
        if (found is null || found.Steps.Count < 3)
        {
            return HandleResult.Fail(ErrorCodes.JobSequenceNotFound, name);
        }

        if (ReturnPorts(found).Count == 0)
        {
            return HandleResult.Fail(ErrorCodes.JobSequenceNoReturn, found.Name);
        }

        sequence = found;
        return null;
    }

    /// <summary>
    /// Host 按载具号建 PJ：同一个载具上要做的片不能归两个没结束的 PJ——槽号重了，或者其中一个没给槽号（= 整个载具）。
    /// 料没到的 PJ 还没有片，只能这样按载具号、槽号查；定片时再按片查一遍归属（<see cref="AssignWafers"/>）。
    /// </summary>
    private HandleResult? CheckSlotsFree(string carrierId, IReadOnlyList<int> slots)
    {
        foreach (var other in _processJobs.ProcessJobs)
        {
            if (!string.Equals(other.CarrierId, carrierId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var taken = other.IsWaitingForMaterial ? other.Slots : other.Rows.Select(row => row.SourceSlot).ToList();
            if (slots.Count == 0 || taken.Count == 0 || slots.Intersect(taken).Any())
            {
                return HandleResult.Fail(ErrorCodes.JobSlotClaimed, carrierId, other.Id);
            }
        }

        return null;
    }

    /// <summary>
    /// 给 PJ 定片（料在这个 LoadPort 上、能取片了）：流程配方第 1 步要勾这个口；没给槽号的取载具上全部有片的槽；
    /// PJ 归了 CJ 而 CJ 还没定口的，这个口不能被别的 CJ 占着；每槽要有片、片正常、没做过、不归别的 PJ，定好回片槽，再照流程配方生成任务行。
    /// 有一项不行就回原因，任务行一行不留。建 PJ 时料已经在就当场定；料没到的扫描里等料到了定（<see cref="AssignWaitingProcessJobs"/>）。
    /// 这里只生成行；挂进任务表、登记片归属由调用方做。
    /// </summary>
    private HandleResult? AssignWafers(ProcessJob job, BaseLoadPortModule port)
    {
        string sourceName = port.Name;
        var sequence = job.Sequence;
        if (!sequence.Steps[0].Stations.Contains(sourceName, StringComparer.OrdinalIgnoreCase))
        {
            return HandleResult.Fail(ErrorCodes.JobSequenceSourceMismatch, sequence.Name, sourceName);
        }

        var ledger = WaferManagerComponent.Current;
        if (ledger is null || !ledger.IsEnable)
        {
            return HandleResult.Fail(ErrorCodes.WaferLedgerDisabled);
        }

        var slots = job.Slots.Count > 0 ? job.Slots : OccupiedSlots(port);
        if (slots.Count == 0)
        {
            return HandleResult.Fail(ErrorCodes.JobNoWafers, sourceName);
        }

        var control = job.ControlJob;
        if (control is not null && control.LoadPort.Length == 0)
        {
            var busy = _controlJobs.FindByLoadPort(sourceName);
            if (busy is not null)
            {
                return HandleResult.Fail(ErrorCodes.JobLoadPortBusy, sourceName, busy.Id);
            }
        }

        var rows = new List<TaskRow>();
        foreach (int slot in slots)
        {
            string slotText = slot.ToString(CultureInfo.InvariantCulture);
            var wafer = ledger.Get(sourceName, slot);
            if (wafer is null)
            {
                return HandleResult.Fail(ErrorCodes.JobSlotEmpty, sourceName, slotText);
            }

            if (wafer.Status != WaferStatus.Normal)
            {
                return HandleResult.Fail(ErrorCodes.JobWaferNotNormal, sourceName, slotText, wafer.WaferId, wafer.Status.ToString());
            }

            if (wafer.ProcessState != WaferProcessState.Idle)
            {
                return HandleResult.Fail(ErrorCodes.JobWaferProcessed, sourceName, slotText, wafer.WaferId, wafer.ProcessState.ToString());
            }

            string? owner = _processJobs.OwnerOf(wafer.Id);
            if (owner is not null)
            {
                return HandleResult.Fail(ErrorCodes.JobWaferOwned, wafer.WaferId, owner);
            }

            var returnError = PickReturnSlot(sequence, sourceName, slot, out string returnPort);
            if (returnError is not null)
            {
                return returnError;
            }

            rows.Add(new TaskRow
            {
                Owner = job.Id,
                WaferId = wafer.Id,
                WaferName = wafer.WaferId,
                SourcePort = sourceName,
                SourceSlot = slot,
                ReturnPort = returnPort,
                ReturnSlot = slot,
            });
        }

        // 每片的任务行照流程配方生成（任务组件管）；生成不了就一行不留，PJ 还是没定片
        job.Rows.AddRange(rows);
        var taskError = Tasks.Build(job);
        if (taskError is not null)
        {
            job.Rows.Clear();
            return taskError;
        }

        return null;
    }

    /// <summary>载具上有片的槽（槽图里正常和有片说不准的），从下往上：Host 没给槽号时就做这些。</summary>
    private static List<int> OccupiedSlots(BaseLoadPortModule port)
    {
        var slots = new List<int>();
        var map = port._carrier.SlotMap;
        for (int index = 0; index < map.Count; index++)
        {
            var state = map[index];
            if (state == SlotState.CorrectlyOccupied || state == SlotState.NotEmpty)
            {
                slots.Add(index + 1);
            }
        }

        return slots;
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

        string? port = returnPorts.FirstOrDefault(name => LoadPort(name)?.IsCarrierReady == true);
        if (port is null)
        {
            return HandleResult.Fail(ErrorCodes.JobReturnSlotUnavailable, returnPorts[0], slotText);
        }

        var target = LoadPort(port);
        bool claimed = _processJobs.ProcessJobs.Any(job => job.Rows.Any(row =>
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

    /// <summary>CJ 取消、停止或中止时，由 JobManager 协调下属 PJ。</summary>
    private void EndControlJobProcesses(ControlJob controlJob, ControlJobCommand command, ControlJobAction action)
    {
        foreach (var process in controlJob.ProcessJobs.ToList())
        {
            if (process.IsEnded)
            {
                continue;
            }

            if (process.State == ProcessJobState.QueuedPooled)
            {
                if (action == ControlJobAction.RemoveJobs)
                {
                    _processJobs.Dequeue(process);
                }
                else
                {
                    process.ControlJob = null;
                    controlJob.ProcessJobs.Remove(process);
                }

                continue;
            }

            // 已经 PROCESS COMPLETE 的 PJ 不接受 Stop / Abort，回片后自然结束。
            if (command == ControlJobCommand.Stop)
            {
                _processJobs.Stop(process);
            }
            else if (command == ControlJobCommand.Abort)
            {
                _processJobs.Abort(process);
            }
        }
    }


    /// <summary>
    /// PJ 转了：刚进 ABORTING（只有 #13 / #14 / #15 转到它）给设备发中止（<see cref="AbortDevices"/>）；结束了它的行从任务表拿掉、
    /// 写最后一遍库（不归 CJ 的结束了就不在全貌里了）；往 EAP 报（E40，派发线程上按先后发，PJ 结束一定先于 CJ 完成）。
    /// </summary>
    private void OnProcessJobStateChanged(ProcessJob job, ProcessJobState state, int e40TransitionNumber)
    {
        if (state == ProcessJobState.Aborting)
        {
            AbortDevices(job);
        }

        if (job.IsEnded)
        {
            _processJobs.Remove(job);
            Tasks.Close(job);
            Store(job, JobDtos.Of(job, ProcessJobAutoStart));
        }

        var callback = E40Callback;
        if (callback is not null)
        {
            var dto = JobDtos.Of(job, ProcessJobAutoStart);
            EapNotifierComponent.Current?.Post(() => callback.ProcessJobStateChanged(dto, e40TransitionNumber));
        }
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
    /// CJ 转了：完成或中止收尾（内部 Completed / Aborted，上报 E94 COMPLETED）告诉 LoadPort 这个载具的活干完了（转成 E87 的 CarrierComplete）；
    /// 删掉了（#2 / #13）写最后一遍库（之后就不在全貌里了）；往 EAP 报（E94）。
    /// </summary>
    private void OnControlJobStateChanged(ControlJob job, ControlJobState state, int e94TransitionNumber)
    {
        if (state is ControlJobState.Completed or ControlJobState.Aborted && !job.IsEnded)
        {
            LoadPort(job.LoadPort)?._carrier.NoteComplete();
        }

        if (job.IsEnded)
        {
            Store(job, JobDtos.Of(job, ControlJobAutoStart));
            _controlJobs.Remove(job);
        }

        var callback = E94Callback;
        if (callback is not null && e94TransitionNumber != 0)
        {
            var dto = JobDtos.Of(job, ControlJobAutoStart);
            EapNotifierComponent.Current?.Post(() => callback.ControlJobStateChanged(dto, e94TransitionNumber));
        }

    }

    #endregion

    #region 扫描

    /// <summary>
    /// 每拍固定六步：① 调度引擎收做完的（搬运、站内任务），结果记回任务表 → ② 任务组件核对片位 → ③ 料没到先建的 PJ，料到了定片 →
    /// ④ 按任务进度提交 PJ、CJ 状态动作、定许可 → ⑤ 调度引擎派新任务 → ⑥ 发布当前快照。整拍拿着命令那把锁，命令和扫描不会交叉着改。
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
            AssignWaitingProcessJobs();
            Advance();
            Dispatch(scheduler, tasks);
            Publish();
        }
    }

    /// <summary>
    /// ③ 料没到先建的 PJ（还在排队）：载具到了某个 LoadPort、能取片了（Load 好，接了 EAP 时槽图也被 Host 认定了）就定片——
    /// 任务行挂进任务表、登记片归属，归了 CJ 而 CJ 还没定口的把口填上。定不了（槽里没片、片不正常、流程配方没勾这个口……）就报警、
    /// 日志写清哪个 PJ、什么原因（同样的原因只记一次），PJ 留在排队等人或 Host 收场（停掉 CJ，或取消没归 CJ 的 PJ）；
    /// 每拍还会再试（账改对了就接着定），报警复位了还没解决会再报。
    /// </summary>
    private void AssignWaitingProcessJobs()
    {
        foreach (var job in _processJobs.ProcessJobs)
        {
            if (!job.IsWaitingForMaterial || job.State != ProcessJobState.QueuedPooled || string.IsNullOrEmpty(job.CarrierId))
            {
                continue;
            }

            var port = FindCarrierPort(job.CarrierId);
            if (port is null || !port.IsCarrierReady)
            {
                continue;
            }

            var error = AssignWafers(job, port);
            if (error is not null)
            {
                RaiseAlarm(MaterialUnusableAlarm);
                string reason = $"{error.ErrorMessage} [{string.Join(", ", error.Args)}]";
                if (reason != job.MaterialError)
                {
                    job.MaterialError = reason;
                    LogHelper.Warn(Name, $"PJ {job.Id} 等的载具 {job.CarrierId} 到了 {port.Name}，但定不了片：{reason}。PJ 留在排队，核对后取消或重新建");
                }

                continue;
            }

            job.MaterialError = null;
            Tasks.Add(job);
            _processJobs.RegisterWafers(job);
            var control = job.ControlJob;
            if (control is not null && control.LoadPort.Length == 0)
            {
                control.LoadPort = port.Name;
            }

            LogHelper.Info(Name, $"PJ {job.Id} 的料到了：{port.Name} 槽 {string.Join(",", job.Rows.Select(row => row.SourceSlot))}");
        }
    }

    /// <summary>CJ 允许启动，且前面的 PJ 已投完片或结束，才准备这个 PJ。</summary>
    private static bool MayStartProcessJob(ProcessJob job)
    {
        var control = job.ControlJob;
        if (control is null || !control.CanStartProcessJobs)
        {
            return false;
        }

        foreach (var earlier in control.ProcessJobs)
        {
            if (ReferenceEquals(earlier, job))
            {
                break;
            }

            if (!earlier.IsEnded && earlier.HasWaitingRows)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// ③ 自动转换：先 PJ 后 CJ（PJ 结束先报，CJ 完成后报），转了就再问一遍，直到不再转；最后按 PJ 状态给它的行定许可。
    /// JobManager 根据载具与任务进度提交 CJ/PJ 动作，各自的状态表决定目标状态。
    /// </summary>
    private void Advance()
    {
        bool changed;
        do
        {
            changed = AdvanceProcessJobs();
            if (AdvanceControlJobs())
            {
                changed = true;
            }
        }
        while (changed);

        UpdateTaskPermissions();
    }

    /// <summary>按任务进度提交 PJ 动作，目标状态由 PJ 状态表决定。</summary>
    private bool AdvanceProcessJobs()
    {
        bool changed = false;
        foreach (var job in _processJobs.ProcessJobs)
        {
            bool idle = !job.HasRowsInMachine && !job.HasRunning;
            switch (job.State)
            {
                case ProcessJobState.QueuedPooled:
                    if (MayStartProcessJob(job))
                    {
                        changed |= _processJobs.Setup(job).IsSuccess;
                    }

                    break;
                case ProcessJobState.SettingUp:
                    if (!job.HasErrors)
                    {
                        if (ProcessJobAutoStart)
                        {
                            changed |= _processJobs.Activate(job).IsSuccess;
                        }
                        else
                        {
                            changed |= _processJobs.WaitForStart(job).IsSuccess;
                        }
                    }

                    break;
                case ProcessJobState.Processing:
                    if (job.Rows.All(row => row.IsProcessFinished))
                    {
                        changed |= _processJobs.Complete(job).IsSuccess;
                    }

                    break;
                case ProcessJobState.ProcessComplete:
                    if (!job.HasRunning && job.Rows.All(row => row.IsReturned))
                    {
                        changed |= _processJobs.Finish(job).IsSuccess;
                    }

                    break;
                case ProcessJobState.Pausing:
                    if (idle)
                    {
                        changed |= _processJobs.FinishPause(job).IsSuccess;
                    }

                    break;
                case ProcessJobState.Stopping:
                    if (idle)
                    {
                        changed |= _processJobs.FinishStop(job).IsSuccess;
                    }

                    break;
                case ProcessJobState.Aborting:
                    if (!job.HasRunning && job.DeviceAborts.All(abort => abort.IsSettled) && !job.HasErrors
                        && TransferManager.Current?.HeldOperations.Any(operation =>
                            string.Equals(operation.Owner, job.Id, StringComparison.Ordinal)) != true)
                    {
                        changed |= _processJobs.FinishAbort(job).IsSuccess;
                    }

                    break;
            }
        }

        return changed;
    }

    /// <summary>
    /// 按载具状态及下属 PJ 的完成情况提交 CJ 动作。料到了（E94 #5 / #6）= 载具能取片、下面没结束的 PJ 都定了片；
    /// 料没到的 CJ 还没定口（LoadPort 为空），找不到口就一直等在选中。
    /// </summary>
    private bool AdvanceControlJobs()
    {
        bool changed = false;
        foreach (var job in _controlJobs.ControlJobs)
        {
            var port = LoadPort(job.LoadPort);
            if (job.State == ControlJobState.Queued)
            {
                changed |= _controlJobs.Select(job).IsSuccess;
            }

            if (job.State == ControlJobState.Selected && job.Ending == ControlJobEnding.None
                && port?.IsCarrierReady == true
                && job.ProcessJobs.All(process => process.IsEnded || !process.IsWaitingForMaterial))
            {
                if (ControlJobAutoStart)
                {
                    changed |= _controlJobs.Activate(job).IsSuccess;
                }
                else
                {
                    changed |= _controlJobs.WaitForStart(job).IsSuccess;
                }
            }

            if ((job.ProcessJobs.Count > 0 || job.Ending != ControlJobEnding.None)
                && job.ProcessJobs.All(process => process.IsEnded))
            {
                if (job.State == ControlJobState.Aborting)
                {
                    changed |= _controlJobs.FinishAbort(job).IsSuccess;
                }
                else if (job.IsActive && job.Ending == ControlJobEnding.Stop)
                {
                    changed |= _controlJobs.FinishStop(job).IsSuccess;
                }
                else if (job.State is ControlJobState.Executing or ControlJobState.Paused)
                {
                    changed |= _controlJobs.Complete(job).IsSuccess;
                }
            }

            if (job.State is ControlJobState.Completed or ControlJobState.Aborted
                && port?._carrier.IsArrived != true)
            {
                changed |= _controlJobs.Delete(job).IsSuccess;
            }
        }

        return changed;
    }

    /// <summary>根据 PJ 状态设置任务行的投片和机内推进许可。</summary>
    private void UpdateTaskPermissions()
    {
        foreach (var job in _processJobs.ProcessJobs)
        {
            TaskPermission permission;
            switch (job.State)
            {
                case ProcessJobState.Processing:
                    permission = TaskPermission.Advance | TaskPermission.Feed;
                    break;
                case ProcessJobState.Pausing:
                case ProcessJobState.Stopping:
                case ProcessJobState.ProcessComplete:
                    permission = TaskPermission.Advance;
                    break;
                default:
                    permission = TaskPermission.None;
                    break;
            }

            foreach (var row in job.Rows)
            {
                row.Permission = permission;
            }
        }
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

        var rows = _controlJobs.ControlJobs
            .SelectMany(controlJob => controlJob.ProcessJobs)
            .Where(process => !process.IsEnded)
            .SelectMany(process => process.Rows)
            .ToList();
        scheduler.Dispatch(rows, tasks);
    }

    /// <summary>
    /// ⑤ 发布当前快照：本次生成没删的 CJ、界面要看的 PJ（没结束的，加上没删的 CJ 下面已经结束的）对应的 DTO；
    /// 推给界面（留存，重连就能拿到最新的）；里面的每个 CJ、PJ 交给写库线程更新库里那一行（任务进度也跟着进库）。
    /// </summary>
    private void Publish()
    {
        var controlJobs = _controlJobs.ControlJobs;
        var processJobs = controlJobs.SelectMany(control => control.ProcessJobs)
            .Concat(_processJobs.ProcessJobs).Distinct().ToList();

        var snapshot = new JobListDto
        {
            ControlJobs = controlJobs.Select(job => JobDtos.Of(job, ControlJobAutoStart)).ToList(),
            ProcessJobs = processJobs.Select(job => JobDtos.Of(job, ProcessJobAutoStart)).ToList(),
        };
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
    // 写库不在扫描线程上做——磁盘一卡，扫描就卡。写不进去只记日志，Job 照跑，下一次发布再写。

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
                if (row.State != 5) // 历史库保留 E94 COMPLETED = 5。
                {
                    row.State = 5;
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
                    State = dto.E94State ?? dto.State,
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
                    LotId = dto.LotId,
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
    private BaseTaskComponent Tasks
    {
        get
        {
            if (_tasks is null)
            {
                throw new InvalidOperationException($"{Name}：还没装配，没有任务组件");
            }

            return _tasks;
        }
    }

    /// <summary>Auto 模式（自动派单开着）：Manual 下不派新动作，启动 Job 也要 Auto。</summary>
    private static bool IsAuto
    {
        get { return TransferManager.Current?.IsAutoDispatch == true; }
    }

    /// <summary>装了的 LoadPort（按 sc.xml 先后）。</summary>
    private IEnumerable<BaseLoadPortModule> LoadPorts
    {
        get { return _modules.Values.OfType<BaseLoadPortModule>(); }
    }

    private BaseModule? Module(string name)
    {
        if (_modules.TryGetValue(name.Trim(), out var module))
        {
            return module;
        }

        return null;
    }

    private BaseLoadPortModule? LoadPort(string name)
    {
        return Module(name) as BaseLoadPortModule;
    }

    /// <summary>这个腔体正在给 Job 做工艺（整机停止时由 Job 的中止去停它，不直接发中止）。</summary>
    public static bool IsJobProcess(BaseModule module)
    {
        return module is IProcessStation process && process.CurrentProcess?.Origin == ProcessOrigin.Job;
    }

    #endregion
}
