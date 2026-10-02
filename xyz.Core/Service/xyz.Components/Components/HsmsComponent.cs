using System.Globalization;
using System.Net;
using xyz.Common.Log;
using xyz.Components.Alarm;
using xyz.Components.Attributes;
using xyz.Components.Collectors;
using xyz.Components.Enums;
using xyz.Secs;
using xyz.Secs.Diagnostics;
using xyz.Secs.Hsms;
using xyz.Secs.SecsII;

namespace xyz.Components.Components;

/// <summary>
/// EAP 主机链路（sc.xml 的 Hsms 节点）：SECS/GEM over HSMS 的设备端。
/// 传输与编解码在 xyz.Secs 类库（专用线程 + 同步泵），本组件只做三件事：
/// ① 按 sc.xml 建链路（被动监听等 EAP 连入是设备常规，IsActive=True 主动连出）；
/// ② GEM 答话——S1F13 通讯建立、S1F1 在线询问、S1F3 按 SVID 表答状态、S1F17 上线请求、S2F17 时间查询、S2F31 对时；
///    没实现的报文按 E5 回 S9（整个 Stream 没实现回 S9F3，Function 没实现回 S9F5），不让 EAP 干等 T3；
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

    [SCEditor("5000", "Hsms", "HSMS 端口，SEMI 惯例 5000；不能跟 Rpc 节点的 gRPC 端口相同（相同时 EAP 链路不启动、记错误），接 EAP 时把 gRPC 挪走")]
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

        // 配置不对（设备号超出 15 位、超时填了 0 这类）：EAP 链路不起，记错误，不拖垮整个后端。
        if (DeviceId is < 0 or > 0x7FFF)
        {
            LogHelper.Error(Name, $"EAP 链路没起来：DeviceId={DeviceId} 不对，HSMS 设备号只能是 0~32767");
            return;
        }

        var settings = new HsmsSettings
        {
            IsEquipment = true,
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
        try
        {
            settings.Validate();
        }
        catch (ArgumentException exception)
        {
            LogHelper.Error(Name, $"EAP 链路没起来：Hsms 配置不对，{exception.Message}");
            return;
        }

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
            // 跟 gRPC 同端口：宿主先开 EAP 链路、后起 gRPC，监听是独占的，gRPC 就绑不上、整个后端起不来。
            // 只能让一边让路——界面要靠 gRPC，所以 EAP 链路不起，记错误。
            int rpcPort = RpcComponent.Current?.Port ?? RpcComponent.DefaultPort;
            if (Port == rpcPort)
            {
                LogHelper.Error(Name, $"EAP 链路没起来：Hsms 端口 {Port} 跟 Rpc 节点的 gRPC 端口相同，把其中一个挪开");
                return;
            }

            _listener = new HsmsListener(settings, sink);
            _listener.SessionEstablished += Wire;
            try
            {
                _listener.Start();
            }
            catch (HsmsConnectionException exception)
            {
                // 端口被别的程序占着：EAP 链路起不来，但不拖垮整个后端。
                LogHelper.Error(Name, $"EAP 链路没起来：{exception.Message}；换个端口或关掉占着它的程序");
                _listener.Dispose();
                _listener = null;
                return;
            }

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
        try
        {
            Answer(session, message);
        }
        catch (SecsException exception)
        {
            // 报文里的数据不对（类型、取值）：按 E5 回 S9F7 非法数据
            LogHelper.Warn(Name, $"{message.Name} 数据不对，回 S9F7: {exception.Message}");
            session.SendError(message.Header, 7);
        }
        catch (Exception exception)
        {
            // 本端处理出了错：要回复的回 F0 中止事务，别让 EAP 干等 T3
            LogHelper.Error(Name, $"处理 {message.Name} 出错: {exception.Message}");
            if (message.Header.ReplyExpected)
            {
                session.Reply(message, new SecsMessage(message.Header.Stream, 0, false));
            }
        }
    }

    /// <summary>
    /// 按 Stream/Function 答话；对方没要回复（W=0）时 Reply 自动不发。
    /// </summary>
    private void Answer(HsmsSession session, HsmsMessage message)
    {
        switch (message.Header.Stream, message.Header.Function)
        {
            // S1F13 通讯建立 → S1F14：COMMACK=0 + MDLN/SOFTREV
            case (1, 13):
                session.Reply(message, message.CreateReply(SecsItem.L(SecsItem.B(0), Identity())));
                break;

            // S1F1 在线询问（Are You There）→ S1F2：设备回 MDLN/SOFTREV（只有 Host 回的才是空 L）
            case (1, 1):
                session.Reply(message, message.CreateReply(Identity()));
                break;

            // S1F3 状态查询 → S1F4：按 SVID 逐个答（空列表 = 全查），值类型按 SV 声明的格式落
            case (1, 3):
                session.Reply(message, message.CreateReply(AnswerSvQuery(message.Body)));
                break;

            // S1F17 上线请求 → S1F18 ONLACK。框架还没有 GEM 控制状态模型，设备一直算在线：回 2（已经在线）
            case (1, 17):
                session.Reply(message, message.CreateReply(SecsItem.B(2)));
                break;

            // S2F17 时间查询 → S2F18：E5 的 16 位格式 YYYYMMDDhhmmsscc（24 小时制，带厘秒）
            case (2, 17):
                session.Reply(message, message.CreateReply(
                    SecsItem.A(DateTime.Now.ToString("yyyyMMddHHmmssff", CultureInfo.InvariantCulture))));
                break;

            // S2F31 对时 → S2F32 TIACK=0：先只答收下，不动机器时钟——要不要真对时是现场策略，定了一起改
            case (2, 31):
                session.Reply(message, message.CreateReply(SecsItem.B(0)));
                break;

            // S2F41 远程命令 → S2F42 L[2]{HCACK=4（命令不接受）, L[0]}。下一阶段接 TransferManager 派单后按命令名路由
            case (2, 41):
                LogHelper.Warn(Name, $"远程命令暂不支持: {RcmdText(message.Body)}");
                session.Reply(message, message.CreateReply(SecsItem.L(SecsItem.B(4), SecsItem.L())));
                break;

            default:
                // 没实现的报文按 E5 回 S9：整个 Stream 一条都没实现回 S9F3，Stream 用到了但这个 Function 没实现回 S9F5。
                // 不回的话 EAP 要干等 T3 才知道。
                byte error = message.Header.Stream is 1 or 2 or 5 ? (byte)5 : (byte)3;
                LogHelper.Warn(Name, $"未实现的报文 {message.Name}，回 S9F{error}");
                session.SendError(message.Header, error);
                break;
        }
    }

    /// <summary>
    /// 设备身份 L[2]{MDLN, SOFTREV}：S1F2、S1F14 都带它。
    /// </summary>
    private SecsItem Identity()
    {
        return SecsItem.L(SecsItem.A(Ascii(EquipmentModel)), SecsItem.A(Ascii(SoftwareRevision)));
    }

    /// <summary>
    /// 远程命令名（日志用）：RCMD 一般是 ASCII，也可能是整数。
    /// </summary>
    private static string RcmdText(SecsItem? body)
    {
        var rcmd = body is { Format: SecsFormat.List, Count: > 0 } ? body.Items[0] : null;
        return rcmd is null ? "（没带 RCMD）" : rcmd.Format == SecsFormat.Ascii ? rcmd.GetString() : SecsMessageText.Format(rcmd);
    }

    /// <summary>
    /// SECS 的 A 类型只能是 ASCII：中文这类字符换成 ?——编码器碰到非 ASCII 会直接报错，一个字符就能毁掉整条回复。
    /// </summary>
    private static string Ascii(string text)
    {
        return string.Concat(text.Select(ch => ch <= '\x7F' ? ch : '?'));
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
            int svid = ReadSvid(requested);
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
    /// SVID 只认单个非负整数（本机编号都是 U4，哪种整数类型都收）；文字、多个值、负数算数据不对，抛出去由上层回 S9F7。
    /// </summary>
    private static int ReadSvid(SecsItem item)
    {
        long[] ids;
        try
        {
            ids = item.GetInt64Array();
        }
        catch (OverflowException)
        {
            ids = [];
        }

        if (ids.Length != 1 || ids[0] < 0 || ids[0] > int.MaxValue)
        {
            throw new SecsException($"SVID 应是单个非负整数，收到 {SecsMessageText.Format(item)}");
        }

        return (int)ids[0];
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
                return SecsItem.A(string.IsNullOrEmpty(sv.Value) ? " " : Ascii(sv.Value));
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

            // S5F1: L[3]{ALCD(B), ALID(U4), ALTX(A)}。ALCD 最高位 1 = 报出、0 = 清除，低 7 位的报警类别先不细分
            var alcd = alarm.IsActive ? (byte)0x80 : (byte)0;
            session.Send(new SecsMessage(5, 1, true,
                SecsItem.L(SecsItem.B(alcd), SecsItem.U4((uint)alid), SecsItem.A(AlarmText(alarm)))));
        }
        catch (Exception exception)
        {
            // 推送失败绝不能反过来影响报警系统本身
            LogHelper.Warn(Name, $"S5F1 推送失败: {exception.Message}");
        }
    }

    /// <summary>
    /// ALTX：E5 限 40 个字符、只能 ASCII。报警文字是中文发不出去，发"组件全路径.报警代码"（跟编号表里的名字一样），
    /// 超长就只发报警代码。EAP 一般按 ALID 对自己的报警表，这段只是方便人看。
    /// </summary>
    private static string AlarmText(AlarmItem alarm)
    {
        var text = Ascii($"{alarm.SourcePath}.{alarm.AlarmCode}");
        if (text.Length > 40)
        {
            text = Ascii(alarm.AlarmCode);
        }

        return text.Length > 40 ? text[..40] : text;
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
