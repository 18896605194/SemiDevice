using System.Runtime.InteropServices;
using xyz.Components.Components;
using xyz.Components.Motion;

static class AxisSubscriptions
{
    public static void Run()
    {
        Check(Marshal.SizeOf<MotionCSharpToPlcCommand>() == 96, "command protocol must be 96 bytes");
        Check(Marshal.OffsetOf<MotionCSharpToPlcCommand>(nameof(MotionCSharpToPlcCommand.Param1)).ToInt32() == 8,
            "command parameters must use Pack=8 alignment");
        Check(Marshal.OffsetOf<MotionCSharpToPlcCommand>(nameof(MotionCSharpToPlcCommand.Command_Sync_No)).ToInt32() == 88,
            "sync offset must match PLC");
        Check(Marshal.SizeOf<MotionPlcToCSharpData>() == 80, "status protocol must be 80 bytes");
        var plc = new NotificationPlc { Host = "offline-test" };
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
            plc.Tick();
            Check(axis.HasPlcData && plc.SuccessfulWrites.Count == 0, "baseline must not be written back");
            Check(axis.TryGetStatus(out var snapshot) && snapshot.Is_Ready == 1, "valid status snapshot");
            Check(axis.MoveTo(0) && axis.OperationState == AxisOperationState.Completed
                && plc.SuccessfulWrites.Count == 0, "already at target needs no new movement");
            Check(!axis.MoveTo(double.NaN) && !axis.MoveTo(10, 1000), "invalid motion parameters");
            Check(axis.MoveTo(10), "queue move");
            Check(!axis.MoveTo(20), "pending move must not be overwritten");
            Check(axis.IsCommandPending && plc.SuccessfulWrites.Count == 0, "queueing is not writing");
            axis.Tick();
            plc.Tick();
            Check(plc.SuccessfulWrites.Last().Command_Sync_No == 41
                && plc.SuccessfulWrites.Last().Param1 == 10, "send complete command with next sync");
            Check(!axis.IsCommandPending && axis.OperationState == AxisOperationState.Running, "write is not completion");
            plc.Emit("Axis.Status", status);
            Check(axis.OperationState == AxisOperationState.Running, "old in-position flag must not complete new movement");
            status.Is_Busy = 1; status.Is_Stopped = 0; status.Is_In_Position = 0;
            plc.Emit("Axis.Status", status);
            status.Is_Busy = 0; status.Is_Stopped = 1; status.Is_In_Position = 1; status.Current_Position = 10;
            plc.Emit("Axis.Status", status);
            Check(axis.OperationState == AxisOperationState.Completed, "fresh movement then matching position completes");

            Check(axis.MoveTo(20), "second move");
            plc.FailWrites = true;
            axis.Tick();
            plc.Tick();
            Check(axis.IsCommandPending && plc.SuccessfulWrites.Count == 1, "failure must remain pending");
            plc.FailWrites = false;
            axis.Tick();
            plc.Tick();
            Check(!axis.IsCommandPending && plc.SuccessfulWrites.Last().Param1 == 20, "failed output must retry");
            Check(axis.Stop(), "stop may replace active movement");
            axis.Tick();
            plc.Tick();
            Check(plc.SuccessfulWrites.Last().Axis_Command == (byte)MotionCommandId.Stop, "stop command");
            plc.Emit("Axis.Status", status);
            Check(axis.OperationState == AxisOperationState.Completed, "fresh stopped status");

            Check(axis.MoveTo(30) && axis.EmergencyStop(), "emergency stop may replace unsent movement");
            int before = plc.SuccessfulWrites.Count;
            axis.Tick();
            plc.Tick();
            Check(plc.SuccessfulWrites.Count == before + 1
                && plc.SuccessfulWrites.Last().Axis_Command == (byte)MotionCommandId.EStop, "replaced move must never be sent");
            plc.Emit("Axis.Status", status);

            Check(axis.MoveTo(40), "pending command before disconnect");
            var staleCallback = plc.LastCallback;
            plc.Disconnect();
            Check(!axis.HasPlcData && !axis.MoveTo(50) && axis.OperationState == AxisOperationState.Failed,
                "disconnect immediately invalidates axis");
            axis.Tick();
            plc.Tick();
            Check(plc.ActiveNotifications == 0, "disconnect releases device notifications");
            plc.Put("Axis.Command", new MotionCSharpToPlcCommand { Axis_Servo = 1, Command_Sync_No = 100 });
            plc.Connect();
            axis.Tick();
            plc.Tick();
            Check(axis.HasPlcData && plc.SuccessfulWrites.Count == before + 1, "reconnect must not replay old movement");
            var staleStatus = status; staleStatus.Current_Position = 999;
            staleCallback!(staleStatus);
            Check(axis.CurrentPosition == 10, "old connection callback must be ignored");
            Check(axis.Home(), "home after reconnect");
            axis.Tick();
            plc.Tick();
            Check(plc.SuccessfulWrites.Last().Command_Sync_No == 101, "sync must follow new PLC baseline");
            plc.Emit("Axis.Status", status);
            Check(axis.OperationState == AxisOperationState.Running, "old homed flag must not finish new home");
            status.Is_Busy = 1; status.Is_Stopped = 0; status.Is_Homed = 0;
            plc.Emit("Axis.Status", status);
            status.Is_Busy = 0; status.Is_Stopped = 1; status.Is_Homed = 1;
            plc.Emit("Axis.Status", status);
            Check(axis.OperationState == AxisOperationState.Completed, "home completes from new feedback");

            axis.Close();
            Check(plc.ActiveNotifications == 0 && !axis.HasPlcData, "close unsubscribes");
            Check(axis.Open(plc) && axis.HasPlcData && plc.ActiveNotifications == 2,
                "axis Open subscribes directly without a PLC scan");
            axis.Close();
            plc.FailCommandRead = true;
            Check(axis.Open(plc), "reopen");
            axis.Tick();
            plc.Tick();
            Check(!axis.HasPlcData && !axis.Home(), "missing command baseline blocks motion");
            plc.FailCommandRead = false;
            Thread.Sleep(1100);
            axis.Tick();
            plc.Tick();
            Check(axis.HasPlcData, "late baseline succeeds even when status arrived first");
            var afterCloseCallback = plc.LastCallback;
            axis.Close();
            afterCloseCallback!(staleStatus);
            Check(!axis.HasPlcData && axis.CurrentPosition != 999, "disposed callback must be ignored");

            plc.FailStatusSubscribe = true;
            Check(axis.Open(plc), "register for partial-subscribe test");
            axis.Tick();
            plc.Tick();
            Check(!axis.HasPlcData && plc.ActiveNotifications == 0,
                "input subscription failure must release the already-created output notification");
            plc.FailStatusSubscribe = false;
            Thread.Sleep(1100);
            axis.Tick();
            plc.Tick();
            Check(axis.HasPlcData && plc.ActiveNotifications == 2, "retry must rebuild both notifications exactly once");
            int notifications = plc.ActiveNotifications;
            axis.Tick();
            plc.Tick();
            Check(plc.ActiveNotifications == notifications, "connected axis must not subscribe twice");
            axis.Close();
            plc.Close();
            axis.Tick();
            plc.Tick();
            Check(!plc.IsConnected, "closed PLC must not reconnect");
            Console.WriteLine("PASS: axis layouts, subscriptions, commands, failures, stop priority, reconnect and disposal");
        }
        finally { axis.Close(); plc.Close(); }
    }

    private static MotionPlcToCSharpData ReadyStatus() => new()
    {
        Is_Ready = 1, Is_Servo_On = 1, Is_Stopped = 1, Is_Homed = 1, Is_In_Position = 1,
    };

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

    private sealed class NotificationPlc : PlcComponent
    {
        private readonly Dictionary<string, byte[]> _data = [];
        private readonly Dictionary<string, Action<object>> _callbacks = [];
        private readonly List<Action> _cyclicCallbacks = [];
        public List<MotionCSharpToPlcCommand> SuccessfulWrites { get; } = [];
        public bool FailWrites { get; set; }
        public bool FailCommandRead { get; set; }
        public bool FailStatusSubscribe { get; set; }
        public Action<object>? LastCallback { get; private set; }
        public int ActiveNotifications => _callbacks.Count;
        public void Connect() => IsConnected = true;
        public void Disconnect() => IsConnected = false;
        public void Tick()
        {
            OnScan();
            foreach (var callback in _cyclicCallbacks.ToArray()) callback();
        }
        protected override bool ConnectDevice() => false;

        public void Put<T>(string path, T value) where T : unmanaged
        {
            byte[] bytes = new byte[Marshal.SizeOf<T>()];
            MemoryMarshal.Write(bytes, in value);
            _data[path] = bytes;
        }
        public void Emit<T>(string path, T value) where T : unmanaged
        {
            Put(path, value);
            if (_callbacks.TryGetValue(path, out var callback)) callback(value);
            Tick();
        }
        protected override bool ReadDevice(string path, out byte[] data)
        {
            data = [];
            return !(FailCommandRead && path == "Axis.Command") && _data.TryGetValue(path, out data!);
        }
        protected override bool WriteDevice(string path, byte[] data)
        {
            if (FailWrites) return false;
            Check(path == "Axis.Command", "axis output path");
            SuccessfulWrites.Add(MemoryMarshal.Read<MotionCSharpToPlcCommand>(data));
            _data[path] = data;
            return true;
        }
        protected override IDisposable SubscribeDevice<T>(string path, Action<T> received, bool cyclic = false)
        {
            if (FailStatusSubscribe && path == "Axis.Status") throw new InvalidOperationException("simulated input subscribe failure");
            Action<object> callback = obj => received((T)obj);
            _callbacks.Add(path, callback);
            Action cycle = () => received(MemoryMarshal.Read<T>(_data[path]));
            if (cyclic) _cyclicCallbacks.Add(cycle);
            else LastCallback = callback;
            return new Release(() => { _callbacks.Remove(path); _cyclicCallbacks.Remove(cycle); });
        }
        private sealed class Release(Action release) : IDisposable
        {
            public void Dispose() => release();
        }
    }
}
