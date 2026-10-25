using System.Net;
using xyz.Common.Log;
using xyz.Components.Attributes;
using xyz.Components.Enums;
using xyz.Components.Interfaces;
using xyz.Secs;
using xyz.Secs.Gem;
using xyz.Secs.Hsms;
using xyz.Tools;

namespace xyz.Components.Components;

/// <summary>
/// EAP 主机链路（SEMI E37 HSMS，machine.xml 的 Eap 下的 Hsms 节点）：链路本身（登记处理方、派发、闸门、收发）在 xyz.Secs 的 HsmsLink，
/// 这里给它 machine.xml 的设定、查配置（设备号、跟 gRPC 端口撞没撞）、日志转到框架、起停。
/// 本节点 IsEnable=False 时整个 EAP 不起（不监听端口），设备照常跑
/// </summary>
[Component(description: "EAP 主机链路（SEMI E37 HSMS）：监听或连出、按 Stream/Function 分发报文")]
public class HsmsComponent : ComponentBase, IHsms
{
    private HsmsLink? _link;

    #region 配置（[Machine]，现场可在 EC 页改 machine.xml）

    [Machine("False", "Hsms", "是否启用 EAP；没接 EAP 的机器保持 False，不监听端口、不连设备，各标准组件也不接到设备上")]
    public bool IsEnable { get; set; }

    [Machine("False", "Hsms", "主动连出（设备连 EAP）：False = 被动监听等 EAP 连入（设备常规模式）")]
    public bool IsActive { get; set; }

    [Machine("", "Hsms", "对端 EAP 地址（IsActive=True 时用，如 192.168.1.10）；被动模式不用")]
    public string Host { get; set; } = string.Empty;

    [Machine("5000", "Hsms", "HSMS 端口，SEMI 惯例 5000；不能跟 Rpc 节点的 gRPC 端口相同（相同时 EAP 链路不启动、记错误），接 EAP 时把 gRPC 挪走")]
    public int Port { get; set; } = 5000;

    [Machine("0", "Hsms", "设备号（DeviceId/SessionId），要和 EAP 侧配置一致，不一致的报文会被丢弃")]
    public int DeviceId { get; set; }

    [Machine("45000", "Hsms", "T3：数据事务等回复超时（毫秒）")]
    public int T3ReplyTimeoutMs { get; set; } = 45_000;

    [Machine("10000", "Hsms", "T5：断线后重连间隔（毫秒）")]
    public int T5ConnectRetryMs { get; set; } = 10_000;

    [Machine("5000", "Hsms", "T6：控制事务（Select/Linktest）等回复超时（毫秒）")]
    public int T6ControlTimeoutMs { get; set; } = 5_000;

    [Machine("10000", "Hsms", "T7：TCP 连上后等 Select 的超时（毫秒），超时断开等重连")]
    public int T7NotSelectedTimeoutMs { get; set; } = 10_000;

    [Machine("5000", "Hsms", "T8：一帧内部字节间超时（毫秒），防半包挂死")]
    public int T8IntercharacterTimeoutMs { get; set; } = 5_000;

    [Machine("30000", "Hsms", "Linktest 心跳周期（毫秒），0 = 不主动发；双方都必应答")]
    public int LinktestIntervalMs { get; set; } = 30_000;

    [Machine("True", "Hsms", "报文明文进日志（fab 验收和现场排障全靠它；报文量大时可以关）")]
    public bool LogMessages { get; set; } = true;

    #endregion

    /// <summary>
    /// 链路状态（SV）：EAP 连没连上、过没过 Select，界面和 EAP 都能查
    /// </summary>
    [VariableMark(VariableType.SV, ValueFormat.Enum, description: "EAP 链路状态（NotConnected/ConnectedNotSelected/Selected）")]
    public HsmsLinkState LinkState => Link.LinkState;

    /// <summary>
    /// 链路：各标准开机时往上登记处理方、闸门，设备主动发的报文也经它（第一次用时建，日志按组件名记）
    /// </summary>
    public HsmsLink Link
    {
        get
        {
            _link ??= new HsmsLink(new SecsLog(this, () => LogMessages), Name);
            return _link;
        }
    }

    /// <summary>
    /// 被动模式实际监听地址（Port 配 0 时系统分配，冒烟测试用）
    /// </summary>
    public IPEndPoint? LocalEndpoint => Link.LocalEndpoint;

    /// <summary>
    /// 链路现在能不能发报文（过了 Select）
    /// </summary>
    public bool IsSelected => Link.IsSelected;

    #region 生命周期

    /// <summary>
    /// 开机：Hsms 得挂在 Eap 节点下面，才有人接各标准、开链路
    /// </summary>
    public override void Bind()
    {
        if (IocHelper.GetService<IEapComponent>() is null)
        {
            LogHelper.Error(this, "machine.xml 的 Hsms 节点要放在 Eap 节点下（跟 E30 等标准组件一起），现在这样 EAP 不接");
        }
    }

    /// <summary>
    /// 建链路：EAP 组件把各标准都接好、登记完之后调（Host 一连进来就可能发报文，处理方得先在）。
    /// 配置不对、端口被占只记错误，EAP 链路不起，不拖垮整个后端
    /// </summary>
    public void Open()
    {
        if (!IsEnable)
        {
            LogHelper.Info(this, "EAP 链路未启用（IsEnable=False），不监听端口");
            return;
        }

        if (DeviceId is < 0 or > 0x7FFF)
        {
            LogHelper.Error(this, $"EAP 链路没起来：DeviceId={DeviceId} 不对，HSMS 设备号只能是 0~32767");
            return;
        }

        string host = Host;
        if (string.IsNullOrWhiteSpace(host))
        {
            host = "127.0.0.1";
        }

        var settings = new HsmsSettings
        {
            IsEquipment = true,
            IsActive = IsActive,
            Host = host,
            Port = Port,
            DeviceId = (ushort)DeviceId,
            T3ReplyTimeoutMs = T3ReplyTimeoutMs,
            T5ConnectRetryMs = T5ConnectRetryMs,
            T6ControlTimeoutMs = T6ControlTimeoutMs,
            T7NotSelectedTimeoutMs = T7NotSelectedTimeoutMs,
            T8IntercharacterTimeoutMs = T8IntercharacterTimeoutMs,
            LinktestIntervalMs = LinktestIntervalMs,
        };

        // 跟 gRPC 同端口：宿主先开 EAP 链路、后起 gRPC，监听是独占的，gRPC 就绑不上、整个后端起不来。
        // 只能让一边让路——界面要靠 gRPC，所以 EAP 链路不起，记错误
        int rpcPort = IocHelper.GetService<IRpc>()?.Port ?? RpcComponent.DefaultPort;
        if (!IsActive && Port == rpcPort)
        {
            LogHelper.Error(this, $"EAP 链路没起来：Hsms 端口 {Port} 跟 Rpc 节点的 gRPC 端口相同，把其中一个挪开");
            return;
        }

        try
        {
            Link.Open(settings);
        }
        catch (ArgumentException exception)
        {
            LogHelper.Error(this, $"EAP 链路没起来：Hsms 配置不对，{exception.Message}");
            return;
        }
        catch (HsmsConnectionException exception)
        {
            // 端口被别的程序占着：EAP 链路起不来，但不拖垮整个后端
            LogHelper.Error(this, $"EAP 链路没起来：{exception.Message}；换个端口或关掉占着它的程序");
            return;
        }

        if (IsActive)
        {
            LogHelper.Info(this, $"EAP 链路：主动连出 {host}:{Port}，T5 重连 {T5ConnectRetryMs}ms");
            return;
        }

        LogHelper.Info(this, $"EAP 链路：监听 0.0.0.0:{Port} 等 EAP 连入，DeviceId={DeviceId}");
    }

    /// <summary>
    /// 收链路：发 Separate 优雅断开（被动 / 主动都一样），停派发线程。宿主退出时调
    /// </summary>
    public void Close()
    {
        Link.Close();
    }

    #endregion
}
