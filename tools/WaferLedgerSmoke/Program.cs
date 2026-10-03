using System.Diagnostics.CodeAnalysis;
using xyz.Components;
using xyz.Components.Components;
using xyz.Components.Enums;
using xyz.Components.Models;
using xyz.Configs.Models;
using xyz.Database.Alarms;
using xyz.Database.DbProvider;
using xyz.Database.Wafers;
using xyz.Modules;
using xyz.Modules.Enums;
using xyz.Service.Wafers;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Shared.Rpc;
using xyz.Tools;

// 晶圆账冒烟：装配、四个原子操作、事件、对账、并发抢槽。不连设备、不写配置文件。
var checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + message);
    checks++;
}

// 0. 真 sc.xml 里配了账本节点（这里只装配这一个节点：整份 sc.xml 要机型模块程序集，不是本工具的事）。
// 读编译时从 xyz.Configs 拷到输出目录的那份（跟源码 sc.xml 同一份），不写死仓库位置。
var scConfig = XmlHelper.Deserialize<ScConfig>(Path.Combine(AppContext.BaseDirectory, "Config", "sc.xml"));
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
ledger.RegisterLoadPort("LoadPort1", 25);
ledger.RegisterLocation("Robot1", 2);
Check(ledger.IsRegistered("LoadPort1") && !ledger.IsRegistered("Chamber1"), "注册过的位置才认");

// 1. 建片：位置、来源、片号都记上；同一个槽不能建两次。
var wafer = ledger.Create("LoadPort1", 5, WaferStatus.Normal, "FOUP-001", "LOT-A");
Check(wafer is not null && wafer.WaferId == "FOUP-001.05" && wafer.Module == "LoadPort1" && wafer.Slot == 5
      && wafer.OriginCarrierId == "FOUP-001" && wafer.LotId == "LOT-A", "建片应记下位置、来源与批次");
Check(wafer!.SourceLoadPort == "LoadPort1" && wafer.SourceSlot == 5, "在 LoadPort 上建的片记下来源 LoadPort 和槽号");
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
Check(onArm.SourceLoadPort == "LoadPort1" && onArm.SourceSlot == 5, "移到手臂上，来源 LoadPort 和槽号不变");
var onArmDto = WaferLedgerSnapshot.ToDto(onArm);
Check(onArmDto.SourceLoadPort == "LoadPort1" && onArmDto.SourceSlot == 5, "推给界面的片带来源 LoadPort 和槽号（片上显示 LoadPort1-5）");
var bare = ledger.Create("Robot1", 2);
Check(bare is not null && bare.SourceLoadPort is null && bare.SourceSlot == 0
      && WaferLedgerSnapshot.ToDto(bare).SourceLoadPort is null, "不在 LoadPort 上建的片没有来源 LoadPort（片上不显示）");
ledger.Delete("Robot1", 2);
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
ledger.RegisterLoadPort("LoadPort2", 3);
Check(ledger.ApplySlotMap("LoadPort2", [WaferStatus.Normal, null, WaferStatus.Crossed], "FOUP-002") == 2, "Mapping 应建出 2 片");
Check(ledger.CountWafers("LoadPort2") == 2 && ledger.Get("LoadPort2", 3)!.Status == WaferStatus.Crossed, "交叉片状态应带进账");
Check(ledger.Get("LoadPort2", 3)!.SourceLoadPort == "LoadPort2" && ledger.Get("LoadPort2", 3)!.SourceSlot == 3, "Mapping 落账的片记下来源 LoadPort 和槽号");
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

// 14. 报警：账实不符要报出来，不能只写日志（CTC 那边就是静默，现场表现成"片凭空消失"）；
//     报出去以后只能人工复位清；报出、清除都入库。
{
    var alarmNode = scConfig.Modules.FirstOrDefault(setting => string.Equals(setting.Name, "Alarm", StringComparison.OrdinalIgnoreCase));
    Check(alarmNode is not null && alarmNode.Type == typeof(AlarmComponent).FullName
          && alarmNode.Values.Any(value => string.Equals(value.Name, "HistoryDatabase", StringComparison.OrdinalIgnoreCase)
                                           && string.Equals(value.Value, "Default", StringComparison.OrdinalIgnoreCase)),
        "sc.xml 应有报警节点；报警记录属业务数据，应配在默认库");

    // 装配出来（读到 SC）的报警组件才入库；这里落到冒烟库，不碰默认库
    var alarmSince = DateTime.Now.AddSeconds(-1);
    var alarms = ComponentLoader.Load([
        new ModuleConfig
        {
            Name = "Alarm",
            Type = typeof(AlarmComponent).FullName,
            Values =
            [
                new ValueConfig { Name = "EnableHistory", Value = "True" },
                new ValueConfig { Name = "HistoryDatabase", Value = "SmokeWafer" },
                new ValueConfig { Name = "HistoryKeepDays", Value = "90" },
            ],
        },
    ]).OfType<AlarmComponent>().Single();
    Check(ReferenceEquals(AlarmComponent.Current, alarms), "装出来的报警组件应成为 Current");

    var raised = new List<AlarmItem>();
    alarms.AlarmChanged += item => { lock (raised) { raised.Add(item); } };

    var faulty = new WaferManager();
    typeof(ComponentBase).GetProperty("Name")!.SetValue(faulty, "SmokeLedger");
    typeof(ComponentBase).GetProperty("FullPath")!.SetValue(faulty, "SmokeLedger");
    faulty.RegisterLocation("AlarmLp", 2);

    Check(alarms.ActiveAlarms.Count == 0 && !faulty.HasAlarm, "还没出事时不该有报警");

    // 源上没片却要移：账实不符
    Check(!faulty.Move("AlarmLp", 1, "AlarmLp", 2), "源上没片应该移不动");
    Check(alarms.ActiveAlarms.Count == 1 && faulty.HasAlarm, $"账实不符应报出来，实际 {alarms.ActiveAlarms.Count} 条");

    var alarm = alarms.ActiveAlarms[0];
    Check(alarm.SourcePath == "SmokeLedger" && alarm.AlarmCode == "WaferLedgerAlarm",
        $"报警应带上来源与代码，实际 {alarm.SourcePath}.{alarm.AlarmCode}");
    Check(alarm.AlarmText == "晶圆账异常" && alarm.Level == AlarmLevel.Alarm1
          && alarm.Category == AlarmCategory.ProcessError,
        "报警文本/等级/分类应来自组件上的 [Alarm] 定义——不用事先注册");
    Check(!string.IsNullOrWhiteSpace(alarm.Solution), "报警应带处理建议，界面要显示给操作员");
    Check(alarm.IsActive, "刚报出来应是活动的");

    // 同一个报警反复触发不刷屏
    faulty.Move("AlarmLp", 1, "AlarmLp", 2);
    faulty.Move("AlarmLp", 1, "AlarmLp", 2);
    Check(alarms.ActiveAlarms.Count == 1, "同一个报警重复触发不该堆出多条");
    lock (raised)
    {
        Check(raised.Count == 1, $"重复触发不该重复通知，实际 {raised.Count} 条");
    }

    // 只能人工复位清：界面按来源复位，走到账本组件的 Reset
    Check(!alarms.Reset("没报过的来源"), "没报过报警的来源复位返回 false");
    Check(alarms.Reset("SmokeLedger"), "按来源复位应找到账本");
    Check(alarms.ActiveAlarms.Count == 0 && !faulty.HasAlarm, "复位后应移出报警列表");
    lock (raised)
    {
        Check(raised.Count == 2 && raised[^1].ClearedAt is not null,
            $"报出、清除各推一条，实际 {raised.Count} 条");
    }

    // 入库：报出、清除各一行，清除那行带上报出时刻
    alarms.StopHistory();
    var alarmRows = new List<AlarmHistoryEntity>();
    for (int attempt = 0; attempt < 50 && alarmRows.Count < 2; attempt++)
    {
        using var db = XyzDb.Create("SmokeWafer");
        if (db.DbMaintenance.IsAnyTable($"alarm_history_{DateTime.Today:yyyyMMdd}", false))
        {
            alarmRows = db.Queryable<AlarmHistoryEntity>()
                .SplitTable(tables => tables.Take(2))
                .Where(row => row.Source == "SmokeLedger")
                .OrderBy(row => row.Id)
                .ToList()
                .Where(row => row.OccurredAt >= alarmSince)
                .ToList();
        }

        if (alarmRows.Count < 2)
        {
            Thread.Sleep(100);
        }
    }

    Check(alarmRows.Select(row => row.Action).SequenceEqual(new[] { "Raised", "Cleared" }),
        "报警应入库：报出、清除各一行，实际: " + string.Join(",", alarmRows.Select(row => row.Action)));
    Check(alarmRows.All(row => row.Code == "WaferLedgerAlarm" && row.Level == "Alarm1" && row.Text == "晶圆账异常")
          && alarmRows[1].RaisedAt == alarmRows[0].RaisedAt && alarmRows[1].OccurredAt >= alarmRows[1].RaisedAt,
        "报警记录应带代码、等级、文本；清除那行带上报出时刻");

    using (var cleanup = XyzDb.Create("SmokeWafer"))
    {
        cleanup.Deleteable<AlarmHistoryEntity>().Where(row => row.Source == "SmokeLedger")
            .SplitTable(tables => tables.Take(2)).ExecuteCommand();
    }

    // 报警上报不能把设备线程带崩：没装报警组件时是空操作
    AlarmComponent.Current = null;
    Check(!ledger.Move("AlarmLp", 1, "AlarmLp", 2), "没装报警组件时账本照常工作");
}

// 15. 人工调整（设置 → 账单调整页）：人按实物移账、删账。校验不过只把原因返回给界面，不报警（人在核对，不是账实不符）；
//     改成了照常发事件、记流水，另记一条调整记录（操作人、原因）；流水落库时调整记录也落库。
{
    var manualGuard = new AlarmComponent();
    var manual = new WaferManager();
    manual.RegisterLocation("ManualPM", 1);
    manual.RegisterLocation("ManualRB", 2);
    manual.RegisterLoadPort("ManualLP", 2);
    var inChamber = manual.Create("ManualPM", 1, WaferStatus.Normal, "FOUP-MAN", "LOT-MAN")!;

    var manualEvents = new List<string>();
    DateTime deletedStamp = default;
    manual.WaferMoved += (moved, fromModule, fromSlot) => manualEvents.Add($"moved:{fromModule}.{fromSlot}->{moved.Module}.{moved.Slot}");
    manual.WaferDeleted += deleted =>
    {
        manualEvents.Add($"deleted:{deleted.Module}.{deleted.Slot}");
        deletedStamp = deleted.UpdatedAt;
    };

    Check(manual.ManualMove("ManualPM", 1, "ManualRB", 1, "Tester", "取片报警，片已在手指上") == WaferAdjustResult.Ok,
        "人工移账：腔体 → 手指");
    var onFinger = manual.Get("ManualRB", 1);
    Check(onFinger is not null && manual.Get("ManualPM", 1) is null && onFinger.Id == inChamber.Id
          && onFinger.WaferId == inChamber.WaferId && onFinger.CarrierId == "FOUP-MAN" && onFinger.LotId == "LOT-MAN",
        "片号、批次、载具、内部 ID 跟着片走");
    Check(manualEvents.SequenceEqual(new[] { "moved:ManualPM.1->ManualRB.1" }), "人工移账照常发 WaferMoved");

    // 校验不过：只返回原因，不改账、不发事件、不报警
    Check(manual.ManualMove("ManualPM", 1, "ManualRB", 2, "Tester", null) == WaferAdjustResult.NoWafer, "源上没片");
    manual.Create("ManualPM", 1, WaferStatus.Normal, "FOUP-MAN");
    Check(manual.ManualMove("ManualPM", 1, "ManualRB", 1, "Tester", null) == WaferAdjustResult.SlotOccupied, "目标已有片");
    Check(manual.ManualMove("ManualRB", 1, "manualrb", 1, "Tester", null) == WaferAdjustResult.SameSlot, "源和目标同一个槽（名字不分大小写）");
    Check(manual.ManualMove("ManualRB", 3, "ManualPM", 1, "Tester", null) == WaferAdjustResult.SlotOutOfRange
          && manual.ManualMove("ManualRB", 1, "ManualPM", 2, "Tester", null) == WaferAdjustResult.SlotOutOfRange,
        "槽号超范围（源、目标都查）");
    Check(manual.ManualMove("Nowhere", 1, "ManualPM", 1, "Tester", null) == WaferAdjustResult.LocationNotFound
          && manual.ManualDelete("Nowhere", 1, "Tester", null) == WaferAdjustResult.LocationNotFound,
        "没登记的位置");
    Check(manual.ManualDelete("ManualRB", 2, "Tester", null) == WaferAdjustResult.NoWafer
          && manual.ManualDelete("ManualRB", 0, "Tester", null) == WaferAdjustResult.SlotOutOfRange,
        "删账：空槽、槽号超范围");
    Check(manualGuard.ActiveAlarms.Count == 0 && !manual.HasAlarm, "人工调整校验不过不该报晶圆账报警");
    Check(manual.CountWafers("ManualRB") == 1 && manual.CountWafers("ManualPM") == 1 && manualEvents.Count == 1,
        "被拒的调整不改账、不发事件");

    var beforeDelete = DateTime.Now;
    Check(manual.ManualDelete("ManualRB", 1, "Tester", "  碎片已取出  ") == WaferAdjustResult.Ok && manual.Get("ManualRB", 1) is null,
        "人工删账");
    Check(manualEvents.Count == 2 && manualEvents[1] == "deleted:ManualRB.1", "人工删账照常发 WaferDeleted");
    Check(deletedStamp >= beforeDelete, "删账事件、流水带的是删的那一刻，不是片最后一次变动的时间");

    var manualRecords = manual.GetRecentAdjustments();
    Check(manualRecords.Count == 2 && manualRecords[0].Action == "Delete" && manualRecords[1].Action == "Move", "调整记录新的在前");
    var moveRecord = manualRecords[1];
    Check(moveRecord.FromModule == "ManualPM" && moveRecord.FromSlot == 1 && moveRecord.ToModule == "ManualRB" && moveRecord.ToSlot == 1
          && moveRecord.Operator == "Tester" && moveRecord.Reason == "取片报警，片已在手指上"
          && moveRecord.WaferGuid == inChamber.Id.ToString() && moveRecord.CarrierId == "FOUP-MAN" && moveRecord.LotId == "LOT-MAN",
        "移账记录带从哪到哪、操作人、原因、片的身份");
    Check(manualRecords[0].ToModule is null && manualRecords[0].ToSlot is null && manualRecords[0].Reason == "碎片已取出",
        "删账记录没有去向，原因去掉首尾空白");

    // 补账：在空槽上按填的片号建一片，其余按默认；没填片号、片号已在账上、槽上有片、位置不对都拒，不报警
    var createdEvents = new List<WaferInfo>();
    manual.WaferCreated += created => createdEvents.Add(created);
    Check(manual.ManualCreate("ManualRB", 1, "  W-NEW  ", "Tester", "重启后账丢了") == WaferAdjustResult.Ok, "人工补账");
    var made = manual.Get("ManualRB", 1);
    Check(made is not null && made.WaferId == "W-NEW" && made.Status == WaferStatus.Normal && made.ProcessState == WaferProcessState.Idle
          && made.CarrierId is null && made.LotId is null && made.Module == "ManualRB" && made.Slot == 1,
        "补出来的片：片号去掉首尾空白，正常片、未处理，没有批次和载具");
    Check(made!.SourceLoadPort is null && made.SourceSlot == 0, "在手臂上补账的片没有来源 LoadPort");
    Check(createdEvents.Count == 1 && createdEvents[0].WaferId == "W-NEW", "人工补账照常发 WaferCreated");
    Check(manual.ManualCreate("ManualRB", 2, "  ", "Tester", null) == WaferAdjustResult.WaferIdRequired, "补账没填片号");
    Check(manual.ManualCreate("ManualRB", 2, "w-new", "Tester", null) == WaferAdjustResult.DuplicateWaferId, "片号已经在账上（不分大小写）");
    Check(manual.ManualCreate("ManualRB", 1, "W-OTHER", "Tester", null) == WaferAdjustResult.SlotOccupied, "补账：槽上已经有片");
    Check(manual.ManualCreate("Nowhere", 1, "W-OTHER", "Tester", null) == WaferAdjustResult.LocationNotFound
          && manual.ManualCreate("ManualRB", 3, "W-OTHER", "Tester", null) == WaferAdjustResult.SlotOutOfRange,
        "补账：位置不对、槽号超范围");
    Check(manualGuard.ActiveAlarms.Count == 0 && createdEvents.Count == 1, "补账被拒不报警、不发事件");
    var createRecord = manual.GetRecentAdjustments()[0];
    Check(createRecord.Action == "Create" && createRecord.WaferId == "W-NEW" && createRecord.FromModule == "ManualRB"
          && createRecord.FromSlot == 1 && createRecord.ToModule is null && createRecord.ToSlot is null
          && createRecord.Operator == "Tester" && createRecord.Reason == "重启后账丢了",
        "补账记录：建在哪、操作人、原因");
    Check(manual.ManualCreate("ManualLP", 2, "W-LP", "Tester", null) == WaferAdjustResult.Ok
          && manual.Get("ManualLP", 2)!.SourceLoadPort == "ManualLP" && manual.Get("ManualLP", 2)!.SourceSlot == 2,
        "在 LoadPort 槽上补账的片记下来源 LoadPort 和槽号");

    for (int round = 0; round < 30; round++)
    {
        manual.ManualMove("ManualPM", 1, "ManualRB", 2, "Tester", null);
        manual.ManualMove("ManualRB", 2, "ManualPM", 1, "Tester", null);
    }

    Check(manual.GetRecentAdjustments().Count == WaferManager.RecentAdjustmentCount, "不落库时内存里只留最近 50 条");

    var disabledLedger = new WaferManager { IsEnable = false };
    Check(disabledLedger.ManualMove("A", 1, "B", 1, "Tester", null) == WaferAdjustResult.Disabled
          && disabledLedger.ManualDelete("A", 1, "Tester", null) == WaferAdjustResult.Disabled
          && disabledLedger.ManualCreate("A", 1, "W-1", "Tester", null) == WaferAdjustResult.Disabled,
        "账没开时人工调整直接拒");

    // 落库：调整记录写进 wafer_adjustment（不分日表，调整很少），查最近的从库里查——重启过也看得到
    var persisted = ComponentLoader.Load([
        new ModuleConfig
        {
            Name = "ManualLedger",
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
    persisted.RegisterLocation("DbPM", 1);
    persisted.RegisterLocation("DbRB", 1);
    var dbWafer = persisted.Create("DbPM", 1, WaferStatus.Normal, "FOUP-DB")!;
    string dbGuid = dbWafer.Id.ToString();
    Check(persisted.ManualMove("DbPM", 1, "DbRB", 1, "Tester", "落库验证") == WaferAdjustResult.Ok, "落库的账本也能人工移账");
    using (var db = XyzDb.Create("SmokeWafer"))
    {
        var stored = db.Queryable<WaferAdjustmentEntity>().Where(row => row.WaferGuid == dbGuid).ToList();
        Check(stored.Count == 1 && stored[0].Action == "Move" && stored[0].Reason == "落库验证" && stored[0].ToModule == "DbRB",
            "调整记录应写进 wafer_adjustment 表");
    }

    var fromDb = persisted.GetRecentAdjustments();
    Check(fromDb.Count > 0 && fromDb[0].WaferGuid == dbGuid, "落库时最近的调整记录从库里查");
    persisted.StopHistory();

    var dbHistory = new List<WaferHistoryEntity>();
    for (int attempt = 0; attempt < 50 && dbHistory.Count < 2; attempt++)
    {
        using var db = XyzDb.Create("SmokeWafer");
        dbHistory = db.Queryable<WaferHistoryEntity>()
            .SplitTable(tables => tables.Take(2))
            .Where(row => row.WaferGuid == dbGuid)
            .OrderBy(row => row.Id)
            .ToList();
        if (dbHistory.Count < 2)
        {
            Thread.Sleep(100);
        }
    }

    Check(dbHistory.Select(row => row.Action).SequenceEqual(new[] { "Created", "Moved" }), "人工移账照常记流水");
    using (var cleanup = XyzDb.Create("SmokeWafer"))
    {
        cleanup.Deleteable<WaferAdjustmentEntity>().Where(row => row.WaferGuid == dbGuid).ExecuteCommand();
        cleanup.Deleteable<WaferHistoryEntity>().Where(row => row.WaferGuid == dbGuid)
            .SplitTable(tables => tables.Take(2)).ExecuteCommand();
    }

    AlarmComponent.Current = null;
}

// 16. 账单调整服务（直接调 gRPC 服务类，不起网络）：位置跟着机械手的站点表走——机械手在前，站点按站点表的先后，
//     几台机械手共用的站点只列一次，没登记槽位的站点不列；移账、删账的结果翻成错误码；没带操作人记成 Unknown。
{
    var serviceGuard = new AlarmComponent();
    var serviceLedger = new WaferManager();
    var robot1 = new LedgerProbeRobot("SvcRobot1", "SvcLP1", "SvcPM1");
    var robot2 = new LedgerProbeRobot("SvcRobot2", "SvcPM1", "SvcBuffer", "SvcGhost");
    serviceLedger.RegisterLocation("SvcRobot1", 2);
    serviceLedger.RegisterLocation("SvcRobot2", 2);
    serviceLedger.RegisterLocation("SvcLP1", 25);
    serviceLedger.RegisterLocation("SvcPM1", 1);
    serviceLedger.RegisterLocation("SvcBuffer", 4);
    serviceLedger.Create("SvcPM1", 1, WaferStatus.Normal, "FOUP-SVC");
    serviceLedger.SetWaferId("SvcPM1", 1, "W-PM");
    serviceLedger.Create("SvcLP1", 5, WaferStatus.Crossed, "FOUP-SVC");
    serviceLedger.SetWaferId("SvcLP1", 5, "W-LP05");

    var service = new WaferLedgerService([robot1, robot2]);
    var view = (await service.GetLedgerAsync(new RpcRequest())).DeserializeData<WaferLedgerDto>();
    Check(view.IsEnabled && view.Locations.Select(location => location.Name)
              .SequenceEqual(new[] { "SvcRobot1", "SvcRobot2", "SvcLP1", "SvcPM1", "SvcBuffer" }),
        "位置：机械手在前、站点按站点表先后、共用的只列一次、没登记槽位的不列，实际 "
        + string.Join(",", view.Locations.Select(location => location.Name)));
    Check(view.Locations[0].Kind == WaferLocationKind.Robot && view.Locations[2].Kind == WaferLocationKind.Other,
        "机械手带种类；不是模块的站点归到其他");
    Check(view.Locations[0].Slots.Count == 2 && view.Locations[2].Slots.Count == 25 && view.Locations[4].Slots.Count == 4
          && view.Locations[2].Slots.Select(slot => slot.Slot).SequenceEqual(Enumerable.Range(1, 25)),
        "机械手按手指数、站点按登记的槽数，槽号从 1 起");
    var pmWafer = view.Locations[3].Slots[0].Wafer;
    Check(pmWafer is not null && pmWafer.WaferId == "W-PM" && pmWafer.CarrierId == "FOUP-SVC"
          && pmWafer.Status == "Normal" && pmWafer.ProcessState == "Idle",
        "槽上带片号、载具、物理状态、工艺状态");
    Check(view.Locations[2].Slots[4].Wafer?.Status == "Crossed" && view.Locations[2].Slots[3].Wafer is null, "空槽为 null");

    var moved = await service.MoveAsync(new WaferMoveRequest
    {
        FromModule = "SvcPM1", FromSlot = 1, ToModule = "SvcRobot2", ToSlot = 1, Reason = "取片报警", Operator = "Tester",
    });
    Check(moved.Success && serviceLedger.Get("SvcRobot2", 1)?.WaferId == "W-PM" && serviceLedger.Get("SvcPM1", 1) is null, "移账");

    var noWafer = await service.MoveAsync(new WaferMoveRequest { FromModule = "SvcPM1", FromSlot = 1, ToModule = "SvcRobot2", ToSlot = 2 });
    Check(!noWafer.Success && noWafer.Code == ErrorCodes.WaferNoWafer && noWafer.Args.SequenceEqual(new[] { "SvcPM1", "1" }),
        "源上没片：错误码带位置、槽号");
    var occupied = await service.MoveAsync(new WaferMoveRequest { FromModule = "SvcRobot2", FromSlot = 1, ToModule = "SvcLP1", ToSlot = 5 });
    Check(!occupied.Success && occupied.Code == ErrorCodes.WaferSlotOccupied && occupied.Args.SequenceEqual(new[] { "SvcLP1", "5", "W-LP05" }),
        "目标有片：带上那片的片号");
    var outOfRange = await service.MoveAsync(new WaferMoveRequest { FromModule = "SvcRobot2", FromSlot = 1, ToModule = "SvcPM1", ToSlot = 2 });
    Check(!outOfRange.Success && outOfRange.Code == ErrorCodes.WaferSlotOutOfRange && outOfRange.Args.SequenceEqual(new[] { "SvcPM1", "2", "1" }),
        "槽号超范围：说清是哪个位置、共几槽");
    var nowhere = await service.MoveAsync(new WaferMoveRequest { FromModule = "SvcRobot2", FromSlot = 1, ToModule = " Nowhere ", ToSlot = 1 });
    Check(!nowhere.Success && nowhere.Code == ErrorCodes.WaferLocationNotFound && nowhere.Args.SequenceEqual(new[] { "Nowhere" }),
        "没有的位置：说出是哪个（去掉首尾空白）");
    var sameSlot = await service.MoveAsync(new WaferMoveRequest { FromModule = "SvcRobot2", FromSlot = 1, ToModule = "svcrobot2", ToSlot = 1 });
    Check(!sameSlot.Success && sameSlot.Code == ErrorCodes.WaferSameSlot, "同一个槽");

    var deletedResponse = await service.DeleteAsync(new WaferDeleteRequest { Module = "SvcLP1", Slot = 5, Reason = "交叉片已取出" });
    Check(deletedResponse.Success && serviceLedger.Get("SvcLP1", 5) is null, "删账");
    var deleteEmpty = await service.DeleteAsync(new WaferDeleteRequest { Module = "SvcLP1", Slot = 5 });
    Check(!deleteEmpty.Success && deleteEmpty.Code == ErrorCodes.WaferNoWafer && deleteEmpty.Args.SequenceEqual(new[] { "SvcLP1", "5" }),
        "删空槽：错误码");
    var deleteOutOfRange = await service.DeleteAsync(new WaferDeleteRequest { Module = "SvcLP1", Slot = 26 });
    Check(!deleteOutOfRange.Success && deleteOutOfRange.Code == ErrorCodes.WaferSlotOutOfRange
          && deleteOutOfRange.Args.SequenceEqual(new[] { "SvcLP1", "26", "25" }),
        "删账槽号超范围");
    Check(serviceGuard.ActiveAlarms.Count == 0, "界面上调账没成不该报晶圆账报警");

    var serviceRecords = (await service.GetAdjustmentsAsync(new RpcRequest())).DeserializeData<List<WaferAdjustmentDto>>();
    Check(serviceRecords.Count == 2
          && serviceRecords[0].Action == "Delete" && serviceRecords[0].WaferId == "W-LP05" && serviceRecords[0].Operator == "Unknown"
          && serviceRecords[0].FromModule == "SvcLP1" && serviceRecords[0].FromSlot == 5 && serviceRecords[0].ToModule is null
          && serviceRecords[1].Action == "Move" && serviceRecords[1].FromModule == "SvcPM1" && serviceRecords[1].ToModule == "SvcRobot2"
          && serviceRecords[1].ToSlot == 1 && serviceRecords[1].Operator == "Tester" && serviceRecords[1].Reason == "取片报警",
        "调整记录：新的在前，没带操作人记成 Unknown");

    var svcCreated = await service.CreateAsync(new WaferCreateRequest
    {
        Module = "SvcPM1", Slot = 1, WaferId = "W-NEW", Reason = "重启后账丢了", Operator = "Tester",
    });
    Check(svcCreated.Success && serviceLedger.Get("SvcPM1", 1)?.WaferId == "W-NEW", "补账");
    var noWaferId = await service.CreateAsync(new WaferCreateRequest { Module = "SvcBuffer", Slot = 1, WaferId = "  " });
    Check(!noWaferId.Success && noWaferId.Code == ErrorCodes.WaferIdRequired, "补账没填片号：错误码");
    var duplicateId = await service.CreateAsync(new WaferCreateRequest { Module = "SvcBuffer", Slot = 1, WaferId = "W-PM" });
    Check(!duplicateId.Success && duplicateId.Code == ErrorCodes.WaferDuplicateId
          && duplicateId.Args.SequenceEqual(new[] { "W-PM", "SvcRobot2", "1" }),
        "片号已经在账上：说出在哪个位置哪个槽");
    var createOccupied = await service.CreateAsync(new WaferCreateRequest { Module = "SvcPM1", Slot = 1, WaferId = "W-X" });
    Check(!createOccupied.Success && createOccupied.Code == ErrorCodes.WaferSlotOccupied
          && createOccupied.Args.SequenceEqual(new[] { "SvcPM1", "1", "W-NEW" }),
        "补账槽上有片：带上那片的片号");
    var createNowhere = await service.CreateAsync(new WaferCreateRequest { Module = "Nowhere", Slot = 1, WaferId = "W-X" });
    var createOutOfRange = await service.CreateAsync(new WaferCreateRequest { Module = "SvcBuffer", Slot = 5, WaferId = "W-X" });
    Check(!createNowhere.Success && createNowhere.Code == ErrorCodes.WaferLocationNotFound && createNowhere.Args.SequenceEqual(new[] { "Nowhere" })
          && !createOutOfRange.Success && createOutOfRange.Code == ErrorCodes.WaferSlotOutOfRange
          && createOutOfRange.Args.SequenceEqual(new[] { "SvcBuffer", "5", "4" }),
        "补账位置不对、槽号超范围：错误码");
    var afterCreate = (await service.GetAdjustmentsAsync(new RpcRequest())).DeserializeData<List<WaferAdjustmentDto>>();
    Check(afterCreate.Count == 3 && afterCreate[0].Action == "Create" && afterCreate[0].WaferId == "W-NEW"
          && afterCreate[0].FromModule == "SvcPM1" && afterCreate[0].FromSlot == 1 && afterCreate[0].ToModule is null
          && afterCreate[0].Operator == "Tester" && afterCreate[0].Reason == "重启后账丢了",
        "补账记录排在最前面，记着建在哪");
    Check(serviceGuard.ActiveAlarms.Count == 0, "补账没成也不报晶圆账报警");

    serviceLedger.IsEnable = false;
    var disabledView = (await service.GetLedgerAsync(new RpcRequest())).DeserializeData<WaferLedgerDto>();
    var disabledMove = await service.MoveAsync(new WaferMoveRequest { FromModule = "SvcRobot2", FromSlot = 1, ToModule = "SvcPM1", ToSlot = 1 });
    var disabledCreate = await service.CreateAsync(new WaferCreateRequest { Module = "SvcBuffer", Slot = 1, WaferId = "W-Y" });
    Check(!disabledView.IsEnabled && disabledView.Locations.Count == 0
          && !disabledMove.Success && disabledMove.Code == ErrorCodes.WaferLedgerDisabled
          && !disabledCreate.Success && disabledCreate.Code == ErrorCodes.WaferLedgerDisabled,
        "账没开：位置表是空的，调账给错误码");

    WaferManager.Current = null;
    var noLedgerView = (await service.GetLedgerAsync(new RpcRequest())).DeserializeData<WaferLedgerDto>();
    var noLedgerRecords = (await service.GetAdjustmentsAsync(new RpcRequest())).DeserializeData<List<WaferAdjustmentDto>>();
    Check(!noLedgerView.IsEnabled && noLedgerView.Locations.Count == 0 && noLedgerRecords.Count == 0, "没装账本也不出错");
    AlarmComponent.Current = null;
}

Console.WriteLine($"PASS: {checks} wafer ledger checks (including 50 concurrent slot races, the wafer history persistence path, the ledger alarm raised, manually reset and written to the alarm history, manual move/delete/create with adjustment records in memory and in the database, and the ledger adjustment service: locations from the robot station tables and every error code; the rejection error logs above are expected).");

// 探针机械手：只给账单调整服务一张站点表（位置跟着它列），不连设备、不做动作。
sealed class LedgerProbeRobot : BaseModule, IRobot
{
    private readonly Dictionary<string, RobotStation> _stations = new(StringComparer.OrdinalIgnoreCase);

    public LedgerProbeRobot(string name, params string[] stations)
    {
        typeof(ComponentBase).GetProperty(nameof(Name))!.SetValue(this, name);
        typeof(ComponentBase).GetProperty(nameof(FullPath))!.SetValue(this, name);
        for (int index = 0; index < stations.Length; index++)
        {
            _stations[stations[index]] = new RobotStation(stations[index], index + 1, RobotDirection.South, 0, [1, 2]);
        }
    }

    public override int State { get; protected set; } = ModuleState.Idle;

    public bool? IsServoOn => null;

    public string? DeviceError => null;

    public bool? HasWafer(int arm) => null;

    public IReadOnlyDictionary<string, RobotStation> Stations => _stations;

    public bool TryGetStation(string station, [MaybeNullWhen(false)] out RobotStation config) => _stations.TryGetValue(station, out config);

    ModuleOperation? IRobot.Home() => null;

    ModuleOperation? IRobot.Init() => null;

    ModuleOperation? IRobot.Reset() => null;

    ModuleOperation? IRobot.Abort() => null;

    ModuleOperation? IRobot.Pick(int arm, string station, int slot) => null;

    ModuleOperation? IRobot.Place(int arm, string station, int slot) => null;

    ModuleOperation? IRobot.PowerOn() => null;

    ModuleOperation? IRobot.PowerOff() => null;
}
