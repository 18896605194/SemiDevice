using xyz.Common.Log;
using xyz.Components.Attributes;
using xyz.Components.Enums;
using xyz.Components.Interfaces;
using xyz.Components.Models;
using xyz.Configs.Models;
using xyz.Drivers.Loadport;
using xyz.Secs;
using xyz.Secs.Hsms;
using xyz.Secs.SecsII;
using xyz.Shared.Dtos;

namespace xyz.Components.Components;

/// <summary>
/// PJ 管理（SEMI E40，sc.xml 的 Eap 下的 E40 节点）：Host 的 S16 翻成 Job 管理的命令
/// </summary>
[Component(description: "PJ 管理（SEMI E40）：S16 建 PJ、PJ 命令、查 PJ，PJ 状态转换报事件")]
public class E40Component : ComponentBase, IE40Callback
{
    /// <summary>PrMtlType：载具加槽号。</summary>
    private const byte MaterialCarriers = 0x0D;

    /// <summary>PrRecipeMethod：只给配方名（不带参数）。</summary>
    private const byte RecipeOnly = 1;

    /// <summary>GRANT：可以发。</summary>
    private const byte Granted = 0;

    #region SC

    [SCEditor("START", "E40", "S16F5 的 PJ 启动命令字符串（忽略大小写及首尾空格；不能重复或占用其他命令的默认名称）")]
    public string StartCommandName { get; set; } = "START";

    [SCEditor("PAUSE", "E40", "S16F5 的 PJ 暂停命令字符串")]
    public string PauseCommandName { get; set; } = "PAUSE";

    [SCEditor("RESUME", "E40", "S16F5 的 PJ 恢复命令字符串")]
    public string ResumeCommandName { get; set; } = "RESUME";

    [SCEditor("STOP", "E40", "S16F5 的 PJ 停止命令字符串")]
    public string StopCommandName { get; set; } = "STOP";

    [SCEditor("ABORT", "E40", "S16F5 的 PJ 中止命令字符串")]
    public string AbortCommandName { get; set; } = "ABORT";

    [SCEditor("CANCEL", "E40", "S16F5 的 PJ 取消命令字符串")]
    public string CancelCommandName { get; set; } = "CANCEL";

    #endregion

    /// <summary>
    /// 装配校验
    /// </summary>
    /// <param name="setting"></param>
    /// <exception cref="InvalidOperationException"></exception>
    protected internal override void OnSettingLoaded(ModuleConfig setting)
    {
        base.OnSettingLoaded(setting);

        (string Name, ProcessJobCommand Command)[] commands =
        [
            (StartCommandName, ProcessJobCommand.Start),
            (PauseCommandName, ProcessJobCommand.Pause),
            (ResumeCommandName, ProcessJobCommand.Resume),
            (StopCommandName, ProcessJobCommand.Stop),
            (AbortCommandName, ProcessJobCommand.Abort),
            (CancelCommandName, ProcessJobCommand.Cancel),
        ];

        foreach (var entry in commands)
        {
            string name = entry.Name?.Trim() ?? string.Empty;
            if (name.Length == 0)
            {
                throw new InvalidOperationException($"{Name}：{entry.Command} 的命令字符串不能为空");
            }

            foreach (var standard in StandardCommands)
            {
                if (string.Equals(name, standard.Name, StringComparison.OrdinalIgnoreCase)
                    && standard.Command != entry.Command)
                {
                    throw new InvalidOperationException(
                        $"{Name}：{entry.Command} 的命令字符串 {name} 占用了 {standard.Command} 的默认名称 {standard.Name}");
                }
            }
        }

        for (int first = 0; first < commands.Length; first++)
        {
            for (int second = first + 1; second < commands.Length; second++)
            {
                string firstName = commands[first].Name?.Trim() ?? string.Empty;
                string secondName = commands[second].Name?.Trim() ?? string.Empty;
                if (string.Equals(firstName, secondName, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"{Name}：{commands[first].Command} 和 {commands[second].Command} 的命令字符串都是 {firstName}");
                }
            }
        }
    }

    /// <summary>
    /// 默认命令名：用于兜底，并检查 SC 是否占用了其他命令的名称
    /// </summary>
    private static readonly (string Name, ProcessJobCommand Command)[] StandardCommands =
    [
        ("START", ProcessJobCommand.Start),
        ("STARTPROCESS", ProcessJobCommand.Start),
        ("PAUSE", ProcessJobCommand.Pause),
        ("RESUME", ProcessJobCommand.Resume),
        ("STOP", ProcessJobCommand.Stop),
        ("ABORT", ProcessJobCommand.Abort),
        ("CANCEL", ProcessJobCommand.Cancel),
    ];

    #region DV、事件

    private const string DvJobId = "PRJobID";
    private const string DvJobState = "PRJobState";
    private const string DvRecipe = "RecID";
    private const string DvControlJob = "CtrlJobID";
    private const string DvMaterial = "PRMtlNameList";

    [DataVariable(ValueFormat.String, "PJ 号")]
    public readonly string JobIdData = DvJobId;

    [DataVariable(ValueFormat.Int, "PJ 状态：0 排队、1 准备、2 等启动、3 在做、4 做完、6 暂停中、7 暂停、8 停止中、9 中止中、10 停止、11 中止")]
    public readonly string JobStateData = DvJobState;

    [DataVariable(ValueFormat.String, "PJ 的配方（流程配方名）")]
    public readonly string RecipeData = DvRecipe;

    [DataVariable(ValueFormat.String, "PJ 归哪个 CJ（还不归任何 CJ 为空）")]
    public readonly string ControlJobData = DvControlJob;

    [DataVariable(ValueFormat.String, "PJ 的料：L{L[2]{载具号, L{槽号}}}")]
    public readonly string MaterialData = DvMaterial;

    [EventAttribut("PJ 建了（#1）", Data = new[] { DvJobId, DvJobState, DvRecipe, DvMaterial })]
    public readonly string PrJobTrans01 = "PrJobSMTrans01";

    [EventAttribut("PJ 开始准备（#2）", Data = new[] { DvJobId, DvJobState, DvControlJob })]
    public readonly string PrJobTrans02 = "PrJobSMTrans02";

    [EventAttribut("PJ 等启动（#3）", Data = new[] { DvJobId, DvJobState, DvControlJob })]
    public readonly string PrJobTrans03 = "PrJobSMTrans03";

    [EventAttribut("PJ 准备好直接开始（#4）", Data = new[] { DvJobId, DvJobState, DvControlJob })]
    public readonly string PrJobTrans04 = "PrJobSMTrans04";

    [EventAttribut("PJ 启动（#5）", Data = new[] { DvJobId, DvJobState, DvControlJob })]
    public readonly string PrJobTrans05 = "PrJobSMTrans05";

    [EventAttribut("PJ 片都做完了（#6）", Data = new[] { DvJobId, DvJobState, DvControlJob })]
    public readonly string PrJobTrans06 = "PrJobSMTrans06";

    [EventAttribut("PJ 正常结束（#7）", Data = new[] { DvJobId, DvJobState, DvControlJob })]
    public readonly string PrJobTrans07 = "PrJobSMTrans07";

    [EventAttribut("PJ 暂停中（#8）", Data = new[] { DvJobId, DvJobState, DvControlJob })]
    public readonly string PrJobTrans08 = "PrJobSMTrans08";

    [EventAttribut("PJ 暂停了（#9）", Data = new[] { DvJobId, DvJobState, DvControlJob })]
    public readonly string PrJobTrans09 = "PrJobSMTrans09";

    [EventAttribut("PJ 恢复（#10）", Data = new[] { DvJobId, DvJobState, DvControlJob })]
    public readonly string PrJobTrans10 = "PrJobSMTrans10";

    [EventAttribut("PJ 停止中（#11，从执行中）", Data = new[] { DvJobId, DvJobState, DvControlJob })]
    public readonly string PrJobTrans11 = "PrJobSMTrans11";

    [EventAttribut("PJ 停止中（#12，从暂停）", Data = new[] { DvJobId, DvJobState, DvControlJob })]
    public readonly string PrJobTrans12 = "PrJobSMTrans12";

    [EventAttribut("PJ 中止中（#13，从执行中）", Data = new[] { DvJobId, DvJobState, DvControlJob })]
    public readonly string PrJobTrans13 = "PrJobSMTrans13";

    [EventAttribut("PJ 中止中（#14，从停止中）", Data = new[] { DvJobId, DvJobState, DvControlJob })]
    public readonly string PrJobTrans14 = "PrJobSMTrans14";

    [EventAttribut("PJ 中止中（#15，从暂停）", Data = new[] { DvJobId, DvJobState, DvControlJob })]
    public readonly string PrJobTrans15 = "PrJobSMTrans15";

    [EventAttribut("PJ 中止完结束（#16）", Data = new[] { DvJobId, DvJobState, DvControlJob })]
    public readonly string PrJobTrans16 = "PrJobSMTrans16";

    [EventAttribut("PJ 停止完结束（#17）", Data = new[] { DvJobId, DvJobState, DvControlJob })]
    public readonly string PrJobTrans17 = "PrJobSMTrans17";

    [EventAttribut("排队的 PJ 删了（#18）", Data = new[] { DvJobId, DvJobState })]
    public readonly string PrJobTrans18 = "PrJobSMTrans18";

    #endregion

    #region 组件+服务

    private E30Component? _gem;
    private IJobManager? _jobs;
    private IReadOnlyList<ILoadPort> _ports = [];

    #endregion

    #region 接设备

    /// <summary>
    /// 挂载eap
    /// </summary>
    /// <param name="link"></param>
    /// <param name="gem"></param>
    /// <param name="objects"></param>
    /// <param name="jobs"></param>
    /// <param name="ports"></param>
    public void Attach(HsmsComponent link, E30Component gem, E39Component? objects, IJobManager jobs, IReadOnlyList<ILoadPort> ports)
    {
        ArgumentNullException.ThrowIfNull(link);
        ArgumentNullException.ThrowIfNull(gem);
        ArgumentNullException.ThrowIfNull(jobs);
        _gem = gem;
        _jobs = jobs;
        _ports = ports;
        jobs.E40Callback = this;
        link.Handle(16, 1, Inquire);
        link.Handle(16, 5, CommandAsync);
        link.Handle(16, 11, CreateAsync);
        link.Handle(16, 15, MultiCreateAsync);
        link.Handle(16, 17, DequeueAsync);
        link.Handle(16, 19, ListJobs);
        link.Handle(16, 21, JobSpace);
        objects?.Register(new ProcessJobType(this));
    }

    /// <summary>
    /// 宿主退出时
    /// </summary>
    public void Detach()
    {
        var jobs = _jobs;
        if (jobs is not null && ReferenceEquals(jobs.E40Callback, this))
        {
            jobs.E40Callback = null;
        }
    }

    #endregion

    #region 上报（IE40Callback，在 EAP 的上报派发线程上）

    public void ProcessJobStateChanged(ProcessJobDto job, int transition)
    {
        if (transition is < 1 or > 18)
        {
            return;
        }

        _gem?.Report(this, $"PrJobSMTrans{transition:00}",
            new GemData(DvJobId, GemValue.Ascii(job.Id)),
            new GemData(DvJobState, (byte)job.State),
            new GemData(DvRecipe, GemValue.Ascii(job.Sequence)),
            new GemData(DvControlJob, GemValue.Ascii(job.ControlJob)),
            new GemData(DvMaterial, MaterialOf(job)));
    }

    #endregion

    #region S16 报文

    /// <summary>
    /// S16F1 多块询问 → S16F2 GRANT=0。
    /// </summary>
    /// <param name="message"></param>
    /// <returns></returns>
    private SecsReply Inquire(HsmsMessage message)
    {
        return SecsReply.Of(SecsItem.B(Granted));
    }

    /// <summary>
    /// S16F11 建一个 PJ → S16F12 L[2]{PRJOBID, L[2]{ACKA, 错误表}}：
    /// L[7]{DATAID, PRJOBID, MF, PRMTLNAMELIST, L[3]{PRRECIPEMETHOD, RCPSPEC, L{参数}}, PRPROCESSSTART, PRPAUSEEVENT}。
    /// </summary>
    private async Task<SecsReply> CreateAsync(HsmsMessage message)
    {
        var body = SecsRead.List(SecsRead.Body(message), "S16F11", 7);
        string id = SecsRead.Text(body[1], "PRJOBID").Trim();
        var errors = await CreateOneAsync(id, body[3], body[4], body[5], body[6]).ConfigureAwait(false);
        return SecsReply.Of(SecsItem.L(SecsItem.A(GemValue.Ascii(id)), Ack(errors)));
    }

    /// <summary>
    /// S16F15 一次建多个 PJ → S16F16 L[2]{L{建成的 PRJOBID}, L[2]{ACKA, 错误表}}：L[2]{DATAID, L{L[6]{PRJOBID, MF, 料, 配方, 自动开始, 暂停事件}}}。
    /// 一个一个建，建不成的记错误，建成的照留。
    /// </summary>
    private async Task<SecsReply> MultiCreateAsync(HsmsMessage message)
    {
        var body = SecsRead.List(SecsRead.Body(message), "S16F15", 2);
        var created = new List<SecsItem>();
        var errors = new List<E5Error>();
        foreach (var item in SecsRead.List(body[1], "PJ 表"))
        {
            var spec = SecsRead.List(item, "PJ", 6);
            string id = SecsRead.Text(spec[0], "PRJOBID").Trim();
            var failed = await CreateOneAsync(id, spec[2], spec[3], spec[4], spec[5]).ConfigureAwait(false);
            if (failed.Count == 0)
            {
                created.Add(SecsItem.A(GemValue.Ascii(id)));
            }

            errors.AddRange(failed);
        }

        return SecsReply.Of(SecsItem.L(SecsItem.L(created), Ack(errors)));
    }

    /// <summary>建一个 PJ：查控制状态、料、配方，交给 Job 管理建（它再查一遍片和配方）。返回错误（空 = 建成了）。</summary>
    private async Task<List<E5Error>> CreateOneAsync(string id, SecsItem material, SecsItem recipe, SecsItem autoStart, SecsItem pauseEvents)
    {
        var jobs = _jobs;
        if (_gem is null || !_gem.IsRemote)
        {
            return [E5Error.NotRemote()];
        }

        if (jobs is null)
        {
            return [E5Error.Of(E5Error.NotAvailable, "Job manager not installed")];
        }

        var recipeParts = SecsRead.List(recipe, "配方", 3);
        string sequence = SecsRead.Text(recipeParts[1], "RCPSPEC").Trim();
        if (recipeParts[2].Count > 0)
        {
            return [E5Error.Of(E5Error.RecipeError, "Recipe variables not supported")];
        }

        if (SecsRead.List(pauseEvents, "PRPAUSEEVENT").Count > 0)
        {
            return [E5Error.Of(E5Error.UnsupportedOption, "PRPAUSEEVENT not supported")];
        }

        var carriers = SecsRead.List(material, "PRMTLNAMELIST");
        if (carriers.Count != 1 || carriers[0].Format != SecsFormat.List)
        {
            return [E5Error.Of(E5Error.UnsupportedOption, "Material must be exactly one carrier with slots")];
        }

        var entry = SecsRead.List(carriers[0], "L{CARRIERID, L{SLOTID}}", 2);
        string carrierId = SecsRead.Text(entry[0], "CARRIERID").Trim();
        var slots = SecsRead.List(entry[1], "SLOTID 表").Select(slot => (int)SecsRead.Code(slot, "SLOTID")).ToList();
        if (slots.Count == 0)
        {
            slots = OccupiedSlots(carrierId);
            if (slots.Count == 0)
            {
                return [E5Error.Of(E5Error.LackOfMaterial, $"Carrier {carrierId} not at a port or has no wafer")];
            }
        }

        // 保留报文格式校验；PJ 启动方式统一由设备的 SC 配置决定。
        SecsRead.Flag(autoStart, "PRPROCESSSTART");
        var result = await jobs.CreateProcessJobAsync(
            loadPort: null,
            pjName: id,
            slots: slots,
            sequence: sequence,
            lotId: null,
            carrierId: carrierId).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return [JobErrors.Of(result)];
        }

        LogHelper.Info(Name, $"Host 建了 PJ {id}：{carrierId} 槽 {string.Join(",", slots)}，流程配方 {sequence}");
        return [];
    }

    /// <summary>这个载具（在端口上的）里正常有片的槽（槽图里正常和有片说不准的）。</summary>
    private List<int> OccupiedSlots(string carrierId)
    {
        var port = _ports.FirstOrDefault(item => item.IsCarrierArrived && string.Equals(item.CarrierId, carrierId, StringComparison.OrdinalIgnoreCase));
        if (port is null)
        {
            return [];
        }

        var slots = new List<int>();
        for (int index = 0; index < port.SlotMap.Count; index++)
        {
            if (port.SlotMap[index] is SlotState.CorrectlyOccupied or SlotState.NotEmpty)
            {
                slots.Add(index + 1);
            }
        }

        return slots;
    }

    /// <summary>
    /// S16F5 PJ 命令 → S16F6 L[2]{PRJOBID, L[2]{ACKA, 错误表}}：L[4]{DATAID, PRJOBID, PRCMDNAME, L{参数}}。
    /// PRCMDNAME 只收命令字符串：优先识别 SC 名称，默认 START（也认 STARTPROCESS）/ PAUSE / RESUME / STOP / ABORT / CANCEL 兜底。
    /// </summary>
    private async Task<SecsReply> CommandAsync(HsmsMessage message)
    {
        var body = SecsRead.List(SecsRead.Body(message), "S16F5", 4);
        string id = SecsRead.Text(body[1], "PRJOBID").Trim();
        string name = SecsRead.Text(body[2], "PRCMDNAME").Trim();
        var command = ParseCommand(name);

        List<E5Error> errors;
        if (command is null)
        {
            errors = [E5Error.Of(E5Error.ParametersImproperlySpecified, $"PRCMDNAME {name} unknown")];
        }
        else
        {
            errors = await RunCommandAsync(id, command.Value).ConfigureAwait(false);
        }

        return SecsReply.Of(SecsItem.L(SecsItem.A(GemValue.Ascii(id)), Ack(errors)));
    }

    /// <summary>解析 PJ 命令名；运行期间 SC 被修改后，仍拒绝重复名称或与默认名称含义冲突的命令。</summary>
    private ProcessJobCommand? ParseCommand(string name)
    {
        name = name.Trim();
        if (name.Length == 0)
        {
            return null;
        }

        (string Name, ProcessJobCommand Command)[] commands =
        [
            (StartCommandName, ProcessJobCommand.Start),
            (PauseCommandName, ProcessJobCommand.Pause),
            (ResumeCommandName, ProcessJobCommand.Resume),
            (StopCommandName, ProcessJobCommand.Stop),
            (AbortCommandName, ProcessJobCommand.Abort),
            (CancelCommandName, ProcessJobCommand.Cancel),
        ];
        ProcessJobCommand? matched = null;
        foreach (var entry in commands)
        {
            if (!string.Equals(name, entry.Name?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (matched is not null)
            {
                return null;
            }

            matched = entry.Command;
        }

        foreach (var entry in StandardCommands)
        {
            if (string.Equals(name, entry.Name, StringComparison.OrdinalIgnoreCase))
            {
                if (matched is not null && matched != entry.Command)
                {
                    LogHelper.Warn(Name, $"PJ 命令字符串配置冲突：{name} 在 SC 中表示 {matched}，默认名称表示 {entry.Command}，拒绝执行");
                    return null;
                }

                return entry.Command;
            }
        }

        return matched;
    }

    private async Task<List<E5Error>> RunCommandAsync(string id, ProcessJobCommand command)
    {
        var jobs = _jobs;
        if (_gem is null || !_gem.IsRemote)
        {
            return [E5Error.NotRemote()];
        }

        if (jobs is null)
        {
            return [E5Error.Of(E5Error.NotAvailable, "Job manager not installed")];
        }

        var result = await jobs.ExecuteProcessJobCommandAsync(id, command).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return [JobErrors.Of(result)];
        }

        LogHelper.Info(Name, $"Host PJ 命令 {command}：{id}");
        return [];
    }

    /// <summary>
    /// S16F17 撤掉排队的 PJ → S16F18 L[2]{L{撤掉的 PRJOBID}, L[2]{ACKA, 错误表}}：L{PRJOBID}，空 = 全部排队、还不归 CJ 的。
    /// </summary>
    private async Task<SecsReply> DequeueAsync(HsmsMessage message)
    {
        var requested = SecsRead.List(SecsRead.Body(message), "PRJOBID 表").Select(item => SecsRead.Text(item, "PRJOBID").Trim()).ToList();
        if (requested.Count == 0)
        {
            requested = (_jobs?.Snapshot.ProcessJobs ?? [])
                .Where(job => job.State == (int)ProcessJobState.QueuedPooled && job.ControlJob.Length == 0)
                .Select(job => job.Id).ToList();
        }

        var removed = new List<SecsItem>();
        var errors = new List<E5Error>();
        foreach (string id in requested)
        {
            var failed = await RunCommandAsync(id, ProcessJobCommand.Cancel).ConfigureAwait(false);
            if (failed.Count == 0)
            {
                removed.Add(SecsItem.A(GemValue.Ascii(id)));
            }

            errors.AddRange(failed);
        }

        return SecsReply.Of(SecsItem.L(SecsItem.L(removed), Ack(errors)));
    }

    /// <summary>S16F19 列 PJ → S16F20 L{L[2]{PRJOBID, PRSTATE}}：没结束、不在"做完"的。</summary>
    private SecsReply ListJobs(HsmsMessage message)
    {
        var jobs = _jobs?.Snapshot.ProcessJobs ?? [];
        return SecsReply.Of(SecsItem.L(jobs.Where(job => job.State != (int)ProcessJobState.ProcessComplete)
            .Select(job => SecsItem.L(SecsItem.A(GemValue.Ascii(job.Id)), SecsItem.U1((byte)job.State)))));
    }

    /// <summary>
    /// S16F21 还能建几个 PJ → S16F22 U2。Job 管理不限 PJ 个数（一片只能归一个 PJ，个数自然有数），答 U2 最大值；没接 Job 管理答 0。
    /// </summary>
    private SecsReply JobSpace(HsmsMessage message)
    {
        return SecsReply.Of(SecsItem.U2(_jobs is null ? (ushort)0 : ushort.MaxValue));
    }

    /// <summary>L[2]{ACKA, 错误表}：没错 ACKA = TRUE。</summary>
    private static SecsItem Ack(IReadOnlyList<E5Error> errors)
    {
        return SecsItem.L(SecsItem.Boolean(errors.Count == 0), E5Error.List(errors));
    }

    #endregion

    #region 料、E39 对象

    /// <summary>PJ 的料 L{L[2]{载具号, L{槽号}}}：一个 PJ 的片都在一个载具上，载具号用 PJ 建的时候记下的。</summary>
    private static SecsItem MaterialOf(ProcessJobDto job)
    {
        if (job.Wafers.Count == 0)
        {
            return SecsItem.L();
        }

        return SecsItem.L(SecsItem.L(SecsItem.A(GemValue.Ascii(job.CarrierId)),
            SecsItem.L(job.Wafers.Select(wafer => SecsItem.U1((byte)Math.Clamp(wafer.SourceSlot, 0, byte.MaxValue))))));
    }

    private IReadOnlyList<string> JobIds()
    {
        return (_jobs?.Snapshot.ProcessJobs ?? []).Select(job => job.Id).ToList();
    }

    private bool TryGetJob(string id, string attribute, out SecsItem value)
    {
        value = SecsItem.L();
        var job = (_jobs?.Snapshot.ProcessJobs ?? []).FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
        if (job is null)
        {
            return false;
        }

        value = attribute switch
        {
            ProcessJobNames.ObjType => SecsItem.A(ProcessJobNames.ObjectType),
            ProcessJobNames.ObjId => SecsItem.A(GemValue.Ascii(job.Id)),
            ProcessJobNames.PauseEvent => SecsItem.L(),
            ProcessJobNames.ProcessJobState => SecsItem.U1((byte)job.State),
            ProcessJobNames.PrMtlNameList => MaterialOf(job),
            ProcessJobNames.PrMtlType => SecsItem.B(MaterialCarriers),
            ProcessJobNames.PrProcessStart => SecsItem.Boolean(job.AutoStart),
            ProcessJobNames.PrRecipeMethod => SecsItem.U1(RecipeOnly),
            ProcessJobNames.RecId => SecsItem.A(GemValue.Ascii(job.Sequence)),
            ProcessJobNames.RecVariableList => SecsItem.L(),
            _ => SecsItem.L(),
        };
        return true;
    }

    /// <summary>E39 对象类型 ProcessJob。</summary>
    private sealed class ProcessJobType : IE39ObjectType
    {
        private readonly E40Component _owner;

        public ProcessJobType(E40Component owner)
        {
            _owner = owner;
        }

        public string TypeName => ProcessJobNames.ObjectType;

        public IReadOnlyList<string> AttributeNames => ProcessJobNames.Attributes;

        public IReadOnlyList<string> ObjectIds()
        {
            return _owner.JobIds();
        }

        public bool TryGetAttribute(string objectId, string attribute, out SecsItem value)
        {
            return _owner.TryGetJob(objectId, attribute, out value);
        }
    }

    #endregion
}

/// <summary>E39/E40 的 PJ 对象类型及属性名，统一保留协议中的名称写法。</summary>
public static class ProcessJobNames
{
    public const string ObjectType = "ProcessJob";
    public const string ObjType = "ObjType";
    public const string ObjId = "ObjID";
    public const string PauseEvent = "PauseEvent";
    public const string ProcessJobState = "ProcessJobState";
    public const string PrMtlNameList = "PrMtlNameList";
    public const string PrMtlType = "PrMtlType";
    public const string PrProcessStart = "PrProcessStart";
    public const string PrRecipeMethod = "PrRecipeMethod";
    public const string RecId = "RecID";
    public const string RecVariableList = "RecVariableList";

    // 保持原有顺序，Host 使用数字 ATTRID 时按此顺序定位属性。
    public static IReadOnlyList<string> Attributes { get; } =
    [
        ObjType, ObjId, PauseEvent, ProcessJobState, PrMtlNameList, PrMtlType, PrProcessStart, PrRecipeMethod, RecId,
        RecVariableList,
    ];
}
