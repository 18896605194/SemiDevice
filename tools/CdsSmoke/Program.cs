using xyz.Components;
using xyz.Components.Attributes;
using xyz.Components.Components;
using xyz.Components.Enums;
using xyz.Components.Interfaces;
using xyz.Configs.Models;
using xyz.Shared.Errors;

// CDS 冒烟：槽（Tank）照 sc 认液位 DI 信号（LL / L / H / HH / Overfill，没挂 = 没有这个点）、触发值照配的电平、
// 按配了的信号算液位（Overfill 亮了就是溢流）、读不到算不知道、信号对不上 / 液位高高 / 溢流报警且只能人工复位清；
// 五个功能（补液、排液、换液、供液、停止）开关哪组阀、状态怎么走（就绪只能补液得到、有报警就出错）、开始前的检查和被拒原因。
// 全用假 PLC（摆 DI、记 DO）和内存 EC，不连设备、不读写配置文件。

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
var previousPlc = PlcComponent.Current;
var previousIo = IoComponent.Current;
var previousEc = EcComponent.Current;
var previousAlarm = AlarmComponent.Current;
try
{
    // 0. 假 PLC、IO 表（0~19 号点）、内存 EC、报警
    var csv = Path.Combine(directory, "points.csv");
    File.WriteAllText(csv, "Index,Name,PhysicalMin,PhysicalMax,LogicalMin,LogicalMax\n"
        + string.Concat(Enumerable.Range(0, 20).Select(i => $"{i},P{i},0,0,0,0\n")));
    var io = new IoComponent();
    io.Di.Load(csv);
    io.Do.Load(csv);
    var plc = new FakePlc();
    PlcComponent.Current = plc;
    _ = new EcComponent();
    var alarms = new AlarmComponent();

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
}
finally
{
    PlcComponent.Current = previousPlc;
    IoComponent.Current = previousIo;
    EcComponent.Current = previousEc;
    AlarmComponent.Current = previousAlarm;
    Directory.Delete(directory, recursive: true);
}

Console.WriteLine($"PASS: {checks} cds checks (tank: level DI signals found by node name (a missing node means no such point), "
    + "trigger values loaded from sc, the level band from the signals that are configured with overfill as the top one, "
    + "unknown when a signal cannot be read unless overfill is on, the signal mismatch / high-high / overfill alarms raised by the tank "
    + "after each one's EC debounce time and cleared only by a manual reset; the fill / drain / exchange / supply / stop functions "
    + "driving their valve groups, ready only through a fill, any alarm putting the tank into error with every valve closed, "
    + "the SC fill target, and the checks before each function with their error codes)");

// 只挂液位信号的槽
static SmokeTank Load(string name, params (string Signal, int Di, bool Trigger)[] signals)
{
    return LoadTank(name, null, signals, [], [], []);
}

// 照 sc 的写法装一个槽：CDS、IPA 是分组，信号按 (节点名, DI 号, 触发值)、阀组的阀按 (DO 号, 到位 DI 号) 挂上去，阀名叫 V+DO 号；
// 信号和报警的防抖都设成 0，一拍就认
static SmokeTank LoadTank(string name, string? fillTo, (string Signal, int Di, bool Trigger)[] signals,
    (int Do, int Di)[] fill, (int Do, int Di)[] drain, (int Do, int Di)[] supply)
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

    var group = new ModuleConfig
    {
        Name = "CDS",
        Children = [new ModuleConfig { Name = "IPA", Children = [tank] }]
    };
    var loaded = (SmokeTank)ComponentLoader.Load([group]).Single();
    foreach (var sensor in loaded.FindChildren<DiSensorComponent>())
    {
        sensor.DebounceMs = 0;
    }

    loaded.LevelSensorDebounceMs = 0;
    loaded.HighHighDebounceMs = 0;
    loaded.OverfillDebounceMs = 0;
    loaded.InitComponent();
    return loaded;
}

// 一个阀组：ValveGroupComponent 下面每个阀按 (DO 号, 到位 DI 号) 挂；一个阀都没有就不挂这个阀组
static void AddGroup(ModuleConfig tank, string name, (int Do, int Di)[] valves)
{
    if (valves.Length == 0)
    {
        return;
    }

    var group = new ModuleConfig { Name = name, Type = typeof(ValveGroupComponent).FullName };
    foreach (var valve in valves)
    {
        group.Children.Add(new ModuleConfig
        {
            Name = "V" + valve.Do,
            Type = typeof(ValveComponent).FullName,
            Values =
            [
                new() { Name = "DoIndex", Value = valve.Do.ToString() },
                new() { Name = "DiIndex", Value = valve.Di.ToString() }
            ]
        });
    }

    tank.Children.Add(group);
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
/// 假 PLC：DI 由测试摆，DO 记下写进来的值；没连上时都读写不了
/// </summary>
public sealed class FakePlc : IPlc
{
    private readonly object _gate = new();
    private readonly Dictionary<int, bool> _di = [];
    private readonly Dictionary<int, bool> _do = [];
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

    public bool TryReadAi(int index, out double value)
    {
        value = 0;
        return false;
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

    public void Register(string path)
    {
    }

    public bool TryReadBlock(string path, out byte[] block)
    {
        block = [];
        return false;
    }

    public bool WriteBlock(string path, byte[] data)
    {
        return false;
    }
}
