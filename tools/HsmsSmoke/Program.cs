using xyz.Components;
using xyz.Components.Collectors;
using xyz.Components.Components;
using xyz.Configs.Models;
using xyz.Secs.Diagnostics;
using xyz.Secs.Hsms;
using xyz.Secs.SecsII;

// HsmsComponent 冒烟：组件经 ComponentLoader 按 sc.xml 同款结构装配（不走真 sc.xml，免拉整机），
// 假 EAP 用 HsmsConnector 连入，验证 GEM 答话——S1F13 通讯建立、S1F1、S1F3 按 SVID 表答值（含空查全量、
// 未知号占位）、S1F17 对时、S2F41 回 HCACK=4、断线重连后组件照常答话、Close 发 Separate。
// 编号表用临时目录合并（组件自身的 LinkState SV 会分到 SVID）。
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
var roots = ComponentLoader.Load([node]);
var hsms = roots.OfType<HsmsComponent>().Single();
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
eap.SessionEstablished += session =>
{
    established++;
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

// 3.2 S1F1 在线询问：回 B(0)。
var s1f2 = await session.SendAsync(new SecsMessage(1, 1, true));
Check(s1f2.Name == "S1F2" && s1f2.Body!.GetBinary()[0] == 0, "S1F1 应答出 S1F2 在线");

// 3.3 S1F3 按号查：LinkState 此刻应是 Selected（SV 值来自组件属性反射）。
var s1f4 = await session.SendAsync(new SecsMessage(1, 3, true, SecsItem.L(SecsItem.U4((uint)linkStateSvid))));
Check(s1f4.Body!.Items.Count == 1 && s1f4.Body.Items[0].GetString() == "Selected", "S1F3 按号应答 LinkState=Selected");

// 3.4 S1F3 未知号：回空 A 占位，保持与请求对齐。
var unknown = await session.SendAsync(new SecsMessage(1, 3, true, SecsItem.L(SecsItem.U4(99999))));
Check(unknown.Body!.Items.Count == 1 && unknown.Body.Items[0].GetString() == string.Empty, "未知 SVID 应回空占位");

// 3.5 S1F17 对时询问：E5 格式 14 位（yyMMddHHmmssff）。
var s1f18 = await session.SendAsync(new SecsMessage(1, 17, true));
Check(s1f18.Body!.GetString().Length == 14, "S1F17 应答出 14 位时间");

// 3.6 S2F41 远程命令：HCACK=4（不接受，下一阶段接派单）。
var s2f42 = await session.SendAsync(new SecsMessage(2, 41, true,
    SecsItem.L(SecsItem.A("START"), SecsItem.L())));
Check(s2f42.Body!.First().GetBinary()[0] == 4, "S2F41 应回 HCACK=4");

// 3.7 S2F31 对时设置：答收下（B(0)）。
var s2f32 = await session.SendAsync(new SecsMessage(2, 31, true, SecsItem.A("2610011200000000")));
Check(s2f32.Body!.GetBinary()[0] == 0, "S2F31 应答收下");

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
Console.WriteLine($"PASS: {checks} 项检查全部通过");

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
