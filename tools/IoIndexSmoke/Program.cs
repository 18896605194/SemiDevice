using xyz.Components;
using xyz.Components.Components;
using xyz.Components.Interfaces;
using xyz.Shared.Dtos;
using xyz.Tools;

SmokeIoc.Reset();

var directory = Path.Combine(Path.GetTempPath(), "xyz-io-index-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
var previousPlc = IocHelper.GetService<IPlcIo>();
var previousIo = IocHelper.GetService<IIoComponent>();
var io = new IoComponent();
SmokeIoc.Use<IIoComponent>(io);
try
{
    SinglePointWrites.Run();
    AxisCommands.Run();
    ActuatorCommands.Run();
    VirtualInputReads.Run(directory);
    IoInterlocks.Run(directory);
    StartupDoWrites.Run(directory);
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
    // IO 组件开机准备时读点表、取 PLC：同一份点表放进它的点表目录，先不给 PLC
    string tables = Path.Combine(directory, "main-io");
    Directory.CreateDirectory(tables);
    foreach (string table in new[] { "DI", "DO", "AI", "AO" })
    {
        File.Copy(csv, Path.Combine(tables, table + ".csv"));
    }

    io.PointTableDirectory = tables;
    SmokeIoc.Use<IPlcIo>(null);
    io.InitComponent();
    Check(!io.IsCollected && !io.TryReadDi(7, out _), "no PLC means nothing is readable");
    SmokeIoc.Use<IPlcIo>(plc);
    io.InitComponent();
    Check(io.IsCollected, "collecting follows the PLC connection");
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
    Check(!io.IsCollected && !io.TryReadDi(7, out _) && !io.TryReadAi(7, out _),
        "offline PLC makes every point unreadable");
    plc.IsConnected = true;
    Check(io.TryReadDi(7, out _), "reads resume with the connection");
    io.Di.Load(Path.Combine(directory, "missing.csv"));
    Check(io.Di.Points.Count == 0 && io.Di.Find(7) is null, "reload must clear index lookup");

    // IO 组件自己推 IO 界面那一包（IoDto，留存）：在自己的扫描里到了 EC 推送周期才推，骨架只建一次、之后每次只刷值
    // 推送周期是 EC：先拨到最长（两拍之间肯定没到），再拨到最短看下一个周期接着推
    var previousEc = IocHelper.GetService<IEcComponent>();
    var ec = new EcComponent();
    SmokeIoc.Use<IEcComponent>(ec);
    var scanning = new ScanIo { PointTableDirectory = tables };
    SmokeIoc.Use<IIoComponent>(scanning);
    scanning.InitComponent();

    ec.Merge([scanning]);
    scanning.PublishIntervalMs = 10000;
    Check(scanning.PublishIntervalMs == 10000, "publish interval comes from EC");
    var pushedSnapshots = new List<IoDto>();
    int testThread = Environment.CurrentManagedThreadId;
    using (EventBus.Register<IoDto>(IoDto.EventToken, dto =>
           {
               // 前面几节起的 IO 组件还在各自的扫描线程上推，只认这里扫的这一个（EventBus 在发的那个线程上回调）
               if (Environment.CurrentManagedThreadId == testThread)
               {
                   pushedSnapshots.Add(dto);
               }
           }))
    {
        pushedSnapshots.Clear();
        scanning.Tick();
        scanning.Tick();
        Check(pushedSnapshots.Count == 1 && pushedSnapshots[0].IsCollecting && pushedSnapshots[0].Types.Count == 4,
            "the IO component pushes its snapshot from its own scan, once per publish interval");
        scanning.PublishIntervalMs = 100;
        Thread.Sleep(200);
        scanning.Tick();
        Check(pushedSnapshots.Count == 2 && ReferenceEquals(pushedSnapshots[0], pushedSnapshots[1]),
            "the next interval pushes again, refreshing values on the same skeleton");
    }

    SmokeIoc.Use<IEcComponent>(previousEc);

    Console.WriteLine("PASS: index lookup, reads, writes, scaling, invalid indices and connection gating, IO snapshot pushed from its own scan");
}
finally
{
    SmokeIoc.Use<IPlcIo>(previousPlc);
    SmokeIoc.Use<IIoComponent>(previousIo);
    Directory.Delete(directory, recursive: true);
}

static void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

sealed class FakePlc : IPlcIo
{
    public bool IsConnected { get; set; } = true;
    public (int, bool) LastDo { get; private set; }
    public (int, double) LastAo { get; private set; }
    public bool TryReadDi(int index, out bool on) { on = IsConnected && index == 7; return IsConnected; }
    public bool TryReadDo(int index, out bool on) { on = IsConnected && index == 12; return IsConnected; }
    public bool TryReadAi(int index, out double value) { value = index == 7 ? 500 : 100; return IsConnected; }
    public bool TryReadAo(int index, out double value) { value = 250; return IsConnected; }
    public bool WriteDo(int index, bool on) { LastDo = (index, on); return true; }
    public bool WriteAo(int index, double value) { LastAo = (index, value); return true; }
}

// IO 组件顶替扫描线程扫一拍
sealed class ScanIo : IoComponent
{
    // EC 的键是组件全路径：生产里由 ComponentLoader 填，这里反射设
    public ScanIo()
    {
        typeof(ComponentBase).GetProperty(nameof(Name))!.SetValue(this, "ScanIo");
        typeof(ComponentBase).GetProperty(nameof(FullPath))!.SetValue(this, "ScanIo");
    }

    public void Tick()
    {
        OnScan();
    }
}
