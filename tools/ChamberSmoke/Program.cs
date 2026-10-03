using System.Diagnostics;
using System.Runtime.InteropServices;
using xyz.Components;
using xyz.Components.Attributes;
using xyz.Components.Components;
using xyz.Components.Enums;
using xyz.Components.Interfaces;
using xyz.Components.Models;
using xyz.Configs.Models;
using xyz.Modules;
using xyz.Modules.Enums;
using xyz.Service;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Tools;

// 腔体部件冒烟：按 sc.xml 结构认部件，部件状态推送（指令侧 / 在走 / 出液 / 转动 / 摆臂 Reach 和到位容差），
// 部件手动动作（找不到、不支持、指令没发出去、执行中 Manual、在途拒绝、Abort 顶替、失败、停用，以及各部件真走一遍）。
// 全用假 PLC 和内存 EC，不连设备、不读写配置文件。

var checks = 0;
void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException("FAIL: " + message);
    }

    checks++;
}

var directory = Path.Combine(Path.GetTempPath(), "xyz-chamber-smoke-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
var previousPlc = PlcComponent.Current;
var previousIo = IoComponent.Current;
var previousEc = EcComponent.Current;
try
{
    // 0. 假 PLC、IO 表（0~9 号点）、内存 EC；接线：Door 0/1、Bowl 2/3、Lift 4/5（DO 与 DI 同号），
    //    DIW 喷嘴只有 DO 6（不接反馈），SC1 喷嘴 DO 7 / DI 7，两根轴各一对数据块。
    var csv = Path.Combine(directory, "points.csv");
    File.WriteAllText(csv, "Index,Name,PhysicalMin,PhysicalMax,LogicalMin,LogicalMax\n"
        + string.Concat(Enumerable.Range(0, 10).Select(i => $"{i},P{i},0,0,0,0\n")));
    var io = new IoComponent();
    io.Di.Load(csv);
    io.Do.Load(csv);
    var plc = new FakePlc();
    PlcComponent.Current = plc;
    _ = new EcComponent();
    plc.Cylinders.Add((0, 1, 0, 1));
    plc.Cylinders.Add((2, 3, 2, 3));
    plc.Cylinders.Add((4, 5, 4, 5));
    plc.Valves.Add((7, 7));
    plc.SetDi(1, true);
    plc.SetDi(3, true);
    plc.SetDi(5, true);
    plc.AddAxis("Spin.Command", "Spin.Status");
    plc.AddAxis("Arm.Command", "Arm.Status");

    var chamber = (SmokeChamber)ComponentLoader.Load([ChamberConfig("Chamber9", enabled: true)]).Single();
    var door = Find<CylinderComponent>(chamber, "Door");
    var bowl = Find<CylinderComponent>(chamber, "Bowl");
    var lift = Find<CylinderComponent>(chamber, "Lift");
    var spin = Find<SpinMotorComponent>(chamber, "SpinMotor");
    var arm = Find<ArmAxisComponent>(chamber, "Arm1");
    var diw = Find<NozzleComponent>(chamber, "Nozzle_DIW");
    var sc1 = Find<NozzleComponent>(chamber, "Nozzle_SC1");
    Check(spin.Open(plc) && arm.Open(plc), "轴登记数据块");
    var service = new ChamberService([chamber]);

    void Tick(int count = 1)
    {
        for (int i = 0; i < count; i++)
        {
            plc.Step();
            chamber.Tick();
        }
    }

    // 后台发 RPC（它会同步等操作做完），这边一拍一拍地扫，直到它回包。
    Task<RpcResponse> Start(Func<Task<RpcResponse>> call)
    {
        var task = Task.Run(call);
        // 等它真发出去（挂上操作或已经回包）再往下走，免得后面的检查跑在发起之前。
        var watch = Stopwatch.StartNew();
        while (!task.IsCompleted && chamber.CurrentOperation is null && watch.ElapsedMilliseconds < 2000)
        {
            Thread.Sleep(2);
        }

        return task;
    }

    RpcResponse Finish(Task<RpcResponse> task)
    {
        var watch = Stopwatch.StartNew();
        while (!task.IsCompleted)
        {
            if (watch.ElapsedMilliseconds > 10000)
            {
                throw new InvalidOperationException("FAIL: RPC 应在 10 s 内回包");
            }

            Tick();
            Thread.Sleep(5);
        }

        Tick();
        return task.Result;
    }

    RpcResponse Act(string part, ChamberPartAction action, string module = "Chamber9")
    {
        return Finish(Start(() => service.PartActionAsync(new ChamberPartActionRequest
        {
            Module = module,
            Part = part,
            Action = action,
        })));
    }

    ChamberCylinderDto Cylinder(Func<ChamberPartsDto, ChamberCylinderDto?> pick)
    {
        var cylinder = pick(chamber.CreatePartsDto());
        if (cylinder is null)
        {
            throw new InvalidOperationException("FAIL: 气缸快照不该为空");
        }

        return cylinder;
    }

    ChamberSpinDto SpinState()
    {
        var state = chamber.CreatePartsDto().Spin;
        if (state is null)
        {
            throw new InvalidOperationException("FAIL: 旋转电机快照不该为空");
        }

        return state;
    }

    double Reach()
    {
        return chamber.CreatePartsDto().Arms[0].Reach;
    }

    void SetArm(double position)
    {
        var status = plc.Read<MotionPlcToCSharpData>("Arm.Status");
        status.Current_Position = position;
        plc.Put("Arm.Status", status);
        Tick();
    }

    void SetSpinSpeed(double speed)
    {
        var status = plc.Read<MotionPlcToCSharpData>("Spin.Status");
        status.Current_Speed = speed;
        plc.Put("Spin.Status", status);
        Tick();
    }

    Tick(2);
    Check(arm.HasPlcData && spin.HasPlcData, "扫两拍后轴拿到 PLC 数据");

    // 1. 部件组成：门、Bowl 按名字认（都是气缸），旋转电机、摆臂按类型认，摆臂下面第一个气缸是 Lift，喷嘴按 sc 先后
    var parts = chamber.CreatePartsDto();
    Check(parts.Name == "Chamber9", "部件快照带模块名");
    Check(parts.Door is not null && parts.Door.Name == "Door" && parts.Door.Path == "Chamber9.Door", "门按名字认出来，路径照 sc");
    Check(parts.Bowl is not null && parts.Bowl.Path == "Chamber9.Bowl", "Bowl 按名字认出来（一个气缸）");
    Check(parts.Spin is not null && parts.Spin.Path == "Chamber9.SpinMotor", "旋转电机按类型认出来");
    Check(parts.Arms.Count == 1 && parts.Arms[0].Path == "Chamber9.Arm1" && parts.Arms[0].Name == "Arm1", "摆臂按类型认出来");
    var armDto = parts.Arms[0];
    Check(armDto.Lift is not null && armDto.Lift.Path == "Chamber9.Arm1.Lift", "摆臂下面的气缸是它的 Lift");
    Check(armDto.Nozzles.Select(nozzle => nozzle.Name).SequenceEqual(["Nozzle_DIW", "Nozzle_SC1"])
        && armDto.Nozzles[0].Chemical == "DIW" && armDto.Nozzles[1].Path == "Chamber9.Arm1.Nozzle_SC1", "喷嘴按 sc 先后，带药液名和路径");
    Check(chamber.FindPart("chamber9.arm1.lift") == lift && chamber.FindPart("Chamber9.Nope") is null, "按全路径找部件，忽略大小写");
    Check(!BaseChamberModule.SupportsPartAction(door, ChamberPartAction.Center)
        && BaseChamberModule.SupportsPartAction(door, ChamberPartAction.Open)
        && BaseChamberModule.SupportsPartAction(diw, ChamberPartAction.On)
        && BaseChamberModule.SupportsPartAction(arm, ChamberPartAction.Center)
        && BaseChamberModule.SupportsPartAction(spin, ChamberPartAction.Start)
        && !BaseChamberModule.SupportsPartAction(spin, ChamberPartAction.Home)
        && !BaseChamberModule.SupportsPartAction(Find<DiSensorComponent>(chamber, "WaferSensor"), ChamberPartAction.On),
        "各部件只支持自己那几个动作");

    // 2. 气缸显示状态：看线圈画指令侧（不等到位），指令侧没到位算在走；两个线圈都没通时按到位反馈
    Check(!Cylinder(dto => dto.Door).IsOpen && !Cylinder(dto => dto.Door).IsMoving, "刚上电线圈都没通、在关侧：关着、没在走");
    plc.HoldCylinders = true;
    Check(door.Open(), "直接开门，线圈写得进去");
    Check(Cylinder(dto => dto.Door).IsOpen && Cylinder(dto => dto.Door).IsMoving, "指令一发出去就算开侧，没到位算在走");
    plc.HoldCylinders = false;
    Tick();
    Check(Cylinder(dto => dto.Door).IsOpen && !Cylinder(dto => dto.Door).IsMoving, "到位后不再在走");
    plc.WriteDo(0, false);
    Check(Cylinder(dto => dto.Door).IsOpen && !Cylinder(dto => dto.Door).IsMoving, "两个线圈都断了按到位反馈：还在开侧");
    Check(door.Close(), "直接关门");
    Tick();
    Check(!Cylinder(dto => dto.Door).IsOpen && !Cylinder(dto => dto.Door).IsMoving, "关到位");
    plc.IsConnected = false;
    Check(!Cylinder(dto => dto.Bowl).IsOpen && !Cylinder(dto => dto.Bowl).IsMoving, "PLC 断了不算开、也不算在走");
    plc.IsConnected = true;
    Tick(2);

    // 3. 喷嘴、旋转电机、摆臂 Reach
    Check(diw.On() && chamber.CreatePartsDto().Arms[0].Nozzles[0].IsOn, "不接反馈的喷嘴按输出回读算出液");
    Check(sc1.On(), "接反馈的喷嘴写得进去");
    Check(!chamber.CreatePartsDto().Arms[0].Nozzles[1].IsOn, "接了流量开关的喷嘴等反馈");
    Tick();
    Check(chamber.CreatePartsDto().Arms[0].Nozzles[1].IsOn, "反馈来了算出液");
    Check(diw.Off() && sc1.Off(), "关喷嘴");
    Tick();
    Check(chamber.CreatePartsDto().Arms[0].Nozzles.All(nozzle => !nozzle.IsOn), "都停液");

    SetSpinSpeed(50);
    Check(SpinState().IsSpinning && SpinState().IsClockwise, "实际转速超出速度容差算在转，正转算顺时针");
    SetSpinSpeed(-50);
    Check(SpinState().IsSpinning && !SpinState().IsClockwise, "反转");
    SetSpinSpeed(2);
    Check(!SpinState().IsSpinning, "速度容差以内不算在转");
    SetSpinSpeed(0);

    Check(Reach() == 0, "回零位、Center 未标定（默认 0）：Home");
    SetArm(10);
    Check(Reach() == 1, "未标定时离开 0 位就算工艺位（只分两档）");
    arm.Center = 40;
    SetArm(20);
    Check(Math.Abs(Reach() - 0.5) < 1e-9, "标定后按轴位置线性换算：20 / 40 = 0.5");
    SetArm(20.5);
    Check(Math.Abs(Reach() - 0.5) < 1e-9, "位置变化在到位容差（1）以内不算动");
    SetArm(22);
    Check(Math.Abs(Reach() - 0.55) < 1e-9, "超出到位容差就跟着变");
    plc.IsConnected = false;
    Tick();
    Check(!arm.HasPlcData && Math.Abs(Reach() - 0.55) < 1e-9, "PLC 断了保持上次推的值");
    plc.IsConnected = true;
    Tick(2);
    SetArm(0);
    Check(Reach() == 0, "回到 0 位");

    // 4. 推送：有变化才推；跟 ChamberDto 同 token、类型不同，各自留存互不覆盖
    int pushes = 0;
    ChamberPartsDto? pushed = null;
    using var partsSubscription = EventBus.Register<ChamberPartsDto>("Chamber9", dto =>
    {
        pushes++;
        pushed = dto;
    });
    ChamberDto? state = null;
    using var stateSubscription = EventBus.Register<ChamberDto>("Chamber9", dto => state = dto);
    Check(pushes == 1 && pushed is not null && pushed.Name == "Chamber9", "部件状态留存：订上就补发最后一条");
    Check(state is not null && state.Name == "Chamber9", "模块状态也还在：两种 DTO 同 token 不互相覆盖");
    Tick(3);
    Check(pushes == 1, "没变化不推");
    Check(bowl.Open(), "升 Bowl");
    Tick();
    Check(pushes == 2 && pushed is not null && pushed.Bowl is not null && pushed.Bowl.IsOpen, "变了推一次");
    Check(bowl.Close(), "降 Bowl");
    Tick();

    // 5. 部件动作：找不到、不支持、模块不存在
    var response = Act("Chamber9.Nope", ChamberPartAction.Open);
    Check(!response.Success && response.Code == ErrorCodes.ChamberPartNotFound
        && response.Args.SequenceEqual(["Chamber9", "Chamber9.Nope"]), "找不到部件：chamber.part_not_found [模块, 路径]");
    response = Act("Chamber9.Door", ChamberPartAction.Center);
    Check(!response.Success && response.Code == ErrorCodes.ChamberPartActionUnsupported
        && response.Args.SequenceEqual(["Chamber9.Door", "Center"]), "门不支持去工艺位：chamber.part_action_unsupported [路径, 动作]");
    response = Act("Chamber9.Door", ChamberPartAction.Open, module: "Chamber404");
    Check(!response.Success && response.Code == ErrorCodes.ModuleNotFound, "模块不存在");

    // 6. 指令没发出去（轴没回零不能定位）：直接回码，模块状态不动、不挂操作
    Check(chamber.State == ModuleState.NotInit, "还没初始化");
    response = Act("Chamber9.Arm1", ChamberPartAction.Center);
    Check(!response.Success && response.Code == ErrorCodes.ChamberPartCommandRejected
        && response.Args.SequenceEqual(["Chamber9.Arm1", "Center"]), "轴没回零去工艺位：chamber.part_command_rejected");
    Check(chamber.State == ModuleState.NotInit && chamber.CurrentOperation is null, "指令没发出去不改模块状态、不挂操作");

    // 7. 执行中落 Manual、在途拒绝别的动作、做完回原来的状态
    plc.HoldCylinders = true;
    var opening = Start(() => service.PartActionAsync(new ChamberPartActionRequest
    {
        Module = "Chamber9",
        Part = "Chamber9.Door",
        Action = ChamberPartAction.Open,
    }));
    Tick(3);
    Check(!opening.IsCompleted && chamber.State == ChamberState.Manual, "门在走：模块落 Manual（120），RPC 还在等");
    response = Act("Chamber9.Bowl", ChamberPartAction.Open);
    Check(!response.Success && response.Code == ErrorCodes.ActionRejected
        && response.Args.SequenceEqual(["Chamber9", ChamberState.Manual.ToString()]), "一个部件动作在途时别的动作被拒");
    Check(!plc.ReadDo(2), "被拒的动作没发指令（Bowl 线圈没通）");
    plc.HoldCylinders = false;
    response = Finish(opening);
    Check(response.Success && chamber.State == ModuleState.NotInit && Cylinder(dto => dto.Door).IsOpen, "门开到位：成功，回到原来的未初始化");

    // 8. Abort 顶替在途的部件动作
    plc.HoldCylinders = true;
    var lifting = Start(() => service.PartActionAsync(new ChamberPartActionRequest
    {
        Module = "Chamber9",
        Part = "Chamber9.Arm1.Lift",
        Action = ChamberPartAction.Open,
    }));
    Tick(2);
    response = Finish(Start(() => service.AbortAsync("Chamber9")));
    Check(response.Success, "Abort 成功");
    response = Finish(lifting);
    Check(!response.Success && response.Code == ErrorCodes.Aborted, "被 Abort 顶掉的部件动作回 module.action_aborted");
    Check(chamber.State == ModuleState.Idle && lift.ActionState == ActionState.Idle, "Abort 后空闲，Lift 不再等到位");
    plc.HoldCylinders = false;
    Tick();

    // 9. 部件做失败（到位超时）：回 chamber.part_action_failed，模块落 Error；Error 下还能手动动部件，做完仍是 Error
    door.ActionTimeoutMs = 200;
    plc.HoldCylinders = true;
    response = Act("Chamber9.Door", ChamberPartAction.Close);
    Check(!response.Success && response.Code == ErrorCodes.ChamberPartActionFailed
        && response.Args.SequenceEqual(["Chamber9.Door", "Close"]), "关门等不到位：chamber.part_action_failed [路径, 动作]");
    Check(chamber.State == ModuleState.Error, "部件动作失败模块落 Error");
    plc.HoldCylinders = false;
    response = Act("Chamber9.Door", ChamberPartAction.Close);
    Check(response.Success && chamber.State == ModuleState.Error, "Error 下手动关门成功，不顺带清错");
    response = Finish(Start(() => service.ResetAsync("Chamber9")));
    Check(response.Success && chamber.State == ModuleState.Idle, "复位回空闲");

    // 10. 各部件真走一遍：摆臂回零 / 工艺位、旋转电机转 / 停、喷嘴、Lift、Bowl
    response = Act("Chamber9.Arm1", ChamberPartAction.Home);
    Check(response.Success && arm.IsHomed && chamber.State == ModuleState.Idle, "摆臂回零");
    response = Act("Chamber9.Arm1", ChamberPartAction.Center);
    Check(response.Success && Math.Abs(arm.CurrentPosition - 40) < 1e-9 && Reach() == 1, "摆臂去工艺位：走到 EC Center，Reach = 1");
    response = Act("Chamber9.Arm1", ChamberPartAction.Home);
    Check(response.Success && Reach() == 0, "摆臂回 Home");
    response = Act("Chamber9.SpinMotor", ChamberPartAction.Start);
    Check(response.Success && SpinState().IsSpinning && SpinState().IsClockwise, "旋转电机按 EC ManualSpeed（默认 50，正转）转起来");
    response = Act("Chamber9.SpinMotor", ChamberPartAction.Stop);
    Check(response.Success && !SpinState().IsSpinning, "旋转电机停");
    response = Act("Chamber9.Arm1.Nozzle_SC1", ChamberPartAction.On);
    Check(response.Success && chamber.CreatePartsDto().Arms[0].Nozzles[1].IsOn, "喷嘴出液（等到流量开关）");
    response = Act("Chamber9.Arm1.Nozzle_SC1", ChamberPartAction.Off);
    Check(response.Success && !chamber.CreatePartsDto().Arms[0].Nozzles[1].IsOn, "喷嘴停液");
    response = Act("Chamber9.Arm1.Lift", ChamberPartAction.Open);
    Check(response.Success && Cylinder(dto => dto.Arms[0].Lift).IsOpen, "Lift 升");
    response = Act("Chamber9.Arm1.Lift", ChamberPartAction.Close);
    Check(response.Success && !Cylinder(dto => dto.Arms[0].Lift).IsOpen, "Lift 降");
    response = Act("Chamber9.Bowl", ChamberPartAction.Open);
    Check(response.Success && Cylinder(dto => dto.Bowl).IsOpen && chamber.State == ModuleState.Idle, "Bowl 升，做完回空闲");

    // 11. 停用的腔体不发部件动作
    var disabled = (SmokeChamber)ComponentLoader.Load([ChamberConfig("Chamber8", enabled: false)]).Single();
    var disabledService = new ChamberService([disabled]);
    var disabledResponse = disabledService.PartActionAsync(new ChamberPartActionRequest
    {
        Module = "Chamber8",
        Part = "Chamber8.Door",
        Action = ChamberPartAction.Open,
    }).Result;
    Check(!disabledResponse.Success && disabledResponse.Code == ErrorCodes.ActionRejected, "停用的腔体部件动作被拒");

    Console.WriteLine($"PASS: {checks} chamber parts checks (structure from sc, cylinder/nozzle/spin/arm display state with reach deadband, "
        + "push on change with retained replay, part actions: not found, unsupported, command rejected, Manual state, busy rejection, "
        + "abort replacement, failure to Error, every part type end to end, disabled chamber)");
    return 0;
}
finally
{
    PlcComponent.Current = previousPlc;
    IoComponent.Current = previousIo;
    EcComponent.Current = previousEc;
    Directory.Delete(directory, recursive: true);
}

static T Find<T>(ComponentBase root, string name) where T : class
{
    var found = root.FindChild<T>(name);
    if (found is null)
    {
        throw new InvalidOperationException($"FAIL: 找不到 {name}");
    }

    return found;
}

static ValueConfig Value(string name, string value)
{
    return new ValueConfig { Name = name, Value = value };
}

static ModuleConfig Node(string name, Type type, params ValueConfig[] values)
{
    return new ModuleConfig { Name = name, Type = type.FullName, Values = [.. values] };
}

static ModuleConfig ChamberConfig(string name, bool enabled)
{
    var arm = Node("Arm1", typeof(ArmAxisComponent), Value("SendPlcDataPath", "Arm.Command"), Value("ReceivePlcDataPath", "Arm.Status"));
    arm.Children =
    [
        Node("Lift", typeof(CylinderComponent), Value("DoOpenIndex", "4"), Value("DoCloseIndex", "5"),
            Value("DiOpenedIndex", "4"), Value("DiClosedIndex", "5")),
        Node("Nozzle_DIW", typeof(NozzleComponent), Value("DoIndex", "6"), Value("Chemical", "DIW")),
        Node("Nozzle_SC1", typeof(NozzleComponent), Value("DoIndex", "7"), Value("DiIndex", "7"), Value("Chemical", "SC1")),
    ];
    var chamber = Node(name, typeof(SmokeChamber), Value("IsEnable", enabled ? "True" : "False"));
    chamber.Children =
    [
        Node("Door", typeof(CylinderComponent), Value("DoOpenIndex", "0"), Value("DoCloseIndex", "1"),
            Value("DiOpenedIndex", "0"), Value("DiClosedIndex", "1")),
        Node("Bowl", typeof(CylinderComponent), Value("DoOpenIndex", "2"), Value("DoCloseIndex", "3"),
            Value("DiOpenedIndex", "2"), Value("DiClosedIndex", "3")),
        Node("SpinMotor", typeof(SpinMotorComponent), Value("SendPlcDataPath", "Spin.Command"), Value("ReceivePlcDataPath", "Spin.Status")),
        arm,
        Node("WaferSensor", typeof(DiSensorComponent), Value("DiIndex", "8"), Value("AlarmEnabled", "False")),
    ];
    return chamber;
}

/// <summary>冒烟用腔体：回零、复位、中止、工艺都是计时空转；Tick 由冒烟线程手动走一拍（跟机型一样扫完发布状态）。</summary>
[Component(description: "冒烟用腔体")]
public sealed class SmokeChamber : BaseChamberModule
{
    private const int ActionMs = 50;

    public override ModuleOperation? Home()
    {
        return Begin(ChamberAction.Home, new TimedProbe("Home", ActionMs));
    }

    protected override ModuleOperation? ResetDevice()
    {
        return Begin(ChamberAction.Reset, new TimedProbe("Reset", ActionMs));
    }

    protected override ModuleOperation? AbortDevice()
    {
        return Begin(ChamberAction.Abort, new TimedProbe("Abort", ActionMs));
    }

    protected override ModuleOperation? StartProcess(string recipe)
    {
        return Begin(ChamberAction.Process, new TimedProbe("Process", ActionMs));
    }

    public void Tick()
    {
        OnScan();
    }

    protected override void OnScan()
    {
        base.OnScan();
        PublishState();
    }
}

/// <summary>到时间就成功的空转操作。</summary>
public sealed class TimedProbe : ModuleOperation
{
    private readonly int _milliseconds;

    public TimedProbe(string name, int milliseconds) : base(name)
    {
        _milliseconds = milliseconds;
    }

    protected override void OnScan()
    {
        if (Watch.ElapsedMilliseconds >= _milliseconds)
        {
            Complete();
        }
    }
}

/// <summary>
/// 假 PLC：DI / DO 两张表、轴数据块存字典。Step() 模拟一拍设备：气缸照线圈走到位（HoldCylinders 时不动）、
/// 带反馈的阀照输出给反馈、轴照新命令回零 / 定位 / 转 / 停。冒烟线程和 RPC 线程都会碰它，全部加锁。
/// </summary>
public sealed class FakePlc : IPlc
{
    private readonly object _gate = new();
    private readonly Dictionary<int, bool> _di = [];
    private readonly Dictionary<int, bool> _do = [];
    private readonly Dictionary<string, byte[]> _blocks = [];
    private readonly Dictionary<string, ulong> _handledSync = [];
    private readonly List<(string Command, string Status)> _axes = [];
    private volatile bool _connected = true;

    public List<(int Open, int Close, int Opened, int Closed)> Cylinders { get; } = [];

    public List<(int Do, int Di)> Valves { get; } = [];

    public bool HoldCylinders { get; set; }

    public bool IsConnected
    {
        get => _connected;
        set => _connected = value;
    }

    public void AddAxis(string command, string status)
    {
        _axes.Add((command, status));
        Put(command, new MotionCSharpToPlcCommand { Axis_Servo = 1 });
        Put(status, new MotionPlcToCSharpData { Is_Ready = 1, Is_Servo_On = 1, Is_Stopped = 1, Is_In_Position = 1 });
        _handledSync[command] = 0;
    }

    public void SetDi(int index, bool on)
    {
        lock (_gate)
        {
            _di[index] = on;
        }
    }

    public bool ReadDo(int index)
    {
        lock (_gate)
        {
            return _do.GetValueOrDefault(index);
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
        lock (_gate)
        {
            if (_connected && _blocks.TryGetValue(path, out var stored))
            {
                block = stored.ToArray();
                return true;
            }

            block = [];
            return false;
        }
    }

    public bool WriteBlock(string path, byte[] data)
    {
        lock (_gate)
        {
            if (!_connected)
            {
                return false;
            }

            _blocks[path] = data.ToArray();
            return true;
        }
    }

    public void Put<T>(string path, T value) where T : unmanaged
    {
        byte[] bytes = new byte[Marshal.SizeOf<T>()];
        MemoryMarshal.Write(bytes, in value);
        lock (_gate)
        {
            _blocks[path] = bytes;
        }
    }

    public T Read<T>(string path) where T : unmanaged
    {
        lock (_gate)
        {
            return MemoryMarshal.Read<T>(_blocks[path]);
        }
    }

    /// <summary>模拟一拍设备动作。</summary>
    public void Step()
    {
        lock (_gate)
        {
            if (!HoldCylinders)
            {
                foreach (var (open, close, opened, closed) in Cylinders)
                {
                    bool openOn = _do.GetValueOrDefault(open);
                    bool closeOn = _do.GetValueOrDefault(close);
                    if (openOn != closeOn)
                    {
                        _di[opened] = openOn;
                        _di[closed] = closeOn;
                    }
                }
            }

            foreach (var (output, feedback) in Valves)
            {
                _di[feedback] = _do.GetValueOrDefault(output);
            }

            foreach (var (commandPath, statusPath) in _axes)
            {
                var command = MemoryMarshal.Read<MotionCSharpToPlcCommand>(_blocks[commandPath]);
                if (command.Command_Sync_No == _handledSync[commandPath])
                {
                    continue;
                }

                _handledSync[commandPath] = command.Command_Sync_No;
                var status = MemoryMarshal.Read<MotionPlcToCSharpData>(_blocks[statusPath]);
                switch ((MotionCommandId)command.Axis_Command)
                {
                    case MotionCommandId.Home:
                        status.Is_Homed = 1;
                        status.Current_Position = 0;
                        status.Is_In_Position = 1;
                        status.Is_Stopped = 1;
                        break;

                    case MotionCommandId.MoveTo:
                        status.Current_Position = command.Param1;
                        status.Is_In_Position = 1;
                        status.Is_Stopped = 1;
                        break;

                    case MotionCommandId.Spin:
                        status.Current_Speed = command.Param2;
                        status.Is_Stopped = 0;
                        break;

                    case MotionCommandId.Stop:
                    case MotionCommandId.EStop:
                        status.Current_Speed = 0;
                        status.Is_Stopped = 1;
                        status.Is_Servo_On = command.Axis_Servo;
                        break;
                }

                byte[] bytes = new byte[Marshal.SizeOf<MotionPlcToCSharpData>()];
                MemoryMarshal.Write(bytes, in status);
                _blocks[statusPath] = bytes;
            }
        }
    }
}
