using System.Collections.Concurrent;
using System.Text;
using xyz.Components;
using xyz.Components.Attributes;
using xyz.Components.Collectors;
using xyz.Components.Components;
using xyz.Components.Enums;
using xyz.Components.Interfaces;
using xyz.Components.Models;
using xyz.Configs.Models;
using xyz.Database.DbProvider;
using xyz.Drivers.Loadport;
using xyz.Secs;
using xyz.Secs.Diagnostics;
using xyz.Secs.Hsms;
using xyz.Secs.SecsII;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;

// EAP 冒烟（SECS/GEM 各标准组件对假 Host）：Eap 节点按 sc.xml 同款结构装配（Hsms + E30 + E39 + E87 + E90 + E40 + E94），
// 设备侧用假的 LoadPort（ILoadPort）、真的晶圆账（WaferManagerComponent）、假的 Job 管理（IJobManager），假 Host 用 HsmsConnector 连进来。
// 验证：E30 通讯建立、控制状态（离线挡报文、上线 / 离线 / 本地 / 远程、操作员上线问 S1F1）、SV / EC / DV / 事件名单、Host 改 EC、
// 报告定义 / 链接 / 开关和 S6F11 带的值、按需要报告、报警 S5F1 和报警事件、缓存（断线进缓存、Host 要了按先后发、清缓存）；
// E39 查类型 / 属性名 / 属性（带条件）；E87 载具核对（没预告等 Host、Host 让继续、Host 给片号、取消、Bind 设备认定、读槽图核对）、
// 端口搬运状态、启停用、存取方式；E90 片对象跟着账走（建、挪、做、跳过、删）；E40 / E94 的建、命令、查询翻成 Job 管理的命令、状态转换报事件。
var checks = 0;
void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException("FAIL: " + message);
    }

    checks++;
}

var directory = Path.Combine(Path.GetTempPath(), "EapSmoke-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
XyzDb.Register("EapSmoke", $"DataSource={Path.Combine(directory, "eap.db")}", SqlSugar.DbType.Sqlite);

// ── 装配 ──────────────────────────────────────────────────────────────────
var port = 5600 + Random.Shared.Next(400);
var alarmNode = new ModuleConfig
{
    Name = "Alarm",
    Type = typeof(AlarmComponent).FullName!,
    Values = { new ValueConfig { Name = "EnableHistory", Value = "False" } },
};
var probeNode = new ModuleConfig { Name = "Probe", Type = typeof(AlarmProbe).FullName! };
var eapNode = new ModuleConfig
{
    Name = "Eap",
    Type = typeof(EapComponent).FullName!,
    Children =
    {
        new ModuleConfig
        {
            Name = "Hsms",
            Type = typeof(HsmsComponent).FullName!,
            Values =
            {
                new ValueConfig { Name = "IsEnable", Value = "True" },
                new ValueConfig { Name = "Port", Value = port.ToString() },
                new ValueConfig { Name = "DeviceId", Value = "3" },
                new ValueConfig { Name = "T3ReplyTimeoutMs", Value = "1500" },
                new ValueConfig { Name = "T7NotSelectedTimeoutMs", Value = "2000" },
                new ValueConfig { Name = "LinktestIntervalMs", Value = "0" },
                new ValueConfig { Name = "LogMessages", Value = "False" },
            },
        },
        new ModuleConfig { Name = "Notifier", Type = typeof(EapNotifierComponent).FullName! },
        new ModuleConfig
        {
            Name = "E30",
            Type = typeof(E30Component).FullName!,
            Values =
            {
                new ValueConfig { Name = "EquipmentModel", Value = "xyz-35021" },
                new ValueConfig { Name = "SoftwareRevision", Value = "2.0" },
                new ValueConfig { Name = "Database", Value = "EapSmoke" },
            },
        },
        new ModuleConfig { Name = "E39", Type = typeof(E39Component).FullName! },
        new ModuleConfig { Name = "Recipe", Type = typeof(E30RecipeComponent).FullName! },
        new ModuleConfig { Name = "E87", Type = typeof(E87Component).FullName! },
        new ModuleConfig { Name = "E90", Type = typeof(E90Component).FullName! },
        new ModuleConfig { Name = "E40", Type = typeof(E40Component).FullName! },
        new ModuleConfig { Name = "E94", Type = typeof(E94Component).FullName! },
    },
};
var roots = ComponentLoader.Load([alarmNode, probeNode, eapNode]);
var ec = new EcComponent();
ec.Merge(roots);
var collectors = new GemCollectors();
collectors.Merge(roots, directory);

var eap = roots.OfType<EapComponent>().Single();
var gem = eap.FindChild<E30Component>()!;
var probe = roots.OfType<AlarmProbe>().Single();
var ledger = new WaferManagerComponent();
ledger.RegisterLoadPort("LP1", 5);
ledger.RegisterLoadPort("LP2", 5);
ledger.RegisterLocation("Robot", 2);
ledger.RegisterLocation("PM1", 1);
var lp1 = new FakePort("LP1", ledger);
var lp2 = new FakePort("LP2", ledger);
var jobs = new FakeJobs();
var sequences = new FakeSequences();
var processRecipes = new FakeProcessRecipes();

int Ceid(string name)
{
    int ceid = collectors.Event.CeidOf(name);
    Check(ceid > 0, $"事件 {name} 应有 CEID");
    return ceid;
}

int Svid(string name)
{
    var row = collectors.Sv.Definitions.FirstOrDefault(item => item.Enabled && item.Name == name);
    Check(row is not null, $"SV {name} 应有 SVID");
    return row!.Id;
}

int Ecid(string name)
{
    var row = collectors.Ec.Definitions.FirstOrDefault(item => item.Enabled && item.Name == name);
    Check(row is not null, $"EC {name} 应有 ECID");
    return row!.Id;
}

int Dvid(string name)
{
    int dvid = collectors.Dv.DvidOf(name);
    Check(dvid > 0, $"DV {name} 应有 DVID");
    return dvid;
}

eap.Bind([lp1, lp2], jobs, sequences, processRecipes);
Check(eap.IsBound && gem.IsAttached, "EAP 接上了：E30 接到链路上");
Check(ReferenceEquals(lp1.E87Callback, eap.FindChild<E87Component>()) && lp1.E84Provider is not null, "E87 挂到 LoadPort 上（回调 + E84 反查口）");
Check(ledger.E90Callback is not null && jobs.E40Callback is not null && jobs.E94Callback is not null, "E90 挂到晶圆账、E40 / E94 挂到 Job 管理上");
Check(gem.CurrentControlState == GemControlState.HostOffline, "开机控制状态按 EC 默认进 HOST OFF-LINE");

// ── 假 Host ────────────────────────────────────────────────────────────────
var host = new HostLog();
var connector = new HsmsConnector(new HsmsSettings
{
    IsActive = true,
    Host = "127.0.0.1",
    Port = port,
    DeviceId = 3,
    T3ReplyTimeoutMs = 1500,
    T5ConnectRetryMs = 1500,
    T6ControlTimeoutMs = 1000,
    T7NotSelectedTimeoutMs = 2000,
    LinktestIntervalMs = 0,
}, NullSecsSink.Instance);
connector.SessionEstablished += session =>
{
    session.PrimaryReceived += message =>
    {
        switch (message.Header.Stream, message.Header.Function)
        {
            case (1, 13):
                session.Reply(message, message.CreateReply(SecsItem.L(SecsItem.B(0), SecsItem.L())));
                break;

            case (1, 1):
                session.Reply(message, message.CreateReply(SecsItem.L()));
                break;

            default:
                session.Reply(message, message.CreateReply(SecsItem.B(0)));
                break;
        }

        host.Add(message);
    };
};
connector.Start();

async Task WaitUntil(Func<bool> condition, string message, int timeoutMs = 5000)
{
    var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
    while (!condition())
    {
        if (DateTime.UtcNow > deadline)
        {
            Check(false, message);
        }

        await Task.Delay(20);
    }

    checks++;
}

async Task<HsmsMessage> Send(byte stream, byte function, SecsItem? body = null)
{
    var session = connector.Current;
    Check(session is not null && session.IsSelected, $"S{stream}F{function} 发之前假 Host 应连着");
    return await session!.SendAsync(new SecsMessage(stream, function, true, body));
}

async Task<string> SendExpectingAbort(byte stream, byte function, SecsItem? body = null)
{
    try
    {
        await Send(stream, function, body);
        return "没回中止";
    }
    catch (SecsException exception) when (exception is not SecsTimeoutException)
    {
        return exception.Message;
    }
}

async Task<HsmsMessage> Event(string name, int from, string message)
{
    var report = await host.WaitEventAsync((uint)Ceid(name), from);
    Check(report is not null, message);
    return report!;
}

async Task NoEvent(string name, int from, string message)
{
    await Task.Delay(300);
    Check(host.FindEvent((uint)Ceid(name), from) is null, message);
}

// ── E30：通讯建立 ─────────────────────────────────────────────────────────
await WaitUntil(() => host.Others.Any(message => message.Name == "S1F13 W"), "链路连上后设备应发 S1F13");
var s1f13 = host.Others.First(message => message.Name == "S1F13 W");
Check(s1f13.Body!.Items[0].GetString() == "xyz-35021" && s1f13.Body.Items[1].GetString() == "2.0", "设备的 S1F13 带 MDLN / SOFTREV");
await WaitUntil(() => gem.CommState == GemCommState.Communicating, "Host 回 COMMACK=0 后通讯建立");

// Host 也能先发 S1F13：设备回 COMMACK=0 + 身份
var s1f14 = await Send(1, 13, SecsItem.L());
Check(s1f14.Body!.Items[0].GetBinary()[0] == 0 && s1f14.Body.Items[1].Items[0].GetString() == "xyz-35021", "Host 的 S1F13 回 S1F14 COMMACK=0 + MDLN");

// ── E30：控制状态 ─────────────────────────────────────────────────────────
Check((await SendExpectingAbort(1, 1)).Contains("S1F0", StringComparison.Ordinal), "HOST OFF-LINE 时 Host 的 S1F1 回 S1F0");
Check((await SendExpectingAbort(1, 3, SecsItem.L())).Contains("S1F0", StringComparison.Ordinal), "离线时 S1F3 也回 S1F0");
int mark = host.EventCount;
var online = await Send(1, 17);
Check(online.Body!.GetBinary()[0] == 0, "HOST OFF-LINE 收 S1F17 回 ONLACK=0");
await Event("Eap.E30.ControlStateRemote", mark, "Host 请求上线后进 ON-LINE REMOTE，报在线远程事件");
Check(gem.CurrentControlState == GemControlState.OnlineRemote && gem.IsRemote, "控制状态 ON-LINE REMOTE");
Check((await Send(1, 17)).Body!.GetBinary()[0] == 2, "已经在线再 S1F17 回 ONLACK=2");
var s1f2 = await Send(1, 1);
Check(s1f2.Body!.Items.Count == 2 && s1f2.Body.Items[0].GetString() == "xyz-35021", "在线后 S1F1 回 S1F2 L[2]{MDLN, SOFTREV}");

mark = host.EventCount;
Check((await Send(1, 15)).Body!.GetBinary()[0] == 0, "S1F15 回 OFLACK=0");
await Event("Eap.E30.EquipmentOffline", mark, "Host 请求离线：先报离线事件");
Check(gem.CurrentControlState == GemControlState.HostOffline, "Host 请求离线后进 HOST OFF-LINE");
await Send(1, 17);
await WaitUntil(() => gem.IsRemote, "Host 再请求上线回到 ON-LINE REMOTE");

mark = host.EventCount;
Check(gem.RequestRemote(false), "操作员切 LOCAL");
await Event("Eap.E30.ControlStateLocal", mark, "切 LOCAL 报在线本地事件");
var hcack = await Send(2, 41, SecsItem.L(SecsItem.A("START"), SecsItem.L()));
Check(hcack.Body!.Items[0].GetBinary()[0] == 1, "S2F41 本机没有远程命令，回 HCACK=1");
Check(gem.RequestRemote(true) && gem.IsRemote, "操作员切回 REMOTE");

mark = host.EventCount;
Check(gem.RequestOffline(), "操作员点离线");
await Event("Eap.E30.EquipmentOffline", mark, "操作员离线报离线事件");
Check(gem.CurrentControlState == GemControlState.EquipmentOffline, "进 EQUIPMENT OFF-LINE");
Check((await Send(1, 17)).Body!.GetBinary()[0] == 1, "EQUIPMENT OFF-LINE 时 Host 请求上线回 ONLACK=1");
mark = host.EventCount;
Check(gem.RequestOnline(), "操作员点上线：设备发 S1F1 问 Host");
await Event("Eap.E30.ControlStateRemote", mark, "Host 回了 S1F2，进 ON-LINE 报在线远程事件");
Check(gem.IsRemote && gem.PreviousControlState == (byte)GemControlState.AttemptOnline, "上线成功，上一个状态是 ATTEMPT ON-LINE");
Check(!gem.RequestOnline(), "已经在线再点上线不动");

// ── E30：变量和常量 ────────────────────────────────────────────────────────
int controlStateSvid = Svid("Eap.E30.ControlState");
var s1f4 = await Send(1, 3, SecsItem.L(SecsItem.U4((uint)controlStateSvid), SecsItem.U4(49999)));
Check(s1f4.Body!.Items[0].Format == SecsFormat.U1 && s1f4.Body.Items[0].GetUInt64() == 5 && s1f4.Body.Items[1].GetString() == string.Empty,
    "S1F3：ControlState 是 U1 5，不认识的号回空 ASCII");
Check((await SendExpectingAbort(1, 3, SecsItem.L(SecsItem.Boolean(true)))).Contains("S9F7", StringComparison.Ordinal), "SVID 给布尔回 S9F7");
var s1f12 = await Send(1, 11, SecsItem.L(SecsItem.U4((uint)controlStateSvid)));
Check(s1f12.Body!.Items[0].Items[1].GetString() == "Eap.E30.ControlState", "S1F11 回 SV 名字");
var allSv = await Send(1, 3, SecsItem.L());
Check(allSv.Body!.Count == collectors.Sv.Collect().Count(sv => sv.Visible), "S1F3 空表 = 全部 SV");

int timeFormatEcid = Ecid("Eap.E30.TimeFormat");
var s2f30 = await Send(2, 29, SecsItem.L(SecsItem.U4((uint)timeFormatEcid)));
var ecRow = s2f30.Body!.Items[0];
Check(ecRow.Items[1].GetString() == "Eap.E30.TimeFormat" && ecRow.Items[2].GetUInt64() == 0 && ecRow.Items[3].GetUInt64() == 2
      && ecRow.Items[4].GetUInt64() == 1, "S2F29：EC 名字、下限 0、上限 2、默认 1");
Check((await Send(2, 13, SecsItem.L(SecsItem.U4((uint)timeFormatEcid)))).Body!.Items[0].GetUInt64() == 1, "S2F13 查 TimeFormat = 1");
Check((await Send(2, 17)).Body!.GetString().Length == 16, "TimeFormat=1 时 S2F17 回 16 位时间");
var eac = await Send(2, 15, SecsItem.L(SecsItem.L(SecsItem.U4((uint)timeFormatEcid), SecsItem.U1(0))));
Check(eac.Body!.GetBinary()[0] == 0 && gem.TimeFormat == 0, "S2F15 改 TimeFormat=0：EAC=0，改进 EC");
Check((await Send(2, 17)).Body!.GetString().Length == 12, "TimeFormat=0 时 S2F17 回 12 位时间");
Check((await Send(2, 15, SecsItem.L(SecsItem.L(SecsItem.U4((uint)timeFormatEcid), SecsItem.U1(7))))).Body!.GetBinary()[0] == 3,
    "超出上下限回 EAC=3");
Check((await Send(2, 15, SecsItem.L(SecsItem.L(SecsItem.U4(29998), SecsItem.U1(1))))).Body!.GetBinary()[0] == 1, "不认识的 ECID 回 EAC=1");
Check((await Send(2, 15, SecsItem.L(SecsItem.L(SecsItem.U4((uint)timeFormatEcid), SecsItem.U1(1)),
          SecsItem.L(SecsItem.U4(29998), SecsItem.U1(1))))).Body!.GetBinary()[0] == 1 && gem.TimeFormat == 0,
    "一条里有一项不对，整条都不改");
await Send(2, 15, SecsItem.L(SecsItem.L(SecsItem.U4((uint)timeFormatEcid), SecsItem.U1(1))));
Check((await Send(2, 31, SecsItem.A("2026100512300000"))).Body!.GetBinary()[0] == 0, "S2F31 对时：格式对回 TIACK=0（不改本机时钟）");
Check((await Send(2, 31, SecsItem.A("not-a-time"))).Body!.GetBinary()[0] == 1, "S2F31 时间格式不对回 TIACK=1");

int changedEcidDv = Dvid("Eap.E30.ChangedEcid");
int ecChangeCeid = Ceid("Eap.E30.OperatorEquipmentConstantChange");
var s1f22 = await Send(1, 21, SecsItem.L(SecsItem.U4((uint)changedEcidDv)));
Check(s1f22.Body!.Items[0].Items[1].GetString() == "Eap.E30.ChangedEcid", "S1F21 回 DV 名字");
var s1f24 = await Send(1, 23, SecsItem.L(SecsItem.U4((uint)ecChangeCeid)));
var ceRow = s1f24.Body!.Items[0];
Check(ceRow.Items[1].GetString() == "Eap.E30.OperatorEquipmentConstantChange" && ceRow.Items[2].Count == 2
      && ceRow.Items[2].Items[0].GetUInt64() == (ulong)changedEcidDv, "S1F23 回事件名字和它带的 DV");

// ── E30：报告 ───────────────────────────────────────────────────────────────
SecsItem DefineReport(uint report, params uint[] vids) =>
    SecsItem.L(SecsItem.U4(1), SecsItem.L(SecsItem.L(SecsItem.U4(report), SecsItem.L(vids.Select(vid => SecsItem.U4(vid))))));
SecsItem LinkReport(uint ceid, params uint[] reports) =>
    SecsItem.L(SecsItem.U4(1), SecsItem.L(SecsItem.L(SecsItem.U4(ceid), SecsItem.L(reports.Select(report => SecsItem.U4(report))))));

Check((await Send(2, 33, DefineReport(1, (uint)changedEcidDv, (uint)controlStateSvid))).Body!.GetBinary()[0] == 0, "S2F33 定义报告 1：DRACK=0");
Check((await Send(2, 33, DefineReport(1, (uint)controlStateSvid))).Body!.GetBinary()[0] == 3, "报告号已定义：DRACK=3");
Check((await Send(2, 33, DefineReport(2, 29997))).Body!.GetBinary()[0] == 4, "变量号不存在：DRACK=4");
Check((await Send(2, 33, SecsItem.A("bad"))).Body!.GetBinary()[0] == 2, "格式不对：DRACK=2");
Check((await Send(2, 35, LinkReport((uint)ecChangeCeid, 1))).Body!.GetBinary()[0] == 0, "S2F35 事件挂报告 1：LRACK=0");
Check((await Send(2, 35, LinkReport((uint)ecChangeCeid, 1))).Body!.GetBinary()[0] == 3, "已经挂了：LRACK=3");
Check((await Send(2, 35, LinkReport(1, 1))).Body!.GetBinary()[0] == 4, "事件号不存在：LRACK=4");
Check((await Send(2, 35, LinkReport((uint)Ceid("Eap.E30.SpoolTransmitFailure"), 77))).Body!.GetBinary()[0] == 5, "报告号不存在：LRACK=5");

mark = host.EventCount;
Check(ec.TrySet("Eap.E30", "MaxSpoolTransmit", "5", out _) == EcSetResult.Ok, "操作员在界面上改 EC");
var changed = await Event("Eap.E30.OperatorEquipmentConstantChange", mark, "操作员改 EC 报事件");
var reports = changed.Body!.Items[2];
Check(reports.Count == 1 && reports.Items[0].Items[0].GetUInt64() == 1, "事件带挂着的报告 1");
var values = reports.Items[0].Items[1];
Check(values.Items[0].GetUInt64() == (ulong)Ecid("Eap.E30.MaxSpoolTransmit") && values.Items[1].Format == SecsFormat.U1
      && values.Items[1].GetUInt64() == 5, "报告的值：DV ChangedEcid = 改的那项、SV ControlState = 5");
mark = host.EventCount;
await Send(2, 15, SecsItem.L(SecsItem.L(SecsItem.U4((uint)Ecid("Eap.E30.MaxSpoolTransmit")), SecsItem.U4(0))));
await NoEvent("Eap.E30.OperatorEquipmentConstantChange", mark, "Host 改的 EC 不报操作员改常量");

var s6f16 = await Send(6, 15, SecsItem.U4((uint)ecChangeCeid));
var requested = s6f16.Body!.Items[2].Items[0].Items[1];
Check(requested.Items[0].Count == 0 && requested.Items[1].GetUInt64() == 5, "S6F15 按需要报告：DV 报空、SV 报现值");
Check((await Send(6, 19, SecsItem.U4(1))).Body!.Count == 2, "S6F19 单个报告两项值");
var annotated = await Send(6, 21, SecsItem.U4(1));
Check(annotated.Body!.Items[1].Items[0].GetUInt64() == (ulong)controlStateSvid, "S6F21 带注释：L[2]{VID, V}");

Check((await Send(2, 37, SecsItem.L(SecsItem.Boolean(false), SecsItem.L()))).Body!.GetBinary()[0] == 0, "S2F37 关掉全部事件");
mark = host.EventCount;
ec.TrySet("Eap.E30", "MaxSpoolTransmit", "3", out _);
await NoEvent("Eap.E30.OperatorEquipmentConstantChange", mark, "事件关了不报");
Check((await Send(2, 37, SecsItem.L(SecsItem.Boolean(true), SecsItem.L(SecsItem.U4(1))))).Body!.GetBinary()[0] == 1, "开不存在的事件：ERACK=1");
Check((await Send(2, 37, SecsItem.L(SecsItem.Boolean(true), SecsItem.L()))).Body!.GetBinary()[0] == 0, "S2F37 开全部事件");
mark = host.EventCount;
ec.TrySet("Eap.E30", "MaxSpoolTransmit", "0", out _);
await Event("Eap.E30.OperatorEquipmentConstantChange", mark, "事件开了又报");

using (var db = XyzDb.Create("EapSmoke"))
{
    string json = db.Ado.GetString("SELECT Json FROM gem_config WHERE Id = 1");
    Check(json.Contains("\"reports\"", StringComparison.OrdinalIgnoreCase) && json.Contains("\"1\"", StringComparison.Ordinal),
        "Host 定的报告存进库里（gem_config）");
}

// ── E30：报警 ───────────────────────────────────────────────────────────────
var alarmRow = collectors.Alarm.Definitions.Single(row => row.Name == "Probe.ProbeAlarm");
uint alid = (uint)alarmRow.Id;
int alarmMark = host.AlarmCount;
mark = host.EventCount;
probe.Fire();
await WaitUntil(() => host.AlarmCount > alarmMark, "报警报出应发 S5F1");
var s5f1 = host.Alarms[alarmMark];
Check(s5f1.Body!.Items[0].GetBinary()[0] == 0x80 && s5f1.Body.Items[1].GetUInt64() == alid && s5f1.Body.Items[2].GetString() == "Probe.ProbeAlarm",
    "S5F1 L[3]{ALCD=0x80, ALID, ALTX}");
Check(host.FindEvent((uint)alarmRow.SetEventId, mark) is not null || await host.WaitEventAsync((uint)alarmRow.SetEventId, mark) is not null,
    "报警报出同时报报警事件");
var s5f6 = await Send(5, 5, SecsItem.U4(alid));
Check(s5f6.Body!.Items[0].Items[0].GetBinary()[0] == 0x80, "S5F5 列报警：在报着的 ALCD 最高位是 1");
Check((await Send(5, 3, SecsItem.L(SecsItem.B(0), SecsItem.U4(alid)))).Body!.GetBinary()[0] == 0, "S5F3 关掉这个报警");
alarmMark = host.AlarmCount;
mark = host.EventCount;
probe.Reset();
await host.WaitEventAsync((uint)alarmRow.ClearEventId, mark);
Check(host.FindEvent((uint)alarmRow.ClearEventId, mark) is not null && host.AlarmCount == alarmMark, "报警关了：清除不发 S5F1，但照报清除事件");
Check((await Send(5, 7)).Body!.Items.All(item => item.Items[1].GetUInt64() != alid), "S5F7 开着的报警里没有它");
Check((await Send(5, 3, SecsItem.L(SecsItem.B(0x80), SecsItem.U4()))).Body!.GetBinary()[0] == 0, "S5F3 ALID 空 = 全部开");
Check((await Send(5, 3, SecsItem.L(SecsItem.B(0x80), SecsItem.U4(1)))).Body!.GetBinary()[0] == 1, "S5F3 不存在的 ALID 回 1");

// ── E30：缓存 ───────────────────────────────────────────────────────────────
var bad = await Send(2, 43, SecsItem.L(SecsItem.L(SecsItem.U1(1), SecsItem.L()), SecsItem.L(SecsItem.U1(6), SecsItem.L(SecsItem.U1(12)))));
Check(bad.Body!.Items[0].GetBinary()[0] == 1 && bad.Body.Items[1].Count == 2
      && bad.Body.Items[1].Items[0].Items[1].GetBinary()[0] == 1 && bad.Body.Items[1].Items[1].Items[1].GetBinary()[0] == 4,
    "S2F43：S1 不许缓存（STRACK 1）、给 secondary（STRACK 4），整条不收");
var good = await Send(2, 43, SecsItem.L(SecsItem.L(SecsItem.U1(6), SecsItem.L())));
Check(good.Body!.Items[0].GetBinary()[0] == 0, "S2F43 缓存 S6：RSPACK=0");
Check((await Send(6, 23, SecsItem.U1(0))).Body!.GetBinary()[0] == 2, "没有缓存时 S6F23 回 RSDA=2");

mark = host.EventCount;
connector.Current!.SendSeparate();
await WaitUntil(() => gem.CommState == GemCommState.NotCommunicating && gem.SpoolCountActual >= 1, "断线后通讯断了、开始缓存（开始缓存事件进缓存）");
gem.RequestRemote(false);
gem.RequestRemote(true);
await WaitUntil(() => gem.SpoolCountActual >= 3, "断线时的事件进缓存");
await WaitUntil(() => gem.CommState == GemCommState.Communicating, "重连上后重新建立通讯", 8000);
int afterReconnect = host.EventCount;
gem.RequestRemote(false);
await NoEvent("Eap.E30.ControlStateLocal", afterReconnect, "缓存没取走之前，新事件也进缓存（先后不乱）");
Check(gem.SpoolCountActual >= 4, "缓存里有 4 条以上");
int spoolMark = host.EventCount;
Check((await Send(6, 23, SecsItem.U1(0))).Body!.GetBinary()[0] == 0, "S6F23 要缓存：RSDA=0");
await Event("Eap.E30.SpoolingDeactivated", spoolMark, "缓存发完报缓存清空");
var spooled = host.EventsFrom(spoolMark).Select(message => (int)HostLog.CeidOf(message)).ToList();
int activated = spooled.IndexOf(Ceid("Eap.E30.SpoolingActivated"));
int local = spooled.IndexOf(Ceid("Eap.E30.ControlStateLocal"));
int deactivated = spooled.IndexOf(Ceid("Eap.E30.SpoolingDeactivated"));
Check(activated == 0 && local > activated && deactivated == spooled.Count - 1, "缓存按先后发：开始缓存在最前，缓存清空在最后");
Check(gem.SpoolCountActual == 0 && (await Send(6, 23, SecsItem.U1(0))).Body!.GetBinary()[0] == 2, "缓存发完了，再要回 RSDA=2");
mark = host.EventCount;
gem.RequestRemote(true);
await Event("Eap.E30.ControlStateRemote", mark, "缓存关了，事件直接发");

connector.Current!.SendSeparate();
await WaitUntil(() => gem.SpoolCountActual >= 1, "再断一次又开始缓存");
await WaitUntil(() => gem.CommState == GemCommState.Communicating, "再重连", 8000);
mark = host.EventCount;
Check((await Send(6, 23, SecsItem.U1(1))).Body!.GetBinary()[0] == 0, "S6F23 清缓存：RSDA=0");
await Event("Eap.E30.SpoolingDeactivated", mark, "清缓存报缓存清空");
Check(gem.SpoolCountActual == 0 && host.FindEvent((uint)Ceid("Eap.E30.SpoolingActivated"), mark) is null, "清掉的缓存不发给 Host");
await Send(2, 43, SecsItem.L());

// ── E39 ─────────────────────────────────────────────────────────────────────
var types = (await Send(14, 5, SecsItem.A(string.Empty))).Body!.Items[0].Items.Select(item => item.GetString()).ToList();
Check(new[] { "Carrier", "Port", "Substrate", "SubstLoc", "ProcessJob", "ControlJob" }.All(types.Contains), "S14F5 列出各标准登记的对象类型");
var names = await Send(14, 7, SecsItem.L(SecsItem.A(string.Empty), SecsItem.L(SecsItem.A("Port"))));
Check(names.Body!.Items[0].Items[0].Items[1].Items.Any(item => item.GetString() == "PortTransferState"), "S14F7 列出 Port 的属性名");
SecsItem GetAttr(string type, string[] ids, SecsItem qualifiers, params string[] attributes) =>
    SecsItem.L(SecsItem.A(string.Empty), SecsItem.A(type), SecsItem.L(ids.Select(SecsItem.A)), qualifiers, SecsItem.L(attributes.Select(SecsItem.A)));
var ports = await Send(14, 1, GetAttr("Port", [], SecsItem.L(), "PortTransferState"));
Check(ports.Body!.Items[0].Count == 2 && ports.Body.Items[0].Items[0].Items[1].Items[0].Items[1].GetUInt64() == 2,
    "S14F1 查两个端口的搬运状态（空的端口等送 = 2）");
var unknownType = await Send(14, 1, GetAttr("Robot", [], SecsItem.L()));
Check(unknownType.Body!.Items[1].Items[0].GetUInt64() == 1 && unknownType.Body.Items[1].Items[1].Items[0].Items[0].GetUInt64() == 2,
    "不认识的对象类型：OBJACK=1、ERRCODE=2");

// ── E87 ─────────────────────────────────────────────────────────────────────
mark = host.EventCount;
lp1.Arrive();
await Event("Eap.E87.MaterialReceived", mark, "载具放上报 MaterialReceived");
await Event("Eap.E87.PortTransferSMTrans06", mark, "端口等送 → 挡着（#6）");
lp1.ReadId("CAR-A");
await Event("Eap.E87.CarrierSMTrans03", mark, "没预告的载具读到号：等 Host 核对（#3）");
await Event("Eap.E87.AssocSMGoAssoc", mark, "端口关联了载具");
Check(lp1.Statuses.Contains("Id:WaitingForHost"), "设备侧的载具 ID 状态写成等 Host");

SecsItem CarrierAction(string action, string carrier, byte? ptn, params SecsItem[] attributes) =>
    SecsItem.L(SecsItem.U4(0), SecsItem.A(action), SecsItem.A(carrier), ptn is null ? SecsItem.U1() : SecsItem.U1(ptn.Value), SecsItem.L(attributes));
byte Caack(HsmsMessage reply) => (byte)reply.Body!.Items[0].GetUInt64();

gem.RequestRemote(false);
Check(Caack(await Send(3, 17, CarrierAction("ProceedWithCarrier", "CAR-A", 1))) == 2, "LOCAL 时载具动作回 CAACK=2");
gem.RequestRemote(true);
mark = host.EventCount;
Check(Caack(await Send(3, 17, CarrierAction("ProceedWithCarrier", "CAR-A", 1))) == 0, "Host 让继续：CAACK=0");
await Event("Eap.E87.CarrierSMTrans08", mark, "ID 认定（#8）");
await WaitUntil(() => lp1.CarrierIdsSet.Contains("CAR-A") && lp1.LoadRequested, "认定的号写回设备，自动 Load");

mark = host.EventCount;
lp1.LoadDone();
lp1.Map(SlotState.CorrectlyOccupied, SlotState.Empty, SlotState.CorrectlyOccupied, SlotState.Empty, SlotState.Empty);
var waiting = await Event("Eap.E87.CarrierSMTrans14", mark, "读到槽图、没给期望：等 Host 核对（#14）");
await Event("Eap.E87.CarrierOpened", mark, "Load 好了报门打开");
SecsItem Content(params (string Lot, string Substrate)[] slots) =>
    SecsItem.L(SecsItem.A("ContentMap"), SecsItem.L(slots.Select(slot => SecsItem.L(SecsItem.A(slot.Lot), SecsItem.A(slot.Substrate)))));
mark = host.EventCount;
Check(Caack(await Send(3, 17, CarrierAction("ProceedWithCarrier", "CAR-A", 1,
    Content(("LOT1", "W1"), ("", ""), ("LOT1", "W3"), ("", ""), ("", ""))))) == 0, "Host 给片号表让继续：CAACK=0");
await Event("Eap.E87.CarrierSMTrans15", mark, "槽图认定（#15）");
await Event("Eap.E90.SubstSMTrans01", mark, "料到了：E90 建片对象（#1）");
Check(ledger.Get("LP1", 1)?.WaferId == "W1" && ledger.Get("LP1", 3)?.LotId == "LOT1", "Host 给的片号、批次号写进晶圆账");
var substrates = await Send(14, 1, GetAttr("Substrate", [], SecsItem.L(), "SubstLocID", "SubstState"));
Check(substrates.Body!.Items[0].Count == 2 && substrates.Body.Items[0].Items.Any(item => item.Items[0].GetString() == "W3"),
    "S14F1 查到两个片对象，片号是 Host 给的");

mark = host.EventCount;
ledger.Move("LP1", 1, "Robot", 1);
await Event("Eap.E90.SubstSMTrans02", mark, "片离开来源载具（#2）");
lp1.AccessStarted();
await Event("Eap.E87.CarrierSMTrans18", mark, "载具开始取放（#18）");
ledger.Move("Robot", 1, "PM1", 1);
await Event("Eap.E90.SubstSMTrans04", mark, "片在机内换位置（#4）");
ledger.SetProcessState("PM1", 1, WaferProcessState.InProcess);
await Event("Eap.E90.SubstSMTrans11", mark, "片开始做（#11）");
ledger.SetProcessState("PM1", 1, WaferProcessState.Completed);
var processed = await Event("Eap.E90.SubstSMTrans12", mark, "片做完（#12）");
ledger.Move("PM1", 1, "Robot", 1);
ledger.Move("Robot", 1, "LP1", 1);
await Event("Eap.E90.SubstSMTrans05", mark, "片回到载具（#5）");
mark = host.EventCount;
ledger.Move("LP1", 3, "Robot", 2);
ledger.Move("Robot", 2, "LP1", 3);
await Event("Eap.E90.SubstSMTrans14", mark, "没做就回来的片记跳过（#14）");
var w3 = await Send(14, 1, GetAttr("Substrate", ["W3"], SecsItem.L(), "SubstProcState", "SubstState", "SubstHistory"));
var w3Attributes = w3.Body!.Items[0].Items[0].Items[1];
Check(w3Attributes.Items[0].Items[1].GetUInt64() == 7 && w3Attributes.Items[1].Items[1].GetUInt64() == 2
      && w3Attributes.Items[2].Items[1].Count == 3, "W3：跳过（7）、回到载具（2）、走过三个位置");

mark = host.EventCount;
lp1.Complete();
await Event("Eap.E87.CarrierSMTrans19", mark, "载具干完（#19）");
await WaitUntil(() => lp1.UnloadRequested, "干完了自动 Unload");
lp1.UnloadDone();
await Event("Eap.E87.CarrierClosed", mark, "Unload 好了报门关上");
await Event("Eap.E87.PortTransferSMTrans09", mark, "端口挡着 → 等取（#9）");
Check(lp1.E84Provider!.GetTransferState(lp1) == LoadPortTransferState.ReadyToUnload, "E84 反查：等取走");
mark = host.EventCount;
lp1.Remove();
await Event("Eap.E87.CarrierSMTrans21", mark, "载具拿走删对象（#21）");
await Event("Eap.E87.PortTransferSMTrans08", mark, "端口挡着 → 等送（#8）");
await Event("Eap.E90.SubstSMTrans07", mark, "片跟着载具走了，片对象删掉（#7）");

// 取消：没预告的载具，Host 不要
mark = host.EventCount;
lp2.Arrive();
lp2.ReadId("CAR-B");
await Event("Eap.E87.CarrierSMTrans03", mark, "LP2 载具等 Host 核对");
Check(Caack(await Send(3, 17, CarrierAction("CancelCarrier", "CAR-B", null))) == 0, "Host 取消载具：CAACK=0");
var cancelled = await Event("Eap.E87.CarrierSMTrans09", mark, "ID 核对不过（#9）");
await Event("Eap.E87.PortTransferSMTrans09", mark, "不要的载具：端口转等取");
Check(Caack(await Send(3, 17, CarrierAction("CancelCarrier", "CAR-B", null))) == 5, "已经取消的再取消回 CAACK=5");
lp2.Remove();

// Bind：Host 预告，号对上由设备认定，读到的槽图跟 Host 给的一样也由设备认定
SecsItem SlotMapAttribute(params byte[] slots) => SecsItem.L(SecsItem.A("SlotMap"), SecsItem.L(slots.Select(slot => SecsItem.U1(slot))));
mark = host.EventCount;
Check(Caack(await Send(3, 17, CarrierAction("Bind", "CAR-C", 2, SecsItem.L(SecsItem.A("Capacity"), SecsItem.U1(5)),
    SlotMapAttribute(3, 3, 1, 1, 1)))) == 0, "Bind：CAACK=0");
await Event("Eap.E87.CarrierSMTrans02", mark, "Bind 建对象，ID 没读（#2）");
await Event("Eap.E87.ReservationSMGoReserved", mark, "Bind 预约端口");
Check(Caack(await Send(3, 17, CarrierAction("Bind", "CAR-D", 2))) == 5, "端口已经预约了再 Bind 回 CAACK=5");
var reserved = await Send(14, 1, GetAttr("Port", ["2"], SecsItem.L(), "PortReservationState", "PortAssociationState"));
Check(reserved.Body!.Items[0].Items[0].Items[1].Items[0].Items[1].GetUInt64() == 1, "S14F1 Port 2 预约了");
mark = host.EventCount;
lp2.Arrive();
await Event("Eap.E87.ReservationSMGoNotReserved", mark, "载具到了取消预约");
lp2.ReadId("CAR-C");
await Event("Eap.E87.CarrierSMTrans06", mark, "号对上预告：设备认定（#6）");
await WaitUntil(() => lp2.LoadRequested, "设备认定后自动 Load");
lp2.LoadDone();
lp2.Map(SlotState.CorrectlyOccupied, SlotState.CorrectlyOccupied, SlotState.Empty, SlotState.Empty, SlotState.Empty);
await Event("Eap.E87.CarrierSMTrans13", mark, "槽图跟 Host 给的一样：设备认定（#13）");

// 端口启停用、存取方式
SecsItem PortAction(string action, byte ptn) => SecsItem.L(SecsItem.A(action), SecsItem.U1(ptn), SecsItem.L());
mark = host.EventCount;
Check(Caack(await Send(3, 25, PortAction("OutOfService", 1))) == 0, "S3F25 停用端口 1");
await Event("Eap.E87.PortTransferSMTrans03", mark, "端口停用（#3）");
Check(lp1.E84Provider!.GetTransferState(lp1) == LoadPortTransferState.OutOfService, "停用的端口 E84 不交接");
Check(Caack(await Send(3, 25, PortAction("In Service", 1))) == 0, "S3F25 启用（带空格的写法也认）");
await Event("Eap.E87.PortTransferSMTrans02", mark, "端口启用（#2）");
mark = host.EventCount;
var s3f28 = await Send(3, 27, SecsItem.L(SecsItem.U1(1), SecsItem.L(SecsItem.U1(1))));
Check(Caack(s3f28) == 0, "S3F27 端口 1 改自动：CAACK=0");
await Event("Eap.E87.AccessSMGoAuto", mark, "端口转自动存取");
Check(lp1.IsAutoMode, "设备侧切到自动");
Check(Caack(await Send(3, 25, PortAction("ReserveAtPort", 1))) == 0, "S3F25 预约端口 1");
var frozen = await Send(3, 27, SecsItem.L(SecsItem.U1(0), SecsItem.L(SecsItem.U1(1))));
Check(Caack(frozen) == 5 && frozen.Body!.Items[1].Count == 1 && lp1.IsAutoMode, "预约着的端口不能改存取方式");
Check(Caack(await Send(3, 25, PortAction("CancelReservationAtPort", 1))) == 0, "取消预约");
Check(Caack(await Send(3, 17, CarrierAction("CarrierRelease", "CAR-C", 2))) == 1, "不支持的载具动作回 CAACK=1");
Check(Caack(await Send(3, 17, CarrierAction("Fly", "CAR-C", 2))) == 1, "不认识的载具动作回 CAACK=1");

// ── E40 / E94 ───────────────────────────────────────────────────────────────
SecsItem Recipe(string name, params SecsItem[] parameters) => SecsItem.L(SecsItem.U1(1), SecsItem.A(name), SecsItem.L(parameters));
SecsItem Material(string carrier, params byte[] slots) =>
    SecsItem.L(SecsItem.L(SecsItem.A(carrier), SecsItem.L(slots.Select(slot => SecsItem.U1(slot)))));
SecsItem CreatePj(string id, SecsItem material, SecsItem recipe, bool autoStart = true) =>
    SecsItem.L(SecsItem.U4(0), SecsItem.A(id), SecsItem.B(0x0D), material, recipe, SecsItem.Boolean(autoStart), SecsItem.L());
bool Acka(SecsItem ack) => ack.Items[0].GetBooleanArray()[0];

var s16f12 = await Send(16, 11, CreatePj("PJ-1", Material("CAR-C"), Recipe("SEQ-1")));
Check(s16f12.Body!.Items[0].GetString() == "PJ-1" && Acka(s16f12.Body.Items[1]), "S16F11 建 PJ：ACKA=TRUE");
var spec = jobs.ProcessJobs.Last();
Check(spec.CarrierId == "CAR-C" && spec.Slots.SequenceEqual([1, 2]) && spec.Sequence == "SEQ-1" && spec.AutoStart,
    "翻成 Job 管理的建 PJ：载具、槽（槽表空 = 载具上的片都做）、流程配方、自动开始");
var withParameters = await Send(16, 11, CreatePj("PJ-2", Material("CAR-C", 1), Recipe("SEQ-1", SecsItem.L(SecsItem.A("Temp"), SecsItem.U4(80)))));
Check(!Acka(withParameters.Body!.Items[1]) && withParameters.Body.Items[1].Items[1].Items[0].Items[0].GetUInt64() == 21,
    "带配方参数：ACKA=FALSE、ERRCODE 21");
jobs.NextResult = HandleResult.Fail(ErrorCodes.JobIdDuplicate, "PJ-1");
var duplicateJob = await Send(16, 11, CreatePj("PJ-1", Material("CAR-C", 1), Recipe("SEQ-1")));
var duplicateError = duplicateJob.Body!.Items[1].Items[1].Items[0];
Check(duplicateError.Items[0].GetUInt64() == 11 && duplicateError.Items[1].GetString().StartsWith(ErrorCodes.JobIdDuplicate, StringComparison.Ordinal),
    "Job 管理拒了（名字重了）：ERRCODE 11，错误文字是错误码名");
jobs.NextResult = null;
var multi = await Send(16, 15, SecsItem.L(SecsItem.U4(0), SecsItem.L(
    SecsItem.L(SecsItem.A("PJ-3"), SecsItem.B(0x0D), Material("CAR-C", 1), Recipe("SEQ-1"), SecsItem.Boolean(false), SecsItem.L()),
    SecsItem.L(SecsItem.A("PJ-4"), SecsItem.B(0x0D), SecsItem.L(), Recipe("SEQ-1"), SecsItem.Boolean(true), SecsItem.L()))));
Check(multi.Body!.Items[0].Count == 1 && multi.Body.Items[0].Items[0].GetString() == "PJ-3" && !Acka(multi.Body.Items[1]),
    "S16F15 一次建多个：建成的列出来，没料的那个报错");

var s16f6 = await Send(16, 5, SecsItem.L(SecsItem.U4(0), SecsItem.A("PJ-1"), SecsItem.A("start"), SecsItem.L()));
Check(Acka(s16f6.Body!.Items[1]) && jobs.ProcessCommands.Last() == ("PJ-1", ProcessJobCommand.Start), "S16F5 START 翻成 PJ 启动");
var unknownCommand = await Send(16, 5, SecsItem.L(SecsItem.U4(0), SecsItem.A("PJ-1"), SecsItem.A("Jump"), SecsItem.L()));
Check(!Acka(unknownCommand.Body!.Items[1]), "不认识的 PJ 命令：ACKA=FALSE");
jobs.Snapshot = new JobListDto
{
    ProcessJobs =
    [
        new ProcessJobDto { Id = "PJ-1", State = (int)ProcessJobState.Processing, Sequence = "SEQ-1", AutoStart = true, CarrierId = "CAR-C",
            Wafers = [new JobWaferDto { WaferId = "C1", SourcePort = "LP2", SourceSlot = 1 }] },
        new ProcessJobDto { Id = "PJ-3", State = (int)ProcessJobState.QueuedPooled, Sequence = "SEQ-1" },
    ],
    ControlJobs = [new ControlJobDto { Id = "CJ-1", State = 3, CarrierId = "CAR-C", ProcessJobs = ["PJ-1"], AutoStart = false }],
};
var s16f18 = await Send(16, 17, SecsItem.L());
Check(s16f18.Body!.Items[0].Count == 1 && jobs.ProcessCommands.Last() == ("PJ-3", ProcessJobCommand.Cancel), "S16F17 撤掉排队、还不归 CJ 的 PJ");
var s16f20 = await Send(16, 19);
Check(s16f20.Body!.Count == 2 && s16f20.Body.Items[0].Items[1].GetUInt64() == 3, "S16F19 列 PJ 和状态");
Check((await Send(16, 21)).Body!.GetUInt64() == ushort.MaxValue, "S16F21 还能建几个 PJ：Job 管理不限个数，答 U2 最大值");

SecsItem CreateCj(params SecsItem[] attributes) => SecsItem.L(SecsItem.A(string.Empty), SecsItem.A("ControlJob"), SecsItem.L(attributes));
SecsItem Attribute(string name, SecsItem value) => SecsItem.L(SecsItem.A(name), value);
var s14f10 = await Send(14, 9, CreateCj(Attribute("ObjID", SecsItem.A("CJ-1")),
    Attribute("ProcessingCtrlSpec", SecsItem.L(SecsItem.L(SecsItem.A("PJ-1"), SecsItem.L(), SecsItem.L()))),
    Attribute("StartMethod", SecsItem.Boolean(false))));
Check(s14f10.Body!.Items[0].GetString() == "CJ-1" && s14f10.Body.Items[2].Items[0].GetUInt64() == 0, "S14F9 建 CJ：OBJSPEC 是新 CJ、OBJACK=0");
var cjSpec = jobs.ControlJobs.Last();
Check(cjSpec.Id == "CJ-1" && cjSpec.ProcessJobs.SequenceEqual(["PJ-1"]) && !cjSpec.AutoStart, "翻成 Job 管理的建 CJ：PJ、手动启动");
var noSpec = await Send(14, 9, CreateCj(Attribute("ObjID", SecsItem.A("CJ-2"))));
Check(noSpec.Body!.Items[2].Items[0].GetUInt64() == 1 && noSpec.Body.Items[2].Items[1].Items[0].Items[0].GetUInt64() == 13,
    "没给 ProcessingCtrlSpec：OBJACK=1、ERRCODE 13");
var redirect = await Send(14, 9, CreateCj(Attribute("ObjID", SecsItem.A("CJ-3")),
    Attribute("ProcessingCtrlSpec", SecsItem.L(SecsItem.L(SecsItem.A("PJ-1"), SecsItem.L(), SecsItem.L()))),
    Attribute("MtrlOutSpec", SecsItem.L(SecsItem.L()))));
Check(redirect.Body!.Items[2].Items[1].Items[0].Items[0].GetUInt64() == 14, "改回片地方不支持：ERRCODE 14");

var s16f28 = await Send(16, 27, SecsItem.L(SecsItem.A("CJ-1"), SecsItem.U1(1), SecsItem.L()));
Check(s16f28.Body!.Items[0].GetBooleanArray()[0] && jobs.ControlCommands.Last() == ("CJ-1", ControlJobCommand.Start, ControlJobAction.SaveJobs),
    "S16F27 CjStart 翻成 CJ 启动");
await Send(16, 27, SecsItem.L(SecsItem.A("CJ-1"), SecsItem.A("CjStop"), SecsItem.L(SecsItem.A("Action"), SecsItem.U1(1))));
Check(jobs.ControlCommands.Last() == ("CJ-1", ControlJobCommand.Stop, ControlJobAction.RemoveJobs), "CjStop 带 RemoveJobs（名字写法也认）");
var badCommand = await Send(16, 27, SecsItem.L(SecsItem.A("CJ-1"), SecsItem.U1(9), SecsItem.L()));
Check(!badCommand.Body!.Items[0].GetBooleanArray()[0] && badCommand.Body.Items[1].Count == 2, "不认识的 CJ 命令：ACKA=FALSE 带一条错误");
gem.RequestRemote(false);
var local1 = await Send(16, 11, CreatePj("PJ-9", Material("CAR-C", 1), Recipe("SEQ-1")));
var local2 = await Send(14, 9, CreateCj(Attribute("ObjID", SecsItem.A("CJ-9")),
    Attribute("ProcessingCtrlSpec", SecsItem.L(SecsItem.L(SecsItem.A("PJ-1"), SecsItem.L(), SecsItem.L())))));
Check(!Acka(local1.Body!.Items[1]) && local2.Body!.Items[2].Items[0].GetUInt64() == 1, "LOCAL 时 Host 建 PJ / CJ 都不收");
gem.RequestRemote(true);

var pjAttributes = await Send(14, 1, GetAttr("ProcessJob", ["PJ-1"], SecsItem.L(), "ProcessJobState", "RecID", "PrMtlNameList"));
var pj = pjAttributes.Body!.Items[0].Items[0].Items[1];
Check(pj.Items[0].Items[1].GetUInt64() == 3 && pj.Items[1].Items[1].GetString() == "SEQ-1"
      && pj.Items[2].Items[1].Items[0].Items[0].GetString() == "CAR-C", "S14F1 查 PJ：状态、配方、料（载具号用 PJ 建的时候记下的）");
var cjAttributes = await Send(14, 1, GetAttr("ControlJob", [], SecsItem.L(), "State", "CurrentPrJob"));
var cj = cjAttributes.Body!.Items[0].Items[0].Items[1];
Check(cj.Items[0].Items[1].GetUInt64() == 3 && cj.Items[1].Items[1].Items[0].GetString() == "PJ-1", "S14F1 查 CJ：状态、在跑的 PJ");

mark = host.EventCount;
jobs.E40Callback!.ProcessJobStateChanged(jobs.Snapshot.ProcessJobs[0], 5);
await Event("Eap.E40.PrJobSMTrans05", mark, "PJ 状态转换报事件（#5）");
jobs.E94Callback!.ControlJobStateChanged(jobs.Snapshot.ControlJobs[0], 7);
await Event("Eap.E94.CtrlJobSMTrans07", mark, "CJ 状态转换报事件（#7）");

// ── 配方管理（E30 工艺程序管理，S7）：配方号就是配方名，按名字到两个库里找；新名字看 JSON 的样子分库 ─────────────
var recipes = eap.FindChild<E30RecipeComponent>()!;
Check(ReferenceEquals(sequences.E30Callback, recipes) && ReferenceEquals(processRecipes.E30Callback, recipes), "配方管理挂到两个独立的配方库接口上");
const string SequenceShape = "{\"steps\":[{\"group\":\"LoadPort\"}]}";
const string RecipeShape = "{\"steps\":[{\"values\":[{\"name\":\"Seconds\",\"value\":\"5\"}]}]}";
sequences.Items["SEQ-1"] = SequenceShape;
processRecipes.Items["R1"] = RecipeShape;
SecsItem Program(string name, SecsItem body) => SecsItem.L(SecsItem.A(name), body);
byte Ackc7(HsmsMessage reply) => reply.Body!.GetBinary()[0];

var s7f20 = await Send(7, 19);
Check(s7f20.Body!.Items.Select(item => item.GetString()).SequenceEqual(["SEQ-1", "R1"]), "S7F19 列配方：两个库的配方名，流程配方在前");
var s7f6 = await Send(7, 5, SecsItem.A("r1"));
Check(s7f6.Body!.Items[0].GetString() == "r1" && Encoding.UTF8.GetString(s7f6.Body.Items[1].GetBinary()) == RecipeShape,
    "S7F5 取配方（名字不分大小写）：PPBODY 是库给的 JSON（B，UTF-8）");
Check((await Send(7, 5, SecsItem.A("NOPE"))).Body!.Count == 0, "没有的配方：S7F6 回空表");
Check((await Send(7, 1, SecsItem.L(SecsItem.A("NEW"), SecsItem.U4(10)))).Body!.GetBinary()[0] == 0, "S7F1 问能不能下：PPGNT=0");

Check((await Send(2, 33, DefineReport(9, (uint)Dvid("Eap.Recipe.PPChangeName"), (uint)Dvid("Eap.Recipe.PPChangeStatus")))).Body!.GetBinary()[0] == 0
      && (await Send(2, 35, LinkReport((uint)Ceid("Eap.Recipe.ProcessProgramChange"), 9))).Body!.GetBinary()[0] == 0, "配方变了事件挂上报告（配方名、怎么变的）");
mark = host.EventCount;
var s7f4 = await Send(7, 3, Program("NEW", SecsItem.B(Encoding.UTF8.GetBytes(SequenceShape))));
Check(Ackc7(s7f4) == 0 && sequences.Items["NEW"] == SequenceShape && sequences.LastOperator == "Host",
    "S7F3 下一个两个库都没有的名字（B）：JSON 是流程配方的样子，存进流程配方库（操作人 Host）");
var recipeChanged = await Event("Eap.Recipe.ProcessProgramChange", mark, "库报配方变了 → 报事件");
var changeValues = recipeChanged.Body!.Items[2].Items[0].Items[1];
Check(changeValues.Items[0].GetString() == "NEW" && changeValues.Items[1].GetUInt64() == (ulong)ChangeKind.Created, "事件带配方名和怎么变的（1 新建）");
Check(Ackc7(await Send(7, 3, Program("R2", SecsItem.A(RecipeShape)))) == 0 && processRecipes.Items.ContainsKey("R2") && !sequences.Items.ContainsKey("R2"),
    "JSON 是工艺配方的样子：存进工艺配方库");
const string RecipeEdited = "{\"steps\":[{\"values\":[{\"name\":\"Seconds\",\"value\":\"9\"}]}]}";
Check(Ackc7(await Send(7, 3, Program("R1", SecsItem.A(RecipeEdited)))) == 0 && processRecipes.Items["R1"] == RecipeEdited, "已有的名字：覆盖它所在的库");
Check(Ackc7(await Send(7, 3, Program("R3", SecsItem.A("{}")))) == 1 && !processRecipes.Items.ContainsKey("R3") && !sequences.Items.ContainsKey("R3"),
    "新名字、内容看不出是哪种配方：ACKC7=1，什么都不存");
Check(Ackc7(await Send(7, 3, Program("R1", SecsItem.A("bad")))) == 1 && processRecipes.Items["R1"] == RecipeEdited, "库没收下（内容不对）：ACKC7=1，原来的不动");
Check(Ackc7(await Send(7, 17, SecsItem.L(SecsItem.A("R1"), SecsItem.A("NOPE")))) == 4 && processRecipes.Items.ContainsKey("R1"),
    "S7F17 删配方：有一个没有就一个都不删（ACKC7=4）");
Check(Ackc7(await Send(7, 17, SecsItem.L(SecsItem.A("R1")))) == 0 && !processRecipes.Items.ContainsKey("R1"), "S7F17 删工艺配方：ACKC7=0");

Check(!recipes.IsLocalEditLocked, "SC 没开：REMOTE 时本地照样能改配方");
recipes.LockLocalEditInRemote = true;
Check(recipes.IsLocalEditLocked, "SC 开了、ON-LINE REMOTE：本地改配方锁住");
gem.RequestRemote(false);
Check(!recipes.IsLocalEditLocked, "LOCAL 时不锁本地");
Check(Ackc7(await Send(7, 3, Program("NEW", SecsItem.A(SequenceShape)))) == 1 && Ackc7(await Send(7, 17, SecsItem.L())) == 1
      && (await Send(7, 1, SecsItem.L(SecsItem.A("NEW"), SecsItem.U4(10)))).Body!.GetBinary()[0] == 5 && sequences.Items.Count == 2,
    "LOCAL 时 Host 下、删配方都不收（ACKC7=1、PPGNT=5）");
Check((await Send(7, 19)).Body!.Count == 3, "LOCAL 时照样能列配方");
gem.RequestRemote(true);
recipes.LockLocalEditInRemote = false;
Check(Ackc7(await Send(7, 17, SecsItem.L())) == 0 && sequences.Items.Count == 0 && processRecipes.Items.Count == 0, "S7F17 空表 = 两个库全删");

// ── 收 ───────────────────────────────────────────────────────────────────────
eap.Close();
Check(lp1.E87Callback is null && ledger.E90Callback is null && jobs.E40Callback is null && sequences.E30Callback is null && processRecipes.E30Callback is null,
    "Close 后各标准从设备上摘下来");
connector.Dispose();
Console.WriteLine($"PASS: {checks} 项检查全部通过");

/// <summary>冒烟用的报警源：一个 [Alarm]，Fire 报出，Reset 清除。</summary>
[Component(description: "EapSmoke 报警探针")]
public sealed class AlarmProbe : ComponentBase
{
    [Alarm("冒烟报警", AlarmCategory.Other)]
    public string ProbeAlarm = "ProbeAlarm";

    public void Fire() => RaiseAlarm(ProbeAlarm);
}

/// <summary>假 Host 收到的报文：事件报告、报警、别的分开记，按到达先后。</summary>
sealed class HostLog
{
    private readonly object _gate = new();
    private readonly List<HsmsMessage> _events = [];
    private readonly List<HsmsMessage> _alarms = [];
    private readonly List<HsmsMessage> _others = [];

    public void Add(HsmsMessage message)
    {
        lock (_gate)
        {
            if (message.Header.Stream == 6 && message.Header.Function == 11)
            {
                _events.Add(message);
            }
            else if (message.Header.Stream == 5 && message.Header.Function == 1)
            {
                _alarms.Add(message);
            }
            else
            {
                _others.Add(message);
            }
        }
    }

    public int EventCount
    {
        get
        {
            lock (_gate)
            {
                return _events.Count;
            }
        }
    }

    public int AlarmCount
    {
        get
        {
            lock (_gate)
            {
                return _alarms.Count;
            }
        }
    }

    public IReadOnlyList<HsmsMessage> Alarms
    {
        get
        {
            lock (_gate)
            {
                return _alarms.ToList();
            }
        }
    }

    public IReadOnlyList<HsmsMessage> Others
    {
        get
        {
            lock (_gate)
            {
                return _others.ToList();
            }
        }
    }

    public IReadOnlyList<HsmsMessage> EventsFrom(int from)
    {
        lock (_gate)
        {
            return _events.Skip(from).ToList();
        }
    }

    public static uint CeidOf(HsmsMessage message) => (uint)message.Body!.Items[1].GetUInt64();

    public HsmsMessage? FindEvent(uint ceid, int from)
    {
        lock (_gate)
        {
            return _events.Skip(from).FirstOrDefault(message => CeidOf(message) == ceid);
        }
    }

    public async Task<HsmsMessage?> WaitEventAsync(uint ceid, int from, int timeoutMs = 4000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            var found = FindEvent(ceid, from);
            if (found is not null)
            {
                return found;
            }

            await Task.Delay(20);
        }

        return null;
    }
}

/// <summary>
/// 假的 LoadPort：状态都是字段，测试按真端口的顺序调回调（放上、读码、读槽图、Load 好、干完、Unload 好、拿走）；
/// 槽图照样落到晶圆账。EAP 发的动作（Load、Unload、改存取方式、写载具号和核对状态）都记下来。
/// </summary>
sealed class FakePort : ILoadPort
{
    private readonly WaferManagerComponent _ledger;
    private bool _loaded;
    private bool _complete;

    public FakePort(string name, WaferManagerComponent ledger)
    {
        Name = name;
        _ledger = ledger;
    }

    public string Name { get; }

    public int State => _loaded ? 110 : 30;

    public bool IsCarrierArrived { get; private set; }

    public bool IsAutoMode { get; private set; }

    public string? CarrierId { get; private set; }

    public IReadOnlyList<SlotState> SlotMap { get; private set; } = [];

    public int SlotCount => 5;

    public bool IsLoaded => _loaded;

    public bool IsIdle => !_loaded;

    public LoadPortTransferState LocalTransferState
    {
        get
        {
            if (_loaded)
            {
                return LoadPortTransferState.TransferBlocked;
            }

            if (!IsCarrierArrived)
            {
                return LoadPortTransferState.ReadyToLoad;
            }

            return _complete ? LoadPortTransferState.ReadyToUnload : LoadPortTransferState.TransferBlocked;
        }
    }

    public IE87Callback? E87Callback { get; set; }

    public IE84Callback? E84Callback { get; set; }

    public IE84Provider? E84Provider { get; set; }

    public bool LoadRequested { get; private set; }

    public bool UnloadRequested { get; private set; }

    public ConcurrentBag<string> CarrierIdsSet { get; } = [];

    public ConcurrentBag<string> Statuses { get; } = [];

    public ModuleOperation? Load()
    {
        LoadRequested = true;
        return new NoOpOperation("Load");
    }

    public ModuleOperation? Unload()
    {
        UnloadRequested = true;
        return new NoOpOperation("Unload");
    }

    public ModuleOperation? Home() => new NoOpOperation("Home");

    public ModuleOperation? Init() => new NoOpOperation("Init");

    public ModuleOperation? Reset() => new NoOpOperation("Reset");

    public ModuleOperation? Abort() => new NoOpOperation("Abort");

    public ModuleOperation? Clamp() => new NoOpOperation("Clamp");

    public ModuleOperation? Unclamp() => new NoOpOperation("Unclamp");

    public void SetAutoMode(bool autoMode)
    {
        if (IsAutoMode == autoMode)
        {
            return;
        }

        IsAutoMode = autoMode;
        E87Callback?.AutoModeChanged(this, autoMode);
    }

    public bool ReadCarrierId() => false;

    public void SetCarrierId(string carrierId)
    {
        CarrierId = carrierId;
        CarrierIdsSet.Add(carrierId);
    }

    public void UpdateCarrierStatus(CarrierIdStatus? idStatus, CarrierSlotMapStatus? slotMapStatus)
    {
        if (idStatus is not null)
        {
            Statuses.Add($"Id:{idStatus}");
        }

        if (slotMapStatus is not null)
        {
            Statuses.Add($"Map:{slotMapStatus}");
        }
    }

    public void NoteCarrierComplete()
    {
        Complete();
    }

    public void Arrive()
    {
        IsCarrierArrived = true;
        _complete = false;
        LoadRequested = false;
        UnloadRequested = false;
        E87Callback?.CarrierArrived(this);
    }

    public void ReadId(string carrierId)
    {
        CarrierId = carrierId;
        E87Callback?.CarrierIdRead(this, carrierId);
    }

    public void LoadDone()
    {
        _loaded = true;
        E87Callback?.LoadCompleted(this);
    }

    public void Map(params SlotState[] slots)
    {
        SlotMap = slots;
        _ledger.ApplySlotMap(Name, slots.Select(slot => slot == SlotState.Empty ? (WaferStatus?)null : WaferStatus.Normal).ToList(), CarrierId);
        E87Callback?.SlotMapRead(this, slots);
    }

    public void AccessStarted()
    {
        E87Callback?.AccessStarted(this);
    }

    public void Complete()
    {
        _complete = true;
        E87Callback?.CarrierComplete(this);
    }

    public void UnloadDone()
    {
        _loaded = false;
        E87Callback?.AccessStopped(this);
        E87Callback?.UnloadCompleted(this);
    }

    public void Remove()
    {
        string? carrierId = CarrierId;
        IsCarrierArrived = false;
        CarrierId = null;
        SlotMap = [];
        _ledger.Clear(Name);
        E87Callback?.CarrierRemoved(this, carrierId);
    }
}

/// <summary>假的 Job 管理：EAP 翻过来的命令都记下来，按 NextResult 回（不给就收下）；全貌由测试给。</summary>
sealed class FakeJobs : IJobManager
{
    public List<ProcessJobSpec> ProcessJobs { get; } = [];

    public List<ControlJobSpec> ControlJobs { get; } = [];

    public List<(string Id, ProcessJobCommand Command)> ProcessCommands { get; } = [];

    public List<(string Id, ControlJobCommand Command, ControlJobAction Action)> ControlCommands { get; } = [];

    public HandleResult? NextResult { get; set; }

    public JobListDto Snapshot { get; set; } = new();

    public ControlJobDto? FindControlJobByCarrier(string carrierId)
    {
        return Snapshot.ControlJobs.FirstOrDefault(job => string.Equals(job.CarrierId, carrierId, StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<ProcessJobDto> FindProcessJobsByCarrier(string carrierId)
    {
        return Snapshot.ProcessJobs.Where(job => string.Equals(job.CarrierId, carrierId, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public IE40Callback? E40Callback { get; set; }

    public IE94Callback? E94Callback { get; set; }

    private Task<HandleResult> Result(string id)
    {
        return Task.FromResult(NextResult ?? HandleResult.Success(id));
    }

    public Task<HandleResult> CreateProcessJobAsync(ProcessJobSpec spec)
    {
        ProcessJobs.Add(spec);
        return Result(spec.Id);
    }

    public Task<HandleResult> CreateControlJobAsync(ControlJobSpec spec)
    {
        ControlJobs.Add(spec);
        return Result(spec.Id);
    }

    public Task<HandleResult> ExecuteControlJobCommandAsync(string id, ControlJobCommand command, ControlJobAction action)
    {
        ControlCommands.Add((id, command, action));
        return Result(id);
    }

    public Task<HandleResult> ExecuteProcessJobCommandAsync(string id, ProcessJobCommand command)
    {
        ProcessCommands.Add((id, command));
        return Result(id);
    }
}

// 假配方库：名字 → JSON；内容是 "bad" 当没过库的检查；每一步带 stepKey 这个属性的 JSON 认作自己的（流程配方 group、工艺配方 values）。
// 跟真的库一样，变了经 EAP 的派发组件调上报口。
abstract class FakeRecipes(string stepKey)
{
    public Dictionary<string, string> Items { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string LastOperator { get; private set; } = string.Empty;

    public IE30Callback? E30Callback { get; set; }

    public IReadOnlyList<string> RecipeNames => Items.Keys.ToList();

    public string? ExportRecipe(string name)
    {
        return Items.TryGetValue(name, out string? json) ? json : null;
    }

    public bool AcceptsRecipe(string json)
    {
        return json.Contains($"\"{stepKey}\"", StringComparison.Ordinal);
    }

    public HandleResult ImportRecipe(string name, string json, string operatorName)
    {
        if (json == "bad")
        {
            return HandleResult.Fail(ErrorCodes.SequenceBodyInvalid, name);
        }

        bool created = !Items.ContainsKey(name);
        Items[name] = json;
        LastOperator = operatorName;
        Notify(name, created ? ChangeKind.Created : ChangeKind.Edited);
        return HandleResult.Success();
    }

    public HandleResult DeleteRecipe(string name, string operatorName)
    {
        if (!Items.Remove(name))
        {
            return HandleResult.Fail(ErrorCodes.SequenceNameNotFound, name);
        }

        LastOperator = operatorName;
        Notify(name, ChangeKind.Deleted);
        return HandleResult.Success();
    }

    protected abstract void Notify(string name, ChangeKind change);
}

sealed class FakeSequences() : FakeRecipes("group"), ISequenceComponent
{
    public IReadOnlyList<string> SequenceNames => RecipeNames;

    public string? ExportSequence(string name) => ExportRecipe(name);

    public bool AcceptsSequence(string json) => AcceptsRecipe(json);

    public HandleResult ImportSequence(string name, string json, string operatorName) => ImportRecipe(name, json, operatorName);

    public HandleResult DeleteSequence(string name, string operatorName) => DeleteRecipe(name, operatorName);

    protected override void Notify(string name, ChangeKind change)
    {
        var callback = E30Callback;
        if (callback is not null)
        {
            EapNotifierComponent.Current?.Post(() => callback.SequenceChanged(name, change));
        }
    }
}

sealed class FakeProcessRecipes() : FakeRecipes("values"), IProcessRecipeComponent
{
    public IReadOnlyList<string> ProcessRecipeNames => RecipeNames;

    public string? ExportProcessRecipe(string name) => ExportRecipe(name);

    public bool AcceptsProcessRecipe(string json) => AcceptsRecipe(json);

    public HandleResult ImportProcessRecipe(string name, string json, string operatorName) => ImportRecipe(name, json, operatorName);

    public HandleResult DeleteProcessRecipe(string name, string operatorName) => DeleteRecipe(name, operatorName);

    protected override void Notify(string name, ChangeKind change)
    {
        var callback = E30Callback;
        if (callback is not null)
        {
            EapNotifierComponent.Current?.Post(() => callback.ProcessRecipeChanged(name, change));
        }
    }
}
