using System.IO;
using System.Reflection;
using System.ServiceModel;
using System.Text;
using MiniExcelLibs;
using NLog;
using ProtoBuf.Grpc;
using SqlSugar;
using xyz.Client.DataCenter.Models;
using xyz.Client.Presentation.Controls;
using xyz.Client.Presentation.Models;
using xyz.Common.Log;
using xyz.Components;
using xyz.Components.Attributes;
using xyz.Components.Collectors;
using xyz.Components.Components;
using xyz.Components.Enums;
using xyz.Components.Interfaces;
using xyz.Configs.Models;
using xyz.Database.Alarms;
using xyz.Database.DbProvider;
using xyz.Service;
using xyz.Service.Alarms;
using xyz.Service.Charts;
using xyz.Service.Events;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Shared.Services;
using xyz.Tools;

// 数据中心冒烟：日志文件解析与日志历史查询、报警服务（当前报警、人工复位、报警历史）、数据曲线（采样、信号名、日表）。
// 不起宿主、不连设备；日志样本写在运行目录 Log 下一个不会跟真日志撞的日期，报警记录、数据曲线写临时库，跑完都删掉。
SmokeIoc.Reset();

var checks = 0;
void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException("FAIL: " + message);
    }

    checks++;
}

static string Line(DateTime time, LogLevel level, string module, string message) =>
    new LogItem(module, string.Empty, level, message) { Time = time }.ToString();

var day = new DateTime(2020, 1, 2);
Directory.CreateDirectory(LogFileReader.LogDirectory);
var stem = Path.Combine(LogFileReader.LogDirectory, $"xyz-{day:yyyy-MM-dd}");
var day2 = new DateTime(2020, 1, 3);
var stem2 = Path.Combine(LogFileReader.LogDirectory, $"xyz-{day2:yyyy-MM-dd}");
var dbFile = Path.Combine(Path.GetTempPath(), $"datacenter-smoke-{Guid.NewGuid():N}.db");

try
{
    // ── 1. 日志文件：按 LogItem 的格式逐行解析；续行（异常堆栈）归到上一条；当天分出去的 .1 文件先读 ─────
    File.WriteAllLines(stem + ".1.log", [Line(day.AddHours(8), LogLevel.Info, "LoadPort1", "旧文件里的一条")], Encoding.UTF8);
    File.WriteAllLines(stem + ".log",
    [
        Line(day.AddHours(9), LogLevel.Info, "LoadPort1", "Home 完成"),
        Line(day.AddHours(10), LogLevel.Warn, "Robot1", "取片慢"),
        Line(day.AddHours(11), LogLevel.Error, "Robot1", "异常：连接断开"),
        "   at xyz.Drivers.Foo()",
        "   at xyz.Drivers.Bar()",
        Line(day.AddHours(12), LogLevel.Debug, "E84", "信号变化"),
    ], Encoding.UTF8);

    var items = LogFileReader.Read(day, day.AddDays(1)).ToList();
    Check(items.Count == 5, $"应读出 5 条，实际 {items.Count}");
    Check(items[0].Message == "旧文件里的一条", "当天分出去的 .1 文件先读");
    Check(items[3].Level == LogLevel.Error && items[3].Module == "Robot1"
          && items[3].Message == "异常：连接断开\n   at xyz.Drivers.Foo()\n   at xyz.Drivers.Bar()", "续行归到上一条");
    Check(items[4].Time == day.AddHours(12) && items[4].Level == LogLevel.Debug, "时刻、级别原样解析");
    Check(!LogFileReader.TryParse("   at xyz.Drivers.Foo()", out _), "续行不是一条日志的开头");

    // ── 2. 日志历史查询：最新的在前；按级别、关键字（不分大小写）、时间段筛；超出条数只留最新的 ─────
    var logService = new LogService();
    async Task<HistoryResult<LogDto>> QueryLogs(LogHistoryQuery query)
    {
        var response = await logService.QueryHistoryAsync(query);
        Check(response.Success, "日志查询应成功");
        return JsonHelper.Deserialize<HistoryResult<LogDto>>(response.Data)!;
    }

    var all = await QueryLogs(new LogHistoryQuery { Start = day, End = day.AddDays(1) });
    Check(all.Items.Count == 5 && all.Items[0].Module == "E84" && all.Items[^1].Message == "旧文件里的一条" && !all.Truncated,
        "全部 5 条，最新的在前");
    Check(all.Items.All(log => log.Source == "Server") && all.Items[0].Level == "Debug", "来源 Server，级别用 Info/Warn 这种名字");
    Check((await QueryLogs(new LogHistoryQuery { Start = day, End = day.AddDays(1), Keyword = "robot1" })).Items.Count == 2,
        "关键字按模块筛，不分大小写");
    Check((await QueryLogs(new LogHistoryQuery { Start = day, End = day.AddDays(1), Keyword = "堆栈不存在" })).Items.Count == 0,
        "关键字对不上就没有");
    Check((await QueryLogs(new LogHistoryQuery { Start = day, End = day.AddDays(1), Keyword = "Bar()" })).Items.Count == 1,
        "关键字也查续行里的内容");
    Check((await QueryLogs(new LogHistoryQuery { Start = day, End = day.AddDays(1), Level = "Error" })).Items.Count == 1,
        "按级别筛");
    Check((await QueryLogs(new LogHistoryQuery { Start = day.AddHours(9), End = day.AddHours(11) })).Items.Count == 2,
        "时间段含头不含尾");
    var limited = await QueryLogs(new LogHistoryQuery { Start = day, End = day.AddDays(1), MaxCount = 2 });
    Check(limited.Truncated && limited.Items.Count == 2 && limited.Items[0].Module == "E84" && limited.Items[1].Level == "Error",
        "超出条数只留最新的，并标出截断");
    Check((await QueryLogs(new LogHistoryQuery { Start = day.AddDays(3), End = day.AddDays(4) })).Items.Count == 0,
        "没有日志文件的日子查出来为空");

    // ── 2b. 最近日志、实时日志：日志组件 Bind 时接上日志队列（接上之前攒在队列里的也补进来），留最近 RecentLogCount 条供客户端连上补拉，
    //     每条推给界面（发生类，不留存）；日志服务从日志组件取最近一段。队列只收 Warn 以上（LogQueue.MinLevel 默认）─────
    var logComponent = new LogComponent { RecentLogCount = 3 };
    SmokeIoc.Use<ILogComponent>(logComponent);
    var pushedLogs = new List<LogDto>();
    bool Pushed(string message)
    {
        lock (pushedLogs)
        {
            return pushedLogs.Any(log => log.Message == message);
        }
    }

    using (EventBus.Register<LogDto>(LogDto.EventToken, dto =>
           {
               lock (pushedLogs)
               {
                   pushedLogs.Add(dto);
               }
           }))
    {
        LogHelper.Warn("SmokeLog", "接上之前的一条");
        logComponent.Bind();
        for (int index = 1; index <= 4; index++)
        {
            LogHelper.Warn("SmokeLog", $"第 {index} 条");
        }

        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline && !Pushed("第 4 条"))
        {
            Thread.Sleep(20);
        }
    }

    var recentLogs = logComponent.Recent().Where(log => log.Module == "SmokeLog").ToList();
    Check(recentLogs.Count == 3 && recentLogs[^1].Message == "第 4 条" && recentLogs.All(log => log.Source == "Server" && log.Level == "Warn"),
        "最近一段只留 RecentLogCount 条，最旧的丢掉");
    lock (pushedLogs)
    {
        Check(pushedLogs.Count(log => log.Module == "SmokeLog") == 5 && Pushed("接上之前的一条"),
            "每条都推给界面；接上之前攒在队列里的也补推");
    }

    var recentReply = await new LogService().GetRecentAsync(new LogQuery { Count = 2 });
    var recentTail = JsonHelper.Deserialize<List<LogDto>>(recentReply.Data)!;
    Check(recentReply.Success && recentTail.Count == 2 && recentTail[^1].Message == "第 4 条", "日志服务从日志组件取最近一段，带了条数只回最新的几条");

    // ── 2c. 两层名字、收发原文、操作日志：文件格式和历史查询的模块筛选 ─────
    File.WriteAllLines(stem2 + ".log",
    [
        new LogItem("LoadPort.LoadPort1", string.Empty, LogLevel.Info, "Load 做完 5746 ms") { Time = day2.AddHours(9) }.ToString(),
        new LogItem("LoadPort.LoadPort1", "Serial", LogLevel.Info, "MOV:CLOAD") { Time = day2.AddHours(9).AddSeconds(1), Direction = LogHelper.Sent }.ToString(),
        new LogItem("LoadPort.LoadPort1", "RFID.Serial", LogLevel.Warn, "读码超时（3000ms）") { Time = day2.AddHours(9).AddSeconds(2) }.ToString(),
        new LogItem(LogHelper.OperationModule, string.Empty, LogLevel.Info, "张三@192.168.1.20 LoadPort.Load \"LoadPort1\"") { Time = day2.AddHours(9).AddSeconds(3) }.ToString(),
        new LogItem("LoadPort.LoadPort2", string.Empty, LogLevel.Error, "Unload 失败在 关门") { Time = day2.AddHours(10) }.ToString(),
    ], Encoding.UTF8);
    var parsed2 = LogFileReader.Read(day2, day2.AddDays(1)).ToList();
    Check(parsed2.Count == 5 && parsed2[1].Module == "LoadPort.LoadPort1" && parsed2[1].Part == "Serial" && parsed2[1].Direction == LogHelper.Sent
          && parsed2[1].Message == "MOV:CLOAD" && parsed2[2].Part == "RFID.Serial" && parsed2[0].Part.Length == 0,
        "新格式：部件那一格有就解出来、没有就空；收发原文的级别那一格是方向");
    Check(LogFileReader.TryParse("【2020-01-03 09:00:00.000】 【INFO】 【LoadPort1】 改之前的旧格式", out var oldItem)
          && oldItem.Module == "LoadPort1" && oldItem.Part.Length == 0 && oldItem.Message == "改之前的旧格式",
        "改之前写的旧格式照样认");

    var byModule = await QueryLogs(new LogHistoryQuery { Start = day2, End = day2.AddDays(1), Module = "LoadPort.LoadPort1" });
    Check(byModule.Items.Count == 3 && byModule.Items.All(log => log.Module == "LoadPort.LoadPort1"),
        "按模块筛：只要第一层完全一样的，它的部件（串口、RFID）也一起出来");
    Check(byModule.Items.Any(log => log.Part == "RFID.Serial") && byModule.Items.Any(log => log.Level == LogHelper.Sent && log.Part == "Serial"),
        "查出来带部件；收发原文的级别显示方向");
    Check((await QueryLogs(new LogHistoryQuery { Start = day2, End = day2.AddDays(1), Module = "LoadPort" })).Items.Count == 0,
        "模块要完全一样：只写分组名查不到");
    Check((await QueryLogs(new LogHistoryQuery { Start = day2, End = day2.AddDays(1), Module = LogHelper.OperationModule })).Items.Count == 1,
        "选 Operation 就是谁点了什么");
    Check((await QueryLogs(new LogHistoryQuery { Start = day2, End = day2.AddDays(1), Level = "Info" })).Items.All(log => log.Level == "Info")
          && (await QueryLogs(new LogHistoryQuery { Start = day2, End = day2.AddDays(1), Level = "Info" })).Items.Count == 2,
        "选了级别就不要收发原文（它们没有级别）");
    Check((await QueryLogs(new LogHistoryQuery { Start = day2, End = day2.AddDays(1), Keyword = "rfid" })).Items.Count == 1,
        "关键字也搜部件");

    // LogHelper 真写文件：用后端的 NLog.config（这个冒烟也引了客户端，运行目录里那份不一定是后端的）
    string repoRoot = AppContext.BaseDirectory;
    while (!File.Exists(Path.Combine(repoRoot, "xyz.Framework.sln")))
    {
        repoRoot = Path.GetDirectoryName(repoRoot.TrimEnd(Path.DirectorySeparatorChar))!;
    }

    LogManager.Setup().LoadConfigurationFromFile(Path.Combine(repoRoot, "xyz.Core", "Service", "xyz.Common", "Log", "NLog.config"));
    var writeStarted = DateTime.Now.AddMilliseconds(-1);
    var serial = new SmokeLogSource("LoadPort.LoadPort1", "Serial");
    LogHelper.Comm(serial, LogHelper.Sent, "MOV:CLOAD\r");
    LogHelper.InfoLoop(serial, LogHelper.Received, "ACK:STATE/0100-smoke");
    LogHelper.Info(new SmokeLogSource("Eap", "E87"), "两层名字写进文件");

    // 操作日志：经接口 IActionFilter 测 ActionFilter（宿主的 ActionFilter 每个调用把方法名、来路、请求头里的操作人交给它）：
    // 记谁、哪台电脑、调了什么、参数，回包记结果；Get / Query 和自动反复发的不记；密码写成 ***
    IActionFilter actionLog = new ActionFilter();
    var load = actionLog.Begin("/xyz.Shared.Services.LoadPortService/Load", "ipv4:192.168.1.20:5123", "张三", "LoadPort1");
    Check(load is not null, "LoadPort.Load 要记");
    if (load is not null)
    {
        actionLog.End(load, RpcResponse.Ok());
    }

    var home = actionLog.Begin("/xyz.Shared.Services.LoadPortService/HomeAsync", "ipv6:[::1]:5123", "李四", "LoadPort1");
    if (home is not null)
    {
        actionLog.End(home, RpcResponse.Fail(ErrorCodes.ActionRejected, ["LoadPort1", "3"]));
    }

    Check(actionLog.Begin("/xyz.Shared.Services.LoadPortService/GetStatus", "ipv4:127.0.0.1:50000", "张三", new RpcRequest()) is null
          && actionLog.Begin("/xyz.Shared.Services.LogService/QueryHistory", "ipv4:127.0.0.1:50000", "张三", new RpcRequest()) is null
          && actionLog.Begin("/xyz.Shared.Services.ChamberService/AxisJogRenew", "ipv4:127.0.0.1:50000", "张三", new RpcRequest()) is null
          && actionLog.Begin("/xyz.Shared.Services.EventService/Subscribe", "ipv4:127.0.0.1:50000", "张三", new RpcRequest()) is null
          && actionLog.Begin("/xyz.Shared.Services.EventService/Publish", "ipv4:127.0.0.1:50000", "张三", new RpcRequest()) is null,
        "Get / Query 开头的、续点动、订阅推送、上行发布不记");
    string noHeader = RpcOperator.Of(SmokeCall.Context(null, "/xyz.Shared.Services.AccountService/Login"));
    var login = actionLog.Begin("/xyz.Shared.Services.AccountService/Login", "ipv4:127.0.0.1:50000", noHeader,
        new LoginRequest { UserName = "张三", Password = "secret-smoke" });
    if (login is not null)
    {
        actionLog.End(login, RpcResponse.Ok());
    }

    var unload = actionLog.Begin("/xyz.Shared.Services.LoadPortService/Unload", "ipv4:127.0.0.1:50000", "张三", "LoadPort1");
    if (unload is not null)
    {
        actionLog.Fault(unload, new InvalidOperationException("smoke-boom"));
    }

    Check(RpcOperator.Of(SmokeCall.As("王五")) == "王五" && RpcOperator.Of(default(CallContext)) == RpcOperator.Unknown,
        "操作人从请求头取（中文名照样）；没带记 Unknown");

    LogHelper.Flush(TimeSpan.FromSeconds(3));
    var written = LogFileReader.Read(writeStarted, DateTime.Now.AddSeconds(1)).ToList();
    Check(written.Any(item => item.Direction == LogHelper.Sent && item.Module == "LoadPort.LoadPort1" && item.Part == "Serial" && item.Message == "MOV:CLOAD<CR>"),
        "收发原文进主日志：方向、两层名字，回车写成 <CR>");
    Check(!written.Any(item => item.Message.Contains("ACK:STATE/0100-smoke")), "轮询原文不进主日志");
    string pollFile = Path.Combine(LogFileReader.LogDirectory, $"poll-{DateTime.Today:yyyy-MM-dd}.log");
    string pollText = string.Empty;
    using (var stream = new FileStream(pollFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
    using (var reader = new StreamReader(stream, Encoding.UTF8))
    {
        pollText = reader.ReadToEnd();
    }

    Check(pollText.Contains("【收】 【LoadPort.LoadPort1】 【Serial】 ACK:STATE/0100-smoke"), "轮询原文记在轮询文件 poll-日期.log");
    Check(written.Any(item => item.Module == "Eap" && item.Part == "E87" && item.Message == "两层名字写进文件"), "运行日志带两层名字写进文件");

    var operations = written.Where(item => item.Module == LogHelper.OperationModule).Select(item => item.Message).ToList();
    Check(operations.Contains("张三@192.168.1.20 LoadPort.Load \"LoadPort1\"") && operations.Any(text => text.StartsWith("张三@192.168.1.20 LoadPort.Load → 成功 ")),
        "操作日志：请求进来一条（谁@哪台电脑 服务.方法 参数），回包一条（成功、用时）");
    Check(operations.Any(text => text.StartsWith("李四@::1 LoadPort.Home → 失败 module.action_rejected [LoadPort1, 3] "))
          && written.Any(item => item.Module == LogHelper.OperationModule && item.Level == LogLevel.Warn && item.Message.StartsWith("李四@::1 LoadPort.Home → 失败")),
        "回的是失败记 Warn，带错误码和参数；方法名去掉 Async，IPv6 去掉方括号");
    Check(!operations.Any(text => text.Contains("GetStatus") || text.Contains("AxisJogRenew")), "Get / Query 开头的、续点动不记");
    Check(operations.Any(text => text.StartsWith("Unknown@127.0.0.1 Account.Login ") && text.Contains("\"password\":\"***\"") && text.Contains("\"userName\":\"张三\""))
          && !operations.Any(text => text.Contains("secret-smoke")),
        "没带操作人记 Unknown；参数里的密码写成 ***，中文原样（不转成 \\uXXXX）");
    Check(written.Any(item => item.Module == LogHelper.OperationModule && item.Level == LogLevel.Error && item.Message.Contains("LoadPort.Unload → 出错") && item.Message.EndsWith("smoke-boom")),
        "服务抛异常记 Error");
    lock (pushedLogs)
    {
        Check(!pushedLogs.Any(log => log.Module == LogHelper.OperationModule || log.Message.Contains("MOV:CLOAD")),
            "操作日志、收发原文不推界面");
    }

    // 保留天数：日志组件开机按 KeepDays 设，主日志、轮询文件都按天清掉更早的（文件名带日期也认），近的留着
    var oldMain = Path.Combine(LogFileReader.LogDirectory, $"xyz-{DateTime.Today.AddDays(-40):yyyy-MM-dd}.log");
    var oldMainSplit = Path.Combine(LogFileReader.LogDirectory, $"xyz-{DateTime.Today.AddDays(-40):yyyy-MM-dd}.1.log");
    var oldPoll = Path.Combine(LogFileReader.LogDirectory, $"poll-{DateTime.Today.AddDays(-40):yyyy-MM-dd}.log");
    var recentMain = Path.Combine(LogFileReader.LogDirectory, $"xyz-{DateTime.Today.AddDays(-5):yyyy-MM-dd}.log");
    foreach (var (path, ageDays) in new[] { (oldMain, 40), (oldMainSplit, 40), (oldPoll, 40), (recentMain, 5) })
    {
        File.WriteAllText(path, "smoke", Encoding.UTF8);
        File.SetCreationTime(path, DateTime.Now.AddDays(-ageDays));
        File.SetLastWriteTime(path, DateTime.Now.AddDays(-ageDays));
    }

    LogHelper.SetKeepDays(30);
    LogHelper.Info(LogHelper.HostModule, "保留天数冒烟");
    LogHelper.InfoLoop(serial, LogHelper.Sent, "GET:STATE");
    LogHelper.Flush(TimeSpan.FromSeconds(3));
    var keepDeadline = DateTime.UtcNow.AddSeconds(3);
    while (DateTime.UtcNow < keepDeadline && (File.Exists(oldMain) || File.Exists(oldPoll)))
    {
        Thread.Sleep(50);
    }

    Check(!File.Exists(oldMain) && !File.Exists(oldMainSplit) && !File.Exists(oldPoll) && File.Exists(recentMain),
        "KeepDays：40 天前的主日志（连拆出来的）、轮询文件删了，5 天前的留着");
    File.Delete(recentMain);

    // 日志的第一层：组件树最顶上那层（带外面的分组名），按 machine.xml 先后；下面的部件是第二层；再加 Operation、Host
    var logTree = ComponentLoader.Load(
    [
        new ModuleConfig
        {
            Name = "LoadPort",
            Children =
            {
                new ModuleConfig
                {
                    Name = "LoadPort1",
                    Type = typeof(MapperComponent).FullName,
                    Children = { new ModuleConfig { Name = "Serial", Type = typeof(MapperComponent).FullName } },
                },
            },
        },
        new ModuleConfig { Name = "Plc", Type = typeof(MapperComponent).FullName },
    ]);
    Check(logTree.Count == 2 && logTree[0].LogModule == "LoadPort.LoadPort1" && logTree[0].LogPart.Length == 0
          && logTree[0].Children[0].LogModule == "LoadPort.LoadPort1" && logTree[0].Children[0].LogPart == "Serial"
          && logTree[1].LogModule == "Plc",
        "两层名字：分组下的模块第一层带分组名，它下面的部件第一层同它、第二层是剩下的路径");
    SmokeIoc.Use<IReadOnlyList<ComponentBase>>(logTree);
    var moduleLog = new LogComponent { KeepDays = 30 };
    moduleLog.Bind();
    Check(moduleLog.Modules.SequenceEqual(["LoadPort.LoadPort1", "Plc", LogHelper.OperationModule, LogHelper.HostModule]),
        "模块下拉的选项：树最顶上那层按先后，再加 Operation、Host");
    SmokeIoc.Use<ILogComponent>(moduleLog);
    var settingsReply = await new LogService().GetSettingsAsync(new RpcRequest());
    var logSettings = JsonHelper.Deserialize<LogSettingsDto>(settingsReply.Data)!;
    Check(settingsReply.Success && logSettings.KeepDays == 30 && logSettings.Modules.SequenceEqual(moduleLog.Modules),
        "日志设置带给客户端：保留天数、模块下拉的选项");
    SmokeIoc.Use<ILogComponent>(logComponent);
    SmokeIoc.Use<IReadOnlyList<ComponentBase>>(null);

    // ── 3. 当前报警与人工复位：走报警组件；复位不存在的来源给错误码；没装报警组件给错误码 ─────
    var alarms = new AlarmComponent();
    SmokeIoc.Use<IAlarm>(alarms);
    SmokeIoc.Use<IMapper>(new MapperComponent()); // 报警、EC、曲线推给界面和服务回包都靠它转（开机必配）
    alarms.Bind(); // 开机 Bind 时取映射组件
    var device = new ProbeDevice("Tool1");
    var alarmService = new AlarmService();
    var pushedAlarms = new List<AlarmDto>();
    using var alarmPushes = EventBus.Register<AlarmDto>(AlarmDto.EventToken, dto => pushedAlarms.Add(dto));
    device.Fault();
    var active = JsonHelper.Deserialize<List<AlarmDto>>((await alarmService.GetActiveAsync(new RpcRequest())).Data)!;
    Check(active.Count == 1 && active[0].Source == "Tool1" && active[0].Code == "ProbeFault"
          && active[0].Level == "Alarm1" && active[0].IsActive, "当前报警带来源、代码、等级");
    Check(active[0].Text == "探针故障" && active[0].Solution == "检查探针后复位", "报警文本与处理建议");
    Check(pushedAlarms.Count == 1 && pushedAlarms[0].Source == "Tool1" && pushedAlarms[0].Code == "ProbeFault" && pushedAlarms[0].IsActive,
        "报警组件自己推给界面：报出来推一条（照映射组件的规则转）");

    var unknown = await alarmService.ResetAsync(new RpcRequest { Parameters = { ["Source"] = "Nobody" } });
    Check(!unknown.Success && unknown.Code == ErrorCodes.AlarmSourceNotFound
          && unknown.Args.Count == 1 && unknown.Args[0] == "Nobody",
        "复位没报过报警的来源：给错误码和来源");
    var reset = await alarmService.ResetAsync(new RpcRequest { Parameters = { ["Source"] = "Tool1" } });
    Check(reset.Success && alarms.ActiveAlarms.Count == 0, "复位来源后报警清掉");
    Check(pushedAlarms.Count == 2 && pushedAlarms[1].Source == "Tool1" && !pushedAlarms[1].IsActive, "清掉也推一条");

    device.Fault();
    var resetAll = await alarmService.ResetAllAsync(new RpcRequest());
    Check(resetAll.Success && JsonHelper.Deserialize<int>(resetAll.Data) == 1 && alarms.ActiveAlarms.Count == 0,
        "全部复位：复位了 1 个来源");

    SmokeIoc.Use<IAlarm>(null);
    alarmService = new AlarmService(); // 服务在构造时取单例（宿主每次调用都新建一个），换了单例要重新建
    var notInstalled = await alarmService.ResetAllAsync(new RpcRequest());
    Check(!notInstalled.Success && notInstalled.Code == ErrorCodes.AlarmNotInstalled, "没装报警组件：给错误码");
    Check(JsonHelper.Deserialize<List<AlarmDto>>((await alarmService.GetActiveAsync(new RpcRequest())).Data)!.Count == 0,
        "没装报警组件：当前报警为空");

    // ── 4. 报警历史：按天分表的记录表，最新的在前；按等级、关键字筛；超出条数截断；没有日表的日子为空 ─────
    XyzDb.Register(XyzDb.DefaultName, $"DataSource={dbFile}", DbType.Sqlite);
    var today = DateTime.Today;
    using (var db = XyzDb.Create())
    {
        db.CodeFirst.SplitTables().InitTables<AlarmHistoryEntity>();
        db.Insertable(new List<AlarmHistoryEntity>
        {
            new() { Action = "Raised", Source = "Tool1", Code = "ProbeFault", Text = "探针故障", Category = "OtherCategories", Level = "Alarm1",
                    RaisedAt = today.AddHours(8), OccurredAt = today.AddHours(8) },
            new() { Action = "Cleared", Source = "Tool1", Code = "ProbeFault", Text = "探针故障", Category = "OtherCategories", Level = "Alarm1",
                    RaisedAt = today.AddHours(8), OccurredAt = today.AddHours(8).AddMinutes(5) },
            new() { Action = "Raised", Source = "Tool2", Code = "Crash", Text = "撞机", Category = "IrrecoverableError", Level = "Fatal",
                    RaisedAt = today.AddHours(9), OccurredAt = today.AddHours(9) },
        }).SplitTable().ExecuteCommand();
    }

    async Task<HistoryResult<AlarmHistoryDto>> QueryAlarms(AlarmHistoryQuery query)
    {
        var response = await alarmService.QueryHistoryAsync(query);
        Check(response.Success, $"报警历史查询应成功：{response.Message}");
        return JsonHelper.Deserialize<HistoryResult<AlarmHistoryDto>>(response.Data)!;
    }

    var history = await QueryAlarms(new AlarmHistoryQuery { Start = today, End = today.AddDays(1) });
    Check(history.Items.Count == 3 && history.Items[0].Code == "Crash" && history.Items[1].Action == "Cleared" && !history.Truncated,
        "全部 3 条，最新的在前");
    Check(history.Items[1].RaisedAt == today.AddHours(8) && history.Items[1].OccurredAt == today.AddHours(8).AddMinutes(5),
        "清除那条带着报出时刻");
    Check((await QueryAlarms(new AlarmHistoryQuery { Start = today, End = today.AddDays(1), Level = "Fatal" })).Items.Count == 1,
        "按等级筛");
    Check((await QueryAlarms(new AlarmHistoryQuery { Start = today, End = today.AddDays(1), Keyword = "探针" })).Items.Count == 2,
        "关键字查报警文本");
    Check((await QueryAlarms(new AlarmHistoryQuery { Start = today, End = today.AddDays(1), Keyword = "Tool2" })).Items.Count == 1,
        "关键字查来源");
    var truncated = await QueryAlarms(new AlarmHistoryQuery { Start = today, End = today.AddDays(1), MaxCount = 2 });
    Check(truncated.Truncated && truncated.Items.Count == 2, "超出条数截断");
    Check((await QueryAlarms(new AlarmHistoryQuery { Start = day, End = day.AddDays(1) })).Items.Count == 0,
        "没有日表的日子查出来为空，不报错");
}
finally
{
    File.Delete(stem + ".1.log");
    File.Delete(stem + ".log");
    File.Delete(stem2 + ".log");
    SqliteCleanup(dbFile);
}

// ── 5. 数据曲线：在自己的扫描里采（配了 IsRun 才采、整周期一行）；信号名按 __ 分层；库表全走 SqlSugar（建表、写、查、开机补列、清过期日表）；名字不合规开机就报 ─────
var chartDbFile = Path.Combine(Path.GetTempPath(), $"datachart-smoke-{Guid.NewGuid():N}.db");
var chartGemDirectory = Path.Combine(Path.GetTempPath(), $"datachart-smoke-{Guid.NewGuid():N}");
Directory.CreateDirectory(chartGemDirectory);
try
{
    XyzDb.Register("ChartSmoke", $"DataSource={chartDbFile}", DbType.Sqlite);

    // 一个模块带两个部件，每个都有一个模拟量 SV、一个开关量 SV；名字合规与否由部件名定。
    List<ComponentBase> ChartTree(params string[] parts)
    {
        var root = new ModuleConfig { Name = "Chart1", Type = typeof(ChartProbe).FullName };
        root.Children.AddRange(parts.Select(part => new ModuleConfig { Name = part, Type = typeof(ChartProbe).FullName }));
        var roots = SmokeIoc.Load([root]);
        var chartGem = new GemCollectors();
        chartGem.Merge(roots, chartGemDirectory);
        SmokeIoc.Use(chartGem); // 数据曲线 Bind 时从容器取 GEM 编号表
        return roots;
    }

    // 跑一小会儿：200ms 采一行，在它自己的扫描里采（跟 machine.xml 配 IsRun 一样起），停下时把攒着的一批写完。
    int sampledRows = 0;
    DataChartComponent RunChart(List<ComponentBase> roots)
    {
        var chart = new DataChartComponent { Database = "ChartSmoke", SampleIntervalMs = 200, IsRun = true };
        SmokeIoc.Use<IDataChart>(chart);
        var times = new List<long>();
        chart.Records.Subscribe(record =>
        {
            lock (times)
            {
                times.Add(record.Time);
            }
        });
        chart.Bind(roots);
        ComponentLoader.StartScanning([chart]);
        Thread.Sleep(1300);
        chart.StopSampling();
        lock (times)
        {
            Check(times.Count >= 3 && times.All(time => time % 200 == 0) && times.Zip(times.Skip(1)).All(pair => pair.First < pair.Second),
                $"在自己的扫描里采：整 200ms 一行、不重复（{times.Count} 行）");
            sampledRows = times.Count;
        }

        return chart;
    }

    using (var db = XyzDb.Create("ChartSmoke"))
    {
        db.DbMaintenance.CreateTable("DataRecord_20000101",
        [
            new DbColumnInfo { TableName = "DataRecord_20000101", DbColumnName = "Time", DataType = db.Ado.DbBind.GetDbTypeName("Int64"), IsPrimarykey = true },
        ]);
    }

    var first = RunChart(ChartTree("Door"));
    int firstRows = sampledRows;
    var names = first.Signals.Select(signal => signal.Name).ToList();
    Check(names.ToHashSet().SetEquals(["Chart1__Speed", "Chart1__IsOn", "Chart1__Door__Speed", "Chart1__Door__IsOn"]),
        $"信号名 = 组件路径 + 属性名，层级用 __ 隔开：{string.Join(",", names)}");
    Check(first.Signals.Single(signal => signal.Name == "Chart1__Door__IsOn").IsDigital
          && !first.Signals.Single(signal => signal.Name == "Chart1__Door__Speed").IsDigital, "布尔 SV 是开关量，浮点 SV 是模拟量");

    string todayTable = $"DataRecord_{DateTime.Today:yyyyMMdd}";
    using (var db = XyzDb.Create("ChartSmoke"))
    {
        var tables = db.DbMaintenance.GetTableInfoList(false).Select(table => table.Name).ToList();
        Check(tables.Contains(todayTable), "当天的日表建出来了");
        Check(!tables.Contains("DataRecord_20000101"), "写第一批之前把过期日表删了");
        var columns = db.DbMaintenance.GetColumnInfosByTableName(todayTable, false).Select(column => column.DbColumnName).ToList();
        Check(columns.SequenceEqual(new[] { "Time" }.Concat(names)), $"列 = Time + 信号名，按信号顺序：{string.Join(",", columns)}");
    }

    var window = (Start: DateTime.Now.AddMinutes(-1), End: DateTime.Now.AddMinutes(1));
    var queried = first.Query(names, window.Start, window.End, CancellationToken.None);
    var speed = queried.Series.Single(series => series.Name == "Chart1__Door__Speed");
    var isOn = queried.Series.Single(series => series.Name == "Chart1__Door__IsOn");
    Check(speed.Count >= 3 && speed.Min == 12.5 && speed.Max == 12.5, $"模拟量写进去、按名字查得回来：{speed.Count} 行，值 {speed.Min}");
    Check(speed.Count == firstRows, $"停采样时攒着的最后一批也写进库了：采了 {firstRows} 行，库里 {speed.Count} 行");
    Check(isOn.Count == speed.Count && isOn.Min == 1 && isOn.Max == 1, "开关量存 1/0，查回来是 1");
    Check(queried.Times.Zip(queried.Times.Skip(1)).All(pair => pair.First < pair.Second), "查出来按时间排好");

    // 第二次开机多了一个部件：当天的表补上新列，之前写的行还在。
    var second = RunChart(ChartTree("Door", "Lift"));
    using (var db = XyzDb.Create("ChartSmoke"))
    {
        var columns = db.DbMaintenance.GetColumnInfosByTableName(todayTable, false).Select(column => column.DbColumnName).ToList();
        Check(columns.Contains("Chart1__Lift__Speed") && columns.Contains("Chart1__Lift__IsOn") && columns.Count == 7,
            $"新部件的信号在当天的表上补了列：{string.Join(",", columns)}");
    }

    var again = second.Query(["Chart1__Door__Speed", "Chart1__Lift__Speed"], window.Start, DateTime.Now.AddMinutes(1), CancellationToken.None);
    var oldSpeed = again.Series[0];
    var newSpeed = again.Series[1];
    Check(oldSpeed.Count > speed.Count && newSpeed.Count > 0 && newSpeed.Count < oldSpeed.Count,
        $"老信号两次的行都在（{oldSpeed.Count}），新信号只有补列以后的（{newSpeed.Count}）");

    // 导出：原始行一段一段给、不抽稀；这天表里没有的列整列空
    List<string> exportNames = ["Chart1__Door__Speed", "Chart1__Lift__Speed", "Chart1__Nope"];
    var exported = second.Export(exportNames, window.Start, DateTime.Now.AddMinutes(1), CancellationToken.None).ToList();
    int exportedRows = exported.Sum(slice => slice.Times.Length);
    Check(exportedRows == oldSpeed.Count && exported.All(slice => slice.Names.SequenceEqual(exportNames)),
        $"导出的行数跟库里一样，不抽稀：{exportedRows} 行");
    Check(exported.SelectMany(slice => slice.Values[0]).All(value => value == 12.5)
          && exported.SelectMany(slice => slice.Values[1]).Count(value => value is not null) == newSpeed.Count
          && exported.SelectMany(slice => slice.Values[2]).All(value => value is null),
        "导出的值照原样：老信号都是 12.5，新信号补列以前为空，库里没有的名字整列空");

    // 按 1 小时一段：造一张 2000-01-02 的日表，00:00 起每 10 分钟一行、3 小时 18 行
    long dayStart = new DateTimeOffset(new DateTime(2000, 1, 2, 0, 0, 0, DateTimeKind.Local)).ToUnixTimeMilliseconds();
    using (var db = XyzDb.Create("ChartSmoke"))
    {
        string table = "DataRecord_20000102";
        db.DbMaintenance.CreateTable(table,
        [
            new DbColumnInfo { TableName = table, DbColumnName = "Time", DataType = db.Ado.DbBind.GetDbTypeName("Int64"), IsPrimarykey = true },
            new DbColumnInfo { TableName = table, DbColumnName = "Chart1__Door__Speed", DataType = db.Ado.DbBind.GetDbTypeName("Double"), IsNullable = true },
        ]);
        for (int index = 0; index < 18; index++)
        {
            db.Ado.ExecuteCommand($"INSERT INTO {table} (Time, Chart1__Door__Speed) VALUES ({dayStart + index * 600_000L}, {index})");
        }
    }

    var hours = second.Export(["Chart1__Door__Speed"], new DateTime(2000, 1, 2), new DateTime(2000, 1, 2, 3, 0, 0), CancellationToken.None).ToList();
    Check(hours.Count == 3 && hours.All(slice => slice.Times.Length == 6)
          && hours.Select((slice, hour) => slice.Times.All(time => time >= dayStart + hour * 3_600_000L && time < dayStart + (hour + 1) * 3_600_000L)).All(inHour => inHour),
        $"导出按 1 小时一段给，每段都在自己那 1 小时里：{string.Join(",", hours.Select(slice => slice.Times.Length))}");

    bool exportStopped = false;
    using (var canceled = new CancellationTokenSource())
    {
        canceled.Cancel();
        try
        {
            second.Export(["Chart1__Door__Speed"], new DateTime(2000, 1, 2), new DateTime(2000, 1, 2, 3, 0, 0), canceled.Token).ToList();
        }
        catch (OperationCanceledException)
        {
            exportStopped = true;
        }
    }

    Check(exportStopped, "导出取消了就不往下读");

    // 导出服务 + 客户端导出：进程里直接把服务交给客户端的导出逻辑（不走网络），写成 xlsx 再读回来核对
    var xlsx = Path.Combine(Path.GetTempPath(), $"datachart-smoke-{Guid.NewGuid():N}.xlsx");
    try
    {
        var percents = new ListProgress();
        var saved = await DataChartExport.SaveAsync(new DataChartService(), ["Chart1__Door__Speed", "Chart1__Nope"],
            new DateTime(2000, 1, 1, 23, 0, 0), new DateTime(2000, 1, 3), xlsx, "Time", percents, CancellationToken.None);
        var sheetNames = MiniExcel.GetSheetNames(xlsx);
        var firstDay = MiniExcel.Query(xlsx, useHeaderRow: false, sheetName: "2000-01-01").Cast<IDictionary<string, object>>().ToList();
        var secondDay = MiniExcel.Query(xlsx, useHeaderRow: true, sheetName: "2000-01-02").Cast<IDictionary<string, object>>().ToList();
        Check(saved.Success && sheetNames.SequenceEqual(["2000-01-01", "2000-01-02"]),
            $"一天一张表，没数据的那天也有：{string.Join(",", sheetNames)}");
        Check(firstDay.Count == 1 && firstDay[0].Values.Select(value => value?.ToString()).SequenceEqual(["Time", "Chart1__Door__Speed", "Chart1__Nope"]),
            "没数据的那天只有表头：时间 + 每条曲线一列");
        Check(secondDay.Count == 18 && secondDay[0]["Time"] is DateTime firstTime && firstTime == new DateTime(2000, 1, 2)
              && secondDay.Select(row => Convert.ToDouble(row["Chart1__Door__Speed"])).SequenceEqual(Enumerable.Range(0, 18).Select(index => (double)index))
              && secondDay.All(row => !row.TryGetValue("Chart1__Nope", out var nope) || nope is null),
            $"有数据的那天每秒原样一行、库里没有的列留空：{secondDay.Count} 行");
        Check(percents.Values.Count >= 3 && percents.Values[^1] == 100 && percents.Values.Zip(percents.Values.Skip(1)).All(pair => pair.First <= pair.Second),
            $"进度按收到的时刻往上走、最后 100：{string.Join(",", percents.Values.Select(value => Math.Round(value)))}");
        Check(!File.Exists(xlsx + ".tmp"), "写完临时文件换上去了，旁边不留");

        File.Delete(xlsx);
        var empty = await DataChartExport.SaveAsync(new DataChartService(), ["Chart1__Door__Speed"],
            new DateTime(2000, 1, 5), new DateTime(2000, 1, 6), xlsx, "Time", new ListProgress(), CancellationToken.None);
        Check(empty.Code == DataChartExport.EmptyCode && !File.Exists(xlsx), "这段时间没数据：回 export_empty，不建文件");

        // 后端写到一半出错、中途取消：原来同名的文件不动，旁边不留临时文件
        File.WriteAllText(xlsx, "原来的文件");
        var broken = await DataChartExport.SaveAsync(new FakeExportService(failAfterFirst: true), ["Chart1__Door__Speed"],
            new DateTime(2000, 1, 2), new DateTime(2000, 1, 3), xlsx, "Time", new ListProgress(), CancellationToken.None);
        Check(broken.Code == ErrorCodes.HistoryQueryFailed && File.ReadAllText(xlsx) == "原来的文件" && !File.Exists(xlsx + ".tmp"),
            $"后端写到一半出错：回后端的错误码（{broken.Code}），原来同名的文件不动、不留临时文件");

        using var cancel = new CancellationTokenSource();
        var cancelOnFirst = new ListProgress(() => cancel.Cancel());
        bool exportCanceled = false;
        try
        {
            await DataChartExport.SaveAsync(new FakeExportService(failAfterFirst: false), ["Chart1__Door__Speed"],
                new DateTime(2000, 1, 2), new DateTime(2000, 1, 3), xlsx, "Time", cancelOnFirst, cancel.Token);
        }
        catch (Exception) when (cancel.IsCancellationRequested)
        {
            exportCanceled = true;
        }

        Check(exportCanceled && File.ReadAllText(xlsx) == "原来的文件" && !File.Exists(xlsx + ".tmp"),
            "导出中途取消：停下、原来同名的文件不动、不留临时文件");

        SmokeIoc.Use<IDataChart>(null);
        File.Delete(xlsx);
        var notInstalled = await DataChartExport.SaveAsync(new DataChartService(), ["Chart1__Door__Speed"],
            new DateTime(2000, 1, 2), new DateTime(2000, 1, 3), xlsx, "Time", new ListProgress(), CancellationToken.None);
        Check(notInstalled.Code == ErrorCodes.DataChartNotInstalled && !File.Exists(xlsx), "没装数据曲线：导出回 datachart.not_installed，不建文件");
        SmokeIoc.Use<IDataChart>(second);
    }
    finally
    {
        File.Delete(xlsx);
        File.Delete(xlsx + ".tmp");
    }

    bool rejected = false;
    try
    {
        new DataChartComponent { Database = "ChartSmoke" }.Bind(ChartTree("Lift_"));
    }
    catch (InvalidOperationException exception)
    {
        rejected = exception.Message.Contains("Lift_");
    }

    Check(rejected, "部件名以下划线结尾（跟分隔符 __ 分不清）开机就报，报错里带名字");

    // machine.xml 里没配 IsRun：不起扫描，一行都不采
    var idle = new DataChartComponent { Database = "ChartSmoke", SampleIntervalMs = 200 };
    SmokeIoc.Use<IDataChart>(idle);
    int idleRows = 0;
    idle.Records.Subscribe(_ => Interlocked.Increment(ref idleRows));
    idle.Bind(ChartTree("Door"));
    ComponentLoader.StartScanning([idle]);
    Thread.Sleep(600);
    idle.StopSampling();
    Check(Volatile.Read(ref idleRows) == 0, "machine.xml 里没配 IsRun 就不起扫描、不采样");

    // 实时曲线：接在数据曲线的采样上，在自己的扫描里（配了 IsRun）把采到的行一帧一帧推给界面（不留存）；每帧带这一轮的标识，值跟信号表一样多
    var realSource = new DataChartComponent { Database = "ChartSmoke", SampleIntervalMs = 200, IsRun = true };
    SmokeIoc.Use<IDataChart>(realSource);
    var realChart = new RealChartComponent { IsRun = true, WindowSeconds = 60 };
    SmokeIoc.Use<IRealChart>(realChart);
    var frames = new List<RealChartFrameDto>();
    using (EventBus.Register<RealChartFrameDto>(RealChartFrameDto.EventToken, frame =>
           {
               lock (frames)
               {
                   frames.Add(frame);
               }
           }))
    {
        realSource.Bind(ChartTree("Door"));
        realChart.Attach(realSource);
        ComponentLoader.StartScanning([realSource, realChart]);
        Thread.Sleep(1300);
        realSource.StopSampling();
        Thread.Sleep(200);
    }

    lock (frames)
    {
        Check(frames.Count >= 3 && frames.All(frame => frame.Session == realChart.Session && frame.Values.Count == realSource.Signals.Count)
              && realChart.Recent().Count >= frames.Count,
            $"实时曲线在自己的扫描里推：{frames.Count} 帧，每帧带这一轮的标识、值跟信号表一样多，最近一段内存里都留着");
    }

    // IO 绑定的信号名：组件路径 + 配置项名去掉 Index；DI 组件只有一个点，就是组件路径（Door__Opened，不再带 __Di）
    var previousIo = IocHelper.GetService<IIo>();
    var ioCsv = Path.Combine(chartGemDirectory, "points.csv");
    File.WriteAllText(ioCsv, "Index,Name,PhysicalMin,PhysicalMax,LogicalMin,LogicalMax\n0,P0,0,0,0,0\n1,P1,0,0,0,0\n2,P2,0,10,0,10\n");
    var chartIo = new IoComponent();
    SmokeIoc.Use<IIo>(chartIo);
    chartIo.Di.Load(ioCsv, chartIo);
    chartIo.Do.Load(ioCsv, chartIo);
    chartIo.Ai.Load(ioCsv, chartIo);
    try
    {
        var door = new ModuleConfig
        {
            Name = "Door",
            Type = typeof(CylinderComponent).FullName,
            Values = [new() { Name = "DoOpenIndex", Value = "0" }, new() { Name = "DoCloseIndex", Value = "1" }],
            Children = [new ModuleConfig { Name = "Opened", Type = typeof(DiSensorComponent).FullName, Values = [new() { Name = "DiIndex", Value = "0" }] }]
        };
        var valve = new ModuleConfig { Name = "Valve", Type = typeof(ValveComponent).FullName, Values = [new() { Name = "DoIndex", Value = "1" }] };
        var pressure = new ModuleConfig { Name = "Pressure", Type = typeof(AiSensorComponent).FullName, Values = [new() { Name = "AiIndex", Value = "2" }] };
        var tank = new ModuleConfig { Name = "Tank", Type = typeof(TankComponent).FullName };
        foreach (var signal in new[] { "LL", "L", "H" })
        {
            tank.Children.Add(new ModuleConfig { Name = signal, Type = typeof(AiLevelComponent).FullName, Values = [new() { Name = "AiIndex", Value = "1" }] });
        }

        var ioRoots = SmokeIoc.Load([new ModuleConfig { Name = "Chart2", Type = typeof(ChartProbe).FullName, Children = [door, valve, pressure, tank] }]);
        var ioGem = new GemCollectors();
        ioGem.Merge(ioRoots, chartGemDirectory);
        SmokeIoc.Use(ioGem); // 数据曲线 Bind 时从容器取 GEM 编号表
        var ioNames = RunChart(ioRoots).Signals.Select(signal => signal.Name).ToHashSet();
        Check(ioNames.Contains("Chart2__Door__Opened") && !ioNames.Any(name => name.EndsWith("__Di", StringComparison.Ordinal)),
            $"DI 组件的信号名就是组件路径（Door__Opened，不带 __Di）：{string.Join(",", ioNames)}");
        Check(ioNames.Contains("Chart2__Door__DoOpen") && ioNames.Contains("Chart2__Valve__Do") && ioNames.Contains("Chart2__Pressure__Ai"),
            "别的 IO 照旧：组件路径 + 配置项名去掉 Index");
        Check(ioNames.Contains("Chart2__Tank__LL__Ai") && !ioNames.Contains("Chart2__Tank__L__Ai") && !ioNames.Contains("Chart2__Tank__H__Ai"),
            "一个液位计挂在槽的几个液位点上：只记第一个点那一列");
    }
    finally
    {
        SmokeIoc.Use<IIo>(previousIo);
    }
}
finally
{
    SmokeIoc.Use<IDataChart>(null);
    SmokeIoc.Use<GemCollectors>(null);
    SqliteCleanup(chartDbFile);
    try
    {
        Directory.Delete(chartGemDirectory, true);
    }
    catch (IOException)
    {
    }
}

// ── 6. gRPC 服务自动认：xyz.Shared 里带 [ServiceContract] 的契约，在装进来的 xyz 程序集里找实现（宿主照这张表注册、映射，加服务不用改宿主）─────
{
    var services = ServiceExtensions.GrpcServices();
    var contracts = typeof(IAlarmService).Assembly.GetTypes()
        .Where(type => type.IsInterface && type.GetCustomAttribute<ServiceContractAttribute>() is not null)
        .Where(type => type != typeof(IRpcService))
        .ToList();
    Check(contracts.Count >= 19 && contracts.All(contract => services.Count(service => service.Contract == contract) == 1),
        "xyz.Service 里的服务一个契约认出一个实现（宿主自己的 RpcService 在宿主程序集里，这里没装）");
    Check(services.All(service => service.Implementation.Name == service.Contract.Name[1..] && !service.Implementation.IsAbstract),
        "认出来的都是契约对应的那个类：IAlarmService → AlarmService");
}

// ── 7. 进度弹框：ProgressState 开、关、取消、后一次顶掉前一次；ProgressOverlay 慢的才弹 ─────
{
    var state = new ProgressState();
    bool openInside = false;
    bool finished = await state.RunAsync((_, _) =>
    {
        openInside = state.IsOpen;
        return Task.CompletedTask;
    });
    Check(finished && openInside && !state.IsOpen, "跑的时候开着，跑完关上、回 true");

    var waiting = new TaskCompletionSource();
    var canceledRun = state.RunAsync((_, token) => waiting.Task.WaitAsync(token));
    Check(state.IsOpen && state.CancelCommand.CanExecute(null), "跑着的时候能点取消");
    state.CancelCommand.Execute(null);
    Check(!await canceledRun && !state.IsOpen && !state.CancelCommand.CanExecute(null), "点了取消：回 false、关上，取消按钮灰掉");

    var firstGate = new TaskCompletionSource();
    var secondGate = new TaskCompletionSource();
    var firstRun = state.RunAsync((_, _) => firstGate.Task);
    var secondRun = state.RunAsync((_, _) => secondGate.Task);
    firstGate.SetResult();
    await firstRun;
    Check(state.IsOpen, "后一次顶掉前一次：前一次收尾不去关后一次开着的弹框");
    secondGate.SetResult();
    await secondRun;
    Check(!state.IsOpen, "最后一次跑完才关");

    bool thrown = false;
    try
    {
        await state.RunAsync((_, _) => throw new InvalidOperationException("坏了"));
    }
    catch (InvalidOperationException)
    {
        thrown = true;
    }

    Check(thrown && !state.IsOpen, "不是取消的错照抛，弹框照样关上");

    // 没有界面线程时 Progress<T> 在线程池上回调，等它落下来
    var reportGate = new TaskCompletionSource();
    var reporting = state.RunAsync(async (progress, _) =>
    {
        progress.Report(42);
        await reportGate.Task;
    });
    SpinWait.SpinUntil(() => state.Value == 42, 2000);
    Check(state.Value == 42, "报的进度落到 Value 上");
    reportGate.SetResult();
    await reporting;

    // 弹框要界面线程：起一个 STA 线程，在里面跑消息循环看什么时候弹
    string overlayResult = "没跑";
    var ui = new Thread(() =>
    {
        try
        {
            overlayResult = OverlayCheck();
        }
        catch (Exception exception)
        {
            overlayResult = exception.ToString();
        }
    });
    ui.SetApartmentState(ApartmentState.STA);
    ui.Start();
    ui.Join();
    Check(overlayResult == "ok", $"弹框 ShowDelay 内跑完的不弹、超过才弹、跑完收起：{overlayResult}");

    static string OverlayCheck()
    {
        SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext());
        var state = new ProgressState();
        var overlay = new ProgressOverlay { ShowDelay = 300, State = state };
        var quick = new TaskCompletionSource();
        _ = state.RunAsync((_, _) => quick.Task);
        Pump(100);
        quick.SetResult();
        Pump(400);
        if (overlay.Visibility == System.Windows.Visibility.Visible)
        {
            return "快的也弹出来了";
        }

        var slow = new TaskCompletionSource();
        _ = state.RunAsync((_, _) => slow.Task);
        Pump(100);
        if (overlay.Visibility == System.Windows.Visibility.Visible)
        {
            return "还没到 ShowDelay 就弹了";
        }

        Pump(400);
        if (overlay.Visibility != System.Windows.Visibility.Visible)
        {
            return "过了 ShowDelay 还没弹";
        }

        slow.SetResult();
        Pump(100);
        if (overlay.Visibility == System.Windows.Visibility.Visible)
        {
            return "跑完没收起";
        }

        return "ok";
    }

    static void Pump(int milliseconds)
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }
}

Console.WriteLine($"PASS: {checks} data center checks (log file parsing with continuation lines and rolled files, " +
                  "log history filters and truncation, two-level log names (module + part) with the module filter, comm lines in the main log and poll lines in the poll file, " +
                  "the operation log through IActionFilter (who, from where, what, arguments, result; reads and repeats skipped; passwords masked; operator from the request header), " +
                  "log files kept for KeepDays, the module list for the history page, recent and realtime logs forwarded by the log component, active alarms with reset / reset-all / not-installed error codes and their pushes, " +
                  "alarm history over day-split tables, and data chart sampling on its own scan (IsRun, whole periods), " +
                  "signal names (a DI component named by its own path) / day tables through SqlSugar, real chart frames pushed from its own scan, " +
                  "data chart export in hour slices written to xlsx one sheet per day (empty days keep the header, backend failure / cancel leave the old file), " +
                  "progress state and overlay (cancel, newer run wins, shown only after the delay), " +
                  "and gRPC services found by contract).");

static void SqliteCleanup(string path)
{
    // SQLite 连接池可能还攥着文件，删不掉就留在临时目录，不影响结果。
    try
    {
        File.Delete(path);
    }
    catch (IOException)
    {
    }
}

/// <summary>
/// 探针设备：带一条报警，能自己报。
/// </summary>
sealed class ProbeDevice : ComponentBase
{
    public ProbeDevice(string name)
    {
        typeof(ComponentBase).GetProperty(nameof(Name))!.SetValue(this, name);
        typeof(ComponentBase).GetProperty(nameof(FullPath))!.SetValue(this, name);
    }

    [Alarm("探针故障", AlarmCategory.OtherCategories, AlarmLevel = AlarmLevel.Alarm1, Solution = "检查探针后复位")]
    public string ProbeFault = nameof(ProbeFault);

    public void Fault() => RaiseAlarm(ProbeFault);
}

/// <summary>
/// 当场记下每次报的进度（不像 Progress&lt;T&gt; 另起线程回调）；给了 onFirst 就在第一次报的时候调一下
/// </summary>
sealed class ListProgress(Action? onFirst = null) : IProgress<double>
{
    private Action? _onFirst = onFirst;

    public List<double> Values { get; } = [];

    public void Report(double value)
    {
        lock (Values)
        {
            Values.Add(value);
        }

        Interlocked.Exchange(ref _onFirst, null)?.Invoke();
    }
}

/// <summary>
/// 假的数据曲线服务：导出先给一段（2000-01-02 零点起 3 行），然后要么回一条失败、要么一直等到被取消
/// </summary>
sealed class FakeExportService(bool failAfterFirst) : IDataChartService
{
    public Task<RpcResponse> GetSignalsAsync(RpcRequest request, CallContext context = default)
    {
        throw new NotSupportedException();
    }

    public Task<RpcResponse> QueryAsync(DataChartQuery query, CallContext context = default)
    {
        throw new NotSupportedException();
    }

    public async IAsyncEnumerable<RpcResponse> ExportAsync(DataChartQuery query, CallContext context = default)
    {
        long start = new DateTimeOffset(new DateTime(2000, 1, 2, 0, 0, 0, DateTimeKind.Local)).ToUnixTimeMilliseconds();
        var first = new DataChartExportDto { Times = [start, start + 1000, start + 2000], Names = ["Chart1__Door__Speed"], Values = [[1, 2, 3]] };
        yield return RpcResponse.Ok(JsonHelper.Serialize(first));
        if (failAfterFirst)
        {
            yield return RpcResponse.Fail(ErrorCodes.HistoryQueryFailed, ["坏了"]);
            yield break;
        }

        await Task.Delay(Timeout.Infinite, context.CancellationToken);
    }
}

/// <summary>
/// 数据曲线探针：一个模拟量 SV、一个开关量 SV，值固定，好对查出来的数。
/// </summary>
[Component(description: "数据曲线冒烟用部件")]
public sealed class ChartProbe : ComponentBase
{
    [VariableMark(VariableType.SV, ValueFormat.Double, unit: "rpm", description: "转速")]
    public double Speed { get; set; } = 12.5;

    [VariableMark(VariableType.SV, ValueFormat.Bool, description: "开着")]
    public bool IsOn { get; set; } = true;
}

/// <summary>
/// 冒烟里写日志用的来源：两层名字由测试给
/// </summary>
sealed class SmokeLogSource(string module, string part) : ILogSource
{
    public string LogModule => module;

    public string LogPart => part;
}
