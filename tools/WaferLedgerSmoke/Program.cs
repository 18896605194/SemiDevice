using xyz.Components;
using xyz.Components.Alarm;
using xyz.Components.Components;
using xyz.Components.Wafers;
using xyz.Configs.Models;
using xyz.Database.DbProvider;
using xyz.Database.Wafers;
using xyz.Modules;
using xyz.Tools;

// 晶圆账冒烟：装配、四个原子操作、事件、对账、并发抢槽。不连设备、不写配置文件。
var checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + message);
    checks++;
}

// 0. 真 sc.xml 里配了账本节点（这里只装配这一个节点：整份 sc.xml 要机型模块程序集，不是本工具的事）。
var scConfig = XmlHelper.Deserialize<ScConfig>(@"D:\Common\xyz.Core\Service\xyz.Configs\Config\sc.xml");
Check(scConfig is not null, "sc.xml 解析失败");
var node = scConfig!.Modules.FirstOrDefault(setting => string.Equals(setting.Name, "WaferManager", StringComparison.OrdinalIgnoreCase));
Check(node is not null && node.Type == typeof(WaferManager).FullName, "sc.xml 里应有指向 WaferManager 的顶层节点");
var databaseGroup = scConfig.Modules.FirstOrDefault(setting => string.Equals(setting.Name, "Database", StringComparison.OrdinalIgnoreCase));
Check(databaseGroup is not null, "sc.xml 应有 Database 节点");

// 连库和账本一起装：账本的流水库名要能在这些连接里找到，否则会回落到默认库。
var roots = ComponentLoader.Load([databaseGroup!, node!]);
Check(XyzDb.IsRegistered("Default") && XyzDb.IsRegistered("Io"),
    "Database 节点应注册出业务库与设备数据库两路连接，实际: " + string.Join(",", XyzDb.RegisteredNames));
Check(node!.Values.Any(value => string.Equals(value.Name, "HistoryDatabase", StringComparison.OrdinalIgnoreCase)
                                && string.Equals(value.Value, "Default", StringComparison.OrdinalIgnoreCase)),
    "晶圆流水属业务数据，应配在默认库");
var assembled = roots.OfType<WaferManager>().SingleOrDefault();
Check(assembled is not null && assembled.IsEnable && assembled.Name == "WaferManager", "sc.xml 应装出一个启用的 WaferManager");
Check(ReferenceEquals(WaferManager.Current, assembled), "装配出来即成为 Current");
// 启动时只对 roots 里的 BaseModule 调 Start()，账本不在其中，所以没有扫描线程。
Check(!roots.OfType<BaseModule>().Any(module => ReferenceEquals(module, assembled)), "账本不该出现在会被 Start 的模块清单里");

// 后面用独立实例，不动装配出来的那本账。
var ledger = new WaferManager();
ledger.RegisterLocation("LoadPort1", 25);
ledger.RegisterLocation("Robot1", 2);
Check(ledger.IsRegistered("LoadPort1") && !ledger.IsRegistered("Chamber1"), "注册过的位置才认");

// 1. 建片：位置、来源、片号都记上；同一个槽不能建两次。
var wafer = ledger.Create("LoadPort1", 5, WaferStatus.Normal, "FOUP-001", "LOT-A");
Check(wafer is not null && wafer.WaferId == "FOUP-001.05" && wafer.Module == "LoadPort1" && wafer.Slot == 5
      && wafer.OriginCarrierId == "FOUP-001" && wafer.LotId == "LOT-A", "建片应记下位置、来源与批次");
Check(ledger.HasWafer("LoadPort1", 5) && !ledger.IsEmpty("LoadPort1", 5) && ledger.CountWafers("LoadPort1") == 1, "查账");
Check(ledger.Create("LoadPort1", 5) is null, "同一个槽不能重复建片");
Check(ledger.Get("LoadPort1", 6) is null && ledger.IsEmpty("LoadPort1", 6), "空槽查出来是 null");
Check(ledger.Get("LoadPort1", 99) is null && !ledger.HasWafer("LoadPort1", 99), "越界槽位不认");
Check(ledger.Create("Chamber9", 1) is null, "没注册的位置建不了片");

// 2. 查询返回快照：两次查到的不是同一个对象，外部拿不到账上的活对象。
Check(!ReferenceEquals(ledger.Get("LoadPort1", 5), ledger.Get("LoadPort1", 5)), "查询应返回快照副本");

// 3. 移片：取片 = 槽位 → 手臂；来源信息不变；源空、目标占都要挡住。
Check(ledger.Move("LoadPort1", 5, "Robot1", 1), "取片：LoadPort1.05 → Robot1 的 1 号手臂");
Check(!ledger.HasWafer("LoadPort1", 5) && ledger.HasWafer("Robot1", 1), "移动后两头的账都要对");
var onArm = ledger.Get("Robot1", 1)!;
Check(onArm.OriginModule == "LoadPort1" && onArm.OriginSlot == 5 && onArm.CarrierId == "FOUP-001", "移动不改来源与载具");
Check(!ledger.Move("LoadPort1", 5, "Robot1", 2), "源上没片应拒绝");
ledger.Create("LoadPort1", 7);
Check(!ledger.Move("LoadPort1", 7, "Robot1", 1), "目标已有片应拒绝");
Check(ledger.CountWafers("Robot1") == 1 && ledger.CountWafers("LoadPort1") == 1, "被拒的移动不能改账");

// 4. 改片信息。
Check(ledger.SetWaferId("Robot1", 1, "W-123") && ledger.Get("Robot1", 1)!.WaferId == "W-123", "改片号");
Check(ledger.SetProcessState("Robot1", 1, WaferProcessState.InProcess)
      && ledger.Get("Robot1", 1)!.ProcessState == WaferProcessState.InProcess, "改工艺状态");
Check(ledger.Find("W-123")?.Module == "Robot1" && ledger.Find("没有这片") is null, "按片号找片");

// 5. 事件：建/移/改/删各发一次，移片带原位置。
var events = new List<string>();
ledger.WaferCreated += w => events.Add($"created:{w.Module}.{w.Slot}");
ledger.WaferMoved += (w, module, slot) => events.Add($"moved:{module}.{slot}->{w.Module}.{w.Slot}");
ledger.WaferUpdated += w => events.Add($"updated:{w.WaferId}");
ledger.WaferDeleted += w => events.Add($"deleted:{w.Module}.{w.Slot}");
ledger.Create("LoadPort1", 9);
ledger.Move("LoadPort1", 9, "Robot1", 2);
ledger.SetLotId("Robot1", 2, "LOT-B");
ledger.Delete("Robot1", 2);
Check(events.SequenceEqual(new[] { "created:LoadPort1.9", "moved:LoadPort1.9->Robot1.2", "updated:LoadPort1.09", "deleted:Robot1.2" }),
    "事件顺序/内容不对: " + string.Join(" | ", events));

// 6. Mapping 整篮重建：先清旧账再按每槽状态建，交叉片状态要带进账。
ledger.RegisterLocation("LoadPort2", 3);
Check(ledger.ApplySlotMap("LoadPort2", [WaferStatus.Normal, null, WaferStatus.Crossed], "FOUP-002") == 2, "Mapping 应建出 2 片");
Check(ledger.CountWafers("LoadPort2") == 2 && ledger.Get("LoadPort2", 3)!.Status == WaferStatus.Crossed, "交叉片状态应带进账");
Check(ledger.ApplySlotMap("LoadPort2", [null, null, null]) == 0 && ledger.CountWafers("LoadPort2") == 0, "重新 Mapping 先清旧账");
Check(ledger.Clear("LoadPort1") == 1 && ledger.CountWafers("LoadPort1") == 0, "整模块清账（载具移走）");

// 7. 对账：跟设备实际在位核对。
Check(ledger.Verify("Robot1", 1, true), "账实一致");
Check(!ledger.Verify("Robot1", 1, false), "账上有片设备没有 → 不一致");
Check(!ledger.Verify("Robot1", 2, true), "设备有片账上没有 → 不一致");

// 8. 并发抢同一个槽：两个线程同时放片，只能成一个，账上不丢片也不覆盖。
var race = new WaferManager();
race.RegisterLocation("A", 2);
race.RegisterLocation("B", 1);
for (int round = 0; round < 50; round++)
{
    race.Clear("A");
    race.Clear("B");
    race.Create("A", 1);
    race.Create("A", 2);

    int won = 0;
    using var start = new ManualResetEventSlim();
    var first = new Thread(() => { start.Wait(); if (race.Move("A", 1, "B", 1)) Interlocked.Increment(ref won); });
    var second = new Thread(() => { start.Wait(); if (race.Move("A", 2, "B", 1)) Interlocked.Increment(ref won); });
    first.Start();
    second.Start();
    start.Set();
    first.Join();
    second.Join();

    Check(won == 1 && race.CountWafers("B") == 1 && race.CountWafers("A") == 1,
        $"并发抢同一个槽应只成功一个（第 {round + 1} 轮：成功 {won} 次，A 上 {race.CountWafers("A")} 片，B 上 {race.CountWafers("B")} 片）");
}

// 9. 关掉记账：不建账，设备照常动作。
var off = new WaferManager { IsEnable = false };
off.RegisterLocation("X", 1);
Check(off.Create("X", 1) is null && off.CountWafers("X") == 0 && !off.Move("X", 1, "X", 1), "IsEnable=False 时不记账");

// 10. 分库：Database 节点名即库名，装配时注册进 XyzDb；流水走自己的库，不跟默认库（鉴权那些）挤。
var databases = ComponentLoader.Load([
    new ModuleConfig
    {
        Name = "Database",
        Children =
        [
            new ModuleConfig
            {
                Name = "SmokeWafer",
                Type = typeof(DataBaseComponent).FullName,
                Values = [new ValueConfig { Name = "ConnectionString", Value = "DataSource=smoke-wafer.db" }],
            },
        ],
    },
]);
Check(databases.Count == 1 && XyzDb.IsRegistered("SmokeWafer"), "Database 节点应按节点名注册连接");
Check(!XyzDb.IsRegistered("没配过的库"), "没配的库名不算注册");

// 11. 流水入库：只有装配出来（读到 SC）的账本才落库，建/移/改/删各写一行，能按片的内部 ID 回溯全程。
var recorded = ComponentLoader.Load([
    new ModuleConfig
    {
        Name = "WaferLedgerSmoke",
        Type = typeof(WaferManager).FullName,
        Values =
        [
            new ValueConfig { Name = "IsEnable", Value = "True" },
            new ValueConfig { Name = "EnableHistory", Value = "True" },
            new ValueConfig { Name = "HistoryDatabase", Value = "SmokeWafer" },
            new ValueConfig { Name = "HistoryKeepDays", Value = "90" },
        ],
    },
]).OfType<WaferManager>().Single();
recorded.RegisterLocation("SmokeLP", 2);
recorded.RegisterLocation("SmokeRB", 1);
var tracked = recorded.Create("SmokeLP", 1, WaferStatus.Normal, "FOUP-SMOKE")!;
recorded.Move("SmokeLP", 1, "SmokeRB", 1);
recorded.SetProcessState("SmokeRB", 1, WaferProcessState.Completed);
recorded.Delete("SmokeRB", 1);
recorded.StopHistory();

string guid = tracked.Id.ToString();
var rows = new List<WaferHistoryEntity>();
for (int attempt = 0; attempt < 50 && rows.Count < 4; attempt++)
{
    using var db = XyzDb.Create("SmokeWafer");
    rows = db.Queryable<WaferHistoryEntity>()
        .SplitTable(tables => tables.Take(2))
        .Where(row => row.WaferGuid == guid)
        .OrderBy(row => row.Id)
        .ToList();
    if (rows.Count < 4)
    {
        Thread.Sleep(100);
    }
}

Check(rows.Select(row => row.Action).SequenceEqual(new[] { "Created", "Moved", "Updated", "Deleted" }),
    "流水应按顺序记下建/移/改/删四行，实际: " + string.Join(",", rows.Select(row => row.Action)));
var moveRow = rows[1];
Check(moveRow.FromModule == "SmokeLP" && moveRow.FromSlot == 1 && moveRow.Module == "SmokeRB" && moveRow.Slot == 1
      && moveRow.CarrierId == "FOUP-SMOKE" && moveRow.WaferId == tracked.WaferId, "移片流水应记下从哪到哪与载具");
Check(rows[2].ProcessState == nameof(WaferProcessState.Completed), "改工艺状态应写进流水");
Check(rows.All(row => row.OccurredAt > DateTime.Now.AddMinutes(-5)), "流水应记账本上的发生时刻");

// 12. 按天分表：流水落在当天那张日表里，过期清理时整张删掉即可。
using (var db = XyzDb.Create("SmokeWafer"))
{
    var tables = db.SplitHelper<WaferHistoryEntity>().GetTables();
    Check(tables.Any(table => table.TableName.StartsWith("wafer_history", StringComparison.OrdinalIgnoreCase)
                              && table.Date.Date == DateTime.Today),
        "流水应按天分表，实际表：" + string.Join(",", tables.Select(table => table.TableName)));
}

// 13. 过期清理：伪造一张陈年日表，走一遍"认出日期→整张删"的路子。
//     这条要是断了，分表就白分了——老数据永远删不掉。
using (var db = XyzDb.Create("SmokeWafer"))
{
    var stale = DateTime.Today.AddDays(-400);
    string staleTable = $"wafer_history_{stale:yyyyMMdd}";
    string todayTable = $"wafer_history_{DateTime.Today:yyyyMMdd}";
    db.Ado.ExecuteCommand($"CREATE TABLE IF NOT EXISTS {staleTable} AS SELECT * FROM {todayTable} WHERE 0");
    Check(db.DbMaintenance.IsAnyTable(staleTable, false), "应能建出陈年日表用于清理验证");

    var seen = db.SplitHelper<WaferHistoryEntity>().GetTables();
    var expired = seen.Where(table => table.Date.Date < DateTime.Today.AddDays(-90)).ToList();
    Check(expired.Any(table => table.TableName.Equals(staleTable, StringComparison.OrdinalIgnoreCase)),
        $"清理应能从表名认出 {staleTable} 已过期，实际扫到：" + string.Join(",", seen.Select(table => table.TableName)));
    Check(!expired.Any(table => table.Date.Date == DateTime.Today), "清理不能把当天的日表算成过期");

    foreach (var table in expired)
    {
        db.DbMaintenance.DropTable(table.TableName);
    }

    Check(!db.DbMaintenance.IsAnyTable(staleTable, false), "过期日表应被整张删掉");
    Check(db.DbMaintenance.IsAnyTable($"wafer_history_{DateTime.Today:yyyyMMdd}", false), "清理不该误删当天的日表");
}

// 流水只进 HistoryDatabase 指的那个库：这里配的是 SmokeWafer，就不该漏到别的连接去。
using (var other = XyzDb.Create())
{
    string todayTable = $"wafer_history_{DateTime.Today:yyyyMMdd}";
    bool leaked = other.DbMaintenance.IsAnyTable(todayTable, false)
                  && other.Queryable<WaferHistoryEntity>().SplitTable(tables => tables.Take(2))
                      .Any(row => row.WaferGuid == guid);
    Check(!leaked, "流水不该落到默认库里");
}

using (var cleanup = XyzDb.Create("SmokeWafer"))
{
    cleanup.Deleteable<WaferHistoryEntity>().Where(row => row.WaferGuid == guid)
        .SplitTable(tables => tables.Take(2)).ExecuteCommand();
}

// 14. 报警：账实不符要报出来，不能只写日志（CTC 那边就是静默，现场表现成"片凭空消失"）
{
    var alarms = new AlarmComponent();
    typeof(ComponentBase).GetProperty("Name")!.SetValue(alarms, "Alarm");
    typeof(ComponentBase).GetProperty("FullPath")!.SetValue(alarms, "Alarm");
    Check(ReferenceEquals(AlarmComponent.Current, alarms), "装出来的报警组件应成为 Current");

    var raised = new List<AlarmItem>();
    alarms.AlarmChanged += item => { lock (raised) { raised.Add(item); } };

    var faulty = new WaferManager();
    typeof(ComponentBase).GetProperty("Name")!.SetValue(faulty, "SmokeLedger");
    typeof(ComponentBase).GetProperty("FullPath")!.SetValue(faulty, "SmokeLedger");
    faulty.RegisterLocation("AlarmLp", 2);

    Check(alarms.ActiveAlarms.Count == 0, "还没出事时不该有活动报警");

    // 源上没片却要移：账实不符
    Check(!faulty.Move("AlarmLp", 1, "AlarmLp", 2), "源上没片应该移不动");
    Check(alarms.ActiveAlarms.Count == 1, $"账实不符应报出来，实际 {alarms.ActiveAlarms.Count} 条");

    var alarm = alarms.ActiveAlarms[0];
    Check(alarm.SourcePath == "SmokeLedger" && alarm.AlarmCode == "WaferLedgerAlarm",
        $"报警应带上来源与代码，实际 {alarm.SourcePath}.{alarm.AlarmCode}");
    Check(alarm.AlarmText == "晶圆账异常" && alarm.Level == AlarmLevel.Alarm1
          && alarm.Category == AlarmCategory.ProcessError,
        "报警文本/等级/分类应来自组件上的 [Alarm] 定义——不用事先 Register");
    Check(!string.IsNullOrWhiteSpace(alarm.Solution), "报警应带处理建议，界面要显示给操作员");
    Check(alarm.IsActive && !alarm.IsAcknowledged, "刚报出来应是活动且未确认");

    // 同一个报警反复触发不刷屏
    faulty.Move("AlarmLp", 1, "AlarmLp", 2);
    faulty.Move("AlarmLp", 1, "AlarmLp", 2);
    Check(alarms.ActiveAlarms.Count == 1, "同一个报警重复触发不该堆出多条");
    lock (raised)
    {
        Check(raised.Count == 1, $"重复触发不该重复通知，实际 {raised.Count} 条");
    }

    // 确认不等于恢复
    Check(alarms.Acknowledge("SmokeLedger", "WaferLedgerAlarm"), "首次确认应成功");
    Check(!alarms.Acknowledge("SmokeLedger", "WaferLedgerAlarm"), "重复确认应返回 false");
    Check(alarms.ActiveAlarms.Count == 1 && alarms.ActiveAlarms[0].IsAcknowledged,
        "确认之后报警仍在活动列表里——确认的是人看到了，不是故障没了");

    // 恢复才出列
    Check(alarms.Clear(faulty, "WaferLedgerAlarm"), "恢复应成功");
    Check(alarms.ActiveAlarms.Count == 0, "恢复后应移出活动列表");
    lock (raised)
    {
        Check(raised.Count == 3 && raised[^1].ClearedAt is not null,
            $"触发/确认/恢复各推一条，实际 {raised.Count} 条");
    }

    // 报警上报不能把设备线程带崩：没装报警组件时是空操作
    AlarmComponent.Current = null;
    Check(!ledger.Move("AlarmLp", 1, "AlarmLp", 2), "没装报警组件时账本照常工作");
}

Console.WriteLine($"PASS: {checks} wafer ledger checks (including 50 concurrent slot races and the wafer history persistence path; the rejection error logs above are expected).");
