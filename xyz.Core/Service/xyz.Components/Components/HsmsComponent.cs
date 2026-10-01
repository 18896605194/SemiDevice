using System.Net;
using xyz.Common.Log;
using xyz.Components.Alarm;
using xyz.Components.Attributes;
using xyz.Components.Collectors;
using xyz.Components.Enums;
using xyz.Secs;
using xyz.Secs.Hsms;
using xyz.Secs.SecsII;

namespace xyz.Components.Components;

/// <summary>
/// EAP 主机链路（sc.xml 的 Hsms 节点）：SECS/GEM over HSMS 的设备端。
/// 传输与编解码在 xyz.Secs 类库（专用线程 + 同步泵），本组件只做三件事：
/// ① 按 sc.xml 建链路（被动监听等 EAP 连入是设备常规，IsActive=True 主动连出）；
/// ② GEM 答话——S1F13 通讯建立、S1F1 在线询问、S1F3 按 SVID 表答状态、S1F17 对时；
/// ③ 报警推 S5F1（ALID 查编号表，报出/清除都推）。
/// S2F41 远程命令暂回 HCACK=4（不接受），路由到 TransferManager 派单是下一阶段；
/// S2F33/35/37 动态报告、S6F11 事件上报同属下一阶段。
/// IsEnable=False 时整个节点只记一条日志，不监听端口——没接 EAP 的机器照常跑。
/// </summary>
[Component(description: "EAP 主机链路（SECS/GEM over HSMS）：监听、GEM 答话、报警上报")]
public class HsmsComponent : ComponentBase
{
    /// <summary>
    /// 当前链路组件；sc.xml 没配 Hsms 节点时为 null。退出时宿主经它做 Separate 优雅断开。
    /// </summary>
    public static HsmsComponent? Current { get; set; }

    public HsmsComponent()
    {
        Current = this;
    }

    #region 配置（[SCEditor]，现场可在 EC 页改 sc.xml）

    [SCEditor("False", "Hsms", "是否启用 EAP 链路；没接 EAP 的机器保持 False，不监听端口、不连设备")]
    public bool IsEnable { get; set; }

    [SCEditor("False", "Hsms", "主动连出（设备连 EAP）：False = 被动监听等 EAP 连入（设备常规模式）")]
    public bool IsActive { get; set; }

    [SCEditor("", "Hsms", "对端 EAP 地址（IsActive=True 时用，如 192.168.1.10）；被动模式不用")]
    public string Host { get; set; } = string.Empty;

    [SCEditor("5000", "Hsms", "HSMS 端口，SEMI 惯例 5000；注意与 Rpc 节点的 gRPC 端口不能同为 5000，接 EAP 时把 gRPC 挪走")]
    public int Port { get; set; } = 5000;

    [SCEditor("0", "Hsms", "设备号（DeviceId/SessionId），要和 EAP 侧配置一致，不一致的报文会被丢弃")]
    public int DeviceId { get; set; }

    [SCEditor("45000", "Hsms", "T3：数据事务等回复超时（毫秒）")]
    public int T3ReplyTimeoutMs { get; set; } = 45_000;

    [SCEditor("10000", "Hsms", "T5：断线后重连间隔（毫秒）")]
    public int T5ConnectRetryMs { get; set; } = 10_000;

    [SCEditor("5000", "Hsms", "T6：控制事务（Select/Linktest）等回复超时（毫秒）")]
    public int T6ControlTimeoutMs { get; set; } = 5_000;

    [SCEditor("10000", "Hsms", "T7：TCP 连上后等 Select 的超时（毫秒），超时断开等重连")]
    public int T7NotSelectedTimeoutMs { get; set; } = 10_000;

    [SCEditor("5000", "Hsms", "T8：一帧内部字节间超时（毫秒），防半包挂死")]
    public int T8IntercharacterTimeoutMs { get; set; } = 5_000;

    [SCEditor("30000", "Hsms", "Linktest 心跳周期（毫秒），0 = 不主动发；双方都必应答")]
    public int LinktestIntervalMs { get; set; } = 30_000;

    [SCEditor("True", "Hsms", "报文明文进日志（fab 验收和现场排障全靠它；报文量大时可以关）")]
    public bool LogMessages { get; set; } = true;

    [SCEditor("xyz", "Hsms", "MDLN 设备型号，S1F13/S1F14 通讯建立时报给 EAP")]
    public string EquipmentModel { get; set; } = "xyz";

    [SCEditor("1.0.0", "Hsms", "SOFTREV 软件版本，S1F13/S1F14 通讯建立时报给 EAP")]
    public string SoftwareRevision { get; set; } = "1.0.0";

    #endregion

    /// <summary>链路状态（SV）：EAP 连没连上、过没过 Select，界面和 EAP 都能查。</summary>
    [VariableMark(VariableType.SV, ValueFormat.Enum, description: "EAP 链路状态（NotConnected/ConnectedNotSelected/Selected）")]
    public HsmsLinkState LinkState { get; private set; } = HsmsLinkState.NotConnected;

    private HsmsListener? _listener;
    private HsmsConnector? _connector;

    /// <summary>被动模式实际监听地址（Port 配 0 时系统分配，冒烟测试用）。</summary>
    public IPEndPoint? LocalEndpoint => _listener?.LocalEndpoint;

    #region 生命周期

    /// <summary>
    /// 建链路：组合根在编号表合并之后调（S1F3 要按 SVID 表答话）；报警推送也在这挂上。
    /// </summary>
    public void Open()
    {
        if (!IsEnable)
        {
            LogHelper.Info(Name, "EAP 链路未启用（IsEnable=False），不监听端口");
            return;
        }

        var settings = new HsmsSettings
        {
            IsActive = IsActive,
            Host = string.IsNullOrWhiteSpace(Host) ? "127.0.0.1" : Host,
            Port = Port,
            DeviceId = (ushort)DeviceId,
            T3ReplyTimeoutMs = T3ReplyTimeoutMs,
            T5ConnectRetryMs = T5ConnectRetryMs,
            T6ControlTimeoutMs = T6ControlTimeoutMs,
            T7NotSelectedTimeoutMs = T7NotSelectedTimeoutMs,
            T8IntercharacterTimeoutMs = T8IntercharacterTimeoutMs,
            LinktestIntervalMs = LinktestIntervalMs,
        };
        var sink = new SecsLogSink(Name, LogMessages);

        if (IsActive)
        {
            _connector = new HsmsConnector(settings, sink);
            _connector.SessionEstablished += Wire;
            _connector.Start();
            LogHelper.Info(Name, $"EAP 链路：主动连出 {settings.Host}:{Port}，T5 重连 {T5ConnectRetryMs}ms");
        }
        else
        {
            _listener = new HsmsListener(settings, sink);
            _listener.SessionEstablished += Wire;
            _listener.Start();
            LogHelper.Info(Name, $"EAP 链路：监听 0.0.0.0:{Port} 等 EAP 连入，DeviceId={DeviceId}");
        }

        // 报警推 EAP：ALID 从编号表查，查不到只记日志（不影响报警入库和客户端推送这两条既有链路）
        if (AlarmComponent.Current is { } alarms)
        {
            alarms.AlarmChanged += PushAlarm;
        }
    }

    /// <summary>
    /// 收链路：发 Separate 优雅断开（被动/主动都一样），摘掉报警订阅。
    /// 宿主退出时调（先于断 PLC，跟采样落库同一段退出钩子）。
    /// </summary>
    public void Close()
    {
        _listener?.Dispose();
        _connector?.Dispose();
        _listener = null;
        _connector = null;

        if (AlarmComponent.Current is { } alarms)
        {
            alarms.AlarmChanged -= PushAlarm;
        }

        LinkState = HsmsLinkState.NotConnected;
    }

    /// <summary>
    /// 每条新会话（含断线重连后的）都要重新挂答话回调。链路状态 SV 跟着会话走。
    /// 注意：SessionEstablished 触发时 Selected 事件已经发完（它在监听器的订阅里先跑），
    /// 所以这里除了挂事件还要直接认一次当前状态，别把已 SELECTED 的链路认成没连。
    /// </summary>
    private void Wire(HsmsSession session)
    {
        session.PrimaryReceived += message => OnPrimary(session, message);
        session.Selected += () => LinkState = HsmsLinkState.Selected;
        session.Closed += _ => LinkState = HsmsLinkState.NotConnected;
        if (session.IsSelected)
        {
            LinkState = HsmsLinkState.Selected;
        }
    }

    #endregion

    #region GEM 答话（EAP 问、设备答）

    private void OnPrimary(HsmsSession session, HsmsMessage message)
    {
        switch (message.Name)
        {
            // 通讯建立：回通讯建立码 0 + MDLN/SOFTREV
            case "S1F13 W":
                session.Reply(message, message.CreateReply(
                    SecsItem.L(SecsItem.B(0), SecsItem.L(SecsItem.A(EquipmentModel), SecsItem.A(SoftwareRevision)))));
                break;

            // 在线询问（Are You There）：回 0（在线）
            case "S1F1 W":
                session.Reply(message, message.CreateReply(SecsItem.B(0)));
                break;

            // 状态查询：按 SVID 逐个答（空列表 = 全查），值类型按 SV 声明的格式落
            case "S1F3 W":
                session.Reply(message, message.CreateReply(AnswerSvQuery(message.Body)));
                break;

            // 对时询问：回本机时间，E5 格式 yyMMddHHmmssff（14 位、24 小时制带厘秒；12 小时制要带 A/P 后缀，不用）
            case "S1F17 W":
                session.Reply(message, message.CreateReply(SecsItem.A(DateTime.Now.ToString("yyMMddHHmmssff"))));
                break;

            // 对时设置：先只答"收下"（0），不动机器时钟——要不要真对时是现场策略，定了一起改
            case "S2F31 W":
                session.Reply(message, message.CreateReply(SecsItem.B(0)));
                break;

            // 远程命令：HCACK=4（命令不接受）。下一阶段接 TransferManager 派单后按命令名路由
            case "S2F41 W":
                LogHelper.Warn(Name, $"远程命令暂不支持: {message.Body?.First().GetString()}");
                session.Reply(message, message.CreateReply(SecsItem.L(SecsItem.B(4))));
                break;

            default:
                // 没实现的报文不硬答：乱答比超时更难排障。W=1 的对端会 T3 超时，这里记警告留痕
                if (message.Header.ReplyExpected)
                {
                    LogHelper.Warn(Name, $"未实现的报文: {message.Name}");
                }
                break;
        }
    }

    /// <summary>
    /// S1F3 的应答体：按请求里的 SVID 顺序取值；查不到的号回空 ASCII 占位（保持与请求对齐）并记警告。
    /// </summary>
    private SecsItem AnswerSvQuery(SecsItem? body)
    {
        var collector = GemCollectors.Current;
        if (collector is null)
        {
            LogHelper.Warn(Name, "编号表还没合并（GemCollectors.Current 为空），S1F3 只能回空");
            return SecsItem.L();
        }

        // 空 body 或空列表 = 全查（E5 语义）
        if (body is null || body.Items.Count == 0)
        {
            return SecsItem.L(collector.Sv.Collect().Where(sv => sv.Visible).Select(SvToItem));
        }

        var values = new List<SecsItem>();
        foreach (var requested in body.Items)
        {
            int svid = (int)requested.GetUInt64();
            var sv = collector.Sv.BySvid(svid);
            if (sv is null)
            {
                LogHelper.Warn(Name, $"S1F3 问了不认识的 SVID {svid}，回空占位");
                values.Add(SecsItem.A(string.Empty));
            }
            else if (!sv.Visible)
            {
                // 现场在编号表里标了不上传的，同样回空占位
                values.Add(SecsItem.A(string.Empty));
            }
            else
            {
                values.Add(SvToItem(sv));
            }
        }

        return SecsItem.L(values);
    }

    /// <summary>
    /// SV 的字符串值按声明的格式落回 SECS 类型：Bool→Boolean、Int→U4/U8/I8（按正负和宽度）、
    /// Double→F8、String/Enum→A。解析失败退 A（原样字符串），别让一条脏值毁掉整个 S1F4。
    /// </summary>
    private static SecsItem SvToItem(CollectedSv sv)
    {
        switch (sv.Format)
        {
            case "Bool":
                return SecsItem.Boolean(bool.TryParse(sv.Value, out var flag) && flag);

            case "Int":
                if (!long.TryParse(sv.Value, out var number))
                {
                    return SecsItem.A(sv.Value);
                }

                return number >= 0
                    ? number <= uint.MaxValue ? SecsItem.U4(unchecked((uint)number)) : SecsItem.U8(unchecked((ulong)number))
                    : SecsItem.I8(number);

            case "Double":
                return double.TryParse(sv.Value, System.Globalization.CultureInfo.InvariantCulture, out var real)
                    ? SecsItem.F8(real)
                    : SecsItem.A(sv.Value);

            default:
                return SecsItem.A(string.IsNullOrEmpty(sv.Value) ? " " : sv.Value);
        }
    }

    #endregion

    #region 报警推送（S5F1，设备→EAP）

    private void PushAlarm(AlarmItem alarm)
    {
        try
        {
            var session = _listener?.Current ?? _connector?.Current;
            if (session is not { IsSelected: true })
            {
                return;  // EAP 不在线就不推（断线缓存 Spooling 是以后的事）
            }

            var alid = FindAlid(alarm);
            if (alid is null)
            {
                LogHelper.Warn(Name, $"报警没有 ALID 编号，不推 EAP: {alarm.SourcePath}.{alarm.AlarmCode}");
                return;
            }

            // S5F1: L[ALID(U4), ALCD(B), ALED(B)]。ALCD 用 0x80（设备级软报警）占位，清除时报 0
            var alcd = alarm.IsActive ? (byte)0x80 : (byte)0;
            session.Send(new SecsMessage(5, 1, true,
                SecsItem.L(SecsItem.U4((uint)alid), SecsItem.B(alcd), SecsItem.B(1))));
        }
        catch (Exception exception)
        {
            // 推送失败绝不能反过来影响报警系统本身
            LogHelper.Warn(Name, $"S5F1 推送失败: {exception.Message}");
        }
    }

    /// <summary>报警实例 → ALID：按编号表里"组件全路径.报警代码"的全名查。</summary>
    private int? FindAlid(AlarmItem alarm)
    {
        var name = $"{alarm.SourcePath}.{alarm.AlarmCode}";
        return GemCollectors.Current?.Alarm.Definitions
            .FirstOrDefault(row => row.Enabled && string.Equals(row.Name, name, StringComparison.OrdinalIgnoreCase))
            ?.Id;
    }

    #endregion
}
