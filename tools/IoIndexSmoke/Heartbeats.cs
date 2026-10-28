using xyz.Elements;
using xyz.Elements.Attributes;
using xyz.Elements.Elements;
using xyz.Elements.Enums;
using xyz.Elements.Interfaces;
using xyz.Configs.Models;
using xyz.Tools;

/// <summary>
/// 心跳：照 machine.xml 装、按 IHeartbeat 找得到；第一拍写通、没满间隔不写、满了写反面；写不成（PLC 断了、点表里没这个点）
/// 报心跳发送失败、等满间隔再写同一个值，恢复后报警还在、人工复位才清；DoIndex 配 -1 不发
/// </summary>
static class Heartbeats
{
    public static void Run(string directory)
    {
        var previousPlc = IocHelper.GetService<IPlcIo>();
        var previousIo = IocHelper.GetService<IIo>();
        var previousAlarm = IocHelper.GetService<IAlarm>();
        try
        {
            // 点表放进 IO 元件的点表目录：心跳 DO 是 0 号点
            string tables = Path.Combine(directory, "heartbeat-io");
            Directory.CreateDirectory(tables);
            File.WriteAllText(Path.Combine(tables, "DO.csv"), "Index,Name\n0,PC_Heartbeat\n");

            var plc = new HeartbeatPlc();
            SmokeIoc.Use<IPlcIo>(plc);
            var io = new IoElement { PointTableDirectory = tables };
            SmokeIoc.Use<IIo>(io);
            io.InitElement();
            var alarms = new AlarmElement();
            SmokeIoc.Use<IAlarm>(alarms);

            // 1. 照 machine.xml 装：挂在 Plc 分组下，DO 号、间隔从 machine.xml 来；没配的用默认值（-1 不发、1000 ms）
            var plcGroup = new ModuleConfig
            {
                Name = "Plc",
                Children =
                [
                    Node("Heartbeat", Value("DoIndex", "0"), Value("IntervalMs", "200")),
                    Node("Missing", Value("DoIndex", "5"), Value("IntervalMs", "200")),
                    Node("Idle"),
                ],
            };
            var roots = SmokeIoc.Load([plcGroup]);
            ElementLoader.BindAll(roots);
            var heartbeat = (HeartbeatProbe)roots[0];
            var missing = (HeartbeatProbe)roots[1];
            var idle = (HeartbeatProbe)roots[2];
            Check(heartbeat.FullPath == "Plc.Heartbeat" && heartbeat.DoIndex == 0 && heartbeat.IntervalMs == 200,
                "心跳照 machine.xml 装：路径带 Plc 分组，DO 号和间隔从 machine.xml 来");
            Check(idle.DoIndex == -1 && idle.IntervalMs == 1000, "没配的用默认值：DO -1、间隔 1000 ms");
            Check(ElementLoader.FindAll<IHeartbeat>().SequenceEqual(new IHeartbeat[] { heartbeat, missing, idle }),
                "心跳是部件、不进容器：在元件树上找得到，按 machine.xml 先后");
            Check(!heartbeat.IsAlive, "还没写过 IsAlive 是 false");

            bool Active(ElementBase source)
            {
                return alarms.ActiveAlarms.Any(alarm => alarm.SourcePath == source.FullPath && alarm.AlarmCode == heartbeat.SendFailedAlarm);
            }

            // 2. 轮流写：第一拍马上写通，没满间隔不写，满了写反面
            heartbeat.Tick();
            Check(plc.Writes.SequenceEqual(new[] { (0, true) }) && heartbeat.IsAlive, "第一拍马上写通");
            heartbeat.Tick();
            Check(plc.Attempts == 1, "没满间隔不写");
            Thread.Sleep(250);
            heartbeat.Tick();
            Check(plc.Writes.SequenceEqual(new[] { (0, true), (0, false) }), "满了间隔写反面：通完写断");
            Thread.Sleep(250);
            heartbeat.Tick();
            Check(plc.Writes.SequenceEqual(new[] { (0, true), (0, false), (0, true) }), "再满一次又写通");
            Check(!Active(heartbeat), "一直写成不报警");

            // 3. PLC 断了：写不成报警、IsAlive 变 false；也是满了间隔才再写，不是每拍都写
            plc.IsConnected = false;
            Thread.Sleep(250);
            heartbeat.Tick();
            var alarm = alarms.ActiveAlarms.SingleOrDefault(item => item.SourcePath == heartbeat.FullPath && item.AlarmCode == heartbeat.SendFailedAlarm);
            Check(alarm is not null && alarm.Level == AlarmLevel.Light && alarm.Category == AlarmCategory.IrrecoverableError && !heartbeat.IsAlive,
                "写不成报心跳发送失败（Light、通讯故障），IsAlive 变 false");
            int attempts = plc.Attempts;
            heartbeat.Tick();
            Check(plc.Attempts == attempts, "写不成以后也等满间隔再写");

            // 4. PLC 恢复：写上次写成的反面（PLC 那边头一下就看到变化），报警还在，人工复位才清
            plc.IsConnected = true;
            Thread.Sleep(250);
            heartbeat.Tick();
            Check(plc.Writes.Count == 4 && plc.Writes[3] == (0, false) && heartbeat.IsAlive,
                "恢复后写上次写成（通）的反面：断");
            Check(Active(heartbeat), "恢复了报警还在");
            alarms.Reset(heartbeat.FullPath);
            Check(!Active(heartbeat), "人工复位清掉");

            // 5. 点表里没有 machine.xml 配的 DO：写不成，报警；没走到 PLC
            attempts = plc.Attempts;
            missing.Tick();
            Check(Active(missing) && !missing.IsAlive && plc.Attempts == attempts, "点表里没有这个 DO：报心跳发送失败，不写 PLC");

            // 6. DoIndex 配 -1：不发、不报警
            idle.Tick();
            Check(plc.Attempts == attempts && !Active(idle) && !idle.IsAlive, "DoIndex -1 不发、不报警");

            Console.WriteLine("PASS: 心跳（照 machine.xml 装、轮流写通断、没满间隔不写、写不成报警且等满间隔再写、恢复写反面、人工复位清、点表外报警、-1 不发）");
        }
        finally
        {
            SmokeIoc.Use<IPlcIo>(previousPlc);
            SmokeIoc.Use<IIo>(previousIo);
            SmokeIoc.Use<IAlarm>(previousAlarm);
        }
    }

    static ModuleConfig Node(string name, params ValueConfig[] values)
    {
        return new ModuleConfig { Name = name, Type = typeof(HeartbeatProbe).FullName, Values = [.. values] };
    }

    static ValueConfig Value(string name, string value)
    {
        return new ValueConfig { Name = name, Value = value };
    }

    static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}

/// <summary>
/// 冒烟用心跳元件：Tick 由冒烟线程手动走一拍
/// </summary>
[Element(description: "冒烟用心跳元件")]
public sealed class HeartbeatProbe : HeartbeatElement
{
    public void Tick()
    {
        OnScan();
    }
}

/// <summary>
/// 记下写成的 DO 和一共写了几次；断开时写不成
/// </summary>
sealed class HeartbeatPlc : IPlcIo
{
    public bool IsConnected { get; set; } = true;

    public List<(int, bool)> Writes { get; } = new();

    public int Attempts { get; private set; }

    public bool WriteDo(int index, bool on)
    {
        Attempts++;
        if (!IsConnected)
        {
            return false;
        }

        Writes.Add((index, on));
        return true;
    }

    public bool TryReadDi(int index, out bool on)
    {
        on = false;
        return false;
    }

    public bool TryReadDo(int index, out bool on)
    {
        on = false;
        return false;
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
}
