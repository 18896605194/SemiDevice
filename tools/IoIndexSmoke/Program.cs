using System.Reflection;
using xyz.Client.Common.Events;
using xyz.Elements;
using xyz.Elements.Elements;
using xyz.Elements.Interfaces;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Tools;

SmokeIoc.Reset();
SmokeEvents.Loopback(); // 后端推给界面的消息投回本进程收

var directory = Path.Combine(Path.GetTempPath(), "xyz-io-index-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
var previousPlc = IocHelper.GetService<IPlcIo>();
var previousIo = IocHelper.GetService<IIo>();
var io = new IoElement();
SmokeIoc.Use<IIo>(io);
try
{
    SinglePointWrites.Run();
    AxisCommands.Run();
    ActuatorCommands.Run();
    VirtualInputReads.Run(directory);
    IoInterlocks.Run(directory);
    StartupDoWrites.Run(directory);
    Heartbeats.Run(directory);
    var csv = Path.Combine(directory, "points.csv");
    File.WriteAllText(csv,
        "Index,Name,PhysicalMin,PhysicalMax,LogicalMin,LogicalMax\n" +
        "7,Same,0,1000,0,100\n" +
        "2,Same,0,1000,0,100\n" +
        "7,Duplicate,0,1000,0,100\n" +
        "12,,0,1000,0,100\n");
    foreach (var table in new[] { io.Di, io.Do, io.Ai, io.Ao })
    {
        table.Load(csv, io);
        Check(table.Points.Count == 3, "duplicate indices must be ignored");
        Check(table.Find(7)?.Name == "Same", "first index definition must win");
        Check(table.Find(2)?.Name == "Same", "duplicate names must remain independent");
        Check(table.Find(12) is not null, "unnamed points must work by index");
        Check(table.Find(0) is null && table.Find(-1) is null, "index is not a list position");
    }

    // 点表照真文件的样子：带 BOM、表头大小写不一、列顺序不同、描述里带逗号（加了引号），中间夹一个空行
    var quoted = Path.Combine(directory, "quoted.csv");
    File.WriteAllText(quoted,
        "description,UNIT,index,Name,PhysicalMax,PhysicalMin,LogicalMax,LogicalMin\r\n" +
        "\"气源,压力\",MPa,3,CDA_Pressure,32767,0,1,0\r\n" +
        "\r\n" +
        "排风,Pa,4,Exhaust,32767,0,1000,0\r\n", new System.Text.UTF8Encoding(true));
    io.Ai.Load(quoted, io);
    var cda = io.Ai.Find(3);
    Check(io.Ai.Points.Count == 2 && cda is not null && cda.Description == "气源,压力" && cda.Unit == "MPa"
          && cda.PhysicalMax == 32767 && cda.LogicalMax == 1 && io.Ai.Find(4)?.LogicalMax == 1000,
        "point table: BOM, any column order and header case, a quoted comma in a description, a blank line skipped");

    // ExcelHelper 写 CSV：UTF-8 带 BOM、带逗号的格子加引号、空格子留空，读回来一样；一行都没有也有表头；不留临时文件
    var written = Path.Combine(directory, "written.csv");
    ExcelHelper.SaveCsv(written, ["时间", "说明", "值"], [["2026-10-25 10:00:00", "带,逗号", 1.5], ["2026-10-25 10:00:01", null, 2.0]]);
    var bytes = File.ReadAllBytes(written);
    var back = ExcelHelper.ReadCsv(written);
    Check(bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF && back.Count == 2
          && back[0]["说明"] == "带,逗号" && back[0]["值"] == "1.5" && back[1]["说明"] == string.Empty && back[1]["值"] == "2",
        $"csv written as UTF-8 with BOM, quoted commas and empty cells read back the same: {File.ReadAllText(written)}");
    var emptyCsv = Path.Combine(directory, "empty.csv");
    ExcelHelper.SaveCsv(emptyCsv, ["Index", "Name"], []);
    Check(File.ReadAllText(emptyCsv).Trim() == "Index,Name" && !File.Exists(written + ".tmp") && !File.Exists(emptyCsv + ".tmp"),
        "csv with no rows still has the header, and no temp file is left");

    var plc = new FakePlc();
    // IO 元件开机准备时读点表、取 PLC：同一份点表放进它的点表目录，先不给 PLC
    string tables = Path.Combine(directory, "main-io");
    Directory.CreateDirectory(tables);
    foreach (string table in new[] { "DI", "DO", "AI", "AO" })
    {
        File.Copy(csv, Path.Combine(tables, table + ".csv"));
    }

    io.PointTableDirectory = tables;
    SmokeIoc.Use<IPlcIo>(null);
    io.InitElement();
    Check(!io.IsCollected && !io.TryReadDi(7, out _), "no PLC means nothing is readable");
    SmokeIoc.Use<IPlcIo>(plc);
    io.InitElement();
    Check(io.IsCollected, "collecting follows the PLC connection");
    Check(io.TryReadAi(7, out var value) && value == 50, "AI must resolve sparse PLC indices and scale");
    Check(io.TryReadDi(7, out var on) && on, "DI index 7");
    Check(io.TryReadDi(2, out on) && !on, "same name at index 2 must not read index 7");
    Check(io.TryReadDo(12, out on) && on, "unnamed DO read");
    Check(io.TryReadAo(7, out value) && value == 25, "AO readback keeps engineering conversion");
    Check(!io.TryReadAi(0, out _), "missing AI index");
    Check(io.WriteDo(12, true).IsSuccess && plc.LastDo == (12, true), "DO writes use the requested index");
    Check(io.WriteAo(7, 25).IsSuccess && plc.LastAo == (7, 250d), "AO keeps engineering conversion");
    Check(io.WriteDo(-1, true).ErrorMessage == ErrorCodes.IoWriteFailed && io.WriteAo(0, 25).ErrorMessage == ErrorCodes.IoWriteFailed, "invalid writes must fail");
    var outOfRange = io.WriteAo(7, 150);
    Check(outOfRange.ErrorMessage == ErrorCodes.IoOutOfRange && outOfRange.Args.Take(3).SequenceEqual(new[] { "7", "0", "100" }),
        "scaled AO beyond its engineering range is refused with the range");
    Check(plc.LastAo == (7, 250d), "invalid write must not reach PLC");

    plc.IsConnected = false;
    Check(!io.IsCollected && !io.TryReadDi(7, out _) && !io.TryReadAi(7, out _),
        "offline PLC makes every point unreadable");
    plc.IsConnected = true;
    Check(io.TryReadDi(7, out _), "reads resume with the connection");
    io.Di.Load(Path.Combine(directory, "missing.csv"), io);
    Check(io.Di.Points.Count == 0 && io.Di.Find(7) is null, "reload must clear index lookup");

    // IO 元件自己推 IO 界面那一包（IoDto，留存）：在自己的扫描里到了 EC 推送周期才推，骨架只建一次、之后每次只刷值
    // 推送周期是 EC：先拨到最长（两拍之间肯定没到），再拨到最短看下一个周期接着推
    var previousEc = IocHelper.GetService<IEc>();
    var ec = new EcElement();
    SmokeIoc.Use<IEc>(ec);
    var scanning = new ScanIo { PointTableDirectory = tables };
    SmokeIoc.Use<IIo>(scanning);
    scanning.InitElement();

    ec.Merge([scanning]);
    scanning.PublishIntervalMs = 10000;
    Check(scanning.PublishIntervalMs == 10000, "publish interval comes from EC");
    var pushedSnapshots = new List<IoDto>();
    int testThread = Environment.CurrentManagedThreadId;
    // 订阅收到的是拆包出来的副本（跟真客户端一样），骨架是不是同一个只能看元件里存着的那一份
    var snapshotField = typeof(IoElement).GetField("_snapshot", BindingFlags.NonPublic | BindingFlags.Instance)!;
    using (RemoteEventSubscriber.Register<IoDto>(IoDto.EventToken, dto =>
           {
               // 前面几节起的 IO 元件还在各自的扫描线程上推，只认这里扫的这一个（回环在发的那个线程上回调）
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
            "the IO element pushes its snapshot from its own scan, once per publish interval");
        var skeleton = snapshotField.GetValue(scanning);
        scanning.PublishIntervalMs = 100;
        Thread.Sleep(200);
        scanning.Tick();
        Check(pushedSnapshots.Count == 2 && skeleton is not null && ReferenceEquals(snapshotField.GetValue(scanning), skeleton)
              && pushedSnapshots[1].Types.Count == pushedSnapshots[0].Types.Count,
            "the next interval pushes again, refreshing values on the same skeleton");
    }

    SmokeIoc.Use<IEc>(previousEc);

    Console.WriteLine("PASS: index lookup, point tables read as csv (BOM, column order, quoted commas), csv write / read through ExcelHelper, " +
                      "reads, writes, scaling, invalid indices and connection gating, IO snapshot pushed from its own scan");
}
finally
{
    SmokeIoc.Use<IPlcIo>(previousPlc);
    SmokeIoc.Use<IIo>(previousIo);
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

// IO 元件顶替扫描线程扫一拍
sealed class ScanIo : IoElement
{
    // EC 的键是元件全路径：生产里由 ElementLoader 填，这里反射设
    public ScanIo()
    {
        typeof(ElementBase).GetProperty(nameof(Name))!.SetValue(this, "ScanIo");
        typeof(ElementBase).GetProperty(nameof(FullPath))!.SetValue(this, "ScanIo");
    }

    public void Tick()
    {
        ScanOnce();
    }
}
