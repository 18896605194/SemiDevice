using xyz.Components.Components;
using xyz.Components.Interfaces;

var directory = Path.Combine(Path.GetTempPath(), "xyz-io-index-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
var previousPlc = PlcComponent.Current;
var previousIo = IoComponent.Current;
var io = new IoComponent();
try
{
    SinglePointWrites.Run();
    AxisCommands.Run();
    ActuatorCommands.Run();
    var csv = Path.Combine(directory, "points.csv");
    File.WriteAllText(csv,
        "Index,Name,PhysicalMin,PhysicalMax,LogicalMin,LogicalMax\n" +
        "7,Same,0,1000,0,100\n" +
        "2,Same,0,1000,0,100\n" +
        "7,Duplicate,0,1000,0,100\n" +
        "12,,0,1000,0,100\n");
    foreach (var table in new[] { io.Di, io.Do, io.Ai, io.Ao })
    {
        table.Load(csv);
        Check(table.Points.Count == 3, "duplicate indices must be ignored");
        Check(table.Find(7)?.Name == "Same", "first index definition must win");
        Check(table.Find(2)?.Name == "Same", "duplicate names must remain independent");
        Check(table.Find(12) is not null, "unnamed points must work by index");
        Check(table.Find(0) is null && table.Find(-1) is null, "index is not a list position");
    }

    var plc = new FakePlc();
    PlcComponent.Current = null;
    Check(!io.IsCollecting && !io.TryReadDi(7, out _), "no PLC means nothing is readable");
    PlcComponent.Current = plc;
    Check(io.IsCollecting, "collecting follows the PLC connection");
    Check(io.TryReadAi(7, out var value) && value == 50, "AI must resolve sparse PLC indices and scale");
    Check(io.TryReadDi(7, out var on) && on, "DI index 7");
    Check(io.TryReadDi(2, out on) && !on, "same name at index 2 must not read index 7");
    Check(io.TryReadDo(12, out on) && on, "unnamed DO read");
    Check(io.TryReadAo(7, out value) && value == 25, "AO readback keeps engineering conversion");
    Check(!io.TryReadAi(0, out _), "missing AI index");
    Check(io.WriteDo(12, true) && plc.LastDo == (12, true), "DO writes use the requested index");
    Check(io.WriteAo(7, 25) && plc.LastAo == (7, 250d), "AO keeps engineering conversion");
    Check(!io.WriteDo(-1, true) && !io.WriteAo(0, 25), "invalid writes must fail");
    Check(plc.LastAo == (7, 250d), "invalid write must not reach PLC");

    plc.IsConnected = false;
    Check(!io.IsCollecting && !io.TryReadDi(7, out _) && !io.TryReadAi(7, out _),
        "offline PLC makes every point unreadable");
    plc.IsConnected = true;
    Check(io.TryReadDi(7, out _), "reads resume with the connection");
    io.Di.Load(Path.Combine(directory, "missing.csv"));
    Check(io.Di.Points.Count == 0 && io.Di.Find(7) is null, "reload must clear index lookup");
    Console.WriteLine("PASS: index lookup, reads, writes, scaling, invalid indices and connection gating");
}
finally
{
    PlcComponent.Current = previousPlc;
    IoComponent.Current = previousIo;
    Directory.Delete(directory, recursive: true);
}

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class FakePlc : IPlc
{
    public bool IsConnected { get; set; } = true;
    public (int, bool) LastDo { get; private set; }
    public (int, double) LastAo { get; private set; }
    public void Register(string path) { }
    public bool TryReadDi(int index, out bool on) { on = IsConnected && index == 7; return IsConnected; }
    public bool TryReadDo(int index, out bool on) { on = IsConnected && index == 12; return IsConnected; }
    public bool TryReadAi(int index, out double value) { value = index == 7 ? 500 : 100; return IsConnected; }
    public bool TryReadAo(int index, out double value) { value = 250; return IsConnected; }
    public bool WriteDo(int index, bool on) { LastDo = (index, on); return true; }
    public bool WriteAo(int index, double value) { LastAo = (index, value); return true; }
    public bool TryReadBlock(string path, out byte[] block) { block = []; return false; }
    public bool WriteBlock(string path, byte[] data) => false;
}
