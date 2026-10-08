using xyz.Common.Log;
using xyz.Components.Attributes;
using xyz.Components.Enums;
using xyz.Components.Interfaces;
using xyz.Components.Models;
using xyz.Configs.Models;
using xyz.Secs.Hsms;
using xyz.Secs.SecsII;
using xyz.Shared.Dtos;

namespace xyz.Components.Components;

/// <summary>
/// CJ 管理（SEMI E94，sc.xml 的 Eap 下的 E94 节点）：Host 用 S14F9 建 CJ（E39 Create Object，对象类型 ControlJob）
/// </summary>
[Component(description: "CJ 管理（SEMI E94）：S14F9 建 CJ、S16F27 CJ 命令，CJ 状态转换报事件")]
public class E94Component : ComponentBase, IE94Callback
{
    private const byte OrderByList = 3;

    /// <summary>E39/E94 的 CJ 对象类型及属性名，统一保留协议中的名称写法。</summary>
    

    #region SC

    [SCEditor("CJSTART", "E94", "S16F27 的 CJ 启动命令字符串（忽略大小写及首尾空格；不能重复或占用其他命令的默认名称）")]
    public string StartCommandName { get; set; } = "CJSTART";

    [SCEditor("CJPAUSE", "E94", "S16F27 的 CJ 暂停命令字符串")]
    public string PauseCommandName { get; set; } = "CJPAUSE";

    [SCEditor("CJRESUME", "E94", "S16F27 的 CJ 恢复命令字符串")]
    public string ResumeCommandName { get; set; } = "CJRESUME";

    [SCEditor("CJCANCEL", "E94", "S16F27 的 CJ 取消命令字符串")]
    public string CancelCommandName { get; set; } = "CJCANCEL";

    [SCEditor("CJDESELECT", "E94", "S16F27 的 CJ 取消选中命令字符串")]
    public string DeselectCommandName { get; set; } = "CJDESELECT";

    [SCEditor("CJSTOP", "E94", "S16F27 的 CJ 停止命令字符串")]
    public string StopCommandName { get; set; } = "CJSTOP";

    [SCEditor("CJABORT", "E94", "S16F27 的 CJ 中止命令字符串")]
    public string AbortCommandName { get; set; } = "CJABORT";

    [SCEditor("CJHOQ", "E94", "S16F27 的 CJ 插到队首命令字符串（Job 管理当前不支持执行此命令）")]
    public string HeadOfQueueCommandName { get; set; } = "CJHOQ";

    #endregion

    /// <summary>
    /// 默认兼容命令名：用于兜底，并检查 SC 是否占用了其他命令的名称  只是局限于字符串
    /// </summary>
    private static readonly (string Name, ControlJobCommand Command)[] StandardCommands =
    [
        ("CJSTART", ControlJobCommand.Start),
        ("CJPAUSE", ControlJobCommand.Pause),
        ("CJRESUME", ControlJobCommand.Resume),
        ("CJCANCEL", ControlJobCommand.Cancel),
        ("CJDESELECT", ControlJobCommand.Deselect),
        ("CJSTOP", ControlJobCommand.Stop),
        ("CJABORT", ControlJobCommand.Abort),
        ("CJHOQ", ControlJobCommand.HeadOfQueue),
    ];

    /// <summary>
    /// 装配钩子：SC 命令名字符串加载完先查一遍不能为空、不能互相重复、不能占用其他命令的默认名称；
    /// 配置错了装配即失败（开机就暴露），运行期的 ParseCommand 另有一层冲突拒绝兜底。
    /// </summary>
    protected internal override void OnSettingLoaded(ModuleConfig setting)
    {
        base.OnSettingLoaded(setting);

        (string Name, ControlJobCommand Command)[] commands =
        [
            (StartCommandName, ControlJobCommand.Start),
            (PauseCommandName, ControlJobCommand.Pause),
            (ResumeCommandName, ControlJobCommand.Resume),
            (CancelCommandName, ControlJobCommand.Cancel),
            (DeselectCommandName, ControlJobCommand.Deselect),
            (StopCommandName, ControlJobCommand.Stop),
            (AbortCommandName, ControlJobCommand.Abort),
            (HeadOfQueueCommandName, ControlJobCommand.HeadOfQueue),
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

    #region 组件+服务

    private E30Component? _gem;
    private IJobManager? _jobs;

    #endregion

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
            new GemData(DvJobState, (byte)(job.E94State ?? job.State)),
            new GemData(DvCarrierId, GemValue.Ascii(job.CarrierId)),
            new GemData(DvProcessJobs, SecsItem.L(job.ProcessJobs.Select(id => SecsItem.A(GemValue.Ascii(id))))));
    }

    #endregion

    #region S16F27 CJ 命令

    /// <summary>
    /// S16F27 CJ 命令 → S16F28 L[2]{ACKA, L[0 或 2]{ERRCODE, ERRTEXT}}：L[3]{CTLJOBID, CTLJOBCMD, L[0 或 2]{"Action", CPVAL}}。
    /// CTLJOBCMD 的标准格式是 U1（1 Start … 8 HOQ）；兼容 SC 配置的字符串（默认 CjStart…）。Action 给数（0 SaveJobs、1 RemoveJobs）或名字都认，
    /// 也认多包一层的 L[1]{L[2]{...}}；不给 Action 按 SaveJobs。
    /// </summary>
    private async Task<SecsReply> CommandAsync(HsmsMessage message)
    {
        var body = SecsRead.List(SecsRead.Body(message), "S16F27", 3);
        string id = SecsRead.Text(body[0], "CTLJOBID").Trim();
        var command = ParseCommand(body[1]); //解析指令
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

    private ControlJobCommand? ParseCommand(SecsItem item)
    {
        #region 走字符串

        if (item.Format is SecsFormat.Ascii or SecsFormat.Jis8)
        {
            string name = item.GetString().Trim();
            if (name.Length == 0)
            {
                return null;
            }

            (string Name, ControlJobCommand Command)[] commands =
            [
                (StartCommandName, ControlJobCommand.Start),
                (PauseCommandName, ControlJobCommand.Pause),
                (ResumeCommandName, ControlJobCommand.Resume),
                (CancelCommandName, ControlJobCommand.Cancel),
                (DeselectCommandName, ControlJobCommand.Deselect),
                (StopCommandName, ControlJobCommand.Stop),
                (AbortCommandName, ControlJobCommand.Abort),
                (HeadOfQueueCommandName, ControlJobCommand.HeadOfQueue),
            ];
            ControlJobCommand? matched = null;
            foreach (var entry in commands)
            {
                if (!string.Equals(name, entry.Name?.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // 同名配置无法确定 Host 要执行哪条命令，拒绝而不是取第一个。
                if (matched is not null)
                {
                    return null;
                }

                matched = entry.Command;
            }

            // 默认名称兜底；SC 同时命中时必须含义一致，避免把停止请求解析成启动等其他命令。
            foreach (var entry in StandardCommands)
            {
                if (string.Equals(name, entry.Name, StringComparison.OrdinalIgnoreCase))
                {
                    if (matched is not null && matched != entry.Command)
                    {
                        LogHelper.Warn(Name, $"CJ 命令字符串配置冲突：{name} 在 SC 中表示 {matched}，默认名称表示 {entry.Command}，拒绝执行");
                        return null;
                    }

                    return entry.Command;
                }
            }

            return matched;
        }
        #endregion

        #region 走数字

        byte value = SecsRead.Code(item, "CTLJOBCMD");

        #endregion

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
    /// 建 CJ：属性 ObjID（不给就用 OBJSPEC）、ProcessingCtrlSpec（必须，L{L[3]{PRJOBID, 规则, 阈值}}）、StartMethod（BOOLEAN，启动方式由 SC 决定）、
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
            string? attribute = ControlJobNames.Attributes.FirstOrDefault(candidate =>
                string.Equals(candidate, name.Trim(), StringComparison.OrdinalIgnoreCase));
            switch (attribute)
            {
                case ControlJobNames.ObjId:
                    id = SecsRead.Text(value, ControlJobNames.ObjId).Trim();
                    break;

                case ControlJobNames.ProcessingCtrlSpec:
                    hasSpec = true;
                    foreach (var entry in SecsRead.List(value, ControlJobNames.ProcessingCtrlSpec))
                    {
                        processJobs.Add(SecsRead.Text(SecsRead.List(entry, "L{PRJOBID, 规则, 阈值}", 3)[0], "PRJOBID").Trim());
                    }

                    break;

                case ControlJobNames.StartMethod:
                    // 保留报文格式校验；CJ 启动方式统一由设备的 SC 配置决定。
                    SecsRead.Flag(value, ControlJobNames.StartMethod);
                    break;

                case ControlJobNames.ProcessOrderMgmt:
                    byte order = SecsRead.Code(value, ControlJobNames.ProcessOrderMgmt);
                    if (order is < 1 or > OrderByList)
                    {
                        return E39Created.Fail(E5Error.Of(E5Error.InvalidAttributeValue, $"{ControlJobNames.ProcessOrderMgmt} must be 1, 2 or 3"));
                    }

                    break;

                case ControlJobNames.MtrlOutSpec:
                case ControlJobNames.MtrlOutByStatus:
                case ControlJobNames.PauseEvent:
                    if (value.Count > 0)
                    {
                        return E39Created.Fail(E5Error.Of(E5Error.UnsupportedOption, $"{name} not supported"));
                    }

                    break;

                case ControlJobNames.CarrierInputSpec:
                case ControlJobNames.DataCollectionPlan:
                    break;

                default:
                    return E39Created.Fail(E5Error.Of(E5Error.UnknownAttribute, $"{ControlJobNames.ObjectType} attribute {name} not allowed"));
            }
        }

        if (!hasSpec || processJobs.Count == 0)
        {
            return E39Created.Fail(E5Error.Of(E5Error.InsufficientParameters, $"{ControlJobNames.ProcessingCtrlSpec} required"));
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
            ControlJobNames.ObjType => SecsItem.A(ControlJobNames.ObjectType),
            ControlJobNames.ObjId => SecsItem.A(GemValue.Ascii(job.Id)),
            ControlJobNames.CarrierInputSpec => job.CarrierId.Length == 0 ? SecsItem.L() : SecsItem.L(SecsItem.A(GemValue.Ascii(job.CarrierId))),
            ControlJobNames.CurrentPrJob => SecsItem.L(processes.Where(process => IsActive(process.State))
                .Select(process => SecsItem.A(GemValue.Ascii(process.Id)))),
            ControlJobNames.DataCollectionPlan => SecsItem.A(string.Empty),
            ControlJobNames.MtrlOutByStatus => SecsItem.L(),
            ControlJobNames.MtrlOutSpec => SecsItem.L(),
            ControlJobNames.PauseEvent => SecsItem.L(),
            ControlJobNames.ProcessingCtrlSpec => SecsItem.L(job.ProcessJobs.Select(processId =>
                SecsItem.L(SecsItem.A(GemValue.Ascii(processId)), SecsItem.L(), SecsItem.L()))),
            ControlJobNames.ProcessOrderMgmt => SecsItem.U1(OrderByList),
            ControlJobNames.PrJobStatusList => SecsItem.L(job.ProcessJobs.Select(processId => SecsItem.L(SecsItem.A(GemValue.Ascii(processId)),
                StateOf(processes, processId)))),
            ControlJobNames.StartMethod => SecsItem.Boolean(job.AutoStart),
            ControlJobNames.State => SecsItem.U1((byte)(job.E94State ?? job.State)),
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

        public string TypeName => ControlJobNames.ObjectType;

        public IReadOnlyList<string> AttributeNames => ControlJobNames.Attributes;

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

public static class ControlJobNames
{
    public const string ObjectType = "ControlJob";
    public const string ObjType = "ObjType";
    public const string ObjId = "ObjID";
    public const string CarrierInputSpec = "CarrierInputSpec";
    public const string CurrentPrJob = "CurrentPrJob";
    public const string DataCollectionPlan = "DataCollectionPlan";
    public const string MtrlOutByStatus = "MtrlOutByStatus";
    public const string MtrlOutSpec = "MtrlOutSpec";
    public const string PauseEvent = "PauseEvent";
    public const string ProcessingCtrlSpec = "ProcessingCtrlSpec";
    public const string ProcessOrderMgmt = "ProcessOrderMgmt";
    public const string PrJobStatusList = "PRJobStatusList";
    public const string StartMethod = "StartMethod";
    public const string State = "State";

    // 保持原有顺序，Host 使用数字 ATTRID 时按此顺序定位属性。
    public static IReadOnlyList<string> Attributes { get; } =
    [
        ObjType, ObjId, CarrierInputSpec, CurrentPrJob, DataCollectionPlan, MtrlOutByStatus, MtrlOutSpec, PauseEvent,
            ProcessingCtrlSpec, ProcessOrderMgmt, PrJobStatusList, StartMethod, State,
        ];
}
