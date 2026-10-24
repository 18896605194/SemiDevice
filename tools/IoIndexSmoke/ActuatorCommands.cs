using xyz.Components;
using xyz.Components.Components;
using xyz.Components.Enums;
using xyz.Components.Interfaces;
using xyz.Tools;

/// <summary>气缸/阀/喷嘴/DI/AI/四色灯经 IO 表读写 PLC：全部用假 PLC，不连真设备。</summary>
static class ActuatorCommands
{
    public static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "xyz-io-actuator-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var previousPlc = IocHelper.GetService<IPlcIo>();
        var previousIo = IocHelper.GetService<IIo>();
        try
        {
            // 点表放进 IO 组件的点表目录：它开机准备时读点表、取 PLC
            string points =
                "Index,Name,PhysicalMin,PhysicalMax,LogicalMin,LogicalMax\n" +
                "0,OpenCoil,0,0,0,0\n1,CloseCoil,0,0,0,0\n2,Opened,0,0,0,0\n3,Closed,0,0,0,0\n" +
                "4,ValveDo,0,0,0,0\n5,ValveDi,0,0,0,0\n6,Pressure,0,1000,0,100\n7,Red,0,0,0,0\n8,NoValveDo,0,0,0,0\n";
            foreach (string table in new[] { "DI", "DO", "AI" })
            {
                File.WriteAllText(Path.Combine(directory, table + ".csv"), points);
            }

            var io = new IoComponent { PointTableDirectory = directory };
            SmokeIoc.Use<IIo>(io);
            var plc = new StatePlc();

            // 到位反馈是 DI 组件子节点（门 Opened / Closed，阀 Feedback），组件初始化时认
            var door = new ScanningCylinder { DoOpenIndex = 0, DoCloseIndex = 1 };
            door.AddChild(Named(new DiSensorComponent { DiIndex = 2, AlarmEnabled = false }, "Opened"));
            door.AddChild(Named(new DiSensorComponent { DiIndex = 3, AlarmEnabled = false }, "Closed"));
            door.InitComponent();
            var lift = new ScanningCylinder { DoOpenIndex = 0, DoCloseIndex = 1 };
            lift.InitComponent();
            var valve = new ScanningValve { DoIndex = 4 };
            valve.AddChild(Named(new DiSensorComponent { DiIndex = 5, AlarmEnabled = false }, "Feedback"));
            valve.InitComponent();
            var nozzle = new NozzleComponent { DoIndex = 4, Chemical = "DIW" };
            nozzle.InitComponent();
            var normallyOpen = new ScanningValve { DoIndex = 8, NormalState = NormalState.NO };
            normallyOpen.InitComponent();

            // 反馈的防抖是默认 200 ms（这里没装 EC）：改了 DI 先扫一拍、等过防抖再扫一拍才认
            void Settle(Action tick)
            {
                tick();
                Thread.Sleep(250);
                tick();
            }

            // 阀、气缸、DI 开机 Bind 时取 IO 组件；IO 组件开机准备时取 PLC，先不给 PLC
            ComponentLoader.BindAll([door, lift, valve, nozzle, normallyOpen]);
            SmokeIoc.Use<IPlcIo>(null);
            io.InitComponent();
            Check(!door.Open() && !valve.Open() && !door.IsOpened, "no PLC: nothing writable or readable");
            SmokeIoc.Use<IPlcIo>(plc);
            io.InitComponent();

            Check(door.Open() && plc.Do[0] && !plc.Do[1] && door.ActionState == ActionState.Running,
                "open energizes the open coil only and waits for feedback");
            door.Tick();
            Check(door.ActionState == ActionState.Running && !door.IsOpened, "no feedback yet");
            plc.Di[2] = true;
            plc.Di[3] = true;
            Settle(door.Tick);
            Check(door.ActionState == ActionState.Running && !door.IsOpened && !door.IsClosed,
                "contradictory feedback is neither side");
            plc.Di[3] = false;
            Settle(door.Tick);
            Check(door.ActionState == ActionState.Completed && door.IsOpened && !door.IsClosed, "opened feedback completes");

            Check(door.Close() && plc.Do[1] && !plc.Do[0] && door.ActionState == ActionState.Running, "close reverses the coils");
            door.Abort();
            Check(door.ActionState == ActionState.Idle && plc.Do[1] && !plc.Do[0], "abort stops waiting and leaves the coils");
            plc.Di[2] = false;
            plc.Di[3] = true;
            Settle(door.Tick);
            Check(door.ActionState == ActionState.Idle && door.IsClosed, "feedback is readable without an action");
            Check(door.Open() && door.Close() && door.ActionState == ActionState.Running && plc.Do[1],
                "a new command replaces the running one");

            Check(lift.Open() && lift.ActionState == ActionState.Completed && lift.IsOpened && !lift.IsClosed,
                "no feedback: completes at once, side follows the coil readback");

            Check(valve.Open() && plc.Do[4] && valve.ActionState == ActionState.Running && !valve.IsOpened,
                "valve waits for its feedback");
            plc.Di[5] = true;
            Settle(valve.Tick);
            Check(valve.ActionState == ActionState.Completed && valve.IsOpened, "valve opened");
            Check(valve.Close() && !plc.Do[4] && valve.ActionState == ActionState.Completed,
                "close has no feedback and completes at once");
            Check(nozzle.Open() && nozzle.ActionState == ActionState.Completed && nozzle.IsOpened,
                "nozzle without feedback: opened follows the DO readback");

            // Feedback 挂了但 DiIndex 配成 -1：当没有到位反馈，写完就算完成
            var unwired = new ScanningValve { DoIndex = 4 };
            unwired.AddChild(Named(new DiSensorComponent { DiIndex = -1, AlarmEnabled = false }, "Feedback"));
            unwired.InitComponent();
            ComponentLoader.BindAll([unwired]);
            Check(unwired.Open() && unwired.ActionState == ActionState.Completed && unwired.IsOpened,
                "feedback with DI index -1 counts as no feedback: completes at once");

            // NO（常开）阀：开阀写 false、关阀写 true，没反馈时按 false 算开着
            Check(normallyOpen.Open() && plc.Do.ContainsKey(8) && !plc.Do[8] && normallyOpen.IsOpened, "NO valve: open writes false");
            Check(normallyOpen.Close() && plc.Do[8] && !normallyOpen.IsOpened, "NO valve: close writes true");

            var sensor = new ScanningDi { DiIndex = 2 };
            sensor.Bind();
            plc.Di[2] = true;
            sensor.Tick();
            Check(!sensor.IsTriggered, "debounce not elapsed");
            Thread.Sleep(250);
            sensor.Tick();
            Check(sensor.IsTriggered, "DI sensor reads through the IO table");
            var pressure = new ScanningAi { AiIndex = 6 };
            pressure.Bind();
            plc.Ai[6] = 500;
            pressure.Tick();
            Check(pressure.Value == 50, "AI sensor reads the scaled value through the IO table");

            var light = new LightComponent { DoRedIndex = 7 };
            light.Bind();
            light.SetRed(true);
            Check(plc.Do[7] && light.IsRedOn, "light writes through the IO table");

            plc.IsConnected = false;
            door.Tick();
            Check(!nozzle.IsOpened && !nozzle.Close() && !door.IsClosed, "offline PLC: unreadable and unwritable");
            Check(Throws(() => light.SetRed(false)) && light.IsRedOn, "failed light output throws and keeps the last state");
            Console.WriteLine("PASS: cylinder, valve (NC / NO), nozzle with feedback DI components, DI/AI sensors and light through the IO table");
        }
        finally
        {
            SmokeIoc.Use<IPlcIo>(previousPlc);
            SmokeIoc.Use<IIo>(previousIo);
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// 不走 machine.xml 装配直接 new 的组件起个名（Name 只给装配写，这里用反射）
    /// </summary>
    private static T Named<T>(T component, string name) where T : ComponentBase
    {
        typeof(ComponentBase).GetProperty(nameof(ComponentBase.Name))!.SetValue(component, name);
        return component;
    }

    private static bool Throws(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class ScanningCylinder : CylinderComponent
    {
        public void Tick() => OnScan();
    }

    private sealed class ScanningValve : ValveComponent
    {
        public void Tick() => OnScan();
    }

    private sealed class ScanningDi : DiSensorComponent
    {
        public void Tick() => OnScan();
    }

    private sealed class ScanningAi : AiSensorComponent
    {
        public void Tick() => OnScan();
    }

    /// <summary>假 PLC：DI/DO/AI 各一张表，断了什么都读不到、写不进。</summary>
    private sealed class StatePlc : IPlcIo
    {
        public bool IsConnected { get; set; } = true;
        public Dictionary<int, bool> Di { get; } = [];
        public Dictionary<int, bool> Do { get; } = [];
        public Dictionary<int, double> Ai { get; } = [];
        public bool TryReadDi(int index, out bool on) { on = IsConnected && Di.GetValueOrDefault(index); return IsConnected; }
        public bool TryReadDo(int index, out bool on) { on = IsConnected && Do.GetValueOrDefault(index); return IsConnected; }

        public bool WriteDo(int index, bool on)
        {
            if (!IsConnected)
            {
                return false;
            }

            Do[index] = on;
            return true;
        }

        public bool TryReadAi(int index, out double value) { value = Ai.GetValueOrDefault(index); return IsConnected; }
        public bool TryReadAo(int index, out double value) { value = 0; return false; }
        public bool WriteAo(int index, double value) => false;
    }
}
