using System.Net;
using System.Threading.Channels;
using xyz.Common.Log;
using xyz.Components.Attributes;
using xyz.Components.Enums;
using xyz.Components.Models;
using xyz.Secs;
using xyz.Secs.Diagnostics;
using xyz.Secs.Hsms;
using xyz.Secs.SecsII;

namespace xyz.Components.Components;

/// <summary>
/// EAP 主机链路（SEMI E37 HSMS，sc.xml 的 Eap 下的 Hsms 节点）：只管链路和报文分发，不懂 GEM。
/// 传输与编解码在 xyz.Secs 类库（专用线程 + 同步泵），本组件做三件事：
/// ① 按 sc.xml 建链路（被动监听等 EAP 连入是设备常规，IsActive=True 主动连出）；
/// ② Host 发来的 primary 按 Stream/Function 交给登记的处理方（E30、E87……各标准组件开机时登记），
///    在一条专用派发线程上按收到的先后一条一条处理——不占收包线程，处理方可以等设备侧的命令结果；处理完照结果回。
///    处理前先过闸门（E30 的通讯、控制状态）：还没建立通讯时不理、离线时不该收的回 SxF0。
///    没人登记的按 E5 回 S9：整个 Stream 都没人管回 S9F3，Stream 有人管但这个 Function 没有回 S9F5，不让 EAP 干等 T3；
/// ③ 给各标准组件发设备主动报的 primary（S6F11、S5F1……）；链路连上、断开也走同一条派发线程按先后通知它们。
/// 本节点 IsEnable=False 时整个 EAP 不起（不监听端口），设备照常跑。
/// </summary>
[Component(description: "EAP 主机链路（SEMI E37 HSMS）：监听或连出、按 Stream/Function 分发报文")]
public class HsmsComponent : ComponentBase
{
    /// <summary>S9F3：整个 Stream 不认识。</summary>
    private const byte UnknownStreamFunction = 3;

    /// <summary>S9F5：Stream 认识、Function 不认识。</summary>
    private const byte UnknownFunctionFunction = 5;

    /// <summary>
    /// 当前链路组件；sc.xml 没配 Hsms 节点时为 null。退出时宿主经它做 Separate 优雅断开。
    /// </summary>
    public static HsmsComponent? Current { get; set; }

    public HsmsComponent()
    {
        Current = this;
    }

    #region 配置（[SCEditor]，现场可在 EC 页改 sc.xml）

    [SCEditor("False", "Hsms", "是否启用 EAP；没接 EAP 的机器保持 False，不监听端口、不连设备，各标准组件也不接到设备上")]
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

    #endregion

    /// <summary>链路状态（SV）：EAP 连没连上、过没过 Select，界面和 EAP 都能查。</summary>
    [VariableMark(VariableType.SV, ValueFormat.Enum, description: "EAP 链路状态（NotConnected/ConnectedNotSelected/Selected）")]
    public HsmsLinkState LinkState { get; private set; } = HsmsLinkState.NotConnected;

    private readonly object _handlerGate = new();
    private readonly Dictionary<(byte Stream, byte Function), Func<HsmsMessage, Task<SecsReply>>> _handlers = new();
    private readonly HashSet<byte> _streams = new();
    private HsmsListener? _listener;
    private HsmsConnector? _connector;
    private Channel<LinkWork>? _work;

    /// <summary>被动模式实际监听地址（Port 配 0 时系统分配，冒烟测试用）。</summary>
    public IPEndPoint? LocalEndpoint => _listener?.LocalEndpoint;

    /// <summary>链路现在能不能发报文（过了 Select）。</summary>
    public bool IsSelected
    {
        get
        {
            var session = CurrentSession;
            return session is not null && session.IsSelected;
        }
    }

    private HsmsSession? CurrentSession => _listener?.Current ?? _connector?.Current;

    #region 登记（各标准组件开机时调，链路打开之前）

    /// <summary>
    /// 闸门：每条 Host 的 primary 先问它，返回不为 null 就照它回、不再交给处理方（E30 按通讯、控制状态挡）。
    /// 在派发线程上调。
    /// </summary>
    public Func<HsmsMessage, SecsReply?>? Gate { get; set; }

    /// <summary>链路进了 SELECTED（在派发线程上通知，跟收到的报文排在同一条线上，先后不乱）。</summary>
    public event Action? LinkSelected;

    /// <summary>链路断了，带原因（同样在派发线程上通知）。</summary>
    public event Action<string>? LinkClosed;

    /// <summary>
    /// 登记一个 Stream/Function 的处理方：在派发线程上调，可以 await 设备侧的命令结果，返回怎么回。
    /// 同一个 Stream/Function 登记两次是配置错了，开机就抛。
    /// </summary>
    public void Handle(byte stream, byte function, Func<HsmsMessage, Task<SecsReply>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_handlerGate)
        {
            if (!_handlers.TryAdd((stream, function), handler))
            {
                throw new InvalidOperationException($"S{stream}F{function} 登记了两个处理方");
            }

            _streams.Add(stream);
        }
    }

    /// <summary>登记一个不用等待的处理方（当场就能算出回复的）。</summary>
    public void Handle(byte stream, byte function, Func<HsmsMessage, SecsReply> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        Handle(stream, function, message => Task.FromResult(handler(message)));
    }

    #endregion

    #region 生命周期

    /// <summary>
    /// 建链路：EAP 组件把各标准都接好、登记完之后调（Host 一连进来就可能发报文，处理方得先在）。
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

        StartWorker();
        var sink = new SecsLogSink(Name, LogMessages);

        if (IsActive)
        {
            _connector = new HsmsConnector(settings, sink);
            _connector.SessionEstablished += Wire;
            _connector.Start();
            LogHelper.Info(Name, $"EAP 链路：主动连出 {settings.Host}:{Port}，T5 重连 {T5ConnectRetryMs}ms");
            return;
        }

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

    /// <summary>
    /// 收链路：发 Separate 优雅断开（被动/主动都一样），停派发线程。宿主退出时调。
    /// </summary>
    public void Close()
    {
        _listener?.Dispose();
        _connector?.Dispose();
        _listener = null;
        _connector = null;
        LinkState = HsmsLinkState.NotConnected;
        _work?.Writer.TryComplete();
        _work = null;
    }

    /// <summary>
    /// 每条新会话（含断线重连后的）都要重新挂回调。SessionEstablished 是在 Select 成功的那一刻触发的，
    /// 这时会话已经是 SELECTED，所以直接认一次连上；断开先挂上再查一次，免得挂之前就断了的会话漏报。
    /// </summary>
    private void Wire(HsmsSession session)
    {
        int closedPosted = 0;
        void PostClosed(string reason)
        {
            if (Interlocked.Exchange(ref closedPosted, 1) == 0)
            {
                LinkState = HsmsLinkState.NotConnected;
                Post(new ClosedWork(reason));
            }
        }

        session.PrimaryReceived += message => Post(new IncomingWork(session, message));
        session.Closed += PostClosed;
        LinkState = HsmsLinkState.Selected;
        Post(new SelectedWork());
        if (session.State == HsmsLinkState.NotConnected)
        {
            PostClosed("会话已断开");
        }
    }

    #endregion

    #region 发送（设备主动报）

    /// <summary>
    /// 发一条 primary 并等回复（W=1）：没连上抛 HsmsConnectionException，T3 到期抛 SecsTimeoutException，
    /// 对方回 S9 / SxF0 抛 SecsException。别在派发线程的处理方里同步等它。
    /// </summary>
    public Task<HsmsMessage> SendAsync(SecsMessage message)
    {
        var session = CurrentSession;
        if (session is null || !session.IsSelected)
        {
            throw new HsmsConnectionException($"EAP 链路没连上，{message.Name} 发不出去");
        }

        return session.SendAsync(message);
    }

    #endregion

    #region 派发

    private void StartWorker()
    {
        if (_work is not null)
        {
            return;
        }

        var work = Channel.CreateUnbounded<LinkWork>(new UnboundedChannelOptions { SingleReader = true });
        _work = work;
        _ = Task.Run(() => PumpAsync(work.Reader));
    }

    private void Post(LinkWork work)
    {
        _work?.Writer.TryWrite(work);
    }

    /// <summary>
    /// 派发线程：收到的报文、连上、断开都按先后一条一条处理；一条出错只记日志，不连累后面的。
    /// </summary>
    private async Task PumpAsync(ChannelReader<LinkWork> reader)
    {
        await foreach (var work in reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                switch (work)
                {
                    case IncomingWork incoming:
                        await DispatchAsync(incoming.Session, incoming.Message).ConfigureAwait(false);
                        break;

                    case SelectedWork:
                        LinkSelected?.Invoke();
                        break;

                    case ClosedWork closed:
                        LinkClosed?.Invoke(closed.Reason);
                        break;
                }
            }
            catch (Exception exception)
            {
                LogHelper.Error(Name, $"EAP 派发出错：{exception.Message}");
            }
        }
    }

    /// <summary>
    /// 处理一条 Host 的 primary：先过闸门，再找处理方；处理方说数据不对（SecsException）回 S9F7，
    /// 自己出了错回 SxF0（对方要回复的话），别让 EAP 干等 T3。
    /// </summary>
    private async Task DispatchAsync(HsmsSession session, HsmsMessage message)
    {
        SecsReply reply;
        try
        {
            reply = Gate?.Invoke(message) ?? await HandleAsync(message).ConfigureAwait(false);
        }
        catch (SecsException exception) when (exception is not HsmsConnectionException && exception is not SecsTimeoutException)
        {
            LogHelper.Warn(Name, $"{message.Name} 数据不对，回 S9F7：{exception.Message}");
            reply = SecsReply.IllegalData;
        }
        catch (Exception exception)
        {
            LogHelper.Error(Name, $"处理 {message.Name} 出错：{exception.Message}");
            reply = SecsReply.Abort;
        }

        Respond(session, message, reply);
        if (reply.AfterReply is not null)
        {
            try
            {
                reply.AfterReply();
            }
            catch (Exception exception)
            {
                LogHelper.Error(Name, $"回完 {message.Name} 后续处理出错：{exception.Message}");
            }
        }
    }

    private Task<SecsReply> HandleAsync(HsmsMessage message)
    {
        Func<HsmsMessage, Task<SecsReply>>? handler;
        bool streamKnown;
        lock (_handlerGate)
        {
            _handlers.TryGetValue((message.Header.Stream, message.Header.Function), out handler);
            streamKnown = _streams.Contains(message.Header.Stream);
        }

        if (handler is not null)
        {
            return handler(message);
        }

        byte error = streamKnown ? UnknownFunctionFunction : UnknownStreamFunction;
        LogHelper.Warn(Name, $"未实现的报文 {message.Name}，回 S9F{error}");
        return Task.FromResult(SecsReply.Error(error));
    }

    /// <summary>照结果回；对方没要回复（W=0）时正常回复和 SxF0 都不发（会话自己判），S9 照发。会话断了只记日志。</summary>
    private void Respond(HsmsSession session, HsmsMessage message, SecsReply reply)
    {
        try
        {
            switch (reply.Kind)
            {
                case SecsReplyKind.Normal:
                    session.Reply(message, message.CreateReply(reply.Body));
                    break;

                case SecsReplyKind.Abort:
                    session.Reply(message, new SecsMessage(message.Header.Stream, 0, false));
                    break;

                case SecsReplyKind.Error:
                    session.SendError(message.Header, reply.ErrorFunction);
                    break;
            }
        }
        catch (SecsException exception)
        {
            LogHelper.Warn(Name, $"{message.Name} 的回复没发出去：{exception.Message}");
        }
    }

    /// <summary>派发线程上排队的一件事。</summary>
    private abstract class LinkWork
    {
    }

    /// <summary>Host 发来的一条 primary。</summary>
    private sealed class IncomingWork : LinkWork
    {
        public IncomingWork(HsmsSession session, HsmsMessage message)
        {
            Session = session;
            Message = message;
        }

        public HsmsSession Session { get; }

        public HsmsMessage Message { get; }
    }

    /// <summary>链路进了 SELECTED。</summary>
    private sealed class SelectedWork : LinkWork
    {
    }

    /// <summary>链路断了。</summary>
    private sealed class ClosedWork : LinkWork
    {
        public ClosedWork(string reason)
        {
            Reason = reason;
        }

        public string Reason { get; }
    }

    #endregion

    #region 日志转接

    /// <summary>
    /// xyz.Secs 的日志出口接到框架日志：链路状态按级别记，报文明文（LogMessages=True 时）记一行 Info。
    /// </summary>
    private sealed class SecsLogSink : ISecsSink
    {
        private readonly string _category;
        private readonly bool _traceEnabled;

        public SecsLogSink(string category, bool traceEnabled)
        {
            _category = category;
            _traceEnabled = traceEnabled;
        }

        public void Info(string category, string message) => LogHelper.Info(_category, message);

        public void Warn(string category, string message) => LogHelper.Warn(_category, message);

        public void Error(string category, string message) => LogHelper.Error(_category, message);

        public void Trace(SecsMessageDirection direction, HsmsMessage message)
        {
            if (!_traceEnabled)
            {
                return;
            }

            var arrow = direction == SecsMessageDirection.Sent ? ">>" : "<<";
            LogHelper.Info(_category, $"{arrow} {SecsMessageText.Format(message).Replace("\r", " ").Replace("\n", " | ")}");
        }
    }

    #endregion
}
