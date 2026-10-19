using xyz.Components;
using xyz.Components.Attributes;
using xyz.Components.Components;
using xyz.Components.Enums;
using xyz.Components.Interfaces;
using xyz.Configs.Models;
using xyz.Modules;
using xyz.Modules.Enums;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Tools;
using xyz._35021.Module.Cds;
using xyz._35021.Shared.Dtos;

// CDS 冒烟：槽（Tank）照 sc 认液位 DI 信号（LL / L / H / HH / Overfill，没挂 = 没有这个点）、触发值照配的电平、
// 按配了的信号算液位（Overfill 亮了就是溢流）、读不到算不知道、信号对不上 / 液位高高 / 溢流报警且只能人工复位清；
// 五个功能（补液、排液、换液、供液、停止）开关哪组阀、状态怎么走（就绪只能补液得到、有报警就出错）、开始前的检查和被拒原因；
// 推送（槽跟着 IPA 模块推 IpaDto：液位、状态、各信号有变化才推，没配的信号是 null）；
// AI 液位计：液位信号换成读同一个 AI 的 AI 液位信号（触发值、容错、防抖在 EC），只有 AI、AI + 溢流 DI 混着用两种都照样补液、排液、供液。
// 全用假 PLC（摆 DI、AI，记 DO）和内存 EC，不连设备、不读写配置文件。

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

var directory = Path.Combine(Path.GetTempPath(), "xyz-cds-smoke-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
var previousPlc = IocHelper.GetService<IPlcIo>();
var previousIo = IocHelper.GetService<IIoComponent>();
var previousEc = IocHelper.GetService<IEcComponent>();
var previousAlarm = IocHelper.GetService<IAlarmComponent>();
try
{
    // 0. 假 PLC、IO 表（0~19 号点）、内存 EC、报警
    // 点表放进 IO 组件的点表目录：它开机准备时读点表、取 PLC
    string points = "Index,Name,PhysicalMin,PhysicalMax,LogicalMin,LogicalMax\n"
        + string.Concat(Enumerable.Range(0, 20).Select(i => $"{i},P{i},0,0,0,0\n"));
    foreach (string table in new[] { "DI", "DO", "AI" })
    {
        File.WriteAllText(Path.Combine(directory, table + ".csv"), points);
    }

    var plc = new FakePlc();
    SmokeIoc.Use<IPlcIo>(plc);
    var io = new IoComponent { PointTableDirectory = directory };
    SmokeIoc.Use<IIoComponent>(io);
    io.InitComponent();
    SmokeIoc.Use<IEcComponent>(new EcComponent());
    var alarms = new AlarmComponent();
    SmokeIoc.Use<IAlarmComponent>(alarms);

    bool Active(ComponentBase source, string code)
    {
        return alarms.ActiveAlarms.Any(alarm => alarm.SourcePath == source.FullPath && alarm.AlarmCode == code);
    }

    // 1. 装配：CDS、IPA 是分组，Tank1 挂五个信号；LL 配成低电平触发，别的高电平触发
    var tank = Load("Tank1", ("LL", 0, false), ("L", 1, true), ("H", 2, true), ("HH", 3, true), ("Overfill", 4, true));
    Check(tank.FullPath == "CDS.IPA.Tank1", "槽的全路径带上分组 CDS.IPA");
    Check(tank.FindChild<DiSensorComponent>("LL")!.TriggerValue == false
        && tank.FindChild<DiSensorComponent>("L")!.TriggerValue, "信号的触发值照 sc 配的电平装上");
    Check(tank.Level == TankLevel.Unknown, "还没扫过，液位不知道");

    // 液面到了哪几个信号：按各自的触发值摆 DI
    void Fill(bool ll, bool l, bool h, bool hh, bool overfill = false)
    {
        plc.SetDi(0, !ll);
        plc.SetDi(1, l);
        plc.SetDi(2, h);
        plc.SetDi(3, hh);
        plc.SetDi(4, overfill);
        tank.Tick();
    }

    // 2. PLC 没连上：信号读不到，液位不知道，也不报警
    plc.IsConnected = false;
    tank.Tick();
    Check(tank.Level == TankLevel.Unknown && alarms.ActiveAlarms.Count == 0, "信号读不到：液位不知道、不报警");
    plc.IsConnected = true;

    // 3. 液位分档：从下往上到了几个信号
    Fill(false, false, false, false);
    Check(tank.Level == TankLevel.LL, "一个信号都没到：低于 LL");
    Fill(true, false, false, false);
    Check(tank.Level == TankLevel.L, "到了 LL（低电平触发的 DI 为 false）、没到 L：L");
    Fill(true, true, false, false);
    Check(tank.Level == TankLevel.Normal, "到了 L、没到 H：Normal");
    Fill(true, true, true, false);
    Check(tank.Level == TankLevel.H, "到了 H、没到 HH：H");
    Check(alarms.ActiveAlarms.Count == 0, "LL 以下到 H 之间都不报警（低低由用槽的那一层看情况报）");
    Fill(true, true, true, true);
    Check(tank.Level == TankLevel.HH && Active(tank, tank.HighHighAlarm), "到了 HH：HH 并报液位高高");
    Check(!Active(tank, tank.LevelSensorAlarm) && !Active(tank, tank.OverfillAlarm), "到了 HH 只报高高");
    Check(alarms.ActiveAlarms.All(alarm => alarm.SourcePath == "CDS.IPA.Tank1"), "报警来源是槽，信号自己不报");

    // 4. 报警只能人工复位清：液面回落了报警还在，复位清掉；条件还在的复位后下一拍再报
    Fill(true, true, false, false);
    Check(tank.Level == TankLevel.Normal && Active(tank, tank.HighHighAlarm), "液面回落了，高高报警还在等人工复位");
    tank.Reset();
    tank.Tick();
    Check(!Active(tank, tank.HighHighAlarm), "复位清掉高高报警，液面正常不再报");
    Fill(true, true, true, true);
    tank.Reset();
    tank.Tick();
    Check(Active(tank, tank.HighHighAlarm), "还在 HH 以上，复位后下一拍又报");
    Fill(true, true, false, false);
    tank.Reset();
    tank.Tick();

    // 5. 信号对不上：上面的到了、下面的没到
    Fill(false, true, false, false);
    Check(tank.Level == TankLevel.Unknown && Active(tank, tank.LevelSensorAlarm), "到了 L 却没到 LL：液位不知道、报液位信号异常");
    Fill(true, true, false, true);
    Check(tank.Level == TankLevel.Unknown, "到了 HH 却没到 H：液位不知道");
    Check(Active(tank, tank.HighHighAlarm), "HH 亮了照样报高高");
    Fill(true, true, false, false);
    tank.Reset();
    tank.Tick();
    Check(alarms.ActiveAlarms.Count == 0, "信号对上了、复位后不再报");

    // 6. 溢流：Overfill 是最上面一个信号，亮了液位就是溢流
    Fill(true, true, true, true, overfill: true);
    Check(tank.Level == TankLevel.Overfill && Active(tank, tank.OverfillAlarm), "五个都亮：液位溢流、报溢流");
    Check(Active(tank, tank.HighHighAlarm) && !Active(tank, tank.LevelSensorAlarm), "溢流时 HH 也亮着：报高高，信号对得上");
    Fill(true, true, false, false);
    tank.Reset();
    tank.Tick();
    Check(tank.Level == TankLevel.Normal && alarms.ActiveAlarms.Count == 0, "溢流撤了、复位后回到正常");
    Fill(true, true, false, false, overfill: true);
    Check(tank.Level == TankLevel.Overfill, "没到 HH 就亮了 Overfill：液位照样是溢流（亮了就是已经满出来了）");
    Check(Active(tank, tank.OverfillAlarm) && Active(tank, tank.LevelSensorAlarm), "没到 HH 就亮了 Overfill：报溢流，也报液位信号异常");
    Fill(true, true, false, false);
    tank.Reset();
    tank.Tick();

    // 7. 有一个信号读不到（点表里没这个点）：液位不知道、不报警；Overfill 亮了照样是溢流
    var broken = Load("Tank9", ("L", 1, true), ("H", 2, true), ("HH", 99, true));
    plc.SetDi(1, true);
    plc.SetDi(2, true);
    broken.Tick();
    Check(broken.Level == TankLevel.Unknown && !broken.HasAlarm, "HH 的点读不到：液位不知道、不报警");
    var noOverfill = Load("Tank8", ("L", 1, true), ("H", 2, true), ("Overfill", 98, true));
    noOverfill.Tick();
    Check(noOverfill.Level == TankLevel.Unknown, "Overfill 的点读不到：跟别的信号一样，液位不知道");
    var unwired = Load("Tank16", ("L", 1, true), ("H", 2, true), ("HH", -1, true));
    unwired.Tick();
    Check(unwired.Level == TankLevel.H, "HH 挂了但 DiIndex 配成 -1：当没有这个点，不算读不到（L、H 都亮就是 H 档）");
    var spilled = Load("Tank7", ("L", 97, true), ("Overfill", 9, true));
    plc.SetDi(9, true);
    spilled.Tick();
    Check(spilled.Level == TankLevel.Overfill && Active(spilled, spilled.OverfillAlarm), "L 读不到但 Overfill 亮了：液位溢流、报溢流");

    // 8. 没挂的信号就是没有这个点：只装 L、H
    var partial = Load("Tank2", ("L", 5, true), ("H", 6, true));
    void FillPartial(bool l, bool h)
    {
        plc.SetDi(5, l);
        plc.SetDi(6, h);
        partial.Tick();
    }

    FillPartial(false, false);
    Check(partial.Level == TankLevel.L, "只装 L、H：没到 L 就是 L（分不出低低）");
    FillPartial(true, false);
    Check(partial.Level == TankLevel.Normal, "只装 L、H：到了 L 没到 H 是 Normal");
    FillPartial(true, true);
    Check(partial.Level == TankLevel.H && !partial.HasAlarm, "只装 L、H：到了 H 是 H，没装 HH 不报高高");
    FillPartial(false, true);
    Check(partial.Level == TankLevel.Unknown && Active(partial, partial.LevelSensorAlarm), "只装 L、H：到了 H 没到 L 照样算信号对不上");

    // 中间缺一个：只装 LL、H，到了 LL 没到 H 按到了的最高那个算（L），不往高了猜
    var gap = Load("Tank4", ("LL", 7, true), ("H", 8, true));
    plc.SetDi(7, true);
    plc.SetDi(8, false);
    gap.Tick();
    Check(gap.Level == TankLevel.L, "只装 LL、H：到了 LL 没到 H 算 L");

    // 只装 Overfill：没亮也不知道液位，亮了就是溢流
    var overfillOnly = Load("Tank5", ("Overfill", 6, true));
    plc.SetDi(6, false);
    overfillOnly.Tick();
    Check(overfillOnly.Level == TankLevel.Unknown && !overfillOnly.HasAlarm, "只装 Overfill、没亮：液位不知道");

    // 9. 一个信号都没挂：液位不知道，什么都不报
    var bare = Load("Tank3");
    bare.Tick();
    Check(bare.Level == TankLevel.Unknown && !bare.HasAlarm, "没挂信号：液位不知道、不报警");

    // 10. 用的地方走接口
    ITank shared = tank;
    Check(shared.Level == TankLevel.Normal && shared.FullPath == "CDS.IPA.Tank1", "ITank 拿得到液位和路径");

    // 11. 三条报警各有防抖时间（EC）：条件一直成立满这么久才报，中间断了重新计时；液位本身不等
    var defaults = new TankComponent();
    Check(defaults.LevelSensorDebounceMs == 3000 && defaults.HighHighDebounceMs == 1000 && defaults.OverfillDebounceMs == 1000,
        "防抖默认值：信号异常 3000 ms、高高 1000 ms、溢流 1000 ms");

    var slow = Load("Tank6", ("L", 10, true), ("H", 11, true), ("HH", 12, true), ("Overfill", 13, true));
    slow.LevelSensorDebounceMs = 200;
    slow.HighHighDebounceMs = 200;
    slow.OverfillDebounceMs = 200;
    plc.SetDi(10, true);
    plc.SetDi(11, true);
    plc.SetDi(12, true);
    slow.Tick();
    Check(slow.Level == TankLevel.HH && !Active(slow, slow.HighHighAlarm), "HH 刚亮：液位马上是 HH，高高还没满防抖时间不报");
    Thread.Sleep(80);
    plc.SetDi(12, false);
    slow.Tick();
    plc.SetDi(12, true);
    slow.Tick();
    Thread.Sleep(150);
    slow.Tick();
    Check(!Active(slow, slow.HighHighAlarm), "HH 中间灭过一下：重新计时，还没满防抖时间不报");
    Thread.Sleep(100);
    slow.Tick();
    Check(Active(slow, slow.HighHighAlarm), "HH 一直亮满防抖时间：报高高");

    plc.SetDi(13, true);
    slow.Tick();
    Check(slow.Level == TankLevel.Overfill && !Active(slow, slow.OverfillAlarm), "Overfill 刚亮：液位马上是溢流，还没满防抖时间不报");
    Thread.Sleep(250);
    slow.Tick();
    Check(Active(slow, slow.OverfillAlarm), "Overfill 一直亮满防抖时间：报溢流");

    plc.SetDi(12, false);
    plc.SetDi(13, false);
    plc.SetDi(10, false);
    slow.Tick();
    Check(slow.Level == TankLevel.Unknown && !Active(slow, slow.LevelSensorAlarm), "H 亮着 L 没亮：液位马上不知道，信号异常还没满防抖时间不报");
    Thread.Sleep(250);
    slow.Tick();
    Check(Active(slow, slow.LevelSensorAlarm), "信号对不上一直满防抖时间：报液位信号异常");

    // 12. 五个功能和状态：补液阀组两个阀（DO 0、1）、排液阀组两个阀（DO 2、3）、供液阀组一个阀（DO 4），液位信号 DI 14~18
    var cds = LoadTank("Tank10", null, [("LL", 14, true), ("L", 15, true), ("H", 16, true), ("HH", 17, true), ("Overfill", 18, true)],
        fill: [(0, -1), (1, -1)], drain: [(2, -1), (3, -1)], supply: [(4, -1)]);
    cds.FillTimeoutMs = 300;
    cds.DrainTimeoutMs = 300;
    cds.DrainDelayMs = 150;
    cds.SupplyLowLowDebounceMs = 0;

    void Liquid(bool ll, bool l, bool h, bool hh = false, bool overfill = false)
    {
        plc.SetDi(14, ll);
        plc.SetDi(15, l);
        plc.SetDi(16, h);
        plc.SetDi(17, hh);
        plc.SetDi(18, overfill);
        cds.Tick();
    }

    bool AllClosed()
    {
        return !plc.Do(0) && !plc.Do(1) && !plc.Do(2) && !plc.Do(3) && !plc.Do(4);
    }

    Liquid(true, true, false);
    Check(cds.State == TankState.Idle && AllClosed(), "开机：空闲、阀不动；槽里本来有液也不算就绪");
    Check(cds.Supply().ErrorMessage == ErrorCodes.TankNotReady && AllClosed(), "空闲不能供液：要先补液");

    // 补液：开补液阀组，H 亮了关阀、就绪
    var filling = cds.Fill();
    Check(filling.IsSuccess && cds.State == TankState.Filling && plc.Do(0) && plc.Do(1) && !plc.Do(2), "补液：补液阀组两个阀都开");
    Check(cds.Fill().ErrorMessage == ErrorCodes.TankBusy && cds.Drain().ErrorMessage == ErrorCodes.TankBusy,
        "补液中再补液、排液都被拒：要先停止");
    Liquid(true, true, true);
    Check(cds.State == TankState.Ready && AllClosed(), "H 亮了：关补液阀、就绪");

    // 供液：就绪才能供；掉到 L 以下 Tank 不管；停止直接回空闲
    Check(cds.Supply().IsSuccess && cds.State == TankState.Supplying && plc.Do(4) && !plc.Do(0), "就绪了能供液：开供液阀");
    Liquid(true, false, false);
    Check(cds.State == TankState.Supplying && plc.Do(4), "供液中掉到 L 以下：接着供液，Tank 不自动补");
    Check(cds.Stop().IsSuccess && cds.State == TankState.Idle && AllClosed(), "停止：关全部阀、直接回空闲");

    // 本来就到 H：补液不开阀、直接就绪；就绪时掉到 L 以下回空闲
    Liquid(true, true, true);
    Check(cds.Fill().IsSuccess && cds.State == TankState.Ready && AllClosed(), "H 本来就亮着：补液不开阀、直接就绪");
    Liquid(true, false, false);
    Check(cds.State == TankState.Idle, "就绪时液位掉到 L 以下：回空闲");

    // 供液中掉到 LL 以下：报供液低低 → 有报警就出错、阀全关；出错要先复位
    Liquid(true, true, true);
    cds.Fill();
    cds.Supply();
    Liquid(false, false, false);
    Check(cds.State == TankState.Error && AllClosed() && Active(cds, cds.SupplyLowLowAlarm), "供液中掉到 LL 以下：报供液低低、出错、阀全关");
    Check(cds.Fill().ErrorMessage == ErrorCodes.TankError && cds.Drain().ErrorMessage == ErrorCodes.TankError, "出错了什么都不让做：要先复位");
    cds.Reset();
    cds.Tick();
    Check(cds.State == TankState.Idle && !cds.HasAlarm, "复位：清掉报警、回空闲");

    // 排液：开排液阀组，LL 灭了再排满排空延时才关；中间 LL 又亮了重新等
    Liquid(true, true, false);
    Check(cds.Drain().IsSuccess && cds.State == TankState.Draining && plc.Do(2) && plc.Do(3), "排液：排液阀组两个阀都开");
    Liquid(false, false, false);
    Check(cds.State == TankState.Draining && plc.Do(2), "LL 刚灭：排空延时没到，接着排");
    Thread.Sleep(80);
    Liquid(true, false, false);
    Thread.Sleep(100);
    Liquid(false, false, false);
    Check(cds.State == TankState.Draining && plc.Do(2), "LL 中间又亮过：排空延时重新算");
    Thread.Sleep(200);
    cds.Tick();
    Check(cds.State == TankState.Idle && AllClosed(), "LL 灭了满排空延时：关排液阀、回空闲");

    // 换液：先排、排空了接着补、补到 H 就绪
    Liquid(true, true, false);
    Check(cds.Exchange().IsSuccess && cds.State == TankState.Exchanging && plc.Do(2) && !plc.Do(0), "换液：先开排液阀");
    Liquid(false, false, false);
    Thread.Sleep(200);
    cds.Tick();
    Check(cds.State == TankState.Exchanging && !plc.Do(2) && plc.Do(0) && plc.Do(1), "排空了：关排液阀、接着开补液阀");
    Liquid(true, true, true);
    Check(cds.State == TankState.Ready && AllClosed(), "补到 H：换液完成、就绪");

    // 中止跟停止一样：供液也停、回空闲
    cds.Supply();
    cds.Abort();
    Check(cds.State == TankState.Idle && AllClosed(), "中止：关全部阀（供液也停）、回空闲");

    // 补液超时、排液超时：报警 → 出错、阀全关
    Liquid(true, true, false);
    cds.Fill();
    Thread.Sleep(350);
    cds.Tick();
    Check(cds.State == TankState.Error && AllClosed() && Active(cds, cds.FillTimeoutAlarm), "补液超时：报警、出错、阀全关");
    cds.Reset();
    cds.Tick();
    cds.Drain();
    Thread.Sleep(350);
    cds.Tick();
    Check(cds.State == TankState.Error && AllClosed() && Active(cds, cds.DrainTimeoutAlarm), "排液 LL 一直不灭：超时报警、出错、阀全关");
    cds.Reset();
    cds.Tick();

    // 补液时 HH 亮了：当场关补液阀、回空闲；高高报警满防抖时间再进出错
    cds.HighHighDebounceMs = 200;
    cds.Fill();
    Liquid(true, true, true, hh: true);
    Check(cds.State == TankState.Idle && AllClosed() && !Active(cds, cds.HighHighAlarm), "补液时 HH 亮了：当场关补液阀、回空闲");
    Thread.Sleep(250);
    cds.Tick();
    Check(cds.State == TankState.Error && Active(cds, cds.HighHighAlarm), "HH 一直亮满防抖时间：报高高、出错");
    Liquid(true, true, false);
    cds.Reset();
    cds.Tick();

    // 补液前的检查：HH 亮着、液位不知道都不补
    cds.HighHighDebounceMs = 60000;
    Liquid(true, true, true, hh: true);
    Check(cds.Fill().ErrorMessage == ErrorCodes.TankHighHigh, "HH 亮着：不补液");
    cds.HighHighDebounceMs = 0;
    cds.LevelSensorDebounceMs = 60000;
    Liquid(false, true, false);
    Check(cds.Fill().ErrorMessage == ErrorCodes.TankLevelUnknown, "液位对不上（不知道）：不补液");
    cds.LevelSensorDebounceMs = 0;
    Liquid(true, true, false);

    // PLC 没连上：停止关不上阀，回原因
    plc.IsConnected = false;
    Check(cds.Stop().ErrorMessage == ErrorCodes.TankCloseFailed, "PLC 没连上：停止回有阀没关上");
    plc.IsConnected = true;
    cds.Tick();

    // 没挂的阀组、信号：对应功能不能用
    var partialTank = LoadTank("Tank11", null, [("LL", 14, true), ("L", 15, true)], fill: [(5, -1)], drain: [], supply: []);
    var noDrain = partialTank.Drain();
    Check(noDrain.ErrorMessage == ErrorCodes.TankValveGroupMissing && noDrain.Args.SequenceEqual(["CDS.IPA.Tank11", "Drain"]),
        "没挂 Drain 阀组：排液被拒，带槽路径和阀组名");
    var noTarget = partialTank.Fill();
    Check(noTarget.ErrorMessage == ErrorCodes.TankSignalMissing && noTarget.Args.SequenceEqual(["CDS.IPA.Tank11", "H"]),
        "没挂 H（补液完成位）：补液被拒");
    Check(partialTank.Supply().ErrorMessage == ErrorCodes.TankValveGroupMissing, "没挂 Supply 阀组：供液被拒");

    // SC FillTo：补到 L 就算完成；不分大小写；配成 L、H 以外开机就报
    var lowTarget = LoadTank("Tank12", "l", [("LL", 14, true), ("L", 15, true), ("H", 16, true)], fill: [(5, -1)], drain: [], supply: []);
    Check(lowTarget.FillTo == "L", "FillTo 不分大小写，规整成 L");
    plc.SetDi(14, true);
    plc.SetDi(15, false);
    plc.SetDi(16, false);
    lowTarget.Tick();
    Check(lowTarget.Fill().IsSuccess && lowTarget.State == TankState.Filling && plc.Do(5), "FillTo=L：开补液阀");
    plc.SetDi(15, true);
    lowTarget.Tick();
    Check(lowTarget.State == TankState.Ready && !plc.Do(5), "FillTo=L：L 亮了就算补完、就绪");
    var badTargetRejected = false;
    try
    {
        LoadTank("Tank13", "HH", [("H", 16, true)], fill: [], drain: [], supply: []);
    }
    catch (InvalidOperationException)
    {
        badTargetRejected = true;
    }

    Check(badTargetRejected, "FillTo 配成 HH：开机就报");

    // 阀组里有阀开不了（DO 99 点表里没有）：拒绝，开了的全关回去
    var brokenValve = LoadTank("Tank14", null, [("LL", 14, true), ("L", 15, true), ("H", 16, true)], fill: [(6, -1), (99, -1)], drain: [], supply: []);
    plc.SetDi(16, false);
    brokenValve.Tick();
    var openFailed = brokenValve.Fill();
    Check(openFailed.ErrorMessage == ErrorCodes.TankValveFailed && !plc.Do(6) && brokenValve.State == TankState.Idle,
        "补液阀组有阀开不了：拒绝、开了的关回去、还是空闲");

    // 下面阀的报警也算槽出错：供液阀等到位反馈（DI 19 一直不来）超时
    var feedback = LoadTank("Tank15", null, [("LL", 14, true), ("L", 15, true), ("H", 16, true)],
        fill: [(8, -1)], drain: [], supply: [(7, 19)]);
    var supplyValve = feedback.FindChild<ValveComponent>("V7")!;
    supplyValve.ActionTimeoutMs = 100;
    plc.SetDi(16, true);
    feedback.Tick();
    feedback.Fill();
    Check(feedback.Supply().IsSuccess && plc.Do(7), "供液阀开了，等到位反馈");
    Thread.Sleep(150);
    feedback.Tick();
    Check(feedback.State == TankState.Error && !plc.Do(7) && Active(supplyValve, supplyValve.TimeoutAlarm),
        "供液阀到位超时报警：槽进出错、阀全关");

    // 13. 推送：槽跟着它上面的 IPA 模块推（IpaDto：IPA 的状态 + 下面各槽的 TankDto，token 是模块名，留存），
    //     IPA 每拍比一次，有变化（第一拍也算）才推；槽的信号带名字和各自的触发状态，没配的是 null
    var ipa = LoadIpa("Tank16", [("LL", 10, true), ("H", 11, true)], fill: [(9, -1)]);
    var pushed = ipa.FindChild<SmokeTank>("Tank16")!;
    Check(ipa.Tanks.Count == 1 && ReferenceEquals(ipa.Tanks[0], pushed) && IocHelper.GetServices<ISupplyModule>().Single() == ipa,
        "IPA 认出下面的槽；按 ISupplyModule 找得到 IPA");
    Check(pushed.LL is not null && pushed.H is not null && pushed.L is null && pushed.HH is null && pushed.Overfill is null,
        "槽给出配了的信号，没挂的是 null");
    var received = new List<IpaDto>();
    using var subscription = EventBus.Register<IpaDto>(ipa.Name, dto => received.Add(dto));
    plc.SetDi(10, true);
    plc.SetDi(11, false);
    // IPA 开机 Bind 时取映射组件：换了要重新 Bind
    SmokeIoc.Use<IMapperComponent>(null);
    ipa.Bind();
    ipa.Tick();
    Check(received.Count == 0, "没装映射组件：不推（开机时映射组件是必配的）");
    var mapper = new MapperComponent();
    SmokeIoc.Use<IMapperComponent>(mapper);
    ipa.Bind();
    ipa.Tick();
    Check(received.Count == 1 && received[0].Module == "IPA" && received[0].State == ModuleState.NotInit && received[0].Tanks.Count == 1,
        "第一拍：推一条，带 IPA 的名字、状态和下面的槽");
    var first = received[0].Tanks[0];
    Check(first.Path == "CDS.IPA.Tank16" && first.Level == (int)TankLevel.L && first.State == (int)TankState.Idle,
        "槽带全路径、液位、状态");
    Check(first.LL is not null && first.LL.Name == "LL" && first.LL.IsActive == true
        && first.H is not null && first.H.Name == "H" && first.H.IsActive == false, "配了的信号带名字和各自的触发状态");
    Check(first.L is null && first.HH is null && first.Overfill is null, "没配的信号是 null（界面不画）");
    ipa.Tick();
    ipa.Tick();
    Check(received.Count == 1, "没变化：不推");
    plc.SetDi(11, true);
    ipa.Tick();
    Check(received.Count == 2 && received[1].Tanks[0].Level == (int)TankLevel.H && received[1].Tanks[0].H!.IsActive == true, "信号变了：推一条新的");
    plc.IsConnected = false;
    ipa.Tick();
    Check(received.Count == 3 && received[2].Tanks[0].LL!.IsActive is null && received[2].Tanks[0].H!.IsActive is null
        && received[2].Tanks[0].Level == (int)TankLevel.Unknown, "信号读不到：推 null（界面画不知道色）");
    plc.IsConnected = true;
    ipa.Tick();
    Check(received.Count == 4 && received[3].Tanks[0].Level == (int)TankLevel.H, "读回来了：再推一条");
    Check(pushed.Fill().IsSuccess && pushed.State == TankState.Ready && received.Count == 4, "补液（H 已经亮了）直接就绪：在扫描外变的，当场不推");
    ipa.Tick();
    Check(received.Count == 5 && received[4].Tanks[0].State == (int)TankState.Ready, "槽的状态变了：下一拍推");
    Check(ipa.InitModule() is not null && ipa.State == ModuleState.Idle && received.Count == 5, "IPA 初始化（功能还没做）：直接进空闲");
    ipa.Tick();
    Check(received.Count == 6 && received[5].State == ModuleState.Idle, "IPA 自己的状态变了：也推");
    IpaDto? late = null;
    using (EventBus.Register<IpaDto>(ipa.Name, dto => late = dto))
    {
        Check(late is not null && late.State == ModuleState.Idle && late.Tanks[0].State == (int)TankState.Ready,
            "留存：后打开的页面一订上就拿到最新一条");
    }

    // 14. AI 液位计：LL～Overfill 都挂 AI 液位信号、读同一个 AI（0 号），触发值 5 / 6 / 18 / 20 / 22 L；
    // 补液阀 DO 10、排液阀 DO 11、供液阀 DO 12
    var levelDefaults = new AiLevelComponent();
    Check(levelDefaults.TriggerValue == 0 && levelDefaults.Tolerance == 0.2 && levelDefaults.DebounceMs == 200 && !levelDefaults.IsConfigured,
        "AI 液位信号默认：触发值没设（0）、容错 0.2 L、防抖 200 ms、没配 AI 点");

    var meter = LoadTank("Tank20", null, [], fill: [(10, -1)], drain: [(11, -1)], supply: [(12, -1)],
        levels: [("LL", 0, 5), ("L", 0, 6), ("H", 0, 18), ("HH", 0, 20), ("Overfill", 0, 22)]);
    Check(meter.LL is AiLevelComponent && meter.Overfill is AiLevelComponent,
        "LL～Overfill 挂 AI 液位信号：槽照样按节点名认（都走 IDiSensor）");

    void Meter(double liters)
    {
        plc.SetAi(0, liters);
        meter.Tick();
    }

    // 液位分档：读数到了哪几个触发值
    Meter(3);
    Check(meter.Level == TankLevel.LL && !meter.HasAlarm, "读数 3 L：低于 LL");
    Meter(5.5);
    Check(meter.Level == TankLevel.L, "读数 5.5 L：到了 LL、没到 L");
    Meter(7.3);
    Check(meter.Level == TankLevel.Normal, "读数 7.3 L：Normal");
    Meter(18.5);
    Check(meter.Level == TankLevel.H, "读数 18.5 L：H");
    Meter(20.5);
    Check(meter.Level == TankLevel.HH && Active(meter, meter.HighHighAlarm) && !Active(meter, meter.LevelSensorAlarm),
        "读数 20.5 L：HH、报高高；几个点读同一个数，不会上面到了下面没到");
    Meter(22.5);
    Check(meter.Level == TankLevel.Overfill && Active(meter, meter.OverfillAlarm), "读数 22.5 L：溢流、报溢流");
    Meter(10);
    meter.Reset();
    meter.Tick();
    Check(meter.Level == TankLevel.Normal && !meter.HasAlarm && meter.State == TankState.Idle, "回到 10 L、复位：Normal、报警清掉、空闲");

    // 容错：涨到 6 L 才算到 L，掉到 5.8 L 以下才算没到；在中间来回晃液位不变、不推
    Meter(5.5);
    Meter(5.95);
    Check(meter.Level == TankLevel.L, "从下面涨到 5.95 L：还没到 L（要到 6 L）");
    Meter(6.0);
    Check(meter.Level == TankLevel.Normal, "涨到 6 L：到了 L");
    foreach (var liters in new[] { 5.85, 6.05, 5.81, 5.95, 5.82 })
    {
        Meter(liters);
        Check(meter.Level == TankLevel.Normal, $"在 5.8～6 L 之间来回晃（{liters} L）：一直算到了 L，液位不跳");
    }

    Meter(5.79);
    Check(meter.Level == TankLevel.L, "掉到 5.8 L 以下：才算没到 L");
    var lowSignal = meter.FindChild<AiLevelComponent>("L")!;
    lowSignal.Tolerance = 0;
    Meter(6.05);
    Meter(5.95);
    Check(meter.Level == TankLevel.L, "容错设成 0：掉到 6 L 以下马上算没到（容错按液面晃的幅度在 EC 设）");
    lowSignal.Tolerance = 0.2;

    // 读不到（PLC 没连上）：几个点都读不到，液位不知道、不报警、不让补液
    Meter(10);
    plc.IsConnected = false;
    meter.Tick();
    Check(meter.Level == TankLevel.Unknown && !meter.HasAlarm && meter.LL!.IsActive is null, "AI 读不到：液位不知道、信号是 null、不报警");
    Check(meter.Fill().ErrorMessage == ErrorCodes.TankLevelUnknown, "液位不知道：不补液");
    plc.IsConnected = true;
    meter.Tick();
    Check(meter.Level == TankLevel.Normal, "读回来了：液位跟着读数");

    // 补液：防抖防读数冒尖（不然补液时冒一下就提前停了），一直到了 H 满防抖才关阀、就绪
    var highSignal = meter.FindChild<AiLevelComponent>("H")!;
    highSignal.DebounceMs = 200;
    Check(meter.Fill().IsSuccess && meter.State == TankState.Filling && plc.Do(10), "10 L 补液：开补液阀");
    Meter(18.5);
    Meter(12);
    Thread.Sleep(250);
    meter.Tick();
    Check(meter.State == TankState.Filling && plc.Do(10), "读数冒到 18.5 L 一拍又回 12 L：不认，接着补");
    Meter(18.1);
    Check(meter.State == TankState.Filling && plc.Do(10), "到了 18 L、还没满防抖时间：接着补");
    Thread.Sleep(250);
    meter.Tick();
    Check(meter.State == TankState.Ready && !plc.Do(10) && meter.Level == TankLevel.H, "到 H 满防抖时间：关补液阀、就绪");
    highSignal.DebounceMs = 0;

    // 供液：掉到 LL 的容错以下才算低低（4.9 L 还在容错里）→ 报警、出错、阀全关
    meter.SupplyLowLowDebounceMs = 0;
    Check(meter.Supply().IsSuccess && plc.Do(12), "就绪了供液：开供液阀");
    Meter(4.9);
    Check(meter.State == TankState.Supplying && plc.Do(12), "供到 4.9 L：还在 LL 的容错里（4.8 L 以下才算没到），接着供");
    Meter(4.7);
    Check(meter.State == TankState.Error && !plc.Do(12) && Active(meter, meter.SupplyLowLowAlarm), "掉到 4.8 L 以下：报供液低低、出错、阀全关");
    meter.Reset();
    meter.Tick();

    // 排液：LL 掉到 4.8 L 以下才算灭，再排满排空延时关阀
    meter.DrainDelayMs = 100;
    Meter(10);
    Check(meter.Drain().IsSuccess && plc.Do(11), "排液：开排液阀");
    Meter(4.9);
    Thread.Sleep(150);
    meter.Tick();
    Check(meter.State == TankState.Draining && plc.Do(11), "排到 4.9 L：还在 LL 的容错里，不算排空");
    Meter(4.7);
    Thread.Sleep(150);
    meter.Tick();
    Check(meter.State == TankState.Idle && !plc.Do(11), "掉到 4.8 L 以下满排空延时：关排液阀、回空闲");

    // 推送：AI 液位信号跟 DI 信号一样转成带节点名和触发状态的信号（映射组件的规则），界面信号灯照旧
    Meter(7.3);
    var meterDto = mapper.Map<TankDto>(meter);
    Check(meterDto.Level == (int)TankLevel.Normal
        && meterDto.LL!.Name == "LL" && meterDto.LL!.IsActive == true
        && meterDto.L!.IsActive == true && meterDto.H!.IsActive == false && meterDto.Overfill is not null,
        "转成 TankDto：AI 液位信号带节点名和各自的触发状态");

    // 触发值没设（新生成的 EC 是 0）：当读不到；AiIndex 配成 -1：当没有这个点
    var unset = LoadTank("Tank21", null, [], [], [], [], levels: [("L", 0, 0), ("H", 0, 18), ("HH", -1, 20)]);
    unset.Tick();
    Check(unset.Level == TankLevel.Unknown && unset.L!.IsActive is null, "L 的触发值没设：当读不到，液位不知道");
    unset.FindChild<AiLevelComponent>("L")!.TriggerValue = 6;
    unset.Tick();
    Check(unset.Level == TankLevel.Normal && unset.HH is null, "在线设上触发值：下一拍生效（7.3 L 是 Normal）；HH 的 AiIndex 配成 -1 当没有这个点");

    // FillTo=L：补到 L 就绪，之后液面在 6 L 上下晃，有容错就一直就绪，不会晃一下退回空闲
    var lowMeter = LoadTank("Tank22", "L", [], fill: [(13, -1)], drain: [], supply: [],
        levels: [("LL", 2, 5), ("L", 2, 6), ("H", 2, 18)]);
    plc.SetAi(2, 5.5);
    lowMeter.Tick();
    Check(lowMeter.Fill().IsSuccess && plc.Do(13), "FillTo=L、5.5 L：开补液阀");
    plc.SetAi(2, 6.02);
    lowMeter.Tick();
    Check(lowMeter.State == TankState.Ready && !plc.Do(13), "到了 6 L：关补液阀、就绪");
    foreach (var liters in new[] { 5.9, 6.03, 5.85, 5.97 })
    {
        plc.SetAi(2, liters);
        lowMeter.Tick();
    }

    Check(lowMeter.State == TankState.Ready, "关阀后液面在 6 L 上下晃：还是就绪");

    // 15. 混着用：LL～HH 读液位计（AI 1），Overfill 是单独的 DI 浮球（DI 19）；补液阀 DO 14、排液阀 DO 15
    var mixed = LoadTank("Tank23", null, [("Overfill", 19, true)], fill: [(14, -1)], drain: [(15, -1)], supply: [],
        levels: [("LL", 1, 5), ("L", 1, 6), ("H", 1, 18), ("HH", 1, 20)]);
    Check(mixed.Overfill is DiSensorComponent && mixed.HH is AiLevelComponent, "混着用：Overfill 是 DI 信号，别的是 AI 液位信号");
    mixed.DrainDelayMs = 0;
    plc.SetDi(19, false);
    plc.SetAi(1, 10);
    mixed.Tick();
    Check(mixed.Level == TankLevel.Normal && mixed.Fill().IsSuccess && plc.Do(14), "液位计 10 L、浮球没亮：Normal，补液开阀");
    plc.SetAi(1, 18.2);
    mixed.Tick();
    Check(mixed.State == TankState.Ready && !plc.Do(14), "补到 18 L（H）：关补液阀、就绪");
    Check(mixed.Drain().IsSuccess && plc.Do(15), "排液：开排液阀");
    plc.SetAi(1, 4.7);
    mixed.Tick();
    Check(mixed.State == TankState.Idle && !plc.Do(15), "排到 4.8 L 以下：关排液阀、回空闲");
    plc.SetAi(1, 19);
    plc.SetDi(19, true);
    mixed.Tick();
    Check(mixed.Level == TankLevel.Overfill && Active(mixed, mixed.OverfillAlarm) && Active(mixed, mixed.LevelSensorAlarm),
        "浮球亮了、液位计才 19 L：溢流，还报液位信号异常（两边对不上，看得出有一个坏了）");
    var meterLost = LoadTank("Tank24", null, [("Overfill", 19, true)], [], [], [], levels: [("L", 99, 6), ("H", 99, 18)]);
    meterLost.Tick();
    Check(meterLost.Level == TankLevel.Overfill, "液位计读不到（点表里没这个 AI）但浮球亮了：照样是溢流");
}
finally
{
    SmokeIoc.Use<IPlcIo>(previousPlc);
    SmokeIoc.Use<IIoComponent>(previousIo);
    SmokeIoc.Use<IEcComponent>(previousEc);
    SmokeIoc.Use<IAlarmComponent>(previousAlarm);
    Directory.Delete(directory, recursive: true);
}

Console.WriteLine($"PASS: {checks} cds checks (tank: level DI signals found by node name (a missing node means no such point), "
    + "trigger values loaded from sc, the level band from the signals that are configured with overfill as the top one, "
    + "unknown when a signal cannot be read unless overfill is on, the signal mismatch / high-high / overfill alarms raised by the tank "
    + "after each one's EC debounce time and cleared only by a manual reset; the fill / drain / exchange / supply / stop functions "
    + "driving their valve groups, ready only through a fill, any alarm putting the tank into error with every valve closed, "
    + "the SC fill target, the checks before each function with their error codes, "
    + "and the push of level / state / each signal only on change, with unconfigured signals as null; "
    + "AI level signals reading one level meter against EC trigger values with a tolerance and debounce, "
    + "an AI-only tank and an AI + overflow DI tank running fill / drain / supply unchanged)");

// 只挂液位信号的槽
static SmokeTank Load(string name, params (string Signal, int Di, bool Trigger)[] signals)
{
    return LoadTank(name, null, signals, [], [], []);
}

// 照 sc 的写法装一个槽：CDS、IPA 是分组，信号按 (节点名, DI 号, 触发值)、阀组的阀按 (DO 号, 到位 DI 号) 挂上去，阀名叫 V+DO 号；
// AI 液位信号按 (节点名, AI 号, 触发值) 挂，触发值是 EC，装好了再设；信号和报警的防抖都设成 0，一拍就认
static SmokeTank LoadTank(string name, string? fillTo, (string Signal, int Di, bool Trigger)[] signals,
    (int Do, int Di)[] fill, (int Do, int Di)[] drain, (int Do, int Di)[] supply,
    (string Signal, int Ai, double Trigger)[]? levels = null)
{
    var group = new ModuleConfig
    {
        Name = "CDS",
        Children = [new ModuleConfig { Name = "IPA", Children = [TankNode(name, fillTo, signals, fill, drain, supply, levels)] }]
    };
    var loaded = (SmokeTank)SmokeIoc.Load([group]).Single();
    NoDebounce(loaded, levels);
    loaded.InitComponent();
    ComponentLoader.BindAll([loaded]);
    return loaded;
}

// 槽装在 IPA 模块下面（sc 里 CDS → IPA → 槽，IPA 配了 Type）：推送跟着 IPA 走
static SmokeIpa LoadIpa(string name, (string Signal, int Di, bool Trigger)[] signals, (int Do, int Di)[] fill)
{
    var group = new ModuleConfig
    {
        Name = "CDS",
        Children =
        [
            new ModuleConfig { Name = "IPA", Type = typeof(SmokeIpa).FullName, Children = [TankNode(name, null, signals, fill, [], [], null)] },
        ]
    };
    var ipa = (SmokeIpa)SmokeIoc.Load([group]).Single();
    NoDebounce(ipa.FindChild<SmokeTank>(name)!, null);
    ipa.InitComponent();
    ComponentLoader.BindAll([ipa]);
    return ipa;
}

static ModuleConfig TankNode(string name, string? fillTo, (string Signal, int Di, bool Trigger)[] signals,
    (int Do, int Di)[] fill, (int Do, int Di)[] drain, (int Do, int Di)[] supply,
    (string Signal, int Ai, double Trigger)[]? levels)
{
    var tank = new ModuleConfig { Name = name, Type = typeof(SmokeTank).FullName };
    if (fillTo is not null)
    {
        tank.Values.Add(new() { Name = "FillTo", Value = fillTo });
    }

    AddGroup(tank, "Fill", fill);
    AddGroup(tank, "Drain", drain);
    AddGroup(tank, "Supply", supply);
    foreach (var item in signals)
    {
        tank.Children.Add(new ModuleConfig
        {
            Name = item.Signal,
            Type = typeof(DiSensorComponent).FullName,
            Values =
            [
                new() { Name = "DiIndex", Value = item.Di.ToString() },
                new() { Name = "TriggerValue", Value = item.Trigger.ToString() },
                new() { Name = "AlarmEnabled", Value = "False" }
            ]
        });
    }

    foreach (var item in levels ?? [])
    {
        tank.Children.Add(new ModuleConfig
        {
            Name = item.Signal,
            Type = typeof(AiLevelComponent).FullName,
            Values = [new() { Name = "AiIndex", Value = item.Ai.ToString() }]
        });
    }

    return tank;
}

// 防抖都设 0，一拍就认；AI 液位信号的触发值照给的摆上
static void NoDebounce(SmokeTank loaded, (string Signal, int Ai, double Trigger)[]? levels)
{
    foreach (var sensor in loaded.FindChildren<DiSensorComponent>())
    {
        sensor.DebounceMs = 0;
    }

    foreach (var item in levels ?? [])
    {
        var level = loaded.FindChild<AiLevelComponent>(item.Signal)!;
        level.TriggerValue = item.Trigger;
        level.DebounceMs = 0;
    }

    loaded.LevelSensorDebounceMs = 0;
    loaded.HighHighDebounceMs = 0;
    loaded.OverfillDebounceMs = 0;
}

// 一个阀组：ValveGroupComponent 下面每个阀按 (DO 号, 到位 DI 号) 挂，到位 DI 号 -1 = 不挂 Feedback；一个阀都没有就不挂这个阀组
static void AddGroup(ModuleConfig tank, string name, (int Do, int Di)[] valves)
{
    if (valves.Length == 0)
    {
        return;
    }

    var group = new ModuleConfig { Name = name, Type = typeof(ValveGroupComponent).FullName };
    foreach (var valve in valves)
    {
        var node = new ModuleConfig
        {
            Name = "V" + valve.Do,
            Type = typeof(ValveComponent).FullName,
            Values =
            [
                new() { Name = "DoIndex", Value = valve.Do.ToString() },
                new() { Name = "NormalState", Value = "NC" }
            ]
        };
        if (valve.Di >= 0)
        {
            node.Children.Add(new ModuleConfig
            {
                Name = "Feedback",
                Type = typeof(DiSensorComponent).FullName,
                Values =
                [
                    new() { Name = "DiIndex", Value = valve.Di.ToString() },
                    new() { Name = "TriggerValue", Value = "true" },
                    new() { Name = "AlarmEnabled", Value = "False" }
                ]
            });
        }

        group.Children.Add(node);
    }

    tank.Children.Add(group);
}

/// <summary>
/// 测试用的 IPA：顶替扫描线程推一拍（下面的槽跟着扫）
/// </summary>
[Component(description: "冒烟用的 IPA")]
public sealed class SmokeIpa : IpaModule
{
    public void Tick()
    {
        OnScan();
    }
}

/// <summary>
/// 测试用的槽：顶替扫描线程推一拍
/// </summary>
[Component(description: "冒烟用的槽")]
public sealed class SmokeTank : TankComponent
{
    public void Tick()
    {
        OnScan();
    }
}

/// <summary>
/// 假 PLC：DI、AI 由测试摆，DO 记下写进来的值；没连上时都读写不了
/// </summary>
public sealed class FakePlc : IPlcIo
{
    private readonly object _gate = new();
    private readonly Dictionary<int, bool> _di = [];
    private readonly Dictionary<int, bool> _do = [];
    private readonly Dictionary<int, double> _ai = [];
    private volatile bool _connected = true;

    public bool IsConnected
    {
        get
        {
            return _connected;
        }
        set
        {
            _connected = value;
        }
    }

    public void SetDi(int index, bool on)
    {
        lock (_gate)
        {
            _di[index] = on;
        }
    }

    public bool TryReadDi(int index, out bool on)
    {
        lock (_gate)
        {
            on = _connected && _di.GetValueOrDefault(index);
            return _connected;
        }
    }

    /// <summary>
    /// 这个 DO 现在是不是通的（没写过算断）
    /// </summary>
    public bool Do(int index)
    {
        lock (_gate)
        {
            return _do.GetValueOrDefault(index);
        }
    }

    public bool TryReadDo(int index, out bool on)
    {
        lock (_gate)
        {
            on = _connected && _do.GetValueOrDefault(index);
            return _connected;
        }
    }

    public bool WriteDo(int index, bool on)
    {
        lock (_gate)
        {
            if (!_connected)
            {
                return false;
            }

            _do[index] = on;
            return true;
        }
    }

    public void SetAi(int index, double value)
    {
        lock (_gate)
        {
            _ai[index] = value;
        }
    }

    public bool TryReadAi(int index, out double value)
    {
        lock (_gate)
        {
            value = 0;
            if (_connected)
            {
                value = _ai.GetValueOrDefault(index);
            }

            return _connected;
        }
    }

    public bool TryReadAo(int index, out double value)
    {
        value = 0;
        return false;
    }

    public bool WriteAo(int index, double value)
    {
        return false;
    }
}
