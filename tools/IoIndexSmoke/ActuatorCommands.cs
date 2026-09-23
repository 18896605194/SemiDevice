using xyz.Components;
using xyz.Components.Components;
using xyz.Components.Interfaces;

/// <summary>气缸/阀/喷嘴/DI/AI/四色灯经 IO 表读写 PLC：全部用假 PLC，不连真设备。</summary>
static class ActuatorCommands
{
    public static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "xyz-io-actuator-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var previousPlc = PlcComponent.Current;
        var previousIo = IoComponent.Current;
        try
        {
            var csv = Path.Combine(directory, "points.csv");
            File.WriteAllText(csv,
                "Index,Name,PhysicalMin,PhysicalMax,LogicalMin,LogicalMax\n" +
                "0,OpenCoil,0,0,0,0\n1,CloseCoil,0,0,0,0\n2,Opened,0,0,0,0\n3,Closed,0,0,0,0\n" +
                "4,ValveDo,0,0,0,0\n5,ValveDi,0,0,0,0\n6,Pressure,0,1000,0,100\n7,Red,0,0,0,0\n");
            var io = new IoComponent();
            io.Di.Load(csv);
            io.Do.Load(csv);
            io.Ai.Load(csv);
            var plc = new StatePlc();

            var door = new ScanningCylinder { DoOpenIndex = 0, DoCloseIndex = 1, DiOpenedIndex = 2, DiClosedIndex = 3 };
            var lift = new ScanningCylinder { DoOpenIndex = 0, DoCloseIndex = 1 };
            var valve = new ScanningValve { DoIndex = 4, DiIndex = 5 };
            var nozzle = new NozzleComponent { DoIndex = 4, Chemical = "DIW" };

            PlcComponent.Current = null;
            Check(!door.Open() && !valve.Open() && !door.IsOpened, "no PLC: nothing writable or readable");
            PlcComponent.Current = plc;

            Check(door.Open() && plc.Do[0] && !plc.Do[1] && door.ActionState == ActionState.Running,
                "open energizes the open coil only and waits for feedback");
            door.Tick();
            Check(door.ActionState == ActionState.Running && !door.IsOpened, "no feedback yet");
            plc.Di[2] = true;
            plc.Di[3] = true;
            door.Tick();
            Check(door.ActionState == ActionState.Running && !door.IsOpened && !door.IsClosed,
                "contradictory feedback is neither side");
            plc.Di[3] = false;
            door.Tick();
            Check(door.ActionState == ActionState.Completed && door.IsOpened && !door.IsClosed, "opened feedback completes");

            Check(door.Close() && plc.Do[1] && !plc.Do[0] && door.ActionState == ActionState.Running, "close reverses the coils");
            door.Abort();
            Check(door.ActionState == ActionState.Idle && plc.Do[1] && !plc.Do[0], "abort stops waiting and leaves the coils");
            plc.Di[2] = false;
            plc.Di[3] = true;
            door.Tick();
            Check(door.ActionState == ActionState.Idle && door.IsClosed, "feedback is readable without an action");
            Check(door.Open() && door.Close() && door.ActionState == ActionState.Running && plc.Do[1],
                "a new command replaces the running one");

            Check(lift.Open() && lift.ActionState == ActionState.Completed && lift.IsOpened && !lift.IsClosed,
                "no feedback: completes at once, side follows the coil readback");

            Check(valve.Open() && plc.Do[4] && valve.ActionState == ActionState.Running && !valve.IsOpened,
                "valve waits for its feedback");
            plc.Di[5] = true;
            valve.Tick();
            Check(valve.ActionState == ActionState.Completed && valve.IsOpened, "valve opened");
            Check(valve.Close() && !plc.Do[4] && valve.ActionState == ActionState.Completed,
                "close has no feedback and completes at once");
            Check(nozzle.Open() && nozzle.ActionState == ActionState.Completed && nozzle.IsOpened,
                "nozzle without feedback: opened follows the DO readback");

            var sensor = new ScanningDi { DiIndex = 2 };
            plc.Di[2] = true;
            sensor.Tick();
            Check(!sensor.IsTriggered, "debounce not elapsed");
            Thread.Sleep(250);
            sensor.Tick();
            Check(sensor.IsTriggered, "DI sensor reads through the IO table");
            var pressure = new ScanningAi { AiIndex = 6 };
            plc.Ai[6] = 500;
            pressure.Tick();
            Check(pressure.Value == 50, "AI sensor reads the scaled value through the IO table");

            var light = new LightComponent { DoRedIndex = 7 };
            light.SetRed(true);
            Check(plc.Do[7] && light.IsRedOn, "light writes through the IO table");

            plc.IsConnected = false;
            Check(!nozzle.IsOpened && !nozzle.Close() && !door.IsClosed, "offline PLC: unreadable and unwritable");
            Check(Throws(() => light.SetRed(false)) && light.IsRedOn, "failed light output throws and keeps the last state");
            Console.WriteLine("PASS: cylinder, valve, nozzle, DI/AI sensors and light through the IO table");
        }
        finally
        {
            PlcComponent.Current = previousPlc;
            IoComponent.Current = previousIo;
            Directory.Delete(directory, recursive: true);
        }
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
        if (!condition) throw new InvalidOperationException(message);
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
    private sealed class StatePlc : IPlc
    {
        public bool IsConnected { get; set; } = true;
        public Dictionary<int, bool> Di { get; } = [];
        public Dictionary<int, bool> Do { get; } = [];
        public Dictionary<int, double> Ai { get; } = [];
        public void Register(string path) { }
        public bool TryReadDi(int index, out bool on) { on = IsConnected && Di.GetValueOrDefault(index); return IsConnected; }
        public bool TryReadDo(int index, out bool on) { on = IsConnected && Do.GetValueOrDefault(index); return IsConnected; }
        public bool WriteDo(int index, bool on) { if (!IsConnected) return false; Do[index] = on; return true; }
        public bool TryReadAi(int index, out double value) { value = Ai.GetValueOrDefault(index); return IsConnected; }
        public bool TryReadAo(int index, out double value) { value = 0; return false; }
        public bool WriteAo(int index, double value) => false;
        public bool TryReadBlock(string path, out byte[] block) { block = []; return false; }
        public bool WriteBlock(string path, byte[] data) => false;
    }
}
