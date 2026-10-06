using xyz.Common.Log;
using xyz.Components.Attributes;
using xyz.Components.Enums;
using xyz.Components.Interfaces;
using xyz.Components.Models;
using xyz.Drivers.Loadport;
using xyz.Secs;
using xyz.Secs.Hsms;
using xyz.Secs.SecsII;
using xyz.Shared.Dtos;

namespace xyz.Components.Components;

/// <summary>
/// PJ 管理（SEMI E40，sc.xml 的 Eap 下的 E40 节点）：Host 的 S16 翻成 Job 管理的命令（IJobManager，跟本地界面同一套检查），
/// PJ 的状态转换（Job 管理经 IE40Callback 报过来）翻成事件。PJ 的状态只在 Job 管理里有一份。
/// ① S16F11 / S16F15 建 PJ：料要写成一个载具加槽号（槽表空 = 这个载具上正常的片都做），载具要已经在端口上；
///    配方（RCPSPEC）就是流程配方名，不支持配方参数和暂停事件；
/// ② S16F5 PJ 命令（Start / Pause / Resume / Stop / Abort / Cancel）、S16F17 撤掉排队的 PJ；
/// ③ S16F19 列 PJ、S16F21 还能建几个、S16F1 多块询问；E39 对象 ProcessJob。
/// 建、命令要 ON-LINE REMOTE；查在 ON-LINE 就行。
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

    private E30Component? _gem;
    private IJobManager? _jobs;
    private IReadOnlyList<ILoadPort> _ports = [];

    #region 接设备

    /// <summary>接到链路和 Job 管理上（EAP 组件在链路打开之前调）：挂 E40 上报口，登记 S16 的 PJ 报文和 E39 对象类型 ProcessJob。</summary>
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

    /// <summary>从 Job 管理上摘下来（宿主退出时）。</summary>
    public void Detach()
    {
        var jobs = _jobs;
        if (jobs is not null && ReferenceEquals(jobs.E40Callback, this))
        {
            jobs.E40Callback = null;
        }
    }

    #endregion

    #region 上报（IE40Callback，在 Job 管理的 EAP 派发线程上）

    void IE40Callback.ProcessJobTransitioned(ProcessJobDto job, int transition, PrJobState? from, PrJobState? to)
    {
        if (transition is < 1 or > 18)
        {
            return;
        }

        _gem?.Report(this, $"PrJobSMTrans{transition:00}",
            new GemData(DvJobId, GemValue.Ascii(job.Id)),
            new GemData(DvJobState, (byte)(to ?? (PrJobState)job.State)),
            new GemData(DvRecipe, GemValue.Ascii(job.Sequence)),
            new GemData(DvControlJob, GemValue.Ascii(job.ControlJob)),
            new GemData(DvMaterial, MaterialOf(job)));
    }

    void IE40Callback.WaferProcessStarted(ProcessJobDto job, JobWaferDto wafer, string station)
    {
        // 片开始、结束加工由 E90 按晶圆账报（片的工艺状态），E40 没有单独的事件
    }

    void IE40Callback.WaferProcessEnded(ProcessJobDto job, JobWaferDto wafer, string station, bool success)
    {
    }

    #endregion

    #region S16 报文

    /// <summary>S16F1 多块询问 → S16F2 GRANT=0。</summary>
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

        var result = await jobs.CreateProcessJobAsync(new ProcessJobSpec
        {
            Id = id,
            CarrierId = carrierId,
            Slots = slots,
            Sequence = sequence,
            AutoStart = SecsRead.Flag(autoStart, "PRPROCESSSTART"),
        }, JobCommandSource.Host).ConfigureAwait(false);
        if (!result.Accepted)
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
    /// PRCMDNAME：START（也认 STARTPROCESS）/ PAUSE / RESUME / STOP / ABORT / CANCEL，不分大小写。
    /// </summary>
    private async Task<SecsReply> CommandAsync(HsmsMessage message)
    {
        var body = SecsRead.List(SecsRead.Body(message), "S16F5", 4);
        string id = SecsRead.Text(body[1], "PRJOBID").Trim();
        string name = SecsRead.Text(body[2], "PRCMDNAME").Trim().ToUpperInvariant();
        PrJobCommand? command = name switch
        {
            "START" or "STARTPROCESS" => PrJobCommand.Start,
            "PAUSE" => PrJobCommand.Pause,
            "RESUME" => PrJobCommand.Resume,
            "STOP" => PrJobCommand.Stop,
            "ABORT" => PrJobCommand.Abort,
            "CANCEL" => PrJobCommand.Cancel,
            _ => null,
        };

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

    private async Task<List<E5Error>> RunCommandAsync(string id, PrJobCommand command)
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

        var result = await jobs.CommandProcessJobAsync(id, command, JobCommandSource.Host).ConfigureAwait(false);
        if (!result.Accepted)
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
                .Where(job => job.State == (int)PrJobState.QueuedPooled && job.ControlJob.Length == 0)
                .Select(job => job.Id).ToList();
        }

        var removed = new List<SecsItem>();
        var errors = new List<E5Error>();
        foreach (string id in requested)
        {
            var failed = await RunCommandAsync(id, PrJobCommand.Cancel).ConfigureAwait(false);
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
        return SecsReply.Of(SecsItem.L(jobs.Where(job => job.State != (int)PrJobState.ProcessComplete)
            .Select(job => SecsItem.L(SecsItem.A(GemValue.Ascii(job.Id)), SecsItem.U1((byte)job.State)))));
    }

    /// <summary>S16F21 还能建几个 PJ → S16F22 U2。</summary>
    private SecsReply JobSpace(HsmsMessage message)
    {
        int space = _jobs?.ProcessJobSpace ?? 0;
        return SecsReply.Of(SecsItem.U2((ushort)Math.Clamp(space, 0, ushort.MaxValue)));
    }

    /// <summary>L[2]{ACKA, 错误表}：没错 ACKA = TRUE。</summary>
    private static SecsItem Ack(IReadOnlyList<E5Error> errors)
    {
        return SecsItem.L(SecsItem.Boolean(errors.Count == 0), E5Error.List(errors));
    }

    #endregion

    #region 料、E39 对象

    /// <summary>PJ 的料 L{L[2]{载具号, L{槽号}}}：按片的来源端口分组，载具号取那个端口现在的载具。</summary>
    private SecsItem MaterialOf(ProcessJobDto job)
    {
        return SecsItem.L(job.Wafers.GroupBy(wafer => wafer.SourcePort, StringComparer.OrdinalIgnoreCase).Select(group =>
        {
            string carrierId = _ports.FirstOrDefault(port => string.Equals(port.Name, group.Key, StringComparison.OrdinalIgnoreCase))?.CarrierId ?? string.Empty;
            return SecsItem.L(SecsItem.A(GemValue.Ascii(carrierId)),
                SecsItem.L(group.Select(wafer => SecsItem.U1((byte)Math.Clamp(wafer.SourceSlot, 0, byte.MaxValue)))));
        }));
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
            "ObjType" => SecsItem.A("ProcessJob"),
            "ObjID" => SecsItem.A(GemValue.Ascii(job.Id)),
            "PauseEvent" => SecsItem.L(),
            "PrJobState" => SecsItem.U1((byte)job.State),
            "PrMtlNameList" => MaterialOf(job),
            "PrMtlType" => SecsItem.B(MaterialCarriers),
            "PrProcessStart" => SecsItem.Boolean(job.AutoStart),
            "PrRecipeMethod" => SecsItem.U1(RecipeOnly),
            "RecID" => SecsItem.A(GemValue.Ascii(job.Sequence)),
            "RecVariableList" => SecsItem.L(),
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

        public string TypeName => "ProcessJob";

        public IReadOnlyList<string> AttributeNames { get; } =
        [
            "ObjType", "ObjID", "PauseEvent", "PrJobState", "PrMtlNameList", "PrMtlType", "PrProcessStart", "PrRecipeMethod", "RecID",
            "RecVariableList",
        ];

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
