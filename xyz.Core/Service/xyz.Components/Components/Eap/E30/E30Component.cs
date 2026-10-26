using xyz.Common.Log;
using xyz.Components.Attributes;
using xyz.Components.Collectors;
using xyz.Components.Enums;
using xyz.Components.Interfaces;
using xyz.Components.Models;
using xyz.Configs.Models;
using xyz.Database.DbProvider;
using xyz.Secs.Gem;
using xyz.Secs.Gem.E30;
using xyz.Secs.SecsII;
using xyz.Tools;

namespace xyz.Components.Components;

/// <summary>
/// GEM（SEMI E30，machine.xml 的 Eap 下的 E30 节点）：设备跟 Host 打交道的总规矩，别的标准（E87、E40……）都在它上面。
/// 协议（控制状态、通讯建立、报告、报警、变量和常量、缓存）在 xyz.Secs 的 E30Engine；这里给它设定（machine.xml、EC）、
/// 编号表和当前值（GemDictionary）、存盘（GemStore），把组件报的事件（RaiseEvent）换成 CEID、报警换成 ALID 交进去。
/// EAP 没启用时不接到链路上，组件里的事件也不报
/// </summary>
[Component(description: "GEM（SEMI E30）：控制状态、通讯建立、事件报告、报警、变量和常量、缓存")]
public class E30Component : ComponentBase, IE30Component, IEventReceiver, IE30Settings, IGemEvents
{
    private E30Engine? _engine;

    /// <summary>
    /// 报警组件（接链路时取）；没装是 null
    /// </summary>
    private IAlarm? _alarms;

    /// <summary>
    /// EC 组件（接链路时取）；没装是 null
    /// </summary>
    private IEc? _ec;

    /// <summary>
    /// GEM 编号表（接链路时取）：组件报的事件、DV、报警按它换成编号
    /// </summary>
    private GemCollectors? _collectors;

    #region Machine

    [Machine("xyz", "E30", "MDLN 设备型号：通讯建立（S1F13 / S1F14）、在线询问（S1F2）时报给 Host")]
    public string EquipmentModel { get; set; } = "xyz";

    [Machine("1.0.0", "E30", "SOFTREV 软件版本：跟 MDLN 一起报给 Host")]
    public string SoftwareRevision { get; set; } = "1.0.0";

    [Machine("Default", "E30", "Host 定的报告、开关、缓存存哪个库（machine.xml 的 Database 节点名）")]
    public string Database { get; set; } = XyzDb.DefaultName;

    [Machine("False", "E30", "Host 对时（S2F31）时是不是真改本机时钟：False = 只答收下不改（要不要对时是现场的规矩），True = 改（宿主要有改时间的权限，改不了回 TIACK=1）")]
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
            if (Enum.TryParse(GetEcString(nameof(OfflineSubState)), true, out GemControlState state)
                && state is GemControlState.EquipmentOffline or GemControlState.AttemptOnline or GemControlState.HostOffline)
            {
                return state;
            }

            return GemControlState.HostOffline;
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
            if (Enum.TryParse(GetEcString(nameof(OnlineFailedState)), true, out GemControlState state)
                && state is GemControlState.EquipmentOffline or GemControlState.HostOffline)
            {
                return state;
            }

            return GemControlState.HostOffline;
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

    /// <summary>
    /// 控制状态（E30 ControlState，U1：1 EQUIPMENT OFF-LINE、2 ATTEMPT ON-LINE、3 HOST OFF-LINE、4 LOCAL、5 REMOTE）
    /// </summary>
    [VariableMark(VariableType.SV, ValueFormat.Int, description: "GEM 控制状态：1 设备离线、2 正在上线、3 等 Host 上线、4 在线本地、5 在线远程")]
    public byte ControlState => (byte)CurrentControlState;

    /// <summary>
    /// 上一个控制状态（E30 PreviousControlState）
    /// </summary>
    [VariableMark(VariableType.SV, ValueFormat.Int, description: "GEM 上一个控制状态")]
    public byte PreviousControlState => (byte)(_engine?.PreviousControlState ?? GemControlState.EquipmentOffline);

    /// <summary>
    /// 通讯状态：1 没建立、2 建立了
    /// </summary>
    [VariableMark(VariableType.SV, ValueFormat.Int, description: "GEM 通讯状态：1 没建立、2 建立了")]
    public byte CommunicationState => (byte)CommState;

    /// <summary>
    /// 设备时钟（E30 Clock），格式按 TimeFormat
    /// </summary>
    [VariableMark(VariableType.SV, ValueFormat.String, description: "设备时钟（格式按 EC TimeFormat）")]
    public string Clock => GemValue.Time(DateTime.Now, TimeFormat);

    /// <summary>
    /// 开着的事件（E30 EventsEnabled）：CEID 列表
    /// </summary>
    [VariableMark(VariableType.SV, ValueFormat.String, description: "开着的事件（CEID 列表）")]
    public SecsItem EventsEnabled => _engine?.EventsEnabled ?? SecsItem.L();

    /// <summary>
    /// 开着的报警（E30 AlarmsEnabled）：ALID 列表
    /// </summary>
    [VariableMark(VariableType.SV, ValueFormat.String, description: "开着的报警（ALID 列表）")]
    public SecsItem AlarmsEnabled => _engine?.AlarmsEnabled ?? SecsItem.L();

    /// <summary>
    /// 正在报着的报警（E30 AlarmsSet）：ALID 列表
    /// </summary>
    [VariableMark(VariableType.SV, ValueFormat.String, description: "正在报着的报警（ALID 列表）")]
    public SecsItem AlarmsSet => _engine?.AlarmsSet ?? SecsItem.L();

    /// <summary>
    /// 缓存里现在有几条（E30 SpoolCountActual）
    /// </summary>
    [VariableMark(VariableType.SV, ValueFormat.Int, description: "缓存里现在有几条报文")]
    public uint SpoolCountActual => _engine?.SpoolCountActual ?? 0;

    /// <summary>
    /// 这一轮缓存一共进来过几条（E30 SpoolCountTotal）
    /// </summary>
    [VariableMark(VariableType.SV, ValueFormat.Int, description: "这一轮缓存一共进来过几条报文（含满了丢掉的）")]
    public uint SpoolCountTotal => _engine?.SpoolCountTotal ?? 0;

    /// <summary>
    /// 这一轮缓存开始的时刻（E30 SpoolStartTime）
    /// </summary>
    [VariableMark(VariableType.SV, ValueFormat.String, description: "这一轮缓存开始的时刻")]
    public string SpoolStartTime => _engine?.SpoolStartTime ?? string.Empty;

    /// <summary>
    /// 这一轮缓存满了的时刻（E30 SpoolFullTime）
    /// </summary>
    [VariableMark(VariableType.SV, ValueFormat.String, description: "这一轮缓存满了的时刻（没满过为空）")]
    public string SpoolFullTime => _engine?.SpoolFullTime ?? string.Empty;

    #endregion

    #region 事件、DV

    [EventAttribut("控制状态：离线", Description = "进 OFF-LINE（操作员点离线、Host 请求离线）；离线前最后报的一条")]
    public readonly string EquipmentOfflineEvent = E30Codes.EquipmentOffline;

    [EventAttribut("控制状态：在线本地", Description = "进 ON-LINE LOCAL")]
    public readonly string ControlStateLocalEvent = E30Codes.ControlStateLocal;

    [EventAttribut("控制状态：在线远程", Description = "进 ON-LINE REMOTE")]
    public readonly string ControlStateRemoteEvent = E30Codes.ControlStateRemote;

    [EventAttribut("开始缓存", Description = "断了通讯，开始缓存 Host 要缓存的报文（开始时刻看 SV SpoolStartTime）")]
    public readonly string SpoolingActivatedEvent = E30Codes.SpoolingActivated;

    [EventAttribut("缓存清空", Description = "缓存发完或被清掉，不再缓存")]
    public readonly string SpoolingDeactivatedEvent = E30Codes.SpoolingDeactivated;

    [EventAttribut("缓存发送失败", Description = "按 Host 要求发缓存时断了，没发完的留在缓存里")]
    public readonly string SpoolTransmitFailureEvent = E30Codes.SpoolTransmitFailure;

    [EventAttribut("操作员改了设备常量", Description = "操作员在界面上改了 EC（Host 用 S2F15 改的不报）",
        Data = new[] { E30Codes.ChangedEcid, E30Codes.ChangedEcValue })]
    public readonly string OperatorEquipmentConstantChangeEvent = E30Codes.OperatorEquipmentConstantChange;

    [DataVariable(ValueFormat.Int, "操作员改的 EC 的 ECID")]
    public readonly string ChangedEcidData = E30Codes.ChangedEcid;

    [DataVariable(ValueFormat.String, "操作员改的 EC 改成的值")]
    public readonly string ChangedEcValueData = E30Codes.ChangedEcValue;

    #endregion

    #region 状态（给界面、别的组件看）

    /// <summary>
    /// GEM 的协议部分（xyz.Secs）；没接链路为 null
    /// </summary>
    public E30Engine? Engine => _engine;

    /// <summary>
    /// 接到链路上了（EAP 启用了）：Host 的报文有人答、组件报的事件会发
    /// </summary>
    public bool IsAttached => _engine is not null;

    /// <summary>
    /// 当前控制状态（没接链路是设备离线）
    /// </summary>
    public GemControlState CurrentControlState => _engine?.CurrentControlState ?? GemControlState.EquipmentOffline;

    /// <summary>
    /// 当前通讯状态（没接链路是没建立）
    /// </summary>
    public GemCommState CommState => _engine?.CommState ?? GemCommState.NotCommunicating;

    /// <summary>
    /// ON-LINE（LOCAL 或 REMOTE）：事件、报警才往 Host 报
    /// </summary>
    public bool IsOnline => _engine?.IsOnline ?? false;

    /// <summary>
    /// ON-LINE REMOTE：Host 的动作命令（载具动作、建 Job、Job 命令……）才收
    /// </summary>
    public bool IsRemote => _engine?.IsRemote ?? false;

    /// <summary>
    /// 操作员点"上线"（EQUIPMENT OFF-LINE → ATTEMPT ON-LINE，发 S1F1 问 Host）；不在 EQUIPMENT OFF-LINE、没接链路返回 false
    /// </summary>
    public bool RequestOnline()
    {
        return _engine?.RequestOnline() ?? false;
    }

    /// <summary>
    /// 操作员点"离线"；不能离线、没接链路返回 false
    /// </summary>
    public bool RequestOffline()
    {
        return _engine?.RequestOffline() ?? false;
    }

    /// <summary>
    /// 操作员切 REMOTE / LOCAL，只在 ON-LINE 时
    /// </summary>
    public bool RequestRemote(bool remote)
    {
        return _engine?.RequestRemote(remote) ?? false;
    }

    #endregion

    #region 接链路

    /// <summary>
    /// 接到链路上（EAP 组件在链路打开之前调）：建 E30 的协议部分（读回存着的设定、登记各报文的处理方和闸门），
    /// 订报警和 EC 变化，最后按 EC 进开机的控制状态
    /// </summary>
    public void Attach(IHsms link)
    {
        ArgumentNullException.ThrowIfNull(link);
        if (_engine is not null)
        {
            throw new InvalidOperationException($"{Name} 已经接过链路了");
        }

        _collectors = IocHelper.GetService<GemCollectors>();
        _alarms = IocHelper.GetService<IAlarm>();
        _ec = IocHelper.GetService<IEc>();
        var dictionary = new GemDictionary(_collectors, _ec, _alarms);
        _engine = new E30Engine(link.Link, this, dictionary, new GemStore(Database, this), this, new SecsLog(this), Name);
        if (_alarms is not null)
        {
            _alarms.AlarmChanged += OnAlarmChanged;
        }

        if (_ec is not null)
        {
            _ec.ValueChanged += OnConstantChanged;
        }

        _engine.Start();
    }

    /// <summary>
    /// 从链路上摘下来（宿主退出时）：不再订报警和 EC 变化
    /// </summary>
    public void Detach()
    {
        if (_alarms is not null)
        {
            _alarms.AlarmChanged -= OnAlarmChanged;
        }

        if (_ec is not null)
        {
            _ec.ValueChanged -= OnConstantChanged;
        }

        _engine?.Stop();
        _engine = null;
    }

    #endregion

    #region 报事件（S6F11）

    /// <summary>
    /// 收一个事件（任意线程）：code 是 source 组件上 [EventAttribut] 的代码，data 是这个事件带的 DV（[DataVariable] 的代码 → 值）。
    /// 按编号表换成 CEID、DVID，DV 的值按格式落成 SECS 项，交给 E30 排进发送线程。没接链路、事件没编号都不报（离线、被 Host 关了由 E30 挡）
    /// </summary>
    public void Report(ComponentBase source, string code, params GemData[] data)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (_engine is null || _collectors is null)
        {
            return;
        }

        var values = new Dictionary<uint, SecsItem>();
        foreach (var item in data)
        {
            int dvid = _collectors.Dv.DvidOf($"{source.FullPath}.{item.Name}");
            if (dvid == 0)
            {
                LogHelper.Warn(this, $"{source.FullPath} 报事件 {code} 带的 {item.Name} 没有 DV 编号，不带");
                continue;
            }

            values[(uint)dvid] = GemValue.From(item.Value, FormatOf(_collectors, dvid));
        }

        ReportEvent($"{source.FullPath}.{code}", values);
    }

    /// <summary>
    /// E30 自己的事件（控制状态、缓存）也经 RaiseEvent 报：别的收事件的组件（EDA）一样收得到
    /// </summary>
    void IGemEvents.Report(string code, params (string Name, object? Value)[] data)
    {
        RaiseEvent(code, data.Select(item => new GemData(item.Name, item.Value)).ToArray());
    }

    /// <summary>
    /// 按事件全名报（报警事件这种不挂在组件上的）：values 是 DVID → 值
    /// </summary>
    private void ReportEvent(string eventName, IReadOnlyDictionary<uint, SecsItem> values)
    {
        var engine = _engine;
        if (engine is null || _collectors is null)
        {
            return;
        }

        int ceid = _collectors.Event.CeidOf(eventName);
        if (ceid == 0)
        {
            LogHelper.Warn(this, $"事件 {eventName} 没有 CEID（编号表不可用或没声明），不报");
            return;
        }

        engine.ReportEvent((uint)ceid, values);
    }

    /// <summary>
    /// DV 在编号表里的格式；没有这一行给空（按值的类型落格式）
    /// </summary>
    private static string FormatOf(GemCollectors collectors, int dvid)
    {
        var dv = collectors.Dv.ByDvid(dvid);
        if (dv is null)
        {
            return string.Empty;
        }

        return dv.Format;
    }

    #endregion

    #region 报警（S5F1 + 报警事件）

    /// <summary>
    /// 报警报出 / 清除：按编号表找 ALID 交给 E30 发 S5F1（Host 开着这个报警才发），再报这条报警对应的报出 / 清除事件（带报警的几项 DV）。
    /// 没有 ALID 的报警只记日志
    /// </summary>
    private void OnAlarmChanged(AlarmItem alarm)
    {
        try
        {
            var engine = _engine;
            if (engine is null || _collectors is null)
            {
                return;
            }

            string name = $"{alarm.SourcePath}.{alarm.AlarmCode}";
            var definition = _collectors.Alarm.Definitions
                .FirstOrDefault(row => row.Enabled && string.Equals(row.Name, name, StringComparison.OrdinalIgnoreCase));
            if (definition is null)
            {
                LogHelper.Warn(this, $"报警没有 ALID 编号，不报 Host：{name}");
                return;
            }

            engine.ReportAlarm((uint)definition.Id, alarm.IsActive, (byte)alarm.Category, definition.Name);

            var payload = _collectors.Dv.AlarmPayloadDvids;
            var values = new Dictionary<uint, SecsItem>();
            if (payload.Count == DvCollector.AlarmPayload.Count)
            {
                object?[] items =
                [
                    (uint)definition.Id,
                    definition.Name,
                    alarm.SourcePath,
                    alarm.Category.ToString(),
                    alarm.Level.ToString(),
                    alarm.AlarmText,
                ];
                for (int index = 0; index < payload.Count; index++)
                {
                    values[(uint)payload[index]] = GemValue.From(items[index], FormatOf(_collectors, payload[index]));
                }
            }

            ReportEvent(EventCollector.AlarmEventName(definition.Id, clear: !alarm.IsActive), values);
        }
        catch (Exception exception)
        {
            // 报 Host 出错绝不能反过来影响报警系统本身
            LogHelper.Warn(this, $"报警报 Host 失败：{exception.Message}");
        }
    }

    #endregion

    #region 操作员改常量

    /// <summary>
    /// EC 变了：不是 Host 改的就报"操作员改了设备常量"（带 ECID 和新值）
    /// </summary>
    private void OnConstantChanged(string path, EcValueConfig value)
    {
        if (GemDictionary.ApplyingHostConstants)
        {
            return;
        }

        int ecid = _collectors?.Ec.EcidOf(path, value.Name) ?? 0;
        if (ecid == 0)
        {
            return;
        }

        RaiseEvent(E30Codes.OperatorEquipmentConstantChange,
            new GemData(E30Codes.ChangedEcid, (uint)ecid),
            new GemData(E30Codes.ChangedEcValue, GemValue.FromText(value.Value ?? string.Empty, value.Format ?? string.Empty)));
    }

    #endregion
}
