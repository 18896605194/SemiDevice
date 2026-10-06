using xyz.Common.Log;
using xyz.Components.Enums;
using xyz.Components.Models;
using xyz.Secs;
using xyz.Secs.Hsms;
using xyz.Secs.SecsII;

namespace xyz.Components.Components;

/// <summary>
/// E30 的两个状态机：控制状态（离线 / 在线、本地 / 远程，转换号照 E30）和通讯状态（S1F13 建立通讯），
/// 以及跟它们有关的报文：S1F1 在线询问、S1F13 建立通讯、S1F15 请求离线、S1F17 请求上线；Host 报文的闸门也在这里。
/// </summary>
public partial class E30Component
{
    /// <summary>S1F14 COMMACK：收下。</summary>
    private const byte CommunicationAccepted = 0;

    /// <summary>S1F16 OFLACK：收下。</summary>
    private const byte OfflineAccepted = 0;

    /// <summary>S1F18 ONLACK：收下。</summary>
    private const byte OnlineAccepted = 0;

    /// <summary>S1F18 ONLACK：不让上线（设备离线 / 正在问 Host）。</summary>
    private const byte OnlineNotAllowed = 1;

    /// <summary>S1F18 ONLACK：已经在线。</summary>
    private const byte AlreadyOnline = 2;

    /// <summary>MDLN、SOFTREV 最长 20 个字符（E5）。</summary>
    private const int MaxIdentityLength = 20;

    private readonly object _stateGate = new();
    private readonly object _transitionGate = new();
    private GemControlState _control = GemControlState.EquipmentOffline;
    private GemControlState _previous = GemControlState.EquipmentOffline;
    private GemCommState _comm = GemCommState.NotCommunicating;
    private CancellationTokenSource? _establishing;

    /// <summary>当前控制状态。</summary>
    public GemControlState CurrentControlState
    {
        get
        {
            lock (_stateGate)
            {
                return _control;
            }
        }
    }

    /// <summary>当前通讯状态。</summary>
    public GemCommState CommState
    {
        get
        {
            lock (_stateGate)
            {
                return _comm;
            }
        }
    }

    /// <summary>ON-LINE（LOCAL 或 REMOTE）：事件、报警才往 Host 报。</summary>
    public bool IsOnline => IsOnlineState(CurrentControlState);

    /// <summary>ON-LINE REMOTE：Host 的动作命令（载具动作、建 Job、Job 命令……）才收。</summary>
    public bool IsRemote => CurrentControlState == GemControlState.OnlineRemote;

    private static bool IsOnlineState(GemControlState state)
    {
        return state is GemControlState.OnlineLocal or GemControlState.OnlineRemote;
    }

    private static bool IsOffline(GemControlState state)
    {
        return state is GemControlState.EquipmentOffline or GemControlState.AttemptOnline or GemControlState.HostOffline;
    }

    private GemControlState OnlineSubState => OnlineRemote ? GemControlState.OnlineRemote : GemControlState.OnlineLocal;

    #region 控制状态

    /// <summary>
    /// 操作员点"上线"：EQUIPMENT OFF-LINE → ATTEMPT ON-LINE（#3），发 S1F1 问 Host；Host 回了 S1F2 进 ON-LINE（#5），
    /// 没回、回 S1F0 退到 EC OnlineFailedState（#4）。不在 EQUIPMENT OFF-LINE 时返回 false。
    /// </summary>
    public bool RequestOnline()
    {
        if (!Transit(state => state == GemControlState.EquipmentOffline, GemControlState.AttemptOnline, "#3 操作员上线"))
        {
            return false;
        }

        _ = AttemptOnlineAsync();
        return true;
    }

    /// <summary>
    /// 操作员点"离线"：ON-LINE → EQUIPMENT OFF-LINE（#6）、HOST OFF-LINE → EQUIPMENT OFF-LINE（#12）。离线前先报"离线"事件。
    /// </summary>
    public bool RequestOffline()
    {
        return Transit(state => IsOnlineState(state) || state == GemControlState.HostOffline, GemControlState.EquipmentOffline,
            "#6 / #12 操作员离线");
    }

    /// <summary>操作员切 REMOTE（#8）/ LOCAL（#9），只在 ON-LINE 时。</summary>
    public bool RequestRemote(bool remote)
    {
        return Transit(IsOnlineState, remote ? GemControlState.OnlineRemote : GemControlState.OnlineLocal,
            remote ? "#8 LOCAL → REMOTE" : "#9 REMOTE → LOCAL");
    }

    /// <summary>
    /// 开机进初始状态（#1）：EC InitialOnline 为 True 进 ON-LINE（#7，子状态看 OnlineRemote），否则进 OFF-LINE（#2，子状态看 OfflineSubState；
    /// 是 ATTEMPT ON-LINE 的马上问 Host——开机时链路还没连上，问不成就退到 OnlineFailedState）。
    /// </summary>
    private void EnterInitialState()
    {
        if (InitialOnline)
        {
            Transit(_ => true, OnlineSubState, "#1 / #7 开机进 ON-LINE");
            return;
        }

        var offline = OfflineSubState;
        lock (_stateGate)
        {
            _previous = _control;
            _control = offline;
        }

        LogHelper.Info(Name, $"GEM 控制状态 #1 / #2 开机进 OFF-LINE：{offline}");
        if (offline == GemControlState.AttemptOnline)
        {
            _ = AttemptOnlineAsync();
        }
    }

    /// <summary>ATTEMPT ON-LINE：发 S1F1 问 Host，回 S1F2 就进 ON-LINE（#5），别的（S1F0、超时、没连上）退到 OnlineFailedState（#4）。</summary>
    private async Task AttemptOnlineAsync()
    {
        bool accepted = false;
        try
        {
            var link = _link;
            if (link is not null && CommState == GemCommState.Communicating)
            {
                var reply = await link.SendAsync(new SecsMessage(1, 1, true)).ConfigureAwait(false);
                accepted = reply.Header.Function == 2;
            }
            else
            {
                LogHelper.Warn(Name, "上线没成：跟 Host 的通讯还没建立");
            }
        }
        catch (SecsException exception)
        {
            LogHelper.Warn(Name, $"上线没成：Host 没答应 S1F1（{exception.Message}）");
        }

        if (accepted)
        {
            Transit(state => state == GemControlState.AttemptOnline, OnlineSubState, "#5 Host 答应上线");
        }
        else
        {
            Transit(state => state == GemControlState.AttemptOnline, OnlineFailedState, "#4 上线没成");
        }
    }

    /// <summary>
    /// 转控制状态并报事件：离开 ON-LINE 先报"离线"事件再转（离线以后就不报了），进 ON-LINE、切 LOCAL / REMOTE 先转再报。
    /// allowed 判当前状态能不能这么转（操作员和 Host 同时动时，谁先到算谁的）；转了返回 true。
    /// </summary>
    private bool Transit(Func<GemControlState, bool> allowed, GemControlState to, string transition)
    {
        lock (_transitionGate)
        {
            var from = CurrentControlState;
            if (!allowed(from) || from == to)
            {
                return false;
            }

            if (IsOnlineState(from) && !IsOnlineState(to))
            {
                Report(this, EquipmentOfflineEvent);
            }

            lock (_stateGate)
            {
                _previous = from;
                _control = to;
            }

            LogHelper.Info(Name, $"GEM 控制状态 {transition}：{from} → {to}");
            if (to == GemControlState.OnlineLocal && from != GemControlState.OnlineLocal)
            {
                Report(this, ControlStateLocalEvent);
            }
            else if (to == GemControlState.OnlineRemote && from != GemControlState.OnlineRemote)
            {
                Report(this, ControlStateRemoteEvent);
            }

            return true;
        }
    }

    #endregion

    #region 通讯状态

    /// <summary>链路连上（派发线程上）：还没建立通讯，设备开始发 S1F13。</summary>
    private void OnLinkSelected()
    {
        lock (_stateGate)
        {
            _comm = GemCommState.NotCommunicating;
        }

        StartEstablishing();
    }

    private void OnLinkClosed(string reason)
    {
        CommunicationFailed($"链路断了：{reason}");
    }

    /// <summary>
    /// 通讯断了（链路断、设备发的报文等不到回复）：回到没建立。原来是建立着的，又开了缓存、Host 指定过缓存范围，就开缓存并报"开始缓存"
    /// （这条事件自己就进缓存，是缓存里的第一条）。链路还连着（等回复超时这种）就接着发 S1F13 重新建立。
    /// </summary>
    private void CommunicationFailed(string reason)
    {
        bool wasCommunicating;
        CancellationTokenSource? establishing;
        lock (_stateGate)
        {
            wasCommunicating = _comm == GemCommState.Communicating;
            _comm = GemCommState.NotCommunicating;
            establishing = _establishing;
            _establishing = null;
        }

        establishing?.Cancel();
        if (wasCommunicating)
        {
            LogHelper.Warn(Name, $"跟 Host 的通讯断了：{reason}");
            var spool = _spool;
            if (spool is not null && EnableSpooling && _book.HasSpoolStreams && spool.Activate())
            {
                LogHelper.Warn(Name, "开始缓存 Host 要缓存的报文");
                Report(this, SpoolingActivatedEvent);
            }
        }

        var link = _link;
        if (link is not null && link.IsSelected)
        {
            StartEstablishing();
        }
    }

    private void StartEstablishing()
    {
        var establishing = new CancellationTokenSource();
        CancellationTokenSource? previous;
        lock (_stateGate)
        {
            previous = _establishing;
            _establishing = establishing;
        }

        previous?.Cancel();
        _ = EstablishAsync(establishing.Token);
    }

    /// <summary>
    /// 设备发 S1F13 建立通讯：Host 回 COMMACK=0 就算建立了；没回、不答应，隔 EC EstablishCommunicationsTimeout 秒再发。
    /// Host 先发了 S1F13（设备回完也算建立了）或者链路断了就停。
    /// </summary>
    private async Task EstablishAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && CommState == GemCommState.NotCommunicating)
        {
            var link = _link;
            if (link is null || !link.IsSelected)
            {
                return;
            }

            try
            {
                var reply = await link.SendAsync(new SecsMessage(1, 13, true, Identity())).ConfigureAwait(false);
                if (IsCommunicationAccepted(reply.Body))
                {
                    if (!token.IsCancellationRequested)
                    {
                        SetCommunicating();
                    }

                    return;
                }

                LogHelper.Warn(Name, "Host 没答应建立通讯（COMMACK 不是 0），过一会儿再发 S1F13");
            }
            catch (SecsException exception)
            {
                LogHelper.Warn(Name, $"S1F13 没回成：{exception.Message}");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, EstablishCommunicationsTimeout)), token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>S1F14 的 COMMACK 是不是 0：L[2]{COMMACK, L[...]}。</summary>
    private static bool IsCommunicationAccepted(SecsItem? body)
    {
        if (body is null || body.Format != SecsFormat.List || body.Count < 1)
        {
            return false;
        }

        var ack = body.Items[0];
        if (ack.Format != SecsFormat.Binary)
        {
            return false;
        }

        var bytes = ack.GetBinary();
        return bytes.Length == 1 && bytes[0] == CommunicationAccepted;
    }

    private void SetCommunicating()
    {
        CancellationTokenSource? establishing;
        lock (_stateGate)
        {
            if (_comm == GemCommState.Communicating)
            {
                return;
            }

            _comm = GemCommState.Communicating;
            establishing = _establishing;
            _establishing = null;
        }

        establishing?.Cancel();
        LogHelper.Info(Name, "跟 Host 的通讯建立了");
    }

    #endregion

    #region 闸门和 S1 的状态报文

    /// <summary>
    /// Host 报文的闸门：S1F13 什么时候都收；通讯没建立时别的都不理（E30）；
    /// 离线时只收 S1F17（请求上线），别的回 SxF0；在线时都放给处理方。
    /// </summary>
    private SecsReply? Admit(HsmsMessage message)
    {
        byte stream = message.Header.Stream;
        byte function = message.Header.Function;
        if (stream == 1 && function == 13)
        {
            return null;
        }

        if (CommState != GemCommState.Communicating)
        {
            LogHelper.Warn(Name, $"跟 Host 的通讯还没建立（还没换过 S1F13），{message.Name} 不理");
            return SecsReply.None;
        }

        if (IsOnline || (stream == 1 && function == 17))
        {
            return null;
        }

        LogHelper.Warn(Name, $"离线中（{CurrentControlState}），{message.Name} 回 S{stream}F0");
        return SecsReply.Abort;
    }

    /// <summary>设备身份 L[2]{MDLN, SOFTREV}：S1F2、S1F13、S1F14 都带它。</summary>
    private SecsItem Identity()
    {
        return SecsItem.L(SecsItem.A(Limit(EquipmentModel)), SecsItem.A(Limit(SoftwareRevision)));
    }

    private static string Limit(string text)
    {
        string ascii = GemValue.Ascii(text);
        return ascii.Length > MaxIdentityLength ? ascii[..MaxIdentityLength] : ascii;
    }

    /// <summary>S1F1 在线询问 → S1F2 L[2]{MDLN, SOFTREV}。</summary>
    private SecsReply AreYouThere(HsmsMessage message)
    {
        return SecsReply.Of(Identity());
    }

    /// <summary>S1F13 建立通讯 → S1F14 L[2]{COMMACK=0, L[2]{MDLN, SOFTREV}}；回完算建立了通讯。</summary>
    private SecsReply EstablishCommunications(HsmsMessage message)
    {
        return SecsReply.Of(SecsItem.L(SecsItem.B(CommunicationAccepted), Identity())).Then(SetCommunicating);
    }

    /// <summary>S1F15 请求离线（闸门只在 ON-LINE 时放进来）→ S1F16 OFLACK=0；回完报"离线"事件、进 HOST OFF-LINE（#10）。</summary>
    private SecsReply HostRequestOffline(HsmsMessage message)
    {
        return SecsReply.Of(SecsItem.B(OfflineAccepted))
            .Then(() => Transit(IsOnlineState, GemControlState.HostOffline, "#10 Host 请求离线"));
    }

    /// <summary>
    /// S1F17 请求上线 → S1F18 ONLACK：已经在线回 2；HOST OFF-LINE 回 0，回完进 ON-LINE（#11）；设备离线、正在问 Host 回 1。
    /// </summary>
    private SecsReply HostRequestOnline(HsmsMessage message)
    {
        var state = CurrentControlState;
        if (IsOnlineState(state))
        {
            return SecsReply.Of(SecsItem.B(AlreadyOnline));
        }

        if (state != GemControlState.HostOffline)
        {
            return SecsReply.Of(SecsItem.B(OnlineNotAllowed));
        }

        return SecsReply.Of(SecsItem.B(OnlineAccepted))
            .Then(() => Transit(current => current == GemControlState.HostOffline, OnlineSubState, "#11 Host 请求上线"));
    }

    #endregion
}
