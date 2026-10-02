using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using xyz.Components;
using xyz.Components.Alarm;
using xyz.Components.Attributes;
using xyz.Components.Collectors;
using xyz.Components.Components;
using xyz.Configs.Models;
using xyz.Secs;
using xyz.Secs.Diagnostics;
using xyz.Secs.Hsms;
using xyz.Secs.SecsII;

// HsmsComponent 冒烟：组件经 ComponentLoader 按 sc.xml 同款结构装配（不走真 sc.xml，免拉整机），
// 假 EAP 用 HsmsConnector 连入，验证 GEM 答话——S1F13 通讯建立、S1F1 回 MDLN/SOFTREV、S1F3 按 SVID 表答值（含
// 未知号占位、SVID 类型不对回 S9F7）、S1F17 上线请求、S2F17 时间、S2F31 对时、S2F41 回 L[2]{HCACK=4, L}、
// 没实现的报文回 S9F3/S9F5、报警报出/清除推 S5F1 L[3]{ALCD, ALID, ALTX}、断线重连后组件照常答话、Close 发 Separate、
// 跟 gRPC 同端口、配置不对、端口被占时 Open 只记错误不抛。编号表用临时目录合并（组件自身的 LinkState SV 会分到 SVID，探针报警分到 ALID）。
var checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + message);
    checks++;
}

var sink = new ConsoleSecsSink();

// 1. 装配：一个 Hsms 节点，时序全部调小，端口避开 5000（Rpc 的 gRPC 在用）。
var port = 5600 + Random.Shared.Next(400);
var node = new ModuleConfig
{
    Name = "Hsms",
    Type = typeof(HsmsComponent).FullName!,
    Values =
    {
        new ValueConfig { Name = "IsEnable", Value = "True" },
        new ValueConfig { Name = "Port", Value = port.ToString() },
        new ValueConfig { Name = "DeviceId", Value = "7" },
        new ValueConfig { Name = "EquipmentModel", Value = "xyz-35021" },
        new ValueConfig { Name = "SoftwareRevision", Value = "0.1" },
        new ValueConfig { Name = "T3ReplyTimeoutMs", Value = "800" },
        new ValueConfig { Name = "T5ConnectRetryMs", Value = "300" },
        new ValueConfig { Name = "T6ControlTimeoutMs", Value = "1000" },
        new ValueConfig { Name = "T7NotSelectedTimeoutMs", Value = "800" },
        new ValueConfig { Name = "T8IntercharacterTimeoutMs", Value = "2000" },
        new ValueConfig { Name = "LinktestIntervalMs", Value = "500" },
    },
};
var alarmNode = new ModuleConfig
{
    Name = "Alarm",
    Type = typeof(AlarmComponent).FullName!,
    Values = { new ValueConfig { Name = "EnableHistory", Value = "False" } },
};
var probeNode = new ModuleConfig { Name = "Probe", Type = typeof(AlarmProbe).FullName! };
var roots = ComponentLoader.Load([alarmNode, probeNode, node]);
var hsms = roots.OfType<HsmsComponent>().Single();
var probe = roots.OfType<AlarmProbe>().Single();
Check(ReferenceEquals(HsmsComponent.Current, hsms), "装配出来即成为 Current");
Check(hsms.IsEnable && hsms.Port == port && hsms.DeviceId == 7, "[SCEditor] 灌值生效（IsEnable/Port/DeviceId）");

// 2. 编号表：组件树合并进临时目录，LinkState SV 应分到 SVID。
var directory = Path.Combine(Path.GetTempPath(), "HsmsSmoke");
Directory.CreateDirectory(directory);
var collectors = new GemCollectors();
collectors.Merge(roots, directory);
var linkStateRow = collectors.Sv.Definitions.FirstOrDefault(row => row.Name.EndsWith("LinkState", StringComparison.OrdinalIgnoreCase));
Check(linkStateRow is not null && linkStateRow.Id >= SvCollector.FirstId, "LinkState SV 应进编号表");
var linkStateSvid = linkStateRow!.Id;
var probeAlarmRow = collectors.Alarm.Definitions.FirstOrDefault(row => row.Name == "Probe.ProbeAlarm");
Check(probeAlarmRow is not null && probeAlarmRow.Id >= AlarmCollector.FirstId, "探针报警应进编号表");
var probeAlid = (ulong)probeAlarmRow!.Id;

hsms.Open();

// 3. 假 EAP 连入（设备号 7 对上）。
var established = 0;
var ready = new TaskCompletionSource<HsmsSession>(TaskCreationOptions.RunContinuationsAsynchronously);
var eap = new HsmsConnector(new HsmsSettings
{
    IsActive = true,
    Host = "127.0.0.1",
    Port = port,
    DeviceId = 7,
    T3ReplyTimeoutMs = 800,
    T5ConnectRetryMs = 300,
    T6ControlTimeoutMs = 1000,
    T7NotSelectedTimeoutMs = 800,
    T8IntercharacterTimeoutMs = 2000,
    LinktestIntervalMs = 500,
}, sink);
var alarmReports = Channel.CreateUnbounded<HsmsMessage>();
eap.SessionEstablished += session =>
{
    established++;
    session.PrimaryReceived += message =>
    {
        if (message.Name == "S5F1 W")
        {
            session.Reply(message, message.CreateReply(SecsItem.B(0)));
            alarmReports.Writer.TryWrite(message);
        }
    };
    ready.TrySetResult(session);
};
eap.Start();
var session = await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
Check(session.IsSelected, "假 EAP 应 SELECTED");

// 3.1 S1F13 通讯建立：回建立码 0 + MDLN/SOFTREV。
var s1f14 = await session.SendAsync(new SecsMessage(1, 13, true, SecsItem.L(SecsItem.A("MDLN"), SecsItem.A("1.0"))));
Check(s1f14.Name == "S1F14" && s1f14.Body!.Items[0].GetBinary()[0] == 0
      && s1f14.Body.Items[1].Items[0].GetString() == "xyz-35021"
      && s1f14.Body.Items[1].Items[1].GetString() == "0.1", "S1F13 应答出建立码与 MDLN/SOFTREV");

// 3.2 S1F1 在线询问：设备回 L[2]{MDLN, SOFTREV}（只有 Host 回空 L）。
var s1f2 = await session.SendAsync(new SecsMessage(1, 1, true));
Check(s1f2.Name == "S1F2" && s1f2.Body is { Items.Count: 2 } && s1f2.Body.Items[0].GetString() == "xyz-35021"
      && s1f2.Body.Items[1].GetString() == "0.1", "S1F1 应答出 S1F2 L[2]{MDLN, SOFTREV}");

// 3.3 S1F3 按号查：LinkState 此刻应是 Selected（SV 值来自组件属性反射）。
var s1f4 = await session.SendAsync(new SecsMessage(1, 3, true, SecsItem.L(SecsItem.U4((uint)linkStateSvid))));
Check(s1f4.Body!.Items.Count == 1 && s1f4.Body.Items[0].GetString() == "Selected", "S1F3 按号应答 LinkState=Selected");

// 3.4 S1F3 未知号：回空 A 占位，保持与请求对齐。
var unknown = await session.SendAsync(new SecsMessage(1, 3, true, SecsItem.L(SecsItem.U4(99999))));
Check(unknown.Body!.Items.Count == 1 && unknown.Body.Items[0].GetString() == string.Empty, "未知 SVID 应回空占位");

// 3.5 S1F17 上线请求：还没有 GEM 控制状态模型，设备一直在线，回 ONLACK=2（已经在线）。
var s1f18 = await session.SendAsync(new SecsMessage(1, 17, true));
Check(s1f18.Name == "S1F18" && s1f18.Body!.GetBinary()[0] == 2, "S1F17 应回 ONLACK=2");

// 3.5b S2F17 时间查询：16 位 YYYYMMDDhhmmsscc。
var s2f18 = await session.SendAsync(new SecsMessage(2, 17, true));
Check(s2f18.Name == "S2F18" && s2f18.Body!.GetString().Length == 16
      && s2f18.Body.GetString().All(char.IsAsciiDigit), "S2F17 应答出 16 位时间");

// 3.6 S2F41 远程命令：S2F42 L[2]{HCACK=4（不接受，下一阶段接派单）, L[0]}。
var s2f42 = await session.SendAsync(new SecsMessage(2, 41, true,
    SecsItem.L(SecsItem.A("START"), SecsItem.L())));
Check(s2f42.Body is { Items.Count: 2 } && s2f42.Body.Items[0].GetBinary()[0] == 4 && s2f42.Body.Items[1].Count == 0,
    "S2F41 应回 L[2]{HCACK=4, L[0]}");

// 3.7 S2F31 对时设置：答收下（B(0)）。
var s2f32 = await session.SendAsync(new SecsMessage(2, 31, true, SecsItem.A("2610011200000000")));
Check(s2f32.Body!.GetBinary()[0] == 0, "S2F31 应答收下");

// 3.7b 没实现的报文回 S9（体是原始消息头，EAP 的事务立刻失败，不用干等 T3）：Function 没实现 S9F5、
//      整个 Stream 没实现 S9F3；SVID 给成文字是数据不对，回 S9F7。
foreach (var (request, expected) in new[]
{
    (new SecsMessage(1, 11, true, SecsItem.L()), "S9F5"),
    (new SecsMessage(7, 1, true, SecsItem.L()), "S9F3"),
    (new SecsMessage(1, 3, true, SecsItem.L(SecsItem.A("LinkState"))), "S9F7"),
})
{
    try
    {
        await session.SendAsync(request);
        Check(false, $"{request.Name} 应回 {expected}");
    }
    catch (SecsException exception) when (exception is not SecsTimeoutException)
    {
        Check(exception.Message.Contains(expected, StringComparison.Ordinal), $"{request.Name} 应回 {expected}（实际：{exception.Message}）");
    }
}

// 3.7c 报警推 S5F1 L[3]{ALCD, ALID, ALTX}：报出 ALCD 最高位 1，复位清除 ALCD=0，ALTX 是"组件全路径.报警代码"。
probe.Fire();
var raised = await alarmReports.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
Check(raised.Body is { Items.Count: 3 } && raised.Body.Items[0].GetBinary()[0] == 0x80
      && raised.Body.Items[1].Format == SecsFormat.U4 && raised.Body.Items[1].GetUInt64() == probeAlid
      && raised.Body.Items[2].GetString() == "Probe.ProbeAlarm", "报警报出应推 S5F1 L[3]{80, ALID, ALTX}");
probe.Reset();
var cleared = await alarmReports.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
Check(cleared.Body is { Items.Count: 3 } && cleared.Body.Items[0].GetBinary()[0] == 0
      && cleared.Body.Items[1].GetUInt64() == probeAlid, "报警清除应推 S5F1，ALCD=0");

// 3.8 断线重连：会话 Separate 后 T5 重连，组件对新会话照常答话。
session.SendSeparate();
var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
while (established < 2 && DateTime.UtcNow < deadline)
{
    await Task.Delay(50);
}
Check(established == 2, "断开后应自动重连");
var session2 = eap.Current;
Check(session2 is not null && session2.IsSelected, "重连后应有新会话");
var again = await session2!.SendAsync(new SecsMessage(1, 1, true));
Check(again.Name == "S1F2", "重连后的新会话应能正常答话");

// 3.9 收链路：Close 发 Separate，对端应收到断开。
hsms.Close();
var closed = await session2.SendAsync(new SecsMessage(1, 1, true)).ContinueWith(task => task.IsFaulted);
Check(closed, "Close 后对端再问应失败（链路已断）");

eap.Dispose();

// 4. 链路起不来的几种情况：Open 只记错误、不抛，后端照常起，链路状态保持未连接。
// ① 跟 gRPC（Rpc 节点）同端口：不去抢——宿主先开 EAP 链路后起 gRPC，抢了 gRPC 就绑不上、整个后端起不来。
_ = new RpcComponent { Port = port };  // 构造即成为 RpcComponent.Current，宿主从它取 gRPC 端口
var sameAsRpc = new HsmsComponent { IsEnable = true, Port = port, LinktestIntervalMs = 0 };
sameAsRpc.Open();
Check(sameAsRpc.LocalEndpoint is null && sameAsRpc.LinkState == HsmsLinkState.NotConnected, "跟 gRPC 同端口时不监听");
sameAsRpc.Close();
RpcComponent.Current = null;

// ② 配置不对（设备号超 15 位、超时填 0）：同样只记错误。端口给 0，万一没拦住会监听到系统分配的端口，检查就会失败。
foreach (var invalid in new[]
{
    new HsmsComponent { IsEnable = true, Port = 0, DeviceId = 40000 },
    new HsmsComponent { IsEnable = true, Port = 0, T3ReplyTimeoutMs = 0 },
})
{
    invalid.Open();
    Check(invalid.LocalEndpoint is null, $"配置不对时 Open 不抛、不监听（DeviceId={invalid.DeviceId}，T3={invalid.T3ReplyTimeoutMs}）");
    invalid.Close();
}

// ③ 端口被别的程序占着：监听失败。
var occupant = new TcpListener(IPAddress.Loopback, 0);
occupant.Start();
var occupiedPort = ((IPEndPoint)occupant.LocalEndpoint).Port;
var clash = new HsmsComponent { IsEnable = true, Port = occupiedPort, LinktestIntervalMs = 0 };
clash.Open();
Check(clash.LocalEndpoint is null && clash.LinkState == HsmsLinkState.NotConnected, "端口被占时 Open 不抛、不监听");
clash.Close();
occupant.Stop();

Console.WriteLine($"PASS: {checks} 项检查全部通过");

/// <summary>冒烟用的报警源：一个 [Alarm]，Fire 报出，Reset 清除。</summary>
[Component(description: "HsmsSmoke 报警探针")]
public sealed class AlarmProbe : ComponentBase
{
    [Alarm("冒烟报警", AlarmCategory.Other)]
    public string ProbeAlarm = "ProbeAlarm";

    public void Fire() => RaiseAlarm(ProbeAlarm);
}

/// <summary>报文明文打到控制台（复用协议库的格式化）。</summary>
sealed class ConsoleSecsSink : ISecsSink
{
    public void Info(string category, string message) => Console.WriteLine($"[{category}] {message}");

    public void Warn(string category, string message) => Console.WriteLine($"[{category}] WARN {message}");

    public void Error(string category, string message) => Console.WriteLine($"[{category}] ERROR {message}");

    public void Trace(SecsMessageDirection direction, HsmsMessage message)
    {
        var arrow = direction == SecsMessageDirection.Sent ? ">>" : "<<";
        Console.WriteLine($"{arrow} {SecsMessageText.Format(message).Replace("\r", " ").Replace("\n", " | ")}");
    }
}
