using xyz.Components;
using xyz.Components.Alarm;
using xyz.Components.Attributes;
using xyz.Components.Collectors;
using xyz.Components.Components;
using xyz.Components.Enums;
using xyz.Configs.Models;
using xyz.Tools;

// 编号表冒烟：五个采集器在临时目录里生成、合并、改写编号表。不起宿主、不连设备、不碰真配置。
var checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + message);
    checks++;
}

List<ComponentBase> Build(params string[] names) =>
    ComponentLoader.Load(names.Select(name => new ModuleConfig { Name = name, Type = typeof(ProbeTool).FullName }).ToList());

int IdOf<T>(IReadOnlyList<T> rows, string name) where T : IDefinitionRow => rows.Single(row => row.Name == name).Id;
bool EnabledOf<T>(IReadOnlyList<T> rows, string name) where T : IDefinitionRow => rows.Single(row => row.Name == name).Enabled;
IEnumerable<int> Ids<T>(IReadOnlyList<T> rows) where T : IDefinitionRow => rows.Select(row => row.Id);
AlarmDefinition AlarmOf(GemCollectors gem, string name) => gem.Alarm.Definitions.Single(row => row.Name == name);
EventDefinition EventOf(GemCollectors gem, int alid, bool clear) =>
    gem.Event.Definitions.Single(row => row.Name == EventCollector.AlarmEventName(alid, clear));

var directory = Path.Combine(Path.GetTempPath(), $"gem-smoke-{Guid.NewGuid():N}");
var spare = Path.Combine(Path.GetTempPath(), $"gem-smoke-{Guid.NewGuid():N}");
var quiet = Path.Combine(Path.GetTempPath(), $"gem-smoke-{Guid.NewGuid():N}");
Directory.CreateDirectory(directory);
Directory.CreateDirectory(spare);
Directory.CreateDirectory(quiet);
string FileOf(string name) => Path.Combine(directory, name);

try
{
    // ── 1. 全新生成：五张表都出来，各自从号段起点按装配顺序连续分号 ─────────────────────
    var roots = Build("Tool1", "Tool2");
    var gem = new GemCollectors();
    gem.Merge(roots, directory);
    Check(ReferenceEquals(GemCollectors.Current, gem), "new 出来即成为 Current");
    Check(new[] { EcCollector.FileName, SvCollector.FileName, AlarmCollector.FileName, EventCollector.FileName, DvCollector.FileName }
        .All(name => File.Exists(FileOf(name))), "五张编号表都应生成");
    Check(Ids(gem.Ec.Definitions).SequenceEqual(Enumerable.Range(10000, 4)), "ECID 从 10000 连续分");
    Check(Ids(gem.Sv.Definitions).SequenceEqual(Enumerable.Range(30000, 4)), "SVID 从 30000 连续分");
    Check(Ids(gem.Alarm.Definitions).SequenceEqual(Enumerable.Range(50000, 4)), "ALID 从 50000 连续分（Warn 级也有 ALID）");
    Check(Ids(gem.Event.Definitions).SequenceEqual(Enumerable.Range(70000, 6)), "CEID 从 70000 连续分：2 个组件事件 + 2 条报警×报出/清除");
    Check(Ids(gem.Dv.Definitions).SequenceEqual(Enumerable.Range(90000, 6)), "DVID 从 90000 连续分：报警事件带的 6 个 DV");
    Check(IdOf(gem.Ec.Definitions, "Tool1.ActionTimeout") < IdOf(gem.Ec.Definitions, "Tool2.ActionTimeout"),
        "按装配顺序分号：Tool1 在 Tool2 前面");
    Check(gem.Ec.Definitions.Any(row => row.Name == "Tool1.ActionTimeout" && row.Format == "Int" && row.Unit == "ms"
                                        && row.Min == "100" && row.Max == "60000" && row.Default == "3000"),
        "EC 全名 = 组件路径.属性名，元数据取自 [VariableMark]");
    Check(gem.Sv.Definitions.Any(row => row.Name == "Tool1.Carrier"), "SV 用 [VariableMark] 的 Name 覆盖采集名");
    Check(gem.Alarm.Definitions.Any(row => row.Name == "Tool1.TimeoutAlarm" && row.AlarmText == "动作超时"
                                           && row.AlarmLevel == "Alarm1" && row.Category == "Timeout"),
        "报警全名 = 组件路径.报警代码，文本、等级、分类取自 [Alarm]");
    Check(IdOf(gem.Event.Definitions, "Tool1.Arrived") == 70000 && IdOf(gem.Event.Definitions, "Tool2.Arrived") == 70001,
        "组件事件先分号，报警事件跟在后面");
    Check(File.ReadAllText(FileOf(EcCollector.FileName)).Contains("ECID=\"10000\""), "EcDefinitions.xml 按 ECID 写");

    // ── 2. 报警的报出/清除事件：非 Warn 每条两个，带 6 个报警 DV；CEID 回填进报警表；Warn 什么都没有 ───────
    var timeout = AlarmOf(gem, "Tool1.TimeoutAlarm");
    var setEvent = EventOf(gem, timeout.Id, clear: false);
    var clearEvent = EventOf(gem, timeout.Id, clear: true);
    Check(setEvent.Name == $"System.Alarm.{timeout.Id}.Set" && clearEvent.Name == $"System.Alarm.{timeout.Id}.Clear",
        "报警事件按 ALID 起名");
    Check(timeout.SetEventId == setEvent.Id && timeout.ClearEventId == clearEvent.Id, "报出/清除 CEID 回填进报警表");
    Check(setEvent.Payloads.Select(payload => payload.Dvid).SequenceEqual(gem.Dv.AlarmPayloadDvids)
          && gem.Dv.AlarmPayloadDvids.SequenceEqual(Enumerable.Range(90000, 6)), "报警事件带 6 个报警 DV");
    Check(setEvent.Description.Contains("Tool1.TimeoutAlarm") && setEvent.EventText == "报警报出", "报警事件说明带报警全名");
    Check(gem.Dv.Definitions[0] is { Name: "System.Alarm.Alid", Format: "Int" }, "第一个 DV 是 ALID");
    var warn = AlarmOf(gem, "Tool1.HotWarn");
    Check(warn is { SetEventId: 0, ClearEventId: 0 }
          && gem.Event.Definitions.All(row => !row.Name.StartsWith($"System.Alarm.{warn.Id}.")),
        "Warn 级报警不生成事件，事件编号填 0");
    Check(gem.Event.Definitions.Single(row => row.Name == "Tool1.Arrived").Payloads.Count == 0, "组件事件目前不带 DV");
    var alarmXml = File.ReadAllText(FileOf(AlarmCollector.FileName));
    Check(alarmXml.Contains($"SetEventId=\"{setEvent.Id}\"") && alarmXml.Contains($"ClearEventId=\"{clearEvent.Id}\""),
        "AlarmDefinitions.xml 写着报出/清除 CEID");
    Check(File.ReadAllText(FileOf(EventCollector.FileName)).Contains("<Payload DVID=\"90000\""), "EventDefinitions.xml 里事件挂着 DVID");

    // ── 3. 代码没变：再合并一次不写盘，编号不变 ───────────────────────────────────────
    var same = new GemCollectors();
    Check(!same.Ec.Merge(roots, directory) & !same.Sv.Merge(roots, directory) & !same.Alarm.Merge(roots, directory)
          & !same.Dv.Merge(directory, hasAlarmEvents: true)
          & !same.Event.Merge(roots, directory, same.Alarm.WithEvents, same.Dv.AlarmPayloadDvids)
          & !same.Alarm.LinkEvents(same.Event),
        "代码没变不该写盘");
    Check(IdOf(same.Ec.Definitions, "Tool2.AutoRun") == IdOf(gem.Ec.Definitions, "Tool2.AutoRun"), "重启后编号不变");

    // ── 4. 代码新增（code first）：新的接着往下分，老的不动 ─────────────────────────
    var grown = new GemCollectors();
    grown.Merge(Build("Tool1", "Tool2", "Tool3"), directory);
    Check(Ids(grown.Ec.Definitions).SequenceEqual(Enumerable.Range(10000, 6)), "新增 EC 接着分 10004、10005");
    Check(IdOf(grown.Alarm.Definitions, "Tool3.TimeoutAlarm") > 50003, "新增报警接着往下分");
    Check(IdOf(grown.Event.Definitions, "Tool3.Arrived") == 70006, "新增事件接着分 70006");
    var tool3Alarm = AlarmOf(grown, "Tool3.TimeoutAlarm");
    Check(tool3Alarm.SetEventId == 70007 && tool3Alarm.ClearEventId == 70008, "新增报警的报出/清除事件接着分并回填");
    Check(IdOf(grown.Sv.Definitions, "Tool1.State") == IdOf(gem.Sv.Definitions, "Tool1.State")
          && AlarmOf(grown, "Tool1.TimeoutAlarm").SetEventId == setEvent.Id, "老的 SVID、报警事件编号不动");

    // ── 5. 代码删掉：保号、停用，号不回收；再新增的跳过它往下分 ─────────────────────
    var shrunk = new GemCollectors();
    shrunk.Merge(Build("Tool1", "Tool3"), directory);
    Check(!EnabledOf(shrunk.Ec.Definitions, "Tool2.ActionTimeout")
          && IdOf(shrunk.Ec.Definitions, "Tool2.ActionTimeout") == IdOf(gem.Ec.Definitions, "Tool2.ActionTimeout"),
        "删掉的 EC 保号、Enabled=False");
    var tool2Alarm = AlarmOf(shrunk, "Tool2.TimeoutAlarm");
    Check(!tool2Alarm.Enabled && tool2Alarm is { SetEventId: 0, ClearEventId: 0 }
          && !EventOf(shrunk, tool2Alarm.Id, clear: false).Enabled, "删掉的报警停用，它的报出/清除事件也停用保号");
    Check(shrunk.CollectAll() is { } trimmed && trimmed.Ecs.All(item => !item.Name.StartsWith("Tool2."))
          && trimmed.Alarms.Count == 4 && trimmed.Events.Count == 6 && trimmed.Dvs.Count == 6,
        "一键采集只取在用的");

    var refilled = new GemCollectors();
    refilled.Merge(Build("Tool1", "Tool3", "Tool4"), directory);
    Check(IdOf(refilled.Ec.Definitions, "Tool4.ActionTimeout") > IdOf(grown.Ec.Definitions, "Tool3.AutoRun"),
        "新增的不回收 Tool2 的号，接着最大号往下分");
    Check(IdOf(refilled.Alarm.Definitions, "Tool4.TimeoutAlarm") > 50005, "报警同样不回收");

    // ── 6. 删掉的又加回来：原号复用、重新启用，报警事件也回来 ─────────────────────────
    var tools = Build("Tool1", "Tool2", "Tool3", "Tool4");
    var restored = new GemCollectors();
    restored.Merge(tools, directory);
    Check(EnabledOf(restored.Ec.Definitions, "Tool2.ActionTimeout")
          && IdOf(restored.Ec.Definitions, "Tool2.ActionTimeout") == IdOf(gem.Ec.Definitions, "Tool2.ActionTimeout"),
        "加回来的用原号并重新启用");
    Check(AlarmOf(restored, "Tool2.TimeoutAlarm").SetEventId == EventOf(gem, tool2Alarm.Id, clear: false).Id,
        "加回来的报警用回原来的报出事件编号");

    // ── 7. 元数据以代码为准替换；现场改的 Visible 保留 ───────────────────────────────
    var ecPath = FileOf(EcCollector.FileName);
    var edited = XmlHelper.Deserialize<EcDefinitionFile>(ecPath)!;
    var row = edited.Items.Single(item => item.Name == "Tool1.ActionTimeout");
    row.Description = "手改的描述";
    row.Visible = false;
    XmlHelper.Serialize(ecPath, edited);
    var refreshed = new GemCollectors();
    refreshed.Merge(tools, directory);
    var refreshedRow = refreshed.Ec.Definitions.Single(item => item.Name == "Tool1.ActionTimeout");
    Check(refreshedRow.Description == "动作超时", "描述按代码替换回来");
    Check(!refreshedRow.Visible, "现场改的 Visible 不被代码覆盖");

    // ── 8. 一键采集：EC、SV 带当前值；值在 ec.xml（EC 组件），编号表里不存值 ───────────
    var ec = new EcComponent();
    var tool1 = tools.OfType<ProbeTool>().Single(tool => tool.Name == "Tool1");
    tool1.ActionTimeout = 4567;
    tool1.State = 42;
    tool1.CarrierId = "FOUP01";
    var snapshot = refreshed.CollectAll();
    Check(snapshot.Ecs.Count == 8 && snapshot.Svs.Count == 8 && snapshot.Alarms.Count == 8
          && snapshot.Events.Count == 12 && snapshot.Dvs.Count == 6,
        "五类一次取全（事件 = 4 个组件事件 + 4 条报警×2）");
    Check(snapshot.Ecs.Single(item => item.Name == "Tool1.ActionTimeout") is { Value: "4567", Visible: false } one
          && one.Ecid == refreshedRow.Id, "EC 采集带编号和当前值");
    Check(snapshot.Ecs.Single(item => item.Name == "Tool3.ActionTimeout").Value == "3000", "没改过的 EC 取默认值");
    Check(snapshot.Ecs.Single(item => item.Name == "Tool1.AutoRun").Value == "False", "Bool 按 True/False 采");
    Check(snapshot.Svs.Single(item => item.Name == "Tool1.State").Value == "42", "SV 采集带当前值");
    Check(snapshot.Svs.Single(item => item.Name == "Tool1.Carrier").Value == "FOUP01", "SV 按覆盖后的采集名取值");
    Check(snapshot.Svs.Single(item => item.Name == "Tool3.Carrier").Value.Length == 0, "null 采成空串");
    Check(snapshot.Alarms.Single(item => item.Name == "Tool1.TimeoutAlarm").SetEventId == setEvent.Id
          && snapshot.Alarms.Single(item => item.Name == "Tool1.HotWarn").SetEventId == 0, "报警采集带报出/清除 CEID");
    Check(snapshot.Events.Single(item => item.Ceid == setEvent.Id).Dvids.SequenceEqual(Enumerable.Range(90000, 6)),
        "事件采集带 DVID");
    Check(ec.Get("Tool1", nameof(ProbeTool.ActionTimeout)) == "4567", "EC 的值在 EC 组件里");
    Check(!File.ReadAllText(ecPath).Contains("4567"), "EcDefinitions.xml 不存值");

    // ── 9. 表坏了：不覆盖、这一类本次不可用，别的照常 ─────────────────────────────────
    var svPath = FileOf(SvCollector.FileName);
    File.WriteAllText(svPath, "不是 xml");
    var broken = new GemCollectors();
    broken.Merge(tools, directory);
    Check(File.ReadAllText(svPath) == "不是 xml", "坏表不覆盖");
    Check(broken.Sv.Collect().Count == 0 && broken.Sv.Definitions.Count == 0, "坏表这一类本次不可用");
    Check(broken.Ec.Collect().Count == 8 && broken.Event.Collect().Count == 12, "别的表照常");

    var eventPath = FileOf(EventCollector.FileName);
    XmlHelper.Serialize(eventPath, new EventDefinitionFile
    {
        Items =
        [
            new EventDefinition { Id = 70000, Name = "Tool1.Arrived" },
            new EventDefinition { Id = 70000, Name = "Tool2.Arrived" },
        ]
    });
    var duplicateBefore = File.ReadAllText(eventPath);
    Check(!new EventCollector().Merge(tools, directory) && File.ReadAllText(eventPath) == duplicateBefore,
        "编号重复的表不覆盖");

    // ── 10. 号段外的旧号保留；号段用完分不到号 ───────────────────────────────────────────
    XmlHelper.Serialize(Path.Combine(spare, EcCollector.FileName), new EcDefinitionFile
    {
        Items = [new EcDefinition { Id = 800000, Name = "Tool1.ActionTimeout" }]
    });
    var legacy = new EcCollector();
    legacy.Merge(Build("Tool1"), spare);
    Check(IdOf(legacy.Definitions, "Tool1.ActionTimeout") == 800000, "号段外的旧号保留");
    Check(IdOf(legacy.Definitions, "Tool1.AutoRun") == EcCollector.FirstId, "号段内没有号时从起点分");

    XmlHelper.Serialize(Path.Combine(spare, EcCollector.FileName), new EcDefinitionFile
    {
        Items = [new EcDefinition { Id = EcCollector.LastId, Name = "Old.Thing" }]
    });
    var full = new EcCollector();
    full.Merge(Build("Tool1"), spare);
    Check(full.Definitions.Count == 1 && !full.Definitions[0].Enabled && full.Collect().Count == 0,
        "号段用完分不到号，旧的停用保号");

    // ── 11. 只有 Warn 级报警：有 ALID，但没有报警事件，也不需要报警 DV ─────────────────
    var warnOnly = new GemCollectors();
    warnOnly.Merge(ComponentLoader.Load([new ModuleConfig { Name = "Quiet", Type = typeof(ProbeWarnOnly).FullName }]), quiet);
    Check(warnOnly.Alarm.Definitions is [{ Name: "Quiet.OnlyWarn", SetEventId: 0, ClearEventId: 0 }], "Warn 有 ALID、没有事件");
    Check(warnOnly.Event.Definitions.Count == 0 && warnOnly.Dv.Definitions.Count == 0 && warnOnly.Dv.AlarmPayloadDvids.Count == 0,
        "没有报警事件时不生成报警 DV");
}
finally
{
    Directory.Delete(directory, recursive: true);
    Directory.Delete(spare, recursive: true);
    Directory.Delete(quiet, recursive: true);
    EcComponent.Current = null;
    GemCollectors.Current = null;
}

Console.WriteLine($"PASS: {checks} GEM collector checks (fresh generation in five ID ranges, alarm set/clear events with the alarm DV " +
                  "payload and CEIDs written back to the alarm table (none for Warn), unchanged restart, code-first add, " +
                  "disable-and-keep on removal, restore, metadata refresh with site Visible kept, one-click collect with " +
                  "live EC/SV values, broken tables left untouched, legacy IDs kept and range exhaustion).");

/// <summary>
/// 冒烟用设备：EC、SV、报警（一条 Alarm1、一条 Warn）、事件。
/// </summary>
[Component(description: "编号表冒烟用设备")]
public sealed class ProbeTool : ComponentBase
{
    [VariableMark(VariableType.EC, ValueFormat.Int, unit: "ms", min: "100", max: "60000", @default: "3000", description: "动作超时")]
    public int ActionTimeout
    {
        get { return GetEcInt(nameof(ActionTimeout)); }
        set { SetEcInt(nameof(ActionTimeout), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Bool, @default: "False", description: "自动运行")]
    public bool AutoRun
    {
        get { return bool.TryParse(GetEcString(nameof(AutoRun)), out var on) && on; }
        set { SetEc(nameof(AutoRun), value.ToString()); }
    }

    [VariableMark(VariableType.SV, ValueFormat.Int, description: "状态码")]
    public int State { get; set; } = 7;

    [VariableMark(VariableType.SV, ValueFormat.String, description: "当前载具号", Name = "Carrier")]
    public string? CarrierId { get; set; }

    [Alarm("动作超时", AlarmCategory.Timeout, AlarmLevel = AlarmLevel.Alarm1,
        Description = "动作没在规定时间内完成", Solution = "检查设备后复位")]
    public string TimeoutAlarm = nameof(TimeoutAlarm);

    [Alarm("温度偏高", AlarmCategory.ParameterControlError, AlarmLevel = AlarmLevel.Warn)]
    public string HotWarn = nameof(HotWarn);

    [EventAttribut("载具到达", Description = "载具放上")]
    public string ArrivedEvent = "Arrived";
}

/// <summary>
/// 只有一条 Warn 级报警的设备。
/// </summary>
[Component(description: "只有 Warn 报警的冒烟用设备")]
public sealed class ProbeWarnOnly : ComponentBase
{
    [Alarm("仅提示", AlarmCategory.Other, AlarmLevel = AlarmLevel.Warn)]
    public string OnlyWarn = nameof(OnlyWarn);
}
