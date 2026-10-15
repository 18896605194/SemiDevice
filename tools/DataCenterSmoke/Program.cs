using System.Text;
using NLog;
using SqlSugar;
using xyz.Common.Log;
using xyz.Components;
using xyz.Components.Attributes;
using xyz.Components.Collectors;
using xyz.Components.Components;
using xyz.Components.Enums;
using xyz.Configs.Models;
using xyz.Database.Alarms;
using xyz.Database.DbProvider;
using xyz.Service.Alarms;
using xyz.Service.Events;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Tools;

// 数据中心冒烟：日志文件解析与日志历史查询、报警服务（当前报警、人工复位、报警历史）、数据曲线（信号名、日表）。
// 不起宿主、不连设备；日志样本写在运行目录 Log 下一个不会跟真日志撞的日期，报警记录、数据曲线写临时库，跑完都删掉。
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
    Check(active.Count == 1 && active[0].Source == "Tool1" && active[0].Code == "ProbeFault"
          && active[0].Level == "Alarm1" && active[0].IsActive, "当前报警带来源、代码、等级");
    Check(active[0].Text == "探针故障" && active[0].Solution == "检查探针后复位", "报警文本与处理建议");

    var unknown = await alarmService.ResetAsync(new RpcRequest { Parameters = { ["Source"] = "Nobody" } });
    Check(!unknown.Success && unknown.Code == ErrorCodes.AlarmSourceNotFound
          && unknown.Args.Count == 1 && unknown.Args[0] == "Nobody",
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

// ── 5. 数据曲线：信号名按 __ 分层；库表全走 SqlSugar（建表、写、查、开机补列、清过期日表）；名字不合规开机就报 ─────
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
        var roots = ComponentLoader.Load([root]);
        new GemCollectors().Merge(roots, chartGemDirectory);
        return roots;
    }

    // 跑一小会儿：200ms 采一行，停下时把攒着的一批写完。
    DataChartComponent RunChart(List<ComponentBase> roots)
    {
        var chart = new DataChartComponent { Database = "ChartSmoke", SampleIntervalMs = 200 };
        chart.StartSampling(roots);
        Thread.Sleep(1300);
        chart.StopSampling();
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

    bool rejected = false;
    try
    {
        new DataChartComponent { Database = "ChartSmoke" }.StartSampling(ChartTree("Lift_"));
    }
    catch (InvalidOperationException exception)
    {
        rejected = exception.Message.Contains("Lift_");
    }

    Check(rejected, "部件名以下划线结尾（跟分隔符 __ 分不清）开机就报，报错里带名字");
}
finally
{
    DataChartComponent.Current = null;
    GemCollectors.Current = null;
    SqliteCleanup(chartDbFile);
    try
    {
        Directory.Delete(chartGemDirectory, true);
    }
    catch (IOException)
    {
    }
}

Console.WriteLine($"PASS: {checks} data center checks (log file parsing with continuation lines and rolled files, " +
                  "log history filters and truncation, active alarms with reset / reset-all / not-installed error codes, " +
                  "alarm history over day-split tables, and data chart signal names / day tables through SqlSugar).");

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
