using System.Runtime.InteropServices;
using xyz.Components;
using xyz.Components.Components;
using xyz.Components.Motion;

static class AxisCommands
{
    public static void Run()
    {
        Check(Marshal.SizeOf<MotionCSharpToPlcCommand>() == 96, "command protocol must be 96 bytes");
        Check(Marshal.OffsetOf<MotionCSharpToPlcCommand>(nameof(MotionCSharpToPlcCommand.Param1)).ToInt32() == 8,
            "command parameters must use Pack=8 alignment");
        Check(Marshal.OffsetOf<MotionCSharpToPlcCommand>(nameof(MotionCSharpToPlcCommand.Command_Sync_No)).ToInt32() == 88,
            "sync offset must match PLC");
        Check(Marshal.SizeOf<MotionPlcToCSharpData>() == 80, "status protocol must be 80 bytes");

        var plc = new CachePlc { Host = "offline-test" };
        var axis = new ScanningAxis { SendPlcDataPath = "Axis.Command", ReceivePlcDataPath = "Axis.Status" };
        var status = ReadyStatus();
        plc.Put("Axis.Command", new MotionCSharpToPlcCommand { Axis_Servo = 1, Command_Sync_No = 40 });
        plc.Put("Axis.Status", status);
        try
        {
            Check(axis.Open(plc), "register before connect");
            Check(!axis.HasPlcData && !axis.Home(), "no movement before PLC feedback");
            plc.Connect();
            axis.Tick();
            Check(!axis.HasPlcData, "connected but not yet scanned PLC has no frame");
            plc.Tick();
            axis.Tick();
            Check(axis.HasPlcData && plc.Writes.Count == 0, "baseline must not be written back");
            Check(axis.Status.Is_Ready == 1 && axis.IsServoOn && axis.IsHomed, "status snapshot");
            Check(axis.MoveTo(0) && axis.ActionState == ActionState.Completed && plc.Writes.Count == 0,
                "already at target needs no new movement");
            Check(!axis.MoveTo(double.NaN) && !axis.MoveTo(10, 1000) && plc.Writes.Count == 0, "invalid motion parameters");

            Check(axis.MoveTo(10) && axis.ActionState == ActionState.Running, "send writes immediately");
            Check(plc.Writes.Last().Command_Sync_No == 41 && plc.Writes.Last().Param1 == 10 && plc.Writes.Last().Axis_Servo == 1,
                "command carries the next sync number and the PLC servo level");
            Check(!axis.MoveTo(20) && plc.Writes.Count == 1, "running axis rejects a second movement");
            Tick(plc, axis, status);
            Check(axis.ActionState == ActionState.Running, "old in-position flag must not complete new movement");
            status.Is_Busy = 1; status.Is_Stopped = 0; status.Is_In_Position = 0;
            Tick(plc, axis, status);
            status.Is_Busy = 0; status.Is_Stopped = 1; status.Is_In_Position = 1; status.Current_Position = 10;
            Tick(plc, axis, status);
            Check(axis.ActionState == ActionState.Completed, "observed movement then matching position completes");

            // 50ms 内跑完的短行程：从来没看到"运动中"，过了旧帧窗口就按到位算，不会等到超时。
            Check(axis.MoveTo(12), "short move");
            status.Current_Position = 12;
            Tick(plc, axis, status);
            Tick(plc, axis, status);
            Check(axis.ActionState == ActionState.Running, "frames inside the settle window are not trusted");
            Tick(plc, axis, status);
            Check(axis.ActionState == ActionState.Completed, "short move completes after the settle window");

            Check(axis.MoveTo(30) && axis.Stop(), "stop may interrupt a running movement");
            Check(plc.Writes.Last().Axis_Command == (byte)MotionCommandId.Stop && plc.Writes.Last().Command_Sync_No == 44,
                "stop is sent at once");
            Check(!axis.MoveTo(40), "stop in flight blocks new movement");
            Settle(plc, axis, status);
            Check(axis.ActionState == ActionState.Completed, "stopped feedback completes the stop");

            Check(axis.MoveTo(40) && axis.EmergencyStop(), "emergency stop may interrupt");
            Check(plc.Writes[^2].Axis_Command == (byte)MotionCommandId.MoveTo
                && plc.Writes[^1].Axis_Command == (byte)MotionCommandId.EStop, "both commands reach the PLC in order");
            Settle(plc, axis, status);
            Check(axis.ActionState == ActionState.Completed, "stopped feedback completes the emergency stop");

            plc.FailWrites = true;
            int before = plc.Writes.Count;
            Check(!axis.MoveTo(50) && axis.ActionState == ActionState.Completed && plc.Writes.Count == before,
                "rejected write is not an operation");
            plc.FailWrites = false;

            Check(axis.MoveTo(50), "movement before disconnect");
            plc.Disconnect();
            axis.Tick();
            Check(!axis.HasPlcData && axis.ActionState == ActionState.Failed && !axis.MoveTo(60),
                "disconnect fails the movement and blocks new ones");
            plc.Put("Axis.Command", new MotionCSharpToPlcCommand { Axis_Servo = 1, Command_Sync_No = 100 });
            plc.Connect();
            axis.Tick();
            Check(!axis.HasPlcData, "reconnect must not serve frames cached before the disconnect");
            plc.Tick();
            axis.Tick();
            Check(axis.HasPlcData && plc.Writes.Count == before + 1, "reconnect must not replay the old movement");
            Check(axis.CurrentPosition == 12, "status after reconnect");
            Check(axis.Home() && plc.Writes.Last().Axis_Command == (byte)MotionCommandId.Home
                && plc.Writes.Last().Command_Sync_No == 101, "sync must follow the new PLC baseline");
            status.Is_Busy = 1; status.Is_Stopped = 0; status.Is_Homed = 0;
            Tick(plc, axis, status);
            Check(axis.ActionState == ActionState.Running, "homing in progress");
            status.Is_Busy = 0; status.Is_Stopped = 1; status.Is_Homed = 1;
            Tick(plc, axis, status);
            Check(axis.ActionState == ActionState.Completed, "home completes from new feedback");

            // 驱动器报错：在途动作失败，复位后清。
            Check(axis.MoveTo(70), "movement that will fault");
            status.Is_Err = 1;
            Tick(plc, axis, status);
            Check(axis.ActionState == ActionState.Failed && axis.IsError, "axis fault fails the movement");
            Check(!axis.MoveTo(80), "faulted axis rejects movement");
            Check(axis.ResetDrive() && plc.Writes.Last().Axis_Command == (byte)MotionCommandId.Reset, "reset is sent while faulted");
            status.Is_Err = 0;
            Settle(plc, axis, status);
            Check(axis.ActionState == ActionState.Completed, "reset completes when the fault clears");

            // 伺服电平随 Stop 下发，反馈对上才算完成；之后的命令沿用该电平。
            Check(axis.SetServo(false) && plc.Writes.Last().Axis_Command == (byte)MotionCommandId.Stop
                && plc.Writes.Last().Axis_Servo == 0, "servo off rides on a stop command");
            Settle(plc, axis, status);
            Check(axis.ActionState == ActionState.Running, "servo feedback must match before completion");
            status.Is_Servo_On = 0;
            Tick(plc, axis, status);
            Check(axis.ActionState == ActionState.Completed && !axis.MoveTo(5), "servo off completes and rejects movement");
            Check(axis.SetServo(true) && plc.Writes.Last().Axis_Servo == 1, "servo on");
            status.Is_Servo_On = 1;
            Settle(plc, axis, status);
            Check(axis.ActionState == ActionState.Completed, "servo on completes");
            Check(axis.MoveTo(5) && plc.Writes.Last().Axis_Servo == 1, "later commands keep the servo level");

            plc.Close();
            plc.Tick();
            axis.Tick();
            Check(!plc.IsConnected && !axis.HasPlcData, "closed PLC must not reconnect");
            Console.WriteLine("PASS: axis layouts, baseline, immediate commands, settle window, stop priority, faults, servo level and reconnect");
        }
        finally
        {
            plc.Close();
        }
    }

    private static MotionPlcToCSharpData ReadyStatus() => new()
    {
        Is_Ready = 1, Is_Servo_On = 1, Is_Stopped = 1, Is_Homed = 1, Is_In_Position = 1,
    };

    /// <summary>PLC 状态换成 status，PLC 刷一拍缓存，轴扫一拍。</summary>
    private static void Tick(CachePlc plc, ScanningAxis axis, MotionPlcToCSharpData status)
    {
        plc.Put("Axis.Status", status);
        plc.Tick();
        axis.Tick();
    }

    /// <summary>扫够旧帧窗口。</summary>
    private static void Settle(CachePlc plc, ScanningAxis axis, MotionPlcToCSharpData status)
    {
        for (int i = 0; i < 3; i++)
        {
            Tick(plc, axis, status);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class ScanningAxis : AxisComponent
    {
        public void Tick()
        {
            OnScan();
        }
    }

    /// <summary>假 PLC：块存字典里，扫描把字典刷进缓存，写记下来。</summary>
    private sealed class CachePlc : PlcComponent
    {
        private readonly Dictionary<string, byte[]> _data = [];
        public List<MotionCSharpToPlcCommand> Writes { get; } = [];
        public bool FailWrites { get; set; }
        public void Connect() => IsConnected = true;
        public void Disconnect() => IsConnected = false;
        public void Tick() => OnScan();
        protected override bool ConnectDevice() => false;

        public void Put<T>(string path, T value) where T : unmanaged
        {
            byte[] bytes = new byte[Marshal.SizeOf<T>()];
            MemoryMarshal.Write(bytes, in value);
            _data[path] = bytes;
        }

        protected override bool ReadDevice(string path, out byte[] data)
        {
            if (_data.TryGetValue(path, out var cached))
            {
                data = cached;
                return true;
            }

            data = [];
            return false;
        }

        protected override bool WriteDevice(string path, byte[] data)
        {
            if (FailWrites) return false;
            Check(path == "Axis.Command", "axis output path");
            Writes.Add(MemoryMarshal.Read<MotionCSharpToPlcCommand>(data));
            _data[path] = data;
            return true;
        }
    }
}
