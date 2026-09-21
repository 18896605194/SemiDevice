using xyz.Components.Components;

static class SinglePointWrites
{
    public static void Run()
    {
        var plc = new RecordingBeckhoff
        {
            Host = "offline-test",
            DoDataPath = ".DigOut",
            AoDataPath = ".AnaOut",
            IoPointCount = 16,
        };
        plc.SetConnected(true);

        Check(plc.WriteDo(7, true), "single DO write must work before the first cache read");
        Check(plc.LastPath == ".DigOut[7]" && plc.LastData.SequenceEqual(new byte[] { 1 }),
            "DO must address one array element and send one byte");
        Check(!plc.TryReadDo(7, out _), "write must not create a readback cache");
        plc.ScanOnce();
        Check(plc.TryReadDo(7, out var on) && on, "scan must confirm the DO write");

        Check(plc.WriteDo(7, false), "DO off write");
        Check(plc.TryReadDo(7, out on) && on, "successful write must not change the last readback");
        plc.ScanOnce();
        Check(plc.TryReadDo(7, out on) && !on, "next scan must update DO state");

        plc.FailWrites = true;
        Check(!plc.WriteDo(7, true), "failed DO write must return false");
        Check(plc.TryReadDo(7, out on) && !on, "failed write must not change cached state");
        plc.FailWrites = false;

        Check(plc.WriteAo(3, 12.5), "AO write");
        Check(plc.LastPath == ".AnaOut[3]" && plc.LastData.Length == 4
            && BitConverter.ToSingle(plc.LastData) == 12.5f, "AO must send one REAL");
        Check(plc.TryReadAo(3, out var value) && value == 0, "AO must retain previous readback until scan");
        plc.ScanOnce();
        Check(plc.TryReadAo(3, out value) && value == 12.5, "AO readback");
        plc.FailWrites = true;
        Check(!plc.WriteAo(3, 99) && plc.TryReadAo(3, out value) && value == 12.5,
            "failed AO write must not change readback");
        plc.FailWrites = false;

        int count = plc.WriteCount;
        Check(!plc.WriteDo(-1, true) && !plc.WriteDo(16, true), "DO index bounds");
        Check(!plc.WriteAo(-1, 1) && !plc.WriteAo(16, 1), "AO index bounds");
        Check(!plc.WriteAo(0, double.NaN) && !plc.WriteAo(0, double.PositiveInfinity)
            && !plc.WriteAo(0, double.MaxValue), "non-finite or overflowing REAL must be rejected");
        plc.SetConnected(false);
        Check(!plc.WriteDo(0, true) && !plc.WriteAo(0, 1), "offline writes");
        plc.SetConnected(true);
        plc.DoDataPath = "";
        plc.AoDataPath = "";
        Check(!plc.WriteDo(0, true) && !plc.WriteAo(0, 1), "unconfigured paths");
        Check(plc.WriteCount == count, "invalid requests must not reach transport");
        plc.DoDataPath = ".DigOut";
        plc.AoDataPath = ".AnaOut";

        Parallel.For(0, 16, index => Check(plc.WriteDo(index, true), "parallel DO write"));
        Parallel.For(0, 16, index => Check(plc.WriteAo(index, index + 0.5), "parallel AO write"));
        plc.ScanOnce();
        for (int index = 0; index < 16; index++)
        {
            Check(plc.TryReadDo(index, out on) && on, "different DO writes must not overwrite each other");
            Check(plc.TryReadAo(index, out value) && value == index + 0.5,
                "different AO writes must not overwrite each other");
        }
        Console.WriteLine("PASS: single-element DO/AO writes, readback cache, failures and concurrent writes");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class RecordingBeckhoff : BeckhoffPlcComponent
    {
        private readonly object _gate = new();
        private readonly byte[] _digital = new byte[16];
        private readonly byte[] _analog = new byte[16 * sizeof(float)];
        public string LastPath { get; private set; } = "";
        public byte[] LastData { get; private set; } = [];
        public int WriteCount { get; private set; }
        public bool FailWrites { get; set; }
        public void SetConnected(bool connected) => IsConnected = connected;
        public void ScanOnce() => OnScan();

        protected override bool ReadDevice(string path, out byte[] data)
        {
            lock (_gate)
            {
                data = (byte[])(path == DoDataPath ? _digital : _analog).Clone();
                return true;
            }
        }

        protected override bool WriteDevice(string path, byte[] data)
        {
            lock (_gate)
            {
                WriteCount++;
                LastPath = path;
                LastData = (byte[])data.Clone();
                if (FailWrites) return false;
                int bracket = path.IndexOf('[');
                Check(bracket > 0 && path.EndsWith(']'), "transport must receive an element path");
                int index = int.Parse(path[(bracket + 1)..^1]);
                if (path[..bracket] == DoDataPath)
                {
                    Check(data.Length == 1, "DO payload size");
                    _digital[index] = data[0];
                }
                else
                {
                    Check(path[..bracket] == AoDataPath && data.Length == 4, "AO payload size");
                    data.CopyTo(_analog, index * sizeof(float));
                }
                return true;
            }
        }
    }
}
