using System.Reflection;
using System.ServiceModel;
using System.Text;
using NLog;
using SqlSugar;
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

    // ── 3. 当前报警与人工复位：走报警组件；复位不存在的来源给错误码；没装报警组件给错误码 ─────
    var alarms = new AlarmComponent();
    SmokeIoc.Use<IAlarmComponent>(alarms);
    SmokeIoc.Use<IMapperComponent>(new MapperComponent()); // 报警、EC、曲线推给界面和服务回包都靠它转（开机必配）
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

    SmokeIoc.Use<IAlarmComponent>(null);
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

    // 跑一小会儿：200ms 采一行，在它自己的扫描里采（跟 sc 配 IsRun 一样起），停下时把攒着的一批写完。
    int sampledRows = 0;
    DataChartComponent RunChart(List<ComponentBase> roots)
    {
        var chart = new DataChartComponent { Database = "ChartSmoke", SampleIntervalMs = 200, IsRun = true };
        SmokeIoc.Use<IDataChartComponent>(chart);
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

    // sc 里没配 IsRun：不起扫描，一行都不采
    var idle = new DataChartComponent { Database = "ChartSmoke", SampleIntervalMs = 200 };
    SmokeIoc.Use<IDataChartComponent>(idle);
    int idleRows = 0;
    idle.Records.Subscribe(_ => Interlocked.Increment(ref idleRows));
    idle.Bind(ChartTree("Door"));
    ComponentLoader.StartScanning([idle]);
    Thread.Sleep(600);
    idle.StopSampling();
    Check(Volatile.Read(ref idleRows) == 0, "sc 里没配 IsRun 就不起扫描、不采样");

    // 实时曲线：接在数据曲线的采样上，在自己的扫描里（配了 IsRun）把采到的行一帧一帧推给界面（不留存）；每帧带这一轮的标识，值跟信号表一样多
    var realSource = new DataChartComponent { Database = "ChartSmoke", SampleIntervalMs = 200, IsRun = true };
    SmokeIoc.Use<IDataChartComponent>(realSource);
    var realChart = new RealChartComponent { IsRun = true, WindowSeconds = 60 };
    SmokeIoc.Use<IRealChartComponent>(realChart);
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
    chartIo.Di.Load(ioCsv);
    chartIo.Do.Load(ioCsv);
    chartIo.Ai.Load(ioCsv);
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
    SmokeIoc.Use<IDataChartComponent>(null);
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

Console.WriteLine($"PASS: {checks} data center checks (log file parsing with continuation lines and rolled files, " +
                  "log history filters and truncation, recent and realtime logs forwarded by the log component, active alarms with reset / reset-all / not-installed error codes and their pushes, " +
                  "alarm history over day-split tables, and data chart sampling on its own scan (IsRun, whole periods), " +
                  "signal names (a DI component named by its own path) / day tables through SqlSugar, real chart frames pushed from its own scan, " +
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
