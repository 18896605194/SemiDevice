using xyz.Common.Log;
using xyz.Components.Attributes;
using xyz.Components.Enums;
using xyz.Components.Interfaces;
using xyz.Components.Models;
using xyz.Secs.Hsms;
using xyz.Secs.SecsII;
using xyz.Shared.Dtos;

namespace xyz.Components.Components;

/// <summary>
/// CJ 管理（SEMI E94，sc.xml 的 Eap 下的 E94 节点）：Host 用 S14F9 建 CJ（E39 Create Object，对象类型 ControlJob）、
/// S16F27 下 CJ 命令，翻成 Job 管理的命令（IJobManager）；CJ 的状态转换（经 IE94Callback 报过来）翻成事件。CJ 的状态只在 Job 管理里有一份。
/// 建 CJ：ProcessingCtrlSpec 列的 PJ 要已经建好（S16F11 / S16F15），按列的先后做；不支持改回片地方（MtrlOutSpec 只能空，回原槽）、暂停事件；
/// StartMethod 不给默认自动开始。建、命令要 ON-LINE REMOTE。
/// </summary>
[Component(description: "CJ 管理（SEMI E94）：S14F9 建 CJ、S16F27 CJ 命令，CJ 状态转换报事件")]
public class E94Component : ComponentBase, IE94Callback
{
    /// <summary>ProcessOrderMgmt：按 ProcessingCtrlSpec 的先后（本机只这么做）。</summary>
    private const byte OrderByList = 3;

    #region DV、事件

    private const string DvJobId = "CtrlJobID";
    private const string DvJobState = "ControlJobState";
    private const string DvCarrierId = "CtrlJobCarrierID";
    private const string DvProcessJobs = "CtrlJobPRJobs";

    [DataVariable(ValueFormat.String, "CJ 号")]
    public readonly string JobIdData = DvJobId;

    [DataVariable(ValueFormat.Int, "CJ 状态：0 排队、1 选中、2 等启动、3 在跑、4 暂停、5 完成")]
    public readonly string JobStateData = DvJobState;

    [DataVariable(ValueFormat.String, "CJ 的载具号")]
    public readonly string CarrierIdData = DvCarrierId;

    [DataVariable(ValueFormat.String, "CJ 下的 PJ（L{PRJOBID}，按执行先后）")]
    public readonly string ProcessJobsData = DvProcessJobs;

    [EventAttribut("CJ 建了（#1）", Data = new[] { DvJobId, DvJobState, DvCarrierId, DvProcessJobs })]
    public readonly string CtrlJobTrans01 = "CtrlJobSMTrans01";

    [EventAttribut("排队的 CJ 删了（#2）", Data = new[] { DvJobId, DvJobState })]
    public readonly string CtrlJobTrans02 = "CtrlJobSMTrans02";

    [EventAttribut("CJ 选中（#3）", Data = new[] { DvJobId, DvJobState, DvCarrierId })]
    public readonly string CtrlJobTrans03 = "CtrlJobSMTrans03";

    [EventAttribut("CJ 退回排队（#4）", Data = new[] { DvJobId, DvJobState })]
    public readonly string CtrlJobTrans04 = "CtrlJobSMTrans04";

    [EventAttribut("CJ 料到了直接开始（#5）", Data = new[] { DvJobId, DvJobState, DvCarrierId })]
    public readonly string CtrlJobTrans05 = "CtrlJobSMTrans05";

    [EventAttribut("CJ 料到了等启动（#6）", Data = new[] { DvJobId, DvJobState, DvCarrierId })]
    public readonly string CtrlJobTrans06 = "CtrlJobSMTrans06";

    [EventAttribut("CJ 启动（#7）", Data = new[] { DvJobId, DvJobState })]
    public readonly string CtrlJobTrans07 = "CtrlJobSMTrans07";

    [EventAttribut("CJ 暂停（#8）", Data = new[] { DvJobId, DvJobState })]
    public readonly string CtrlJobTrans08 = "CtrlJobSMTrans08";

    [EventAttribut("CJ 恢复（#9）", Data = new[] { DvJobId, DvJobState })]
    public readonly string CtrlJobTrans09 = "CtrlJobSMTrans09";

    [EventAttribut("CJ 正常完成（#10）", Data = new[] { DvJobId, DvJobState, DvCarrierId })]
    public readonly string CtrlJobTrans10 = "CtrlJobSMTrans10";

    [EventAttribut("CJ 停止完成（#11）", Data = new[] { DvJobId, DvJobState, DvCarrierId })]
    public readonly string CtrlJobTrans11 = "CtrlJobSMTrans11";

    [EventAttribut("CJ 中止完成（#12）", Data = new[] { DvJobId, DvJobState, DvCarrierId })]
    public readonly string CtrlJobTrans12 = "CtrlJobSMTrans12";

    [EventAttribut("完成的 CJ 删了（#13，载具拿走）", Data = new[] { DvJobId, DvJobState })]
    public readonly string CtrlJobTrans13 = "CtrlJobSMTrans13";

    #endregion

    private E30Component? _gem;
    private IJobManager? _jobs;

    #region 接设备

    /// <summary>
    /// 接到链路和 Job 管理上（EAP 组件在链路打开之前调）：挂 E94 上报口，登记 S16F27 和 E39 对象类型 ControlJob（S14F9 建 CJ 走它）。
    /// 没配 E39 时 Host 建不了 CJ，记错误。
    /// </summary>
    public void Attach(HsmsComponent link, E30Component gem, E39Component? objects, IJobManager jobs)
    {
        ArgumentNullException.ThrowIfNull(link);
        ArgumentNullException.ThrowIfNull(gem);
        ArgumentNullException.ThrowIfNull(jobs);
        _gem = gem;
        _jobs = jobs;
        jobs.E94Callback = this;
        link.Handle(16, 27, CommandAsync);
        if (objects is null)
        {
            LogHelper.Error(Name, "sc.xml 的 Eap 下没配 E39 节点：Host 建不了 CJ（S14F9）");
            return;
        }

        objects.Register(new ControlJobType(this));
    }

    /// <summary>从 Job 管理上摘下来（宿主退出时）。</summary>
    public void Detach()
    {
        var jobs = _jobs;
        if (jobs is not null && ReferenceEquals(jobs.E94Callback, this))
        {
            jobs.E94Callback = null;
        }
    }

    #endregion

    #region 上报（IE94Callback）

    void IE94Callback.ControlJobStateChanged(ControlJobDto job, int transition)
    {
        if (transition is < 1 or > 13)
        {
            return;
        }

        _gem?.Report(this, $"CtrlJobSMTrans{transition:00}",
            new GemData(DvJobId, GemValue.Ascii(job.Id)),
            new GemData(DvJobState, (byte)job.State),
            new GemData(DvCarrierId, GemValue.Ascii(job.CarrierId)),
            new GemData(DvProcessJobs, SecsItem.L(job.ProcessJobs.Select(id => SecsItem.A(GemValue.Ascii(id))))));
    }

    #endregion

    #region S16F27 CJ 命令

    /// <summary>
    /// S16F27 CJ 命令 → S16F28 L[2]{ACKA, L[0 或 2]{ERRCODE, ERRTEXT}}：L[3]{CTLJOBID, CTLJOBCMD, L[0 或 2]{"Action", CPVAL}}。
    /// CTLJOBCMD 给数（1 Start … 8 HOQ）或名字（CjStart…）都认；Action 给数（0 SaveJobs、1 RemoveJobs）或名字都认，
    /// 也认多包一层的 L[1]{L[2]{...}}；不给 Action 按 SaveJobs。
    /// </summary>
    private async Task<SecsReply> CommandAsync(HsmsMessage message)
    {
        var body = SecsRead.List(SecsRead.Body(message), "S16F27", 3);
        string id = SecsRead.Text(body[0], "CTLJOBID").Trim();
        var command = ReadCommand(body[1]);
        var action = ReadAction(body[2]);
        E5Error? error = null;
        if (command is null)
        {
            error = E5Error.Of(E5Error.ParametersImproperlySpecified, "CTLJOBCMD invalid");
        }
        else if (_gem is null || !_gem.IsRemote)
        {
            error = E5Error.NotRemote();
        }
        else if (_jobs is null)
        {
            error = E5Error.Of(E5Error.NotAvailable, "Job manager not installed");
        }
        else
        {
            var result = await _jobs.ExecuteControlJobCommandAsync(id, command.Value, action).ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                error = JobErrors.Of(result);
            }
            else
            {
                LogHelper.Info(Name, $"Host CJ 命令 {command.Value}（{action}）：{id}");
            }
        }

        var errorItem = error is null ? SecsItem.L() : SecsItem.L(SecsItem.U2(error.Code), SecsItem.A(error.Text));
        return SecsReply.Of(SecsItem.L(SecsItem.Boolean(error is null), errorItem));
    }

    private static ControlJobCommand? ReadCommand(SecsItem item)
    {
        if (item.Format is SecsFormat.Ascii or SecsFormat.Jis8)
        {
            string name = item.GetString().Trim().ToUpperInvariant();
            return name switch
            {
                "CJSTART" => ControlJobCommand.Start,
                "CJPAUSE" => ControlJobCommand.Pause,
                "CJRESUME" => ControlJobCommand.Resume,
                "CJCANCEL" => ControlJobCommand.Cancel,
                "CJDESELECT" => ControlJobCommand.Deselect,
                "CJSTOP" => ControlJobCommand.Stop,
                "CJABORT" => ControlJobCommand.Abort,
                "CJHOQ" => ControlJobCommand.HeadOfQueue,
                _ => null,
            };
        }

        byte value = SecsRead.Code(item, "CTLJOBCMD");
        return Enum.IsDefined(typeof(ControlJobCommand), (int)value) ? (ControlJobCommand)value : null;
    }

    private static ControlJobAction ReadAction(SecsItem item)
    {
        var parts = SecsRead.List(item, "Action 参数");
        if (parts.Count == 1)
        {
            parts = SecsRead.List(parts[0], "Action 参数");
        }

        if (parts.Count < 2)
        {
            return ControlJobAction.SaveJobs;
        }

        var value = parts[1];
        if (value.Format is SecsFormat.Ascii or SecsFormat.Jis8)
        {
            return string.Equals(value.GetString().Trim(), "RemoveJobs", StringComparison.OrdinalIgnoreCase)
                ? ControlJobAction.RemoveJobs
                : ControlJobAction.SaveJobs;
        }

        return SecsRead.Code(value, "Action") == (byte)ControlJobAction.RemoveJobs ? ControlJobAction.RemoveJobs : ControlJobAction.SaveJobs;
    }

    #endregion

    #region 建 CJ（S14F9，经 E39）

    /// <summary>
    /// 建 CJ：属性 ObjID（不给就用 OBJSPEC）、ProcessingCtrlSpec（必须，L{L[3]{PRJOBID, 规则, 阈值}}）、StartMethod（BOOLEAN，默认 TRUE）、
    /// ProcessOrderMgmt（1/2/3 都收，本机按列表先后做）；MtrlOutSpec、MtrlOutByStatus、PauseEvent 只能空；CarrierInputSpec、DataCollectionPlan 不看。
    /// </summary>
    private async Task<E39Created> CreateAsync(string objectSpec, IReadOnlyList<(string Name, SecsItem Value)> attributes)
    {
        var jobs = _jobs;
        if (jobs is null)
        {
            return E39Created.Fail(E5Error.Of(E5Error.NotAvailable, "Job manager not installed"));
        }

        string id = objectSpec.Trim();
        var processJobs = new List<string>();
        bool hasSpec = false;
        foreach (var (name, value) in attributes)
        {
            switch (name.Trim().ToUpperInvariant())
            {
                case "OBJID":
                    id = SecsRead.Text(value, "ObjID").Trim();
                    break;

                case "PROCESSINGCTRLSPEC":
                    hasSpec = true;
                    foreach (var entry in SecsRead.List(value, "ProcessingCtrlSpec"))
                    {
                        processJobs.Add(SecsRead.Text(SecsRead.List(entry, "L{PRJOBID, 规则, 阈值}", 3)[0], "PRJOBID").Trim());
                    }

                    break;

                case "STARTMETHOD":
                    // 保留报文格式校验；CJ 启动方式统一由设备的 SC 配置决定。
                    SecsRead.Flag(value, "StartMethod");
                    break;

                case "PROCESSORDERMGMT":
                    byte order = SecsRead.Code(value, "ProcessOrderMgmt");
                    if (order is < 1 or > OrderByList)
                    {
                        return E39Created.Fail(E5Error.Of(E5Error.InvalidAttributeValue, "ProcessOrderMgmt must be 1, 2 or 3"));
                    }

                    break;

                case "MTRLOUTSPEC":
                case "MTRLOUTBYSTATUS":
                case "PAUSEEVENT":
                    if (value.Count > 0)
                    {
                        return E39Created.Fail(E5Error.Of(E5Error.UnsupportedOption, $"{name} not supported"));
                    }

                    break;

                case "CARRIERINPUTSPEC":
                case "DATACOLLECTIONPLAN":
                    break;

                default:
                    return E39Created.Fail(E5Error.Of(E5Error.UnknownAttribute, $"ControlJob attribute {name} not allowed"));
            }
        }

        if (!hasSpec || processJobs.Count == 0)
        {
            return E39Created.Fail(E5Error.Of(E5Error.InsufficientParameters, "ProcessingCtrlSpec required"));
        }

        var result = await jobs.CreateControlJobAsync(
            loadPort: null,
            processJobs: processJobs,
            cjName: id).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return E39Created.Fail(JobErrors.Of(result));
        }

        LogHelper.Info(Name, $"Host 建了 CJ {id}：{string.Join("、", processJobs)}");
        return E39Created.Ok(id);
    }

    #endregion

    #region E39 对象

    private IReadOnlyList<string> JobIds()
    {
        return (_jobs?.Snapshot.ControlJobs ?? []).Select(job => job.Id).ToList();
    }

    private bool TryGetJob(string id, string attribute, out SecsItem value)
    {
        value = SecsItem.L();
        var snapshot = _jobs?.Snapshot;
        var job = snapshot?.ControlJobs.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
        if (snapshot is null || job is null)
        {
            return false;
        }

        var processes = snapshot.ProcessJobs
            .Where(process => job.ProcessJobs.Contains(process.Id, StringComparer.OrdinalIgnoreCase))
            .ToList();
        value = attribute switch
        {
            "ObjType" => SecsItem.A("ControlJob"),
            "ObjID" => SecsItem.A(GemValue.Ascii(job.Id)),
            "CarrierInputSpec" => job.CarrierId.Length == 0 ? SecsItem.L() : SecsItem.L(SecsItem.A(GemValue.Ascii(job.CarrierId))),
            "CurrentPrJob" => SecsItem.L(processes.Where(process => IsActive(process.State))
                .Select(process => SecsItem.A(GemValue.Ascii(process.Id)))),
            "DataCollectionPlan" => SecsItem.A(string.Empty),
            "MtrlOutByStatus" => SecsItem.L(),
            "MtrlOutSpec" => SecsItem.L(),
            "PauseEvent" => SecsItem.L(),
            "ProcessingCtrlSpec" => SecsItem.L(job.ProcessJobs.Select(processId =>
                SecsItem.L(SecsItem.A(GemValue.Ascii(processId)), SecsItem.L(), SecsItem.L()))),
            "ProcessOrderMgmt" => SecsItem.U1(OrderByList),
            "PRJobStatusList" => SecsItem.L(job.ProcessJobs.Select(processId => SecsItem.L(SecsItem.A(GemValue.Ascii(processId)),
                StateOf(processes, processId)))),
            "StartMethod" => SecsItem.Boolean(job.AutoStart),
            "State" => SecsItem.U1((byte)job.State),
            _ => SecsItem.L(),
        };
        return true;
    }

    /// <summary>PJ 在 ACTIVE 里（准备、等启动、在做、暂停、停止中、中止中；做完不算）。</summary>
    private static bool IsActive(int state)
    {
        return state >= (int)ProcessJobState.SettingUp && state <= (int)ProcessJobState.Aborting && state != (int)ProcessJobState.ProcessComplete;
    }

    /// <summary>一个 PJ 的状态 U1；已经结束（不在没结束的表里）的报空。</summary>
    private static SecsItem StateOf(IReadOnlyList<ProcessJobDto> processes, string processId)
    {
        var process = processes.FirstOrDefault(item => string.Equals(item.Id, processId, StringComparison.OrdinalIgnoreCase));
        return process is null ? SecsItem.U1() : SecsItem.U1((byte)process.State);
    }

    /// <summary>E39 对象类型 ControlJob：查属性、建对象（S14F9）。</summary>
    private sealed class ControlJobType : IE39ObjectType
    {
        private readonly E94Component _owner;

        public ControlJobType(E94Component owner)
        {
            _owner = owner;
        }

        public string TypeName => "ControlJob";

        public IReadOnlyList<string> AttributeNames { get; } =
        [
            "ObjType", "ObjID", "CarrierInputSpec", "CurrentPrJob", "DataCollectionPlan", "MtrlOutByStatus", "MtrlOutSpec", "PauseEvent",
            "ProcessingCtrlSpec", "ProcessOrderMgmt", "PRJobStatusList", "StartMethod", "State",
        ];

        public IReadOnlyList<string> ObjectIds()
        {
            return _owner.JobIds();
        }

        public bool TryGetAttribute(string objectId, string attribute, out SecsItem value)
        {
            return _owner.TryGetJob(objectId, attribute, out value);
        }

        public Task<E39Created> CreateAsync(string objectSpec, IReadOnlyList<(string Name, SecsItem Value)> attributes)
        {
            return _owner.CreateAsync(objectSpec, attributes);
        }
    }

    #endregion
}
