using xyz.Common.Log;
using xyz.Components.Attributes;
using xyz.Components.Collectors;
using xyz.Components.Enums;
using xyz.Components.Models;
using xyz.Configs.Models;
using xyz.Database.DbProvider;
using xyz.Secs.SecsII;

namespace xyz.Components.Components;

/// <summary>
/// GEM（SEMI E30，sc.xml 的 Eap 下的 E30 节点）：设备跟 Host 打交道的总规矩，别的标准（E87、E40……）都在它上面。
/// ① 控制状态（OFF-LINE 三个子状态、ON-LINE LOCAL / REMOTE）：离线时 Host 只能建立通讯、请求上线；LOCAL 时 Host 能查不能下动作命令；
/// ② 通讯状态：链路连上后设备发 S1F13（Host 先发也行），换过一次才算建立了通讯，之前 Host 的报文不理；
/// ③ 事件报告：Host 定义报告（S2F33）、挂到事件上（S2F35）、开关事件（S2F37），设备这边谁报事件都经这里统一发 S6F11；
/// ④ 报警：报出、清除都发 S5F1（Host 能开关，S5F3），同时报对应的报警事件；
/// ⑤ 变量和常量：SV 查询（S1F3）、名单（S1F11 / S1F21 / S1F23）、EC 查改（S2F13 / S2F15 / S2F29）、时间（S2F17 / S2F31）；
/// ⑥ 缓存（Spooling）：断了通讯时 Host 要缓存的报文存盘，Host 用 S6F23 要了再发。
/// Host 定的报告、开关、缓存范围掉电保持（存库）。EAP 没启用时不接到链路上，组件里的事件也不报。
/// </summary>
[Component(description: "GEM（SEMI E30）：控制状态、通讯建立、事件报告、报警、变量和常量、缓存")]
public partial class E30Component : ComponentBase
{
    /// <summary>
    /// 当前 GEM 组件；sc.xml 没配 E30 节点时为 null。组件报事件（ComponentBase.RaiseEvent）经它发。
    /// </summary>
    public static E30Component? Current { get; set; }

    /// <summary>ALTX 最长 40 个字符（E5）。</summary>
    private const int MaxAlarmTextLength = 40;

    /// <summary>ALCD 最高位：报警在报着。</summary>
    private const byte AlarmSetBit = 0x80;

    private readonly GemBook _book = new();
    private HsmsComponent? _link;
    private GemStore? _store;
    private GemSpool? _spool;
    private GemSender? _sender;
    private int _dataId;

    /// <summary>
    /// Host 改 EC（S2F15）的那一刻：EC 变化的通知也在这条线程上发，见到这个标记就不当成操作员改的（不报操作员改常量的事件）。
    /// </summary>
    [ThreadStatic]
    private static bool _applyingHostConstants;

    public E30Component()
    {
        Current = this;
    }

    #region SC

    [SCEditor("xyz", "E30", "MDLN 设备型号：通讯建立（S1F13 / S1F14）、在线询问（S1F2）时报给 Host")]
    public string EquipmentModel { get; set; } = "xyz";

    [SCEditor("1.0.0", "E30", "SOFTREV 软件版本：跟 MDLN 一起报给 Host")]
    public string SoftwareRevision { get; set; } = "1.0.0";

    [SCEditor("Default", "E30", "Host 定的报告、开关、缓存存哪个库（sc.xml 的 Database 节点名）")]
    public string Database { get; set; } = XyzDb.DefaultName;

    [SCEditor("False", "E30", "Host 对时（S2F31）时是不是真改本机时钟：False = 只答收下不改（要不要对时是现场的规矩），True = 改（宿主要有改时间的权限，改不了回 TIACK=1）")]
    public bool ApplyHostTime { get; set; }

    #endregion

    #region EC

    [VariableMark(VariableType.EC, ValueFormat.Bool, @default: "False",
        description: "开机进 ON-LINE（True）还是 OFF-LINE（False）；OFF-LINE 进哪个子状态看 OfflineSubState")]
    public bool InitialOnline
    {
        get { return bool.TryParse(GetEcString(nameof(InitialOnline)), out bool online) && online; }
        set { SetEc(nameof(InitialOnline), value.ToString()); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Enum, @default: "HostOffline",
        description: "进 OFF-LINE 时的子状态：EquipmentOffline = 要操作员点上线，AttemptOnline = 马上问 Host（S1F1），HostOffline = 等 Host 请求上线（S1F17）",
        Options = "EquipmentOffline,AttemptOnline,HostOffline")]
    public GemControlState OfflineSubState
    {
        get
        {
            return Enum.TryParse(GetEcString(nameof(OfflineSubState)), true, out GemControlState state) && IsOffline(state)
                ? state
                : GemControlState.HostOffline;
        }
        set { SetEc(nameof(OfflineSubState), value.ToString()); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Bool, @default: "True",
        description: "进 ON-LINE 时直接 REMOTE（True，Host 控制）还是 LOCAL（False，操作员控制）")]
    public bool OnlineRemote
    {
        get { return !bool.TryParse(GetEcString(nameof(OnlineRemote)), out bool remote) || remote; }
        set { SetEc(nameof(OnlineRemote), value.ToString()); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Enum, @default: "HostOffline",
        description: "操作员点上线但 Host 没答应（S1F0 / 超时）时退到哪：EquipmentOffline 或 HostOffline",
        Options = "EquipmentOffline,HostOffline")]
    public GemControlState OnlineFailedState
    {
        get
        {
            return Enum.TryParse(GetEcString(nameof(OnlineFailedState)), true, out GemControlState state)
                   && state is GemControlState.EquipmentOffline or GemControlState.HostOffline
                ? state
                : GemControlState.HostOffline;
        }
        set { SetEc(nameof(OnlineFailedState), value.ToString()); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, unit: "s", min: "1", max: "1800", @default: "10",
        description: "通讯没建立时设备隔多久再发一次 S1F13（E30 EstablishCommunicationsTimeout）")]
    public int EstablishCommunicationsTimeout
    {
        get { return GetEcInt(nameof(EstablishCommunicationsTimeout)); }
        set { SetEcInt(nameof(EstablishCommunicationsTimeout), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, min: "0", max: "2", @default: "1",
        description: "时间格式（E30 TimeFormat）：0 = 12 位 YYMMDDhhmmss，1 = 16 位 YYYYMMDDhhmmsscc，2 = ISO 8601")]
    public int TimeFormat
    {
        get { return GetEcInt(nameof(TimeFormat)); }
        set { SetEcInt(nameof(TimeFormat), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Bool, @default: "True",
        description: "断了通讯时缓存报文（还要 Host 用 S2F43 指定缓存哪些，没指定就什么都不缓存）")]
    public bool EnableSpooling
    {
        get { return !bool.TryParse(GetEcString(nameof(EnableSpooling)), out bool enabled) || enabled; }
        set { SetEc(nameof(EnableSpooling), value.ToString()); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, min: "1", max: "1000000", @default: "10000",
        description: "缓存最多存几条报文")]
    public int MaxSpoolMessages
    {
        get { return GetEcInt(nameof(MaxSpoolMessages)); }
        set { SetEcInt(nameof(MaxSpoolMessages), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Bool, @default: "False",
        description: "缓存满了：True = 挤掉最早的，False = 丢掉新来的（E30 OverWriteSpool）")]
    public bool OverwriteSpool
    {
        get { return bool.TryParse(GetEcString(nameof(OverwriteSpool)), out bool overwrite) && overwrite; }
        set { SetEc(nameof(OverwriteSpool), value.ToString()); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, min: "0", max: "1000000", @default: "0",
        description: "Host 要缓存（S6F23）时一次最多发几条，0 = 全发（E30 MaxSpoolTransmit）")]
    public int MaxSpoolTransmit
    {
        get { return GetEcInt(nameof(MaxSpoolTransmit)); }
        set { SetEcInt(nameof(MaxSpoolTransmit), value); }
    }

    #endregion

    #region SV

    /// <summary>控制状态（E30 ControlState，U1：1 EQUIPMENT OFF-LINE、2 ATTEMPT ON-LINE、3 HOST OFF-LINE、4 LOCAL、5 REMOTE）。</summary>
    [VariableMark(VariableType.SV, ValueFormat.Int, description: "GEM 控制状态：1 设备离线、2 正在上线、3 等 Host 上线、4 在线本地、5 在线远程")]
    public byte ControlState => (byte)CurrentControlState;

    /// <summary>上一个控制状态（E30 PreviousControlState）。</summary>
    [VariableMark(VariableType.SV, ValueFormat.Int, description: "GEM 上一个控制状态")]
    public byte PreviousControlState
    {
        get
        {
            lock (_stateGate)
            {
                return (byte)_previous;
            }
        }
    }

    /// <summary>通讯状态：1 没建立、2 建立了。</summary>
    [VariableMark(VariableType.SV, ValueFormat.Int, description: "GEM 通讯状态：1 没建立、2 建立了")]
    public byte CommunicationState => (byte)CommState;

    /// <summary>设备时钟（E30 Clock），格式按 TimeFormat。</summary>
    [VariableMark(VariableType.SV, ValueFormat.String, description: "设备时钟（格式按 EC TimeFormat）")]
    public string Clock => GemValue.Time(DateTime.Now, TimeFormat);

    /// <summary>开着的事件（E30 EventsEnabled）：CEID 列表。</summary>
    [VariableMark(VariableType.SV, ValueFormat.String, description: "开着的事件（CEID 列表）")]
    public SecsItem EventsEnabled =>
        SecsItem.L(AllEventIds().Where(_book.IsEventEnabled).Select(ceid => SecsItem.U4(ceid)));

    /// <summary>开着的报警（E30 AlarmsEnabled）：ALID 列表。</summary>
    [VariableMark(VariableType.SV, ValueFormat.String, description: "开着的报警（ALID 列表）")]
    public SecsItem AlarmsEnabled =>
        SecsItem.L(AllAlarmIds().Where(_book.IsAlarmEnabled).Select(alid => SecsItem.U4(alid)));

    /// <summary>正在报着的报警（E30 AlarmsSet）：ALID 列表。</summary>
    [VariableMark(VariableType.SV, ValueFormat.String, description: "正在报着的报警（ALID 列表）")]
    public SecsItem AlarmsSet =>
        SecsItem.L(ActiveAlarmIds().Select(alid => SecsItem.U4(alid)));

    /// <summary>缓存里现在有几条（E30 SpoolCountActual）。</summary>
    [VariableMark(VariableType.SV, ValueFormat.Int, description: "缓存里现在有几条报文")]
    public uint SpoolCountActual => (uint)(_spool?.CountActual ?? 0);

    /// <summary>这一轮缓存一共进来过几条（E30 SpoolCountTotal）。</summary>
    [VariableMark(VariableType.SV, ValueFormat.Int, description: "这一轮缓存一共进来过几条报文（含满了丢掉的）")]
    public uint SpoolCountTotal => _spool?.CountTotal ?? 0;

    /// <summary>这一轮缓存开始的时刻（E30 SpoolStartTime）。</summary>
    [VariableMark(VariableType.SV, ValueFormat.String, description: "这一轮缓存开始的时刻")]
    public string SpoolStartTime => FormatTime(_spool?.StartTime);

    /// <summary>这一轮缓存满了的时刻（E30 SpoolFullTime）。</summary>
    [VariableMark(VariableType.SV, ValueFormat.String, description: "这一轮缓存满了的时刻（没满过为空）")]
    public string SpoolFullTime => FormatTime(_spool?.FullTime);

    #endregion

    #region 事件、DV

    [EventAttribut("控制状态：离线", Description = "进 OFF-LINE（操作员点离线、Host 请求离线）；离线前最后报的一条")]
    public readonly string EquipmentOfflineEvent = "EquipmentOffline";

    [EventAttribut("控制状态：在线本地", Description = "进 ON-LINE LOCAL")]
    public readonly string ControlStateLocalEvent = "ControlStateLocal";

    [EventAttribut("控制状态：在线远程", Description = "进 ON-LINE REMOTE")]
    public readonly string ControlStateRemoteEvent = "ControlStateRemote";

    [EventAttribut("开始缓存", Description = "断了通讯，开始缓存 Host 要缓存的报文（开始时刻看 SV SpoolStartTime）")]
    public readonly string SpoolingActivatedEvent = "SpoolingActivated";

    [EventAttribut("缓存清空", Description = "缓存发完或被清掉，不再缓存")]
    public readonly string SpoolingDeactivatedEvent = "SpoolingDeactivated";

    [EventAttribut("缓存发送失败", Description = "按 Host 要求发缓存时断了，没发完的留在缓存里")]
    public readonly string SpoolTransmitFailureEvent = "SpoolTransmitFailure";

    [EventAttribut("操作员改了设备常量", Description = "操作员在界面上改了 EC（Host 用 S2F15 改的不报）",
        Data = new[] { "ChangedEcid", "ChangedEcValue" })]
    public readonly string OperatorEquipmentConstantChangeEvent = "OperatorEquipmentConstantChange";

    [DataVariable(ValueFormat.Int, "操作员改的 EC 的 ECID")]
    public readonly string ChangedEcidData = "ChangedEcid";

    [DataVariable(ValueFormat.String, "操作员改的 EC 改成的值")]
    public readonly string ChangedEcValueData = "ChangedEcValue";

    #endregion

    #region 接链路

    /// <summary>接到链路上了（EAP 启用了）：Host 的报文有人答、组件报的事件会发。</summary>
    public bool IsAttached => _link is not null;

    /// <summary>
    /// 接到链路上（EAP 组件在链路打开之前调）：读回存着的设定、登记各报文的处理方和闸门、订链路连上 / 断开、
    /// 订报警和 EC 变化，最后按 EC 进开机的控制状态。
    /// </summary>
    public void Attach(HsmsComponent link)
    {
        ArgumentNullException.ThrowIfNull(link);
        if (_link is not null)
        {
            throw new InvalidOperationException($"{Name} 已经接过链路了");
        }

        var store = new GemStore(Database, () => Name);
        _store = store;
        _book.Load(store.LoadConfig());
        var spool = new GemSpool(store, SaveConfig, () => MaxSpoolMessages, () => OverwriteSpool);
        spool.Restore(_book.Config);
        _spool = spool;
        _sender = new GemSender(() => _link, spool, _book, () => CommState == GemCommState.Communicating,
            CommunicationFailed, OnSpoolEmptied, OnSpoolTransmitFailed, () => Name);
        _link = link;

        link.Gate = Admit;
        link.LinkSelected += OnLinkSelected;
        link.LinkClosed += OnLinkClosed;
        RegisterHandlers(link);

        var alarms = AlarmComponent.Current;
        if (alarms is not null)
        {
            alarms.AlarmChanged += OnAlarmChanged;
        }

        var ec = EcComponent.Current;
        if (ec is not null)
        {
            ec.ValueChanged += OnConstantChanged;
        }

        EnterInitialState();
        if (spool.IsActive)
        {
            LogHelper.Warn(Name, $"上次关机时缓存还开着（{spool.CountActual} 条），接着开着，等 Host 用 S6F23 要");
        }
    }

    /// <summary>从链路上摘下来（宿主退出时）：不再订报警和 EC 变化。</summary>
    public void Detach()
    {
        var alarms = AlarmComponent.Current;
        if (alarms is not null)
        {
            alarms.AlarmChanged -= OnAlarmChanged;
        }

        var ec = EcComponent.Current;
        if (ec is not null)
        {
            ec.ValueChanged -= OnConstantChanged;
        }

        var link = _link;
        if (link is not null)
        {
            link.LinkSelected -= OnLinkSelected;
            link.LinkClosed -= OnLinkClosed;
        }

        _link = null;
    }

    /// <summary>把设定（连同缓存的状态）存盘。</summary>
    private void SaveConfig()
    {
        var store = _store;
        if (store is null)
        {
            return;
        }

        var spool = _spool;
        string json = _book.Serialize(config =>
        {
            spool?.CopyTo(config);
        });
        store.SaveConfig(json);
    }

    #endregion

    #region 报事件（S6F11）

    /// <summary>
    /// 报一个事件（任意线程）：code 是 source 组件上 [EventAttribut] 的代码，data 是这个事件带的 DV（[DataVariable] 的代码 → 值）。
    /// 值在这一刻取好（SV、EC 现读，DV 用给的），排进发送线程按先后发。没接链路、离线、事件没编号或被 Host 关了都不报。
    /// </summary>
    public void Report(ComponentBase source, string code, params GemData[] data)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!IsAttached)
        {
            return;
        }

        var collectors = GemCollectors.Current;
        if (collectors is null)
        {
            return;
        }

        var values = new Dictionary<int, object?>();
        foreach (var item in data)
        {
            int dvid = collectors.Dv.DvidOf($"{source.FullPath}.{item.Name}");
            if (dvid == 0)
            {
                LogHelper.Warn(Name, $"{source.FullPath} 报事件 {code} 带的 {item.Name} 没有 DV 编号，不带");
                continue;
            }

            values[dvid] = item.Value;
        }

        ReportEvent($"{source.FullPath}.{code}", values);
    }

    /// <summary>按事件全名报（报警事件这种不挂在组件上的）：dvs 是 DVID → 值。</summary>
    private void ReportEvent(string eventName, IReadOnlyDictionary<int, object?> dvs)
    {
        var sender = _sender;
        var collectors = GemCollectors.Current;
        if (sender is null || collectors is null)
        {
            return;
        }

        int ceid = collectors.Event.CeidOf(eventName);
        if (ceid == 0)
        {
            LogHelper.Warn(Name, $"事件 {eventName} 没有 CEID（编号表不可用或没声明），不报");
            return;
        }

        if (!IsOnline || !_book.IsEventEnabled((uint)ceid))
        {
            return;
        }

        var body = SecsItem.L(SecsItem.U4(NextDataId()), SecsItem.U4((uint)ceid), BuildReports((uint)ceid, dvs, annotated: false));
        sender.Enqueue(new SecsMessage(6, 11, true, body));
    }

    /// <summary>
    /// 一个事件的报告部分 L[a]{L[2]{RPTID, L[b]{V}}}（annotated 时每个值是 L[2]{VID, V}）：按 Host 挂的报告取值。
    /// </summary>
    private SecsItem BuildReports(uint ceid, IReadOnlyDictionary<int, object?>? dvs, bool annotated)
    {
        return SecsItem.L(_book.ReportsOf(ceid).Select(report =>
            SecsItem.L(SecsItem.U4(report.ReportId), ReportValues(report.Variables, dvs, annotated))));
    }

    private SecsItem ReportValues(IReadOnlyList<uint> variables, IReadOnlyDictionary<int, object?>? dvs, bool annotated)
    {
        return SecsItem.L(variables.Select(vid => annotated
            ? SecsItem.L(SecsItem.U4(vid), VariableValue(vid, dvs))
            : VariableValue(vid, dvs)));
    }

    private uint NextDataId()
    {
        return unchecked((uint)Interlocked.Increment(ref _dataId));
    }

    #endregion

    #region 报警（S5F1 + 报警事件）

    /// <summary>
    /// 报警报出 / 清除：Host 开着这个报警就发 S5F1（ALCD 最高位 1 = 报出），再报这条报警对应的报出 / 清除事件（带报警的几项 DV）。
    /// 没有 ALID 的报警只记日志。
    /// </summary>
    private void OnAlarmChanged(AlarmItem alarm)
    {
        try
        {
            var collectors = GemCollectors.Current;
            if (collectors is null)
            {
                return;
            }

            string name = $"{alarm.SourcePath}.{alarm.AlarmCode}";
            var definition = collectors.Alarm.Definitions
                .FirstOrDefault(row => row.Enabled && string.Equals(row.Name, name, StringComparison.OrdinalIgnoreCase));
            if (definition is null)
            {
                LogHelper.Warn(Name, $"报警没有 ALID 编号，不报 Host：{name}");
                return;
            }

            uint alid = (uint)definition.Id;
            var sender = _sender;
            if (sender is not null && IsOnline && _book.IsAlarmEnabled(alid))
            {
                byte alcd = alarm.IsActive ? AlarmSetBit : (byte)0;
                sender.Enqueue(new SecsMessage(5, 1, true,
                    SecsItem.L(SecsItem.B(alcd), SecsItem.U4(alid), SecsItem.A(AlarmText(definition.Name)))));
            }

            var payload = collectors.Dv.AlarmPayloadDvids;
            var dvs = new Dictionary<int, object?>();
            if (payload.Count == DvCollector.AlarmPayload.Count)
            {
                object?[] values =
                [
                    alid,
                    definition.Name,
                    alarm.SourcePath,
                    alarm.Category.ToString(),
                    alarm.Level.ToString(),
                    alarm.AlarmText,
                ];
                for (int index = 0; index < payload.Count; index++)
                {
                    dvs[payload[index]] = values[index];
                }
            }

            ReportEvent(EventCollector.AlarmEventName(definition.Id, clear: !alarm.IsActive), dvs);
        }
        catch (Exception exception)
        {
            // 报 Host 出错绝不能反过来影响报警系统本身
            LogHelper.Warn(Name, $"报警报 Host 失败：{exception.Message}");
        }
    }

    /// <summary>
    /// ALTX：E5 限 40 个字符、只能 ASCII。报警文字是中文发不出去，发"组件全路径.报警代码"（跟编号表里的名字一样），
    /// 超长只留后面 40 个字符（报警代码在后面）。Host 一般按 ALID 对自己的报警表，这段只是方便人看。
    /// </summary>
    private static string AlarmText(string name)
    {
        string text = GemValue.Ascii(name);
        return text.Length > MaxAlarmTextLength ? text[^MaxAlarmTextLength..] : text;
    }

    #endregion

    #region 操作员改常量

    /// <summary>EC 变了：不是 Host 改的就报"操作员改了设备常量"（带 ECID 和新值）。</summary>
    private void OnConstantChanged(string path, EcValueConfig value)
    {
        if (_applyingHostConstants)
        {
            return;
        }

        int ecid = GemCollectors.Current?.Ec.EcidOf(path, value.Name) ?? 0;
        if (ecid == 0)
        {
            return;
        }

        Report(this, OperatorEquipmentConstantChangeEvent,
            new GemData(ChangedEcidData, (uint)ecid),
            new GemData(ChangedEcValueData, GemValue.FromText(value.Value ?? string.Empty, value.Format ?? string.Empty)));
    }

    #endregion

    #region 缓存的回调

    /// <summary>缓存取空或清掉了：关缓存，报"缓存清空"。</summary>
    private void OnSpoolEmptied()
    {
        if (_spool is not null && _spool.Deactivate())
        {
            LogHelper.Info(Name, "缓存清空，不再缓存");
            Report(this, SpoolingDeactivatedEvent);
        }
    }

    /// <summary>发缓存的时候断了：报"缓存发送失败"（这条自己也会进缓存）。</summary>
    private void OnSpoolTransmitFailed()
    {
        Report(this, SpoolTransmitFailureEvent);
    }

    #endregion

    #region 变量（SV / EC / DV）

    /// <summary>变量号存在吗：在用、标了上传的 SV、EC，或者在用的 DV。</summary>
    private static bool VariableExists(uint vid)
    {
        var collectors = GemCollectors.Current;
        if (collectors is null || vid > int.MaxValue)
        {
            return false;
        }

        int id = (int)vid;
        if (collectors.Sv.TryRead(id, out var sv, out _))
        {
            return sv is not null && sv.Visible;
        }

        var ec = collectors.Ec.ByEcid(id);
        if (ec is not null)
        {
            return ec.Visible;
        }

        return collectors.Dv.ByDvid(id) is not null;
    }

    /// <summary>
    /// 一个变量现在的值：SV、EC 现读；DV 用报事件时给的（没给的报空项）。不存在的号报空 ASCII（跟 S1F3 一样）。
    /// </summary>
    private static SecsItem VariableValue(uint vid, IReadOnlyDictionary<int, object?>? dvs)
    {
        var collectors = GemCollectors.Current;
        if (collectors is null || vid > int.MaxValue)
        {
            return SecsItem.A(string.Empty);
        }

        int id = (int)vid;
        if (collectors.Sv.TryRead(id, out var sv, out object? raw))
        {
            return sv is not null && sv.Visible ? GemValue.From(raw, sv.Format) : SecsItem.A(string.Empty);
        }

        var ec = collectors.Ec.ByEcid(id);
        if (ec is not null)
        {
            return ec.Visible ? GemValue.FromText(ec.Value, ec.Format) : SecsItem.A(string.Empty);
        }

        var dv = collectors.Dv.ByDvid(id);
        if (dv is not null)
        {
            return dvs is not null && dvs.TryGetValue(id, out object? value) ? GemValue.From(value, dv.Format) : GemValue.Empty(dv.Format);
        }

        return SecsItem.A(string.Empty);
    }

    /// <summary>全部在用事件的 CEID。</summary>
    private static IReadOnlyList<uint> AllEventIds()
    {
        return GemCollectors.Current?.Event.Definitions.Where(row => row.Enabled).Select(row => (uint)row.Id).ToList() ?? [];
    }

    /// <summary>全部在用报警的 ALID。</summary>
    private static IReadOnlyList<uint> AllAlarmIds()
    {
        return GemCollectors.Current?.Alarm.Definitions.Where(row => row.Enabled).Select(row => (uint)row.Id).ToList() ?? [];
    }

    /// <summary>正在报着的报警的 ALID。</summary>
    private static IReadOnlyList<uint> ActiveAlarmIds()
    {
        var collectors = GemCollectors.Current;
        var alarms = AlarmComponent.Current;
        if (collectors is null || alarms is null)
        {
            return [];
        }

        var active = alarms.ActiveAlarms.Select(alarm => $"{alarm.SourcePath}.{alarm.AlarmCode}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return collectors.Alarm.Definitions.Where(row => row.Enabled && active.Contains(row.Name)).Select(row => (uint)row.Id).ToList();
    }

    private string FormatTime(DateTime? time)
    {
        return time is null ? string.Empty : GemValue.Time(time.Value, TimeFormat);
    }

    #endregion
}
