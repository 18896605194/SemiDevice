using System.Text;
using NLog;
using SqlSugar;
using xyz.Common.Log;
using xyz.Components;
using xyz.Components.Alarm;
using xyz.Components.Components;
using xyz.Database.Alarms;
using xyz.Database.DbProvider;
using xyz.Service.Alarms;
using xyz.Service.Events;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Tools;

// 数据中心冒烟：日志文件解析与日志历史查询、报警服务（当前报警、人工复位、报警历史）。
// 不起宿主、不连设备；日志样本写在运行目录 Log 下一个不会跟真日志撞的日期，报警记录写临时库，跑完都删掉。
var checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + message);
    checks++;
}

static string Line(DateTime time, LogLevel level, string module, string message) =>
    new LogItem(module, level, message) { Time = time }.ToString();

var day = new DateTime(2020, 1, 2);
Directory.CreateDirectory(LogFileReader.LogDirectory);
var stem = Path.Combine(LogFileReader.LogDirectory, $"xyz-{day:yyyy-MM-dd}");
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

    // ── 3. 当前报警与人工复位：走报警组件；复位不存在的来源给错误码；没装报警组件给错误码 ─────
    var alarms = new AlarmComponent();
    var device = new ProbeDevice("Tool1");
    var alarmService = new AlarmService();
    device.Fault();
    var active = JsonHelper.Deserialize<List<AlarmDto>>((await alarmService.GetActiveAsync(new RpcRequest())).Data)!;
    Check(active is [{ Source: "Tool1", Code: "ProbeFault", Level: "Alarm1", IsActive: true }], "当前报警带来源、代码、等级");
    Check(active[0].Text == "探针故障" && active[0].Solution == "检查探针后复位", "报警文本与处理建议");

    var unknown = await alarmService.ResetAsync(new RpcRequest { Parameters = { ["Source"] = "Nobody" } });
    Check(!unknown.Success && unknown.Code == ErrorCodes.AlarmSourceNotFound && unknown.Args is ["Nobody"],
        "复位没报过报警的来源：给错误码和来源");
    var reset = await alarmService.ResetAsync(new RpcRequest { Parameters = { ["Source"] = "Tool1" } });
    Check(reset.Success && alarms.ActiveAlarms.Count == 0, "复位来源后报警清掉");

    device.Fault();
    var resetAll = await alarmService.ResetAllAsync(new RpcRequest());
    Check(resetAll.Success && JsonHelper.Deserialize<int>(resetAll.Data) == 1 && alarms.ActiveAlarms.Count == 0,
        "全部复位：复位了 1 个来源");

    AlarmComponent.Current = null;
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
            new() { Action = "Raised", Source = "Tool1", Code = "ProbeFault", Text = "探针故障", Category = "Other", Level = "Alarm1",
                    RaisedAt = today.AddHours(8), OccurredAt = today.AddHours(8) },
            new() { Action = "Cleared", Source = "Tool1", Code = "ProbeFault", Text = "探针故障", Category = "Other", Level = "Alarm1",
                    RaisedAt = today.AddHours(8), OccurredAt = today.AddHours(8).AddMinutes(5) },
            new() { Action = "Raised", Source = "Tool2", Code = "Crash", Text = "撞机", Category = "MotionError", Level = "Fatal",
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
    SqliteCleanup(dbFile);
}

Console.WriteLine($"PASS: {checks} data center checks (log file parsing with continuation lines and rolled files, " +
                  "log history filters and truncation, active alarms with reset / reset-all / not-installed error codes, " +
                  "and alarm history over day-split tables).");

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

    [Alarm("探针故障", AlarmCategory.Other, AlarmLevel = AlarmLevel.Alarm1, Solution = "检查探针后复位")]
    public string ProbeFault = nameof(ProbeFault);

    public void Fault() => RaiseAlarm(ProbeFault);
}
