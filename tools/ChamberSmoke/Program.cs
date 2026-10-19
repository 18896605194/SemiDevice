using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using xyz.Components;
using xyz.Components.Attributes;
using xyz.Components.Components;
using xyz.Components.Enums;
using xyz.Components.Interfaces;
using xyz.Components.Models;
using xyz.Configs.Models;
using xyz.Database.DbProvider;
using xyz.Database.Lifetime;
using xyz.Database.Process;
using xyz.Modules;
using xyz.Modules.Enums;
using xyz.Service;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Tools;

// 腔体冒烟：照 sc 认设备（轴表、气缸表、喷嘴表，门 / Bowl / Lift / 卡盘 / 摆臂和它上面的喷嘴），气缸三态、喷嘴、卡盘、摆臂 Reach，有变化才推；
// 设备手动动作（找不到、参数不对、指令没发出去、执行中 Manual、在途拒绝、Abort 顶替、失败、停用、停止腔体忙也照发、
// 点动按住 / 续 / 松手 / 没续上自己停，以及各设备真走一遍）；平台默认的整腔动作：回零按先后走（卡盘也回零）、
// 取放准备二（卡盘转到取放位 + 开门，没回零就失败）、复位等清错、中止停液停轴、
// 按工艺配方做工艺（Lift / Bowl 升、转速、摆到位置、开对的喷嘴、给流量、Time / Scan、收尾，失败停液、超时、中止、配方对不上）。
// 工艺的开工片数（按天存库、开机读回来接着数）和进度百分比。部件寿命（盯 DO 计次、警告 / 报警次数、存库读回、清零）。
// 全用假 PLC 和内存 EC，不连设备、不读写配置文件；开工片数、寿命次数写进临时库。

SmokeIoc.Reset();

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

// 开工片数落默认库：默认库指到临时文件（放在临时目录外面，SQLite 连接池占着文件也不耽误删目录）
var countDb = Path.Combine(Path.GetTempPath(), "xyz-chamber-count-" + Guid.NewGuid().ToString("N") + ".db");
XyzDb.Register(XyzDb.DefaultName, $"DataSource={countDb}", SqlSugar.DbType.Sqlite);
var previousPlcIo = IocHelper.GetService<IPlcIo>();
var previousPlcBlock = IocHelper.GetService<IPlcBlock>();
var previousIo = IocHelper.GetService<IIoComponent>();
var previousEc = IocHelper.GetService<IEcComponent>();
try
{
    // 0. 假 PLC、IO 表（0~9 号点）、内存 EC；接线：Door 0/1、Bowl 2/3、Lift 4/5（DO 与 DI 同号），
    //    DIW 喷嘴只有 DO 6（不接反馈）、流量 AO 0，SC1 喷嘴 DO 7 / DI 7、流量 AO 1，两根轴各一对数据块。
    // 点表放进 IO 组件的点表目录：它开机准备时读点表、取 PLC
    string points = "Index,Name,PhysicalMin,PhysicalMax,LogicalMin,LogicalMax\n"
        + string.Concat(Enumerable.Range(0, 10).Select(i => $"{i},P{i},0,0,0,0\n"));
    foreach (string table in new[] { "DI", "DO", "AO" })
    {
        File.WriteAllText(Path.Combine(directory, table + ".csv"), points);
    }

    var plc = new FakePlc();
    SmokeIoc.Use<IPlcIo>(plc);
    SmokeIoc.Use<IPlcBlock>(plc);
    var io = new IoComponent { PointTableDirectory = directory };
    SmokeIoc.Use<IIoComponent>(io);
    io.InitComponent();
    SmokeIoc.Use<IEcComponent>(new EcComponent());
    plc.Cylinders.Add((0, 1, 0, 1));
    plc.Cylinders.Add((2, 3, 2, 3));
    plc.Cylinders.Add((4, 5, 4, 5));
    plc.Valves.Add((7, 7));
    plc.SetDi(1, true);
    plc.SetDi(3, true);
    plc.SetDi(5, true);
    plc.AddAxis("Spin.Command", "Spin.Status");
    plc.AddAxis("Arm.Command", "Arm.Status");

    var chamber = (SmokeChamber)SmokeIoc.Load([ChamberConfig("Chamber9", enabled: true)]).Single();
    var door = Find<ChamberDoorComponent>(chamber, "Door");
    var bowl = Find<ChamberBowlComponent>(chamber, "Bowl1");
    var lift = Find<ChamberLiftComponent>(chamber, "Lift");
    var spin = Find<SpinMotorComponent>(chamber, "SpinMotor");
    var arm = Find<SwingArmComponent>(chamber, "Arm1");
    var diw = Find<NozzleComponent>(chamber, "Nozzle_DIW");
    var sc1 = Find<NozzleComponent>(chamber, "Nozzle_SC1");
    Check(spin.Open(plc) && arm.Open(plc), "轴登记数据块");

    // 到位反馈、流量开关是子节点，各设备做组件初始化时认（腔体自己的组件初始化在第 17 节测）；反馈防抖设 0，一拍就认
    foreach (var sensor in chamber.FindChildren<DiSensorComponent>())
    {
        sensor.DebounceMs = 0;
    }

    Check(door.InitComponent() && bowl.InitComponent() && lift.InitComponent() && diw.InitComponent() && sc1.InitComponent(),
        "门、Bowl、Lift、喷嘴的组件初始化：认出到位反馈、流量开关");
    // 开机 Bind：设备取 IO 组件
    ComponentLoader.BindAll([chamber]);
    var service = new ChamberService();

    void Tick(int count = 1)
    {
        for (int i = 0; i < count; i++)
        {
            plc.Step();
            chamber.Tick();
        }
    }

    // 后台发 RPC（普通动作会同步等操作做完），这边一拍一拍地扫，直到它回包。
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

    RpcResponse Run(Func<Task<RpcResponse>> call)
    {
        return Finish(Start(call));
    }

    ChamberDeviceRequest Device(string path, string module = "Chamber9")
    {
        return new ChamberDeviceRequest { Module = module, Device = path };
    }

    ChamberAxisDto SpinDto()
    {
        var spinDto = chamber.CreateDeviceDataDto().Spin;
        if (spinDto is null)
        {
            throw new InvalidOperationException("FAIL: 推送里应有卡盘");
        }

        return spinDto;
    }

    ChamberArmDto ArmDto()
    {
        return chamber.CreateDeviceDataDto().Arms.Single();
    }

    // 气缸在推送里分在门、Bowl、各臂的 Lift 三处，按路径找
    ChamberCylinderDto CylinderDto(string path)
    {
        var data = chamber.CreateDeviceDataDto();
        var all = new List<ChamberCylinderDto>(data.Bowls);
        if (data.Door is not null)
        {
            all.Add(data.Door);
        }

        all.AddRange(data.Arms.Where(item => item.Lift is not null).Select(item => item.Lift!));
        return all.Single(cylinder => cylinder.Path == path);
    }

    ChamberNozzleDto NozzleDto(string path)
    {
        return chamber.CreateDeviceDataDto().Arms.SelectMany(item => item.Nozzles).Single(nozzle => nozzle.Path == path);
    }

    bool Near(double a, double b)
    {
        return Math.Abs(a - b) < 1e-9;
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
        status.Is_Busy = speed == 0 ? (byte)0 : (byte)1;
        plc.Put("Spin.Status", status);
        Tick();
    }

    void WaitIdle(string message)
    {
        var watch = Stopwatch.StartNew();
        while (chamber.State == ChamberState.Manual && watch.ElapsedMilliseconds < 5000)
        {
            Tick();
            Thread.Sleep(10);
        }

        Check(chamber.State != ChamberState.Manual, message);
    }

    Tick(2);
    Check(arm.HasPlcData && spin.HasPlcData, "扫两拍后轴拿到 PLC 数据");

    // 接口识别：改名、插入普通气缸不会改变部件用途，设备图继续使用配置中的真实路径。
    var flexibleConfig = ChamberConfig("Flexible", enabled: true);
    flexibleConfig.Children[0].Name = "FrontGate";
    flexibleConfig.Children[1].Name = "OuterCup";
    var flexibleArm = flexibleConfig.Children[3];
    flexibleArm.Children[0].Name = "VerticalMount";
    flexibleArm.Children.Insert(0, Node("Lift", typeof(CylinderComponent)));
    flexibleConfig.Children.Insert(0, Node("Door", typeof(CylinderComponent)));
    flexibleConfig.Children.Add(Node("Bowl99", typeof(CylinderComponent)));
    flexibleConfig.Children.Add(Node("InnerCup", typeof(ChamberBowlComponent)));
    var flexible = (SmokeChamber)SmokeIoc.Load([flexibleConfig]).Single();
    Check(flexible.Door?.Name == "FrontGate", "改名的腔门按接口识别，普通气缸叫 Door 也不会被认成腔门");
    Check(flexible.Bowls.Select(item => item.Name).SequenceEqual(["OuterCup", "InnerCup"]),
        "多层接液杯按接口识别，普通气缸叫 Bowl99 也不会混进来");
    Check(flexible.Arms.Single().Lift?.Name == "VerticalMount", "升降按接口识别，不取摆臂下面第一个气缸");
    var flexibleDto = flexible.CreateDeviceDataDto();
    Check(flexibleDto.Door?.Path == "Flexible.FrontGate"
        && flexibleDto.Arms.Single().Lift?.Path == "Flexible.Arm1.VerticalMount",
        "设备图推送跟着真实实例路径，改名不改变部件位置");
    Check(flexible.Cylinders.Count == 7, "普通气缸仍在手动气缸表里，只是不冒充门、Bowl 或 Lift");

    // 同一接口允许另一种实现：这两个探针不继承 CylinderComponent，动作执行器仍能发、等和推送。
    var interfaceConfig = Node("InterfaceOnly", typeof(SmokeChamber));
    interfaceConfig.Children = [Node("AccessGate", typeof(InterfaceDoorProbe)), Node("Collector", typeof(InterfaceBowlProbe))];
    var interfaceChamber = (SmokeChamber)SmokeIoc.Load([interfaceConfig]).Single();
    Check(interfaceChamber.InitComponent(), "只提供部件接口的腔体初始化成功");
    var interfaceHome = interfaceChamber.Home();
    for (int index = 0; index < 8; index++)
    {
        interfaceChamber.Tick();
    }

    Check(interfaceHome is not null && interfaceHome.IsSuccess
        && interfaceChamber.State == ModuleState.Idle, "回零流程通过接口控制不同实现的接液杯");
    var interfaceContext = TransferContext.ForPlace("Robot1", 1, 1);
    Check(interfaceChamber.PrepareTransfer(interfaceContext) is not null, "不同实现的腔门进入取放准备一");
    var interfaceOpen = interfaceChamber.PrepareTransfer2(interfaceContext);
    for (int index = 0; index < 4; index++)
    {
        interfaceChamber.Tick();
    }

    Check(interfaceOpen is not null && interfaceOpen.IsSuccess && interfaceChamber.Door!.IsOpened
        && interfaceChamber.State == TransferModuleState.TransferReady,
        "取放开门和动作等待只依赖接口，不要求气缸具体类；没配卡盘就只开门");
    Check(interfaceChamber.CreateDeviceDataDto().Door?.Position == CylinderPosition.Opened,
        "不同接口实现的腔门也能生成设备状态推送");

    // 单实例部件配重了开机就拒绝，不能悄悄取第一个。
    void RejectDuplicate(ModuleConfig config, string expected, string message)
    {
        var rejected = (SmokeChamber)SmokeIoc.Load([config]).Single();
        try
        {
            rejected.InitComponent();
            Check(false, message);
        }
        catch (InvalidOperationException exception)
        {
            Check(exception.Message.Contains(expected, StringComparison.Ordinal), message);
        }
    }

    var duplicateDoor = ChamberConfig("DuplicateDoor", enabled: true);
    duplicateDoor.Children.Add(Node("OtherGate", typeof(ChamberDoorComponent)));
    RejectDuplicate(duplicateDoor, nameof(IChamberDoor), "同一腔体配置两个腔门，开机报错");
    var duplicateSpin = ChamberConfig("DuplicateSpin", enabled: true);
    duplicateSpin.Children.Add(Node("OtherSpin", typeof(SpinMotorComponent)));
    RejectDuplicate(duplicateSpin, nameof(ISpinMotor), "同一腔体配置两个卡盘，开机报错");
    var duplicateLift = ChamberConfig("DuplicateLift", enabled: true);
    duplicateLift.Children[3].Children.Add(Node("OtherLift", typeof(ChamberLiftComponent)));
    RejectDuplicate(duplicateLift, nameof(IChamberLift), "同一摆臂配置两个升降部件，开机报错");
    var duplicateBlock = ChamberConfig("DuplicateBlock", enabled: true);
    duplicateBlock.Children[3].Values[0].Value = "Spin.Command";
    RejectDuplicate(duplicateBlock, "轴命令块路径重复: Spin.Command", "摆臂跟卡盘配了同一个命令块（会抢着写同一块），开机报错");

    // 1. 照 sc 认设备：轴表、气缸表、喷嘴表按 sc 先后（先父后子）；门、Bowl、Lift、卡盘、摆臂和它上面的喷嘴都认出来；WaferSensor 不在里面
    Check(chamber.Door == door && chamber.Bowls.SequenceEqual([bowl]) && chamber.SpinMotor == spin,
        "腔体按各部件接口认门、Bowl 和卡盘");
    Check(chamber.Arms.SequenceEqual([arm]) && arm.Lift == lift && arm.Nozzles.SequenceEqual([diw, sc1]),
        "摆臂按升降接口认 Lift，按喷嘴接口认药液");
    Check(chamber.Axes.SequenceEqual<IAxis>([spin, arm]) && chamber.Cylinders.SequenceEqual<ICylinder>([door, bowl, lift])
        && chamber.Nozzles.SequenceEqual([diw, sc1]), "轴、气缸、喷嘴照 sc 的先后（先父后子）");
    Check(chamber.FindArm("arm1") == arm && arm.FindNozzle("sc1") == sc1 && chamber.FindArm("Arm2") is null,
        "摆臂按节点名找、喷嘴按药液找，忽略大小写");
    var sc1Meter = sc1.FlowMeter ?? throw new InvalidOperationException("FAIL: SC1 下面挂了流量计，应认出来");
    Check(diw.FlowMeter is null && sc1Meter.FullPath == "Chamber9.Arm1.Nozzle_SC1.Flow", "流量计照 sc、按 IAiSensor 认：SC1 下面挂了，DIW 没挂（没流量计）");
    var dto = chamber.CreateDeviceDataDto();
    Check(dto.Module == "Chamber9", "设备快照带模块名");
    Check(dto.Door is not null && dto.Door.Path == "Chamber9.Door" && dto.Bowls.Select(item => item.Path).SequenceEqual(["Chamber9.Bowl1"])
        && dto.Spin is not null && dto.Spin.Path == "Chamber9.SpinMotor", "跟 sc 一样：门、Bowl、卡盘各在各的位置");
    var armDto = dto.Arms.Single();
    Check(armDto.Path == "Chamber9.Arm1" && armDto.Lift is not null && armDto.Lift.Path == "Chamber9.Arm1.Lift"
        && armDto.Nozzles.Select(nozzle => (nozzle.Path, nozzle.Chemical)).SequenceEqual([
            ("Chamber9.Arm1.Nozzle_DIW", "DIW"), ("Chamber9.Arm1.Nozzle_SC1", "SC1")]),
        "摆臂下面是它的 Lift 和喷嘴（带药液），跟 sc 一样；WaferSensor 不在里面");
    Check(armDto.HasPlcData && armDto.CurrentPosition == 0 && !armDto.IsError && !dto.Spin!.IsBusy, "轴的数据都是实打实的属性");

    // 2. 气缸三态：命令发到哪一侧就看那一侧到没到位，没到是未知；两个线圈都没通只看到位反馈；PLC 断了未知
    Check(CylinderDto("Chamber9.Door").Position == CylinderPosition.Closed, "刚上电线圈都没通、关到位信号亮：关到位");
    plc.HoldCylinders = true;
    Check(door.Open(), "直接开门，线圈写得进去");
    Check(CylinderDto("Chamber9.Door").Position == CylinderPosition.Unknown, "命令发到开侧、开到位信号还没亮：未知（关到位信号还亮着也算未知）");
    plc.HoldCylinders = false;
    Tick();
    Check(CylinderDto("Chamber9.Door").Position == CylinderPosition.Opened, "开到位信号亮了：开到位");
    plc.WriteDo(0, false);
    Check(CylinderDto("Chamber9.Door").Position == CylinderPosition.Opened, "两个线圈都断了只看到位反馈：还是开到位");
    Check(door.Close(), "直接关门");
    Tick();
    Check(CylinderDto("Chamber9.Door").Position == CylinderPosition.Closed, "关到位");
    plc.IsConnected = false;
    Check(CylinderDto("Chamber9.Bowl1").Position == CylinderPosition.Unknown, "PLC 断了：未知");
    plc.IsConnected = true;
    Tick(2);

    // 3. 喷嘴、卡盘、摆臂 Reach
    Check(diw.On() && NozzleDto("Chamber9.Arm1.Nozzle_DIW").IsOn, "不接反馈的喷嘴按输出回读算出液");
    Check(sc1.On() && !NozzleDto("Chamber9.Arm1.Nozzle_SC1").IsOn, "接了流量开关的喷嘴等反馈");
    Tick();
    Check(NozzleDto("Chamber9.Arm1.Nozzle_SC1").IsOn, "反馈来了算出液");
    Check(diw.Stop() && sc1.Stop() && plc.Ao(0) == 0 && plc.Ao(1) == 0, "停液：关阀，接了流量设定的设定清零");
    Tick();
    Check(!NozzleDto("Chamber9.Arm1.Nozzle_DIW").IsOn && !NozzleDto("Chamber9.Arm1.Nozzle_SC1").IsOn, "都停液");
    Check(diw.HasFlowControl && diw.SetFlow(1.5) && plc.Ao(0) == 1.5, "流量设定按点表写 AO");
    Check(sc1.SetFlow(2) && plc.Ao(1) == 2 && sc1Meter.Target == 2, "给流量：接了 AO 的下发，流量计拿它当目标值");
    sc1.AoIndex = -1;
    Check(!sc1.HasFlowControl && sc1.SetFlow(1.2) && plc.Ao(1) == 2 && sc1Meter.Target == 1.2,
        "没接 AO（固定流量的一路）：不下发、也不算失败，只给流量计当目标值");
    sc1.AoIndex = 1;
    Check(sc1.Stop() && plc.Ao(1) == 0 && sc1Meter.Target == 0, "停液：AO 设定和流量计的目标都清零");
    sc1.ClearFlowOnStop = false;
    Check(sc1.SetFlow(2) && sc1.On() && sc1.Stop() && !plc.ReadDo(7) && plc.Ao(1) == 2 && sc1Meter.Target == 2,
        "SC ClearFlowOnStop=False：停液只关阀，AO 设定和流量计目标保持上一步的");
    sc1.ClearFlowOnStop = true;
    Check(sc1.Stop() && plc.Ao(1) == 0 && sc1Meter.Target == 0, "ClearFlowOnStop 改回 True：停液清零");

    SetSpinSpeed(50);
    Check(SpinDto().IsBusy && SpinDto().CurrentSpeed == 50,
        "PLC 报忙就是在转，实际转速正负照推（界面按正负画转向）");
    SetSpinSpeed(-50);
    Check(SpinDto().IsBusy && SpinDto().CurrentSpeed == -50, "反转");
    SetSpinSpeed(0);
    Check(!SpinDto().IsBusy, "停了不报忙，不算在转");

    // 摆臂：回零后 0 位 = Home；示教位默认 Edge12 = 0（第一个边缘，跟 Home 重合）、Center = 150（晶圆中心）；没配晶圆尺寸组件按 12 寸
    double Reach()
    {
        return ArmDto().Reach;
    }

    double EdgeReach()
    {
        return ArmDto().EdgeReach;
    }

    var previousWaferSize = IocHelper.GetService<IWaferSizeComponent>();
    SmokeIoc.Use<IWaferSizeComponent>(null);
    // 摆臂开机 Bind 时取晶圆尺寸组件：换了要重新 Bind
    arm.Bind();
    Check(Near(arm.Center, 150) && Near(arm.Edge12, 0) && Near(arm.Edge, 0) && Near(arm.Edge8, 50),
        "示教位默认 Center = 150、Edge12 = 0（轴位置按 12 寸晶圆坐标走）、Edge8 = 50（12 寸边缘往里 50）；没配晶圆尺寸用 Edge12");
    Check(Near(arm.ToAxisPosition(75), 75), "没示教：配方坐标就是轴位置");
    Check(Reach() == 0 && EdgeReach() == 0, "0 位是 Home；默认 Edge 跟 Home 重合，不分段");
    SetArm(75);
    Check(Near(Reach(), 0.5), "默认示教位下按 Center 线性换算：75 / 150 = 0.5");
    // 示教后（实际轴位置）：Edge = 100、Center = 200
    arm.Edge12 = 100;
    arm.Center = 200;
    Check(Near(arm.ToAxisPosition(0), 100) && Near(arm.ToAxisPosition(150), 200) && Near(arm.ToAxisPosition(75), 150),
        "示教后配方坐标按 Edge、Center 换成轴位置：0 → 100，150 → 200");
    SetArm(100);
    Check(Near(Reach(), 0.5) && Near(EdgeReach(), 0.5), "示教后轴在 Edge：Reach = 100 / 200，边缘在 Reach 上的位置 = Edge / Center");
    SetArm(150);
    Check(Near(Reach(), 0.75), "边缘到中心之间按轴位置线性：150 / 200");
    SetArm(150.0004);
    Check(Reach() == 0.75 && ArmDto().CurrentPosition == 150, "推的值按 3 位小数取整：编码器在这一位以下抖不变");
    SetArm(152);
    Check(Near(Reach(), 0.76), "动了就跟着变");
    plc.IsConnected = false;
    Tick();
    Check(!arm.HasPlcData && Near(Reach(), 0.76) && !ArmDto().HasPlcData,
        "PLC 断了 Reach 停在断线前的值，HasPlcData 推 false（界面位置显示\"—\"）");
    plc.IsConnected = true;
    Tick(2);
    arm.Edge12 = 250;
    Check(EdgeReach() == 0, "Edge 不在 Home 和 Center 之间（示教错了）不分段");
    arm.Edge12 = 100;
    arm.Center = 0;
    SetArm(0);
    Check(Reach() == 0 && EdgeReach() == 0, "Center 被改到跟 0 位分不开：0 位算 Home，不分段");
    SetArm(10);
    Check(Reach() == 1, "Center 跟 0 位分不开时离开 0 位就算工艺位（只分两档）");
    arm.Center = 200;
    SetArm(0);
    Check(Reach() == 0 && Near(EdgeReach(), 0.5), "回到 0 位，示教位恢复");

    // 8 寸 / 12 寸：边缘示教位各一个、Center 共用；半径 8 寸 100、12 寸 150，按晶圆尺寸组件现在的尺寸换算
    var waferSize = new WaferSizeComponent { Size = WaferSizeComponent.Inch8 };
    SmokeIoc.Use<IWaferSizeComponent>(waferSize);
    arm.Bind();
    Check(IocHelper.GetService<IWaferSizeComponent>() == waferSize && WaferSizeComponent.SizeOf(waferSize) == 8, "配了晶圆尺寸组件就按它的尺寸");
    arm.Edge8 = 125;
    Check(Near(arm.Edge, 125) && Near(arm.ToAxisPosition(0), 125) && Near(arm.ToAxisPosition(100), 200) && Near(arm.ToAxisPosition(50), 162.5),
        "8 寸：配方 0 → Edge8，100（8 寸中心）→ Center，中间线性");
    Check(Near(EdgeReach(), 125.0 / 200), "8 寸：三维图的边缘按 Edge8 画");
    waferSize.Size = WaferSizeComponent.Inch12;
    Check(Near(arm.Edge, 100) && Near(arm.ToAxisPosition(0), 100) && Near(arm.ToAxisPosition(150), 200) && Near(EdgeReach(), 0.5),
        "切回 12 寸：按 Edge12 和半径 150，8 寸的示教位留着不动");
    SmokeIoc.Use<IWaferSizeComponent>(previousWaferSize);
    arm.Bind();

    // 4. 推送：有变化才推；跟 ChamberDto 同 token、类型不同，各自留存互不覆盖
    int pushes = 0;
    ChamberDeviceDataDto? pushed = null;
    using var partsSubscription = EventBus.Register<ChamberDeviceDataDto>("Chamber9", item =>
    {
        pushes++;
        pushed = item;
    });
    ChamberDto? state = null;
    using var stateSubscription = EventBus.Register<ChamberDto>("Chamber9", item => state = item);
    Check(pushes == 1 && pushed is not null && pushed.Module == "Chamber9", "设备推送留存：订上就补发最后一条");
    Check(state is not null && state.Name == "Chamber9", "模块状态也还在：两种 DTO 同 token 不互相覆盖");
    Tick(3);
    Check(pushes == 1, "没变化不推");
    SetArm(0.0003);
    Check(pushes == 1, "变化在推的小数位以下不推");
    Check(bowl.Open(), "升 Bowl");
    Tick();
    Check(pushes == 2 && pushed is not null && pushed.Bowls[0].Position == CylinderPosition.Opened,
        "变了推一次");
    Check(bowl.Close(), "降 Bowl");
    Tick();

    // 中间几节另装过腔体（换了整棵树），服务按接口在树上找模块，先换回这台
    SmokeIoc.Tree(chamber);

    // 上线 / 下线走服务基类 → 模块基类的 Online / Offline，只改 Mode、不挂动作
    var response = Run(() => service.OnlineAsync("Chamber404"));
    Check(!response.Success && response.Code == ErrorCodes.ModuleNotFound, "上线：找不到模块回 module.not_found");
    response = Run(() => service.OnlineAsync("Chamber9"));
    Check(response.Success && chamber.Mode == ModuleMode.Online && chamber.CurrentOperation is null, "腔体上线：Mode 变 Online，不挂动作");
    response = Run(() => service.OfflineAsync("Chamber9"));
    Check(response.Success && chamber.Mode == ModuleMode.Offline, "腔体下线：Mode 变 Offline");

    // 5. 设备动作出错：找不到（轴的动作给了气缸路径、不是轴也不是气缸的组件）、参数不对、模块不存在
    response = Run(() => service.CylinderUpAsync(Device("Chamber9.Nope")));
    Check(!response.Success && response.Code == ErrorCodes.ChamberDeviceNotFound
        && response.Args.SequenceEqual(["Chamber9", "Chamber9.Nope"]), "找不到设备：chamber.device_not_found [模块, 路径]");
    response = Run(() => service.CylinderUpAsync(Device("Chamber9.WaferSensor")));
    Check(!response.Success && response.Code == ErrorCodes.ChamberDeviceNotFound, "WaferSensor 不是气缸");
    response = Run(() => service.AxisHomeAsync(Device("Chamber9.Door")));
    Check(!response.Success && response.Code == ErrorCodes.ChamberDeviceNotFound, "门不是轴：轴动作找不到它");
    response = Run(() => service.CylinderDownAsync(Device("Chamber9.Arm1")));
    Check(!response.Success && response.Code == ErrorCodes.ChamberDeviceNotFound, "摆臂不是气缸");
    response = Run(() => service.AxisMoveAsync(new ChamberAxisMoveRequest { Module = "Chamber9", Axis = "Chamber9.Arm1", Position = double.PositiveInfinity }));
    Check(!response.Success && response.Code == ErrorCodes.ChamberDeviceArgsInvalid
        && response.Args.SequenceEqual(["Chamber9.Arm1", "Move"]), "位置不是有限数：chamber.device_args_invalid [路径, 动作]");
    response = Run(() => service.AxisMoveAsync(new ChamberAxisMoveRequest { Module = "Chamber9", Axis = "Chamber9.Arm1", Position = 1, Speed = -5 }));
    Check(!response.Success && response.Code == ErrorCodes.ChamberDeviceArgsInvalid, "速度是负的");
    response = Run(() => service.AxisStepAsync(new ChamberAxisStepRequest { Module = "Chamber9", Axis = "Chamber9.Arm1", Distance = 0 }));
    Check(!response.Success && response.Code == ErrorCodes.ChamberDeviceArgsInvalid
        && response.Args.SequenceEqual(["Chamber9.Arm1", "Step"]), "步距是 0");
    response = Run(() => service.AxisJogAsync(new ChamberAxisJogRequest { Module = "Chamber9", Axis = "Chamber9.SpinMotor", Speed = 0 }));
    Check(!response.Success && response.Code == ErrorCodes.ChamberDeviceArgsInvalid, "点动速度是 0");
    response = Run(() => service.CylinderUpAsync(Device("Chamber9.Door", "Chamber404")));
    Check(!response.Success && response.Code == ErrorCodes.ModuleNotFound, "模块不存在");

    // 6. 指令没发出去（轴没回零不能定位）：直接回码，模块状态不动、不挂操作；路径大小写不论
    Check(chamber.State == ModuleState.NotInit, "还没初始化");
    response = Run(() => service.AxisMoveAsync(new ChamberAxisMoveRequest { Module = "Chamber9", Axis = "chamber9.arm1", Position = 10 }));
    Check(!response.Success && response.Code == ErrorCodes.ChamberDeviceCommandRejected
        && response.Args.SequenceEqual(["chamber9.arm1", "Move"]), "轴没回零不能定位：chamber.device_command_rejected");
    Check(chamber.State == ModuleState.NotInit && chamber.CurrentOperation is null, "指令没发出去不改模块状态、不挂操作");

    // 7. 执行中落 Manual、在途拒绝别的动作、做完回原来的状态
    plc.HoldCylinders = true;
    var opening = Start(() => service.CylinderUpAsync(Device("Chamber9.Door")));
    Tick(3);
    Check(!opening.IsCompleted && chamber.State == ChamberState.Manual, "门在走：模块落 Manual（120），RPC 还在等");
    response = Run(() => service.CylinderUpAsync(Device("Chamber9.Bowl1")));
    Check(!response.Success && response.Code == ErrorCodes.ActionRejected
        && response.Args.SequenceEqual(["Chamber9", ChamberState.Manual.ToString()]), "一个设备动作在途时别的动作被拒");
    Check(!plc.ReadDo(2), "被拒的动作没发指令（Bowl 线圈没通）");
    response = Run(() => service.AxisJogAsync(new ChamberAxisJogRequest { Module = "Chamber9", Axis = "Chamber9.SpinMotor", Speed = 50 }));
    Check(!response.Success && response.Code == ErrorCodes.ActionRejected && !spin.IsBusy, "在途时点动也被拒");
    response = Run(() => service.AxisStopAsync(Device("Chamber9.Arm1")));
    Check(response.Success && chamber.State == ChamberState.Manual, "停止腔体正忙也照发、发出去就回，不动在途的门");
    plc.HoldCylinders = false;
    response = Finish(opening);
    Check(response.Success && chamber.State == ModuleState.NotInit && CylinderDto("Chamber9.Door").Position == CylinderPosition.Opened,
        "门开到位：成功，回到原来的未初始化");

    // 8. Abort 顶替在途的设备动作（平台默认的中止：停液、等轴停下）
    plc.HoldCylinders = true;
    Check(diw.Open() && plc.ReadDo(6), "先开着 DIW");
    var lifting = Start(() => service.CylinderUpAsync(Device("Chamber9.Arm1.Lift")));
    Tick(2);
    response = Run(() => service.AbortAsync("Chamber9"));
    Check(response.Success, "Abort 成功");
    response = Finish(lifting);
    Check(!response.Success && response.Code == ErrorCodes.Aborted, "被 Abort 顶掉的设备动作回 module.action_aborted");
    Check(chamber.State == ModuleState.Idle && lift.ActionState == ActionState.Idle, "Abort 后空闲，Lift 不再等到位");
    Check(!plc.ReadDo(6) && plc.Ao(0) == 0, "中止把喷嘴停液（关阀、流量设定清零）");
    Check(plc.LastCommand("Spin.Command") == MotionCommandId.Stop && plc.LastCommand("Arm.Command") == MotionCommandId.Stop, "中止给每根轴发了停止");
    plc.HoldCylinders = false;
    Tick();

    // 9. 设备做失败（到位超时）：回 chamber.device_action_failed，模块落 Error；Error 下还能手动动设备，做完仍是 Error；复位等每根轴清错
    door.ActionTimeoutMs = 200;
    plc.HoldCylinders = true;
    response = Run(() => service.CylinderDownAsync(Device("Chamber9.Door")));
    Check(!response.Success && response.Code == ErrorCodes.ChamberDeviceActionFailed
        && response.Args.SequenceEqual(["Chamber9.Door", "Down"]), "关门等不到位：chamber.device_action_failed [路径, 动作]");
    Check(chamber.State == ModuleState.Error, "设备动作失败模块落 Error");
    plc.HoldCylinders = false;
    response = Run(() => service.CylinderDownAsync(Device("Chamber9.Door")));
    Check(response.Success && chamber.State == ModuleState.Error, "Error 下手动关门成功，不顺带清错");
    plc.SetAxisError("Arm.Status", true);
    Tick();
    Check(arm.IsError, "摆臂报错");
    response = Run(() => service.ResetAsync("Chamber9"));
    Check(response.Success && chamber.State == ModuleState.Idle && !arm.IsError, "复位：轴发驱动器复位、等清错做完，回空闲");
    Check(plc.LastCommand("Arm.Command") == MotionCommandId.Reset && plc.LastCommand("Spin.Command") == MotionCommandId.Reset, "每根轴都发了复位");

    // 10. 轴真走一遍：回零、移动（带速度 / 速度 0 用 EC）、步进、复位
    response = Run(() => service.AxisHomeAsync(Device("Chamber9.Arm1")));
    Check(response.Success && arm.IsHomed && chamber.State == ModuleState.Idle, "摆臂回零");
    response = Run(() => service.AxisMoveAsync(new ChamberAxisMoveRequest { Module = "Chamber9", Axis = "Chamber9.Arm1", Position = 200, Speed = 20 }));
    Check(response.Success && Near(arm.CurrentPosition, 200) && Reach() == 1 && plc.LastSpeed("Arm.Command") == 20,
        "摆臂移动到 200（= 示教的 Center），Reach = 1，按给的速度走");
    response = Run(() => service.AxisStepAsync(new ChamberAxisStepRequest { Module = "Chamber9", Axis = "Chamber9.Arm1", Distance = -50, Speed = 20 }));
    Check(response.Success && Near(arm.CurrentPosition, 150), "步进 -50 到 150");
    response = Run(() => service.AxisStepAsync(new ChamberAxisStepRequest { Module = "Chamber9", Axis = "Chamber9.Arm1", Distance = 0.5, Speed = 20 }));
    Check(response.Success && Near(arm.CurrentPosition, 150.5), "步距比到位容差（1）还小也照走：相对移动不按\"已经在目标\"省掉");
    response = Run(() => service.AxisMoveAsync(new ChamberAxisMoveRequest { Module = "Chamber9", Axis = "Chamber9.Arm1", Position = 150.2, Speed = 20 }));
    Check(response.Success && Near(arm.CurrentPosition, 150.5), "绝对定位的目标已在到位容差里就不发");
    response = Run(() => service.AxisMoveAsync(new ChamberAxisMoveRequest { Module = "Chamber9", Axis = "Chamber9.Arm1", Position = 0 }));
    Check(response.Success && Near(arm.CurrentPosition, 0) && Reach() == 0 && plc.LastSpeed("Arm.Command") == arm.MoveSpeed,
        "速度给 0 按后端 EC MoveSpeed 走回 0");
    response = Run(() => service.AxisResetAsync(Device("Chamber9.Arm1")));
    Check(response.Success && chamber.State == ModuleState.Idle, "驱动器复位");

    // 11. 点动（按住类）：发起就回、腔体进 Manual；按住期间续；松手发停止，停下来回原状态；松手后再续回 chamber.jog_not_held
    response = Run(() => service.AxisJogAsync(new ChamberAxisJogRequest { Module = "Chamber9", Axis = "Chamber9.SpinMotor", Speed = 50 }));
    Check(response.Success && chamber.State == ChamberState.Manual, "点动发起就回 Ok，腔体进手动中");
    Tick();
    Check(spin.IsBusy && SpinDto().CurrentSpeed == 50, "点动按 50 转起来");
    Check(service.AxisJogRenewAsync(Device("chamber9.spinmotor")).Result.Success, "按住期间续得上（路径大小写不论）");
    response = service.AxisJogRenewAsync(Device("Chamber9.Arm1")).Result;
    Check(!response.Success && response.Code == ErrorCodes.ChamberJogNotHeld
        && response.Args.SequenceEqual(["Chamber9.Arm1", "Jog"]), "续的不是正按着的轴：chamber.jog_not_held");
    response = Run(() => service.CylinderUpAsync(Device("Chamber9.Bowl1")));
    Check(!response.Success && response.Code == ErrorCodes.ActionRejected, "点动按住期间别的普通动作被拒");
    response = Run(() => service.AxisStopAsync(Device("Chamber9.SpinMotor")));
    Check(response.Success, "松手发停止（腔体忙也照发）");
    WaitIdle("松手后等停下就退出手动中");
    Check(chamber.State == ModuleState.Idle && !spin.IsBusy, "停下了，回到原来的空闲");
    Check(!service.AxisJogRenewAsync(Device("Chamber9.SpinMotor")).Result.Success, "松手后再续：没在按住");

    // 12. 没续上（界面断了、客户端退了）：过了 EC HoldTimeoutMs 模块自己发停止
    chamber.HoldTimeoutMs = 500;
    response = Run(() => service.AxisJogAsync(new ChamberAxisJogRequest { Module = "Chamber9", Axis = "Chamber9.SpinMotor", Speed = -50 }));
    Check(response.Success && chamber.State == ChamberState.Manual, "反向点动发起");
    Tick();
    Check(spin.IsBusy && spin.CurrentSpeed < 0, "反向转起来");
    var held = Stopwatch.StartNew();
    WaitIdle("没续上应在保活超时后自己停");
    Check(held.ElapsedMilliseconds >= 400 && !spin.IsBusy && chamber.State == ModuleState.Idle, "过了保活超时（500 ms）自己停，回到空闲");

    // 13. 气缸升降：Lift、Bowl
    response = Run(() => service.CylinderUpAsync(Device("Chamber9.Arm1.Lift")));
    Check(response.Success && CylinderDto("Chamber9.Arm1.Lift").Position == CylinderPosition.Opened, "Lift 升");
    response = Run(() => service.CylinderDownAsync(Device("Chamber9.Arm1.Lift")));
    Check(response.Success && CylinderDto("Chamber9.Arm1.Lift").Position == CylinderPosition.Closed, "Lift 降");
    response = Run(() => service.CylinderUpAsync(Device("Chamber9.Bowl1")));
    Check(response.Success && CylinderDto("Chamber9.Bowl1").Position == CylinderPosition.Opened && chamber.State == ModuleState.Idle, "Bowl 升，做完回空闲");

    // 14. 回零（平台默认）：喷嘴全关 → 卡盘停转 → Lift 升 → 摆臂、卡盘回零 → Bowl 降，门不动；前一段没做完不走下一段
    Check(diw.Open() && sc1.Open(), "回零前开着两路喷嘴");
    SetArm(80);
    Check(door.Open(), "门开着");
    Tick();
    int armCommands = plc.CommandCount("Arm.Command");
    plc.HoldCylinders = true;
    var homing = Start(() => service.HomeAsync("Chamber9"));
    Tick(8);
    Check(chamber.State == ChamberState.Homing && !plc.ReadDo(6) && !plc.ReadDo(7), "第一段：喷嘴全关");
    Check(plc.LastCommand("Spin.Command") == MotionCommandId.Stop, "第二段：卡盘停转");
    Check(plc.ReadDo(4) && !plc.ReadDo(5), "第三段：Lift 升的线圈通了");
    Check(plc.CommandCount("Arm.Command") == armCommands, "Lift 还没升到位，摆臂不回零（免得刮 Bowl 壁）");
    plc.HoldCylinders = false;
    response = Finish(homing);
    Check(response.Success && chamber.State == ModuleState.Idle, "回零做完回空闲");
    Check(plc.LastCommand("Arm.Command") == MotionCommandId.Home && Near(arm.CurrentPosition, 0) && arm.IsHomed, "第四段：摆臂回零");
    Check(plc.LastCommand("Spin.Command") == MotionCommandId.Home && Near(spin.CurrentPosition, 0) && spin.IsHomed,
        "第四段：卡盘（第二段已经停稳）跟摆臂一起回零");
    Check(CylinderDto("Chamber9.Bowl1").Position == CylinderPosition.Closed && CylinderDto("Chamber9.Arm1.Lift").Position == CylinderPosition.Opened,
        "第五段：Bowl 降；Lift 留在升位");
    Check(CylinderDto("Chamber9.Door").Position == CylinderPosition.Opened, "门不动（归站点交互环管）");
    Check(door.Close(), "关门");
    Tick();

    // 14b. 取放准备二（平台默认）：卡盘转到取放位（卡盘 EC TransferPosition）、开门，两样同时发，都到位才算准备好；
    //      卡盘没回零（绝对定位发不出去）就失败，门不开，腔体落 Error
    spin.TransferPosition = 90;
    var transferContext = TransferContext.ForPlace("Robot1", 1, 1);
    Check(chamber.PrepareTransfer(transferContext) is not null && chamber.State == TransferModuleState.PreTransfer, "取放准备一：抢到腔体");
    int transferSpinCommands = plc.CommandCount("Spin.Command");
    Check(chamber.PrepareTransfer2(TransferContext.ForPlace("Robot1", 1, 1)) is null
          && chamber.State == TransferModuleState.PreTransfer && plc.CommandCount("Spin.Command") == transferSpinCommands
          && !plc.ReadDo(0), "同内容的另一轮被公共入口挡住，不发转位或开门指令");
    plc.HoldCylinders = true;
    var prepare = chamber.PrepareTransfer2(transferContext) ?? throw new InvalidOperationException("FAIL: 取放准备二应能发起");
    Check(chamber.PrepareTransfer2(transferContext) is null && !chamber.Transferring(transferContext),
        "本站准备动作执行中不能重复发起或提前进入交互");
    Tick(3);
    Check(plc.LastCommand("Spin.Command") == MotionCommandId.MoveTo && Near(spin.CurrentPosition, 90),
        "卡盘按 EC TransferPosition 转到 90");
    Check(plc.ReadDo(0) && !prepare.IsTerminal && chamber.State == TransferModuleState.PreTransfer,
        "门跟卡盘同时发（开门线圈通了）；门没开到位还不算准备好，机械手不伸手");
    plc.HoldCylinders = false;
    Tick(3);
    Check(prepare.IsSuccess && chamber.State == TransferModuleState.TransferReady
        && CylinderDto("Chamber9.Door").Position == CylinderPosition.Opened, "卡盘到位、门开到位：准备好了");
    Check(chamber.CancelTransfer(transferContext) && chamber.State == ModuleState.Idle && door.Close(), "撤回这一轮，关门");
    Tick();

    var unhomed = plc.Read<MotionPlcToCSharpData>("Spin.Status");
    unhomed.Is_Homed = 0;
    plc.Put("Spin.Status", unhomed);
    Tick();
    transferContext = TransferContext.ForPick("Robot1", 1, 1);
    Check(chamber.PrepareTransfer(transferContext) is not null, "卡盘没回零时再抢腔体");
    prepare = chamber.PrepareTransfer2(transferContext) ?? throw new InvalidOperationException("FAIL: 卡盘没回零也应能发起取放准备二（发了才判失败）");
    Tick(3);
    Check(prepare.Code == ErrorCodes.ChamberDeviceCommandRejected
        && prepare.ErrorArgs.SequenceEqual(["Chamber9.SpinMotor", "Move"]) && chamber.State == ModuleState.Error,
        "卡盘没回零：转不到取放位，准备失败、腔体落 Error");
    Check(!plc.ReadDo(0) && CylinderDto("Chamber9.Door").Position == CylinderPosition.Closed, "卡盘先发、发不出去就不开门");
    unhomed.Is_Homed = 1;
    plc.Put("Spin.Status", unhomed);
    response = Run(() => service.ResetAsync("Chamber9"));
    Check(response.Success && chamber.State == ModuleState.Idle, "复位回空闲");

    // 15. 工艺（平台默认）：按配方快照一步一步做
    spin.MaxSpeed = 3000;
    RecipeStep StepOf(params (string Key, string Value)[] values)
    {
        return new RecipeStep { Values = values.Select(item => new RecipeValue(item.Key, item.Value)).ToList() };
    }

    // 配方快照：超时时间默认 600 s（配方头里的，工艺操作的上限）
    ProcessRequest Recipe(string name, params RecipeStep[] steps)
    {
        return RecipeWithTimeout(name, "600", steps);
    }

    ProcessRequest RecipeWithTimeout(string name, string timeout, params RecipeStep[] steps)
    {
        return new ProcessRequest { RecipeName = name, Recipe = new RecipeData { Name = name, Timeout = timeout, Steps = [.. steps] } };
    }

    // 工艺跑起来，每拍记一下：DIW / SC1 同时开过没有、摆臂到过哪些位置
    var seenPositions = new HashSet<double>();
    bool bothOn = false;
    bool diwOn = false;
    bool sc1On = false;
    int sc1Opens = 0;
    bool sc1Was = false;
    double sc1TargetSeen = 0;
    ModuleOperation RunProcess(ProcessRequest request, Func<bool>? until = null)
    {
        var operation = chamber.StartProcess(request);
        if (operation is null)
        {
            throw new InvalidOperationException("FAIL: 工艺应能发起");
        }

        return Drive(operation, until);
    }

    // 一拍一拍地扫，直到工艺结束（或者 until 成立，停下来看中间状态，之后再接着 Drive）
    ModuleOperation Drive(ModuleOperation operation, Func<bool>? until = null)
    {
        var watch = Stopwatch.StartNew();
        while (!operation.IsTerminal && (until is null || !until()))
        {
            if (watch.ElapsedMilliseconds > 10000)
            {
                throw new InvalidOperationException("FAIL: 工艺应在 10 s 内做完");
            }

            Tick();
            seenPositions.Add(Math.Round(arm.CurrentPosition, 3));
            diwOn |= plc.ReadDo(6);
            sc1On |= plc.ReadDo(7);
            bothOn |= plc.ReadDo(6) && plc.ReadDo(7);
            bool sc1Now = plc.ReadDo(7);
            if (sc1Now && !sc1Was)
            {
                sc1Opens++;
            }

            sc1Was = sc1Now;
            sc1TargetSeen = Math.Max(sc1TargetSeen, sc1Meter.Target);
            Thread.Sleep(5);
        }

        Tick();
        return operation;
    }

    // 两个喷嘴并排装在臂头上：示教位对着先到中心的 DIW，SC1 要喷到同一点轴还得多走 10（EC Offset，现场测的值）
    Check(diw.Offset == 0 && sc1.Offset == 0, "喷嘴偏移默认 0");
    sc1.Offset = 10;
    Check(Near(arm.ToAxisPosition(150, diw), 200) && Near(arm.ToAxisPosition(150, sc1), 210) && Near(arm.ToAxisPosition(0, sc1), 110),
        "换算加上喷嘴的偏移：DIW 中心 200，SC1 中心 210、边缘 110");
    var process = Recipe("Clean",
        StepOf(("Seconds", "0.3"), ("Rpm", "300"), ("Arm", "Arm1"), ("Chemical", "DIW"), ("Flow", "1.5"), ("Mode", "Time"), ("Position", "150")),
        StepOf(("Seconds", "0.5"), ("Rpm", "500"), ("Arm", "Arm1"), ("Chemical", "SC1"), ("Flow", "2"), ("Mode", "Scan"),
            ("Position", "0"), ("ScanTo", "150"), ("ScanSpeed", "15")),
        StepOf(("Seconds", "0.1"), ("Rpm", "500"), ("Arm", "Arm1"), ("Chemical", "SC1"), ("Mode", "Time"), ("Position", "150")),
        StepOf(("Seconds", "0.1"), ("Rpm", "0"), ("Arm", ""), ("Chemical", "")));
    Check(chamber.TodayWafers == 0 && chamber.TotalWafers == 0 && chamber.LastStartedTimestamp is null && chamber.ProcessPercent is null,
        "还没做过工艺：今天、累计都是 0，没有开工时间戳，进度为空");
    long beforeStart = Stopwatch.GetTimestamp();
    int spinCommands = plc.CommandCount("Spin.Command");
    var started = RunProcess(process, () => plc.ReadDo(6));
    Check(chamber.State == ChamberState.Processing && chamber.Recipe == "Clean", "工艺发起：腔体在工艺中，记下配方名");
    Check(CylinderDto("Chamber9.Arm1.Lift").Position == CylinderPosition.Opened && CylinderDto("Chamber9.Bowl1").Position == CylinderPosition.Opened,
        "开喷前 Lift 升、Bowl 升（围住盘面挡液）");
    Check(plc.LastCommand("Spin.Command") == MotionCommandId.Spin && plc.LastSpeed("Spin.Command") == 300 && spin.IsBusy,
        "第 1 步：转速 300 原样给卡盘");
    Check(Near(arm.CurrentPosition, 200) && plc.Ao(0) == 1.5 && !plc.ReadDo(7), "第 1 步：摆臂摆到晶圆中心（示教的 200），DIW 先给流量 1.5 再开阀，SC1 关着");
    var startedAt = chamber.LastStartedTimestamp;
    Check(chamber.TodayWafers == 1 && chamber.TotalWafers == 1 && startedAt is not null && startedAt >= beforeStart && startedAt <= Stopwatch.GetTimestamp(),
        "开工就记一片：今天、累计各 1 片；开工时间戳用开机以来的计时（Stopwatch），不看系统时间");

    // 进度按配方每一步的时间算（0.3 + 0.5 + 0.1 + 0.1 = 1 s）：第 1 步刚开喷不到 30，第 2 步开喷时第 1 步的 0.3 s 算做完
    int? firstStep = chamber.ProcessPercent;
    Check(firstStep is not null && firstStep < 30, $"第 1 步刚开喷：进度不到 30，实际 {firstStep}");
    Drive(started, () => plc.ReadDo(7));
    int? secondStep = chamber.ProcessPercent;
    Check(secondStep is not null && secondStep >= 29 && secondStep < 80, $"第 2 步开喷：进度在 30 上下到 80 之间，实际 {secondStep}");
    Drive(started, () => chamber.ProcessPercent == 99);
    Check(!started.IsTerminal && chamber.ProcessPercent == 99, "步骤都做完、还在收尾：进度停在 99，动作真做完才算满");
    var processed = Drive(started);
    Check(processed.IsSuccess && chamber.State == ModuleState.Idle, "工艺做完回空闲");
    Check(chamber.ProcessPercent is null, "工艺做完：不在做工艺了，进度变空");
    Check(diwOn && sc1On && !bothOn, "DIW、SC1 各喷过，换药液时先关上一路");
    Check(seenPositions.Contains(110) && seenPositions.Contains(210) && !seenPositions.Contains(100),
        "第 2、3 步 SC1：Scan 在位置 0 和到 150 之间来回扫、Time 停在 150，都按 SC1 的偏移多走 10（轴 110 ↔ 210），不按 DIW 的位置");
    Check(plc.Speeds("Arm.Command").Any(speed => Near(speed, 15)), "扫描速度 15 原样给轴（配方速度就是轴的设计速度，不按示教位换算）");
    Check(plc.Ao(1) == 0 && !plc.ReadDo(6) && !plc.ReadDo(7), "做完喷嘴全停、流量设定清零");
    Check(plc.CommandCount("Spin.Command") - spinCommands == 4 && plc.Speeds("Spin.Command").Contains(500),
        "卡盘：第 1 步转 300、第 2 步转着换 500（PLC 报忙也照收）、第 3 步转速一样不重发、第 4 步转速 0 停、收尾再停一次");
    Check(sc1Opens == 1, "第 2、3 步都用 SC1：接着喷，不关了再开");
    Check(sc1TargetSeen == 2 && sc1Meter.Target == 0, "第 2 步 SC1 流量 2 给了流量计当目标；做完停液，目标清零");
    sc1.Offset = 0;
    Check(!plc.OutputWhen("Spin.Command", 500, 6), "换步先关上一步的 DIW 再改转速：卡盘加速时不接着喷上一步的药液");
    Check(Near(arm.CurrentPosition, 0) && CylinderDto("Chamber9.Bowl1").Position == CylinderPosition.Closed && !spin.IsBusy,
        "收尾：摆臂回 Home、卡盘停转、Bowl 降");

    // 工艺出错：转速超出卡盘上限、超过配方头的超时时间；失败后喷嘴停液。
    // 拿不到配方步骤的、Type 跟腔体不一样的、选的摆臂 / 药液这个腔体没有的，起工艺前就挡下，腔体不动
    void ResetChamber()
    {
        var reset = Run(() => service.ResetAsync("Chamber9"));
        Check(reset.Success && chamber.State == ModuleState.Idle, "复位回空闲");
    }

    var noSteps = new ProcessRequest { RecipeName = "NoLibrary" };
    var rejected = chamber.CheckProcess(noSteps);
    Check(rejected is not null && rejected.Code == ErrorCodes.ChamberRecipeStepsMissing && rejected.Args.SequenceEqual(["Chamber9", "NoLibrary"])
        && chamber.StartProcess(noSteps) is null && chamber.State == ModuleState.Idle,
        "没有配方步骤（没装工艺配方库，只有名字）：起工艺前就挡下 chamber.recipe_steps_missing，腔体不动");
    var otherType = Recipe("OtherType", StepOf(("Seconds", "1")));
    otherType.Recipe!.Type = "B";
    rejected = chamber.CheckProcess(otherType);
    Check(rejected is not null && rejected.Code == ErrorCodes.ChamberRecipeOptionMissing && rejected.Args.SequenceEqual(["Chamber9", "OtherType", "Type", "B"]),
        "配方的 Type（B）跟腔体的（没配）不一样：起工艺前就挡下，报字段 Type 和配方的 Type");
    var wrongArm = Recipe("WrongArm", StepOf(("Seconds", "1"), ("Arm", "Arm2")));
    rejected = chamber.CheckProcess(wrongArm);
    Check(rejected is not null && rejected.Code == ErrorCodes.ChamberRecipeOptionMissing && rejected.Args.SequenceEqual(["Chamber9", "WrongArm", "Arm", "Arm2"]),
        "配方选的摆臂这个腔体没有：起工艺前就挡下 chamber.recipe_option_missing（没装工艺配方库，报字段名）");
    rejected = chamber.CheckProcess(Recipe("WrongChemical", StepOf(("Seconds", "1"), ("Arm", "Arm1"), ("Chemical", "HF"))));
    Check(rejected is not null && rejected.Code == ErrorCodes.ChamberRecipeOptionMissing && rejected.Args[2] == "Chemical" && rejected.Args[3] == "HF",
        "这条摆臂上没有这种药液的喷嘴：起工艺前就挡下");
    Check(chamber.StartProcess(wrongArm) is null && chamber.State == ModuleState.Idle, "挡下的不起工艺，腔体不动");
    var failed = RunProcess(Recipe("TooFast", StepOf(("Seconds", "1"), ("Rpm", "5000"))));
    Check(failed.Code == ErrorCodes.ChamberDeviceCommandRejected && failed.ErrorArgs.SequenceEqual(["Chamber9.SpinMotor", "Spin"]),
        "转速超出卡盘 EC MaxSpeed：转速指令发不出去");
    ResetChamber();
    failed = RunProcess(RecipeWithTimeout("TooLong", "1", StepOf(("Seconds", "30"), ("Arm", "Arm1"), ("Chemical", "DIW"), ("Position", "150"))));
    Check(failed.Code == ErrorCodes.ChamberActionTimeout && failed.ErrorArgs.SequenceEqual(["Chamber9", "1000"]),
        "超过配方头的超时时间（1 s）：chamber.action_timeout [模块, ms]");
    Check(!plc.ReadDo(6), "工艺失败先把喷嘴停了");
    ResetChamber();
    var aborting = chamber.StartProcess(Recipe("Abort", StepOf(("Seconds", "30"), ("Arm", "Arm1"), ("Chemical", "DIW"), ("Position", "150"))));
    var watchAbort = Stopwatch.StartNew();
    while (!plc.ReadDo(6) && watchAbort.ElapsedMilliseconds < 5000)
    {
        Tick();
        Thread.Sleep(5);
    }

    Check(aborting is not null && plc.ReadDo(6), "长工艺喷起来了");
    response = Run(() => service.AbortAsync("Chamber9"));
    Check(response.Success && aborting!.State == OperationState.Aborted && chamber.State == ModuleState.Idle, "中止顶掉工艺，回空闲");
    Check(!plc.ReadDo(6) && plc.Ao(0) == 0 && plc.LastCommand("Spin.Command") == MotionCommandId.Stop, "中止停液、停轴");
    Check(chamber.TodayWafers == 4 && chamber.TotalWafers == 4,
        "做成、转速超限、超时、中止的都算开工（4 片）；起工艺前就挡下的不算");

    // 开工片数按天存库：后台写到今天那行；再插一行昨天的，新的计数从库里读回来——今天接着数，累计把各天加起来
    string today = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    bool written = false;
    var waitWrite = Stopwatch.StartNew();
    while (!written && waitWrite.ElapsedMilliseconds < 3000)
    {
        using (var db = XyzDb.Create())
        {
            written = db.Queryable<ProcessDailyCountEntity>().Any(row => row.Station == "Chamber9" && row.Day == today && row.Wafers == 4);
        }

        if (!written)
        {
            Thread.Sleep(20);
        }
    }

    Check(written, "开工片数按天写进库：Chamber9 今天那行 4 片");
    using (var db = XyzDb.Create())
    {
        string yesterday = DateTime.Today.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        db.Insertable(new ProcessDailyCountEntity { Station = "Chamber9", Day = yesterday, Wafers = 5, UpdatedAt = DateTime.Now }).ExecuteCommand();
    }

    var reloaded = new ProcessCounter();
    reloaded.Load("Chamber9", XyzDb.DefaultName);
    Check(reloaded.Today == 4 && reloaded.Total == 9 && reloaded.LastStartedTimestamp is null,
        "重启读回来：今天接着从 4 片数，累计把昨天的 5 片加上（9 片）；开工时间戳只在内存，读回来是空");

    // 写库只给那一天加 1、不写总数：库里今天那行被改成 100（比如时钟往回调到这一天，库里已经有别的数），再开工一片是 101，不会冲成内存里的 5
    using (var db = XyzDb.Create())
    {
        db.Updateable<ProcessDailyCountEntity>()
            .SetColumns(row => new ProcessDailyCountEntity { Wafers = 100 })
            .Where(row => row.Station == "Chamber9" && row.Day == today)
            .ExecuteCommand();
    }

    reloaded.Started();
    bool added = false;
    var waitAdd = Stopwatch.StartNew();
    while (!added && waitAdd.ElapsedMilliseconds < 3000)
    {
        using (var db = XyzDb.Create())
        {
            added = db.Queryable<ProcessDailyCountEntity>().Any(row => row.Station == "Chamber9" && row.Day == today && row.Wafers == 101);
        }

        if (!added)
        {
            Thread.Sleep(20);
        }
    }

    Check(added && reloaded.Today == 5, "写库只给那一天加 1：库里那行 100 变 101，不按内存里的 5 冲掉");

    // 16. 停用的腔体不发设备动作
    var disabled = (SmokeChamber)SmokeIoc.Load([ChamberConfig("Chamber8", enabled: false)]).Single();
    var disabledService = new ChamberService();
    var disabledResponse = disabledService.CylinderUpAsync(Device("Chamber8.Door", "Chamber8")).Result;
    Check(!disabledResponse.Success && disabledResponse.Code == ErrorCodes.ActionRejected, "停用的腔体设备动作被拒");

    // 17. 组件初始化（开机，不动硬件）与模块初始化：腔体开机只占晶圆账的槽位、一路递归到所有设备，不碰任何一根轴；
    //     模块初始化 InitModule 就是 Home（人或调度才调）；停用的腔体连账都不占
    var previousLedger = IocHelper.GetService<ILedgerManagerComponent>();
    var ledger = new LedgerManagerComponent();
    SmokeIoc.Use<ILedgerManagerComponent>(ledger);
    try
    {
        var armBefore = arm.ActionState;
        var spinBefore = spin.ActionState;
        Check(chamber.InitComponent(), "腔体的组件初始化应成功（设备都没有自己的开机动作）");
        Check(ledger.IsRegistered("Chamber9") && ledger.GetSlots("Chamber9").Count == chamber.SlotCount,
            "腔体的组件初始化登记了晶圆账槽位");
        Check(arm.ActionState == armBefore && spin.ActionState == spinBefore && chamber.CurrentOperation is null,
            "组件初始化一路递归下去也不动轴：摆臂和卡盘的动作状态没变，腔体没挂操作");
        Check(disabled.InitComponent() && !ledger.IsRegistered("Chamber8"), "停用的腔体：组件初始化连账都不占");

        var fresh = (SmokeChamber)SmokeIoc.Load([ChamberConfig("Chamber7", enabled: true)]).Single();
        var home = fresh.InitModule();
        Check(home is not null && fresh.State == ChamberState.Homing, "腔体的模块初始化 InitModule = Home：回原点操作挂上，进 Homing");
    }
    finally
    {
        SmokeIoc.Use<ILedgerManagerComponent>(previousLedger);
    }

    // 18. 部件寿命：寿命组件挂在气缸下面、盯它开侧的 DO 8，气缸本身一行不改；DO 每变一次算一次（断→通、通→断各一次），
    //     读不到不算、重新读到那一拍只记下；到警告次数报警告、到报警次数报报警；已用次数按"加一"写库、开机读回接着数，清零连库一起清
    var previousAlarm = IocHelper.GetService<IAlarmComponent>();
    var alarms = new AlarmComponent();
    SmokeIoc.Use<IAlarmComponent>(alarms);
    try
    {
        var gateConfig = With(Node("Gate", typeof(CylinderComponent), Value("DoOpenIndex", "8"), Value("DoCloseIndex", "9")),
            Node("Lifetime", typeof(LifetimeProbe), Value("DoIndex", "8")));
        var gate = (CylinderComponent)SmokeIoc.Load([gateConfig]).Single();
        var life = Find<LifetimeProbe>(gate, "Lifetime");
        ComponentLoader.BindAll([gate]);
        Check(ReferenceEquals(IocHelper.GetServices<ILifetime>().Single(), life) && life.FullPath == "Gate.Lifetime",
            "寿命组件按 ILifetime 找得到，路径挂在气缸下面");

        bool Active(string code)
        {
            return alarms.ActiveAlarms.Any(alarm => alarm.SourcePath == life.FullPath && alarm.AlarmCode == code);
        }

        // 库里那行等后台写到这个数
        bool WaitLifetime(long expected)
        {
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < 3000)
            {
                using (var db = XyzDb.Create())
                {
                    if (db.Queryable<ComponentLifetimeEntity>().Any(row => row.Path == "Gate.Lifetime" && row.UsedCount == expected))
                    {
                        return true;
                    }
                }

                Thread.Sleep(20);
            }

            return false;
        }

        life.WarnCount = 2;
        life.AlarmCount = 3;
        life.Tick();
        Check(life.UsedCount == 0, "第一拍只记下 DO 现在的样子，不算");
        Check(gate.Open(), "开气缸");
        life.Tick();
        Check(life.UsedCount == 1 && !Active(life.WarnAlarm), "开：开侧 DO 断→通，算一次；没到警告次数不报");
        Check(gate.Open(), "已经开着再发开");
        life.Tick();
        Check(life.UsedCount == 1, "DO 没变，不算");
        Check(gate.Close(), "关气缸");
        life.Tick();
        Check(life.UsedCount == 2, "关：开侧 DO 通→断，也算一次");
        var warn = alarms.ActiveAlarms.SingleOrDefault(alarm => alarm.SourcePath == life.FullPath && alarm.AlarmCode == life.WarnAlarm);
        Check(warn is not null && warn.Level == AlarmLevel.Warn && !Active(life.ExpiredAlarm), "到了警告次数（2）报警告级报警，还没到报警次数");

        plc.IsConnected = false;
        life.Tick();
        plc.IsConnected = true;
        plc.WriteDo(8, true);
        life.Tick();
        Check(life.UsedCount == 2, "读不到的那拍不算；重新读到的那一拍只记下，中间变过也不补");
        plc.WriteDo(8, false);
        life.Tick();
        var expired = alarms.ActiveAlarms.SingleOrDefault(alarm => alarm.SourcePath == life.FullPath && alarm.AlarmCode == life.ExpiredAlarm);
        Check(life.UsedCount == 3 && expired is not null && expired.Level == AlarmLevel.Alarm1, "到了报警次数（3）报寿命到期报警");

        alarms.Reset(life.FullPath);
        Check(!Active(life.WarnAlarm) && !Active(life.ExpiredAlarm), "人工复位两条都清掉");
        life.Tick();
        Check(Active(life.ExpiredAlarm) && !Active(life.WarnAlarm), "还到着就再报；到了报警次数只报报警，不再叠一条警告");
        Check(WaitLifetime(3), "已用次数写进库：Gate.Lifetime 那行 3 次");

        // 重启：同一棵树再装一遍，开机读回接着数
        var reloadedGate = (CylinderComponent)SmokeIoc.Load([gateConfig]).Single();
        var reloadedLife = Find<LifetimeProbe>(reloadedGate, "Lifetime");
        ComponentLoader.BindAll([reloadedGate]);
        Check(reloadedLife.UsedCount == 3, "重启读回来：从 3 次接着数");

        // 写库只给那行加一、不写总数：库里那行被改成 100，再变一次是 101，不按内存里的 4 冲掉
        using (var db = XyzDb.Create())
        {
            db.Updateable<ComponentLifetimeEntity>()
                .SetColumns(row => new ComponentLifetimeEntity { UsedCount = 100 })
                .Where(row => row.Path == "Gate.Lifetime")
                .ExecuteCommand();
        }

        reloadedLife.Tick();
        plc.WriteDo(8, true);
        reloadedLife.Tick();
        Check(reloadedLife.UsedCount == 4 && WaitLifetime(101), "写库只给那行加一：库里 100 变 101，内存里是 4");

        reloadedLife.Clear();
        Check(reloadedLife.UsedCount == 0 && WaitLifetime(0), "换件清零：内存和库里都是 0");
        alarms.Reset(reloadedLife.FullPath);
        reloadedLife.AlarmCount = 0;
        reloadedLife.WarnCount = 0;
        reloadedLife.Tick();
        Check(!Active(reloadedLife.WarnAlarm) && !Active(reloadedLife.ExpiredAlarm), "次数配 0 不管，不报");
    }
    finally
    {
        SmokeIoc.Use<IAlarmComponent>(previousAlarm);
    }

    Console.WriteLine($"PASS: {checks} chamber checks (devices from sc: door, bowls, spin, swing arms with their lift and nozzles, push shaped like sc, "
        + "cylinder tri-state, nozzle flow AO, spin / arm values, push on change with retained replay, device actions: not found, invalid args, "
        + "command rejected, Manual state, busy rejection, stop while busy, abort replacement, failure to Error, axis end to end, "
        + "jog hold / renew / release, hold timeout auto stop, cylinders up / down; platform Home order (spin homed too), "
        + "prepare transfer 2 (spin to transfer position + door, unhomed spin fails), Reset, Abort; "
        + "process by recipe: lift / bowl up, rpm, arm to wafer position, nozzle by chemical with flow, Time / Scan, finish, "
        + "missing steps, recipe mismatch, rpm over limit, timeout, abort; process starts counted per day in the database (adding one to the day, "
        + "never overwriting it) and read back, start timestamps on the monotonic clock, "
        + "process percent by recipe step times; disabled chamber, component init and module init = Home; "
        + "part lifetime: counting every change of a DO, warn / alarm counts, stored by adding one and read back, clear on part change)");
    return 0;
}
finally
{
    SmokeIoc.Use<IPlcIo>(previousPlcIo);
    SmokeIoc.Use<IPlcBlock>(previousPlcBlock);
    SmokeIoc.Use<IIoComponent>(previousIo);
    SmokeIoc.Use<IEcComponent>(previousEc);
    Directory.Delete(directory, recursive: true);
    try
    {
        File.Delete(countDb);
    }
    catch (IOException)
    {
        // SQLite 连接池可能还占着文件；在系统临时目录里，留着不碍事
    }
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

// 到位反馈 / 流量开关：一个 DI 组件子节点（高电平触发、不自己报警）
static ModuleConfig Feedback(string name, int di)
{
    return Node(name, typeof(DiSensorComponent), Value("DiIndex", di.ToString()), Value("TriggerValue", "true"), Value("AlarmEnabled", "False"));
}

static ModuleConfig With(ModuleConfig node, params ModuleConfig[] children)
{
    node.Children = [.. children];
    return node;
}

static ModuleConfig ChamberConfig(string name, bool enabled)
{
    // SC1 下面挂流量开关（Feedback，DI 7）和流量计（监控使能 DO 填它的阀 DO 7）；DIW 都不挂，当没流量开关、没流量计的一路
    var sc1Node = With(Node("Nozzle_SC1", typeof(NozzleComponent), Value("DoIndex", "7"), Value("NormalState", "NC"), Value("Chemical", "SC1"), Value("AoIndex", "1")),
        Feedback("Feedback", 7),
        Node("Flow", typeof(AiSensorComponent), Value("AiIndex", "0"), Value("MonitoringDoIndex", "7"), Value("IsPercentOfTarget", "True")));
    var arm = With(Node("Arm1", typeof(SwingArmComponent), Value("SendPlcDataPath", "Arm.Command"), Value("ReceivePlcDataPath", "Arm.Status")),
        With(Node("Lift", typeof(ChamberLiftComponent), Value("DoOpenIndex", "4"), Value("DoCloseIndex", "5")),
            Feedback("Opened", 4), Feedback("Closed", 5)),
        Node("Nozzle_DIW", typeof(NozzleComponent), Value("DoIndex", "6"), Value("NormalState", "NC"), Value("Chemical", "DIW"), Value("AoIndex", "0")),
        sc1Node);
    return With(Node(name, typeof(SmokeChamber), Value("IsEnable", enabled.ToString())),
        With(Node("Door", typeof(ChamberDoorComponent), Value("DoOpenIndex", "0"), Value("DoCloseIndex", "1")),
            Feedback("Opened", 0), Feedback("Closed", 1)),
        With(Node("Bowl1", typeof(ChamberBowlComponent), Value("DoOpenIndex", "2"), Value("DoCloseIndex", "3")),
            Feedback("Opened", 2), Feedback("Closed", 3)),
        Node("SpinMotor", typeof(SpinMotorComponent), Value("SendPlcDataPath", "Spin.Command"), Value("ReceivePlcDataPath", "Spin.Status")),
        arm,
        Node("WaferSensor", typeof(DiSensorComponent), Value("DiIndex", "8"), Value("AlarmEnabled", "False")));
}

/// <summary>冒烟用腔体：跟机型一样什么都不写，回零、复位、中止、工艺全用平台的；Tick 由冒烟线程手动走一拍。</summary>
[Component(description: "冒烟用腔体")]
public sealed class SmokeChamber : BaseChamberModule
{
    public void Tick()
    {
        OnScan();
    }
}

/// <summary>
/// 冒烟用寿命组件：读真 IO 组件，Tick 由冒烟线程手动走一拍
/// </summary>
[Component(description: "冒烟用寿命组件")]
public sealed class LifetimeProbe : LifetimeComponent
{
    public void Tick()
    {
        OnScan();
    }
}

/// <summary>只实现气缸接口的探针，不借用气缸具体类，动作立即完成。</summary>
public abstract class InterfaceCylinderProbe : ComponentBase, ICylinder
{
    private bool _opened;

    /// <summary>开侧到位。</summary>
    public bool IsOpened => _opened;

    /// <summary>关侧到位。</summary>
    public bool IsClosed => !_opened;

    /// <summary>当前到位状态。</summary>
    public TwoStatePosition Position => _opened ? TwoStatePosition.Opened : TwoStatePosition.Closed;

    /// <summary>直接模拟开到位。</summary>
    public bool Open()
    {
        _opened = true;
        return true;
    }

    /// <summary>直接模拟关到位。</summary>
    public bool Close()
    {
        _opened = false;
        return true;
    }
}

/// <summary>腔门的另一种实现，证明模块按接口而不是具体类执行。</summary>
[Component(description: "接口腔门探针")]
public sealed class InterfaceDoorProbe : InterfaceCylinderProbe, IChamberDoor
{
}

/// <summary>接液杯的另一种实现，证明整腔操作按接口控制升降。</summary>
[Component(description: "接口接液杯探针")]
public sealed class InterfaceBowlProbe : InterfaceCylinderProbe, IChamberBowl
{
    /// <summary>模拟升到位。</summary>
    public bool Up()
    {
        return Open();
    }

    /// <summary>模拟降到位。</summary>
    public bool Down()
    {
        return Close();
    }
}

/// <summary>
/// 假 PLC：DI / DO / AO 三张表、轴数据块存字典。Step() 模拟一拍设备：气缸照线圈走到位（HoldCylinders 时不动）、
/// 带反馈的阀照输出给反馈、轴照新命令回零 / 定位 / 步进 / 点动 / 转 / 停 / 复位。冒烟线程和 RPC 线程都会碰它，全部加锁。
/// 每条写进命令块的轴指令都记下来（指令码、速度），检查先后、有没有重发。
/// </summary>
public sealed class FakePlc : IPlcIo, IPlcBlock
{
    private readonly object _gate = new();
    private readonly Dictionary<int, bool> _di = [];
    private readonly Dictionary<int, bool> _do = [];
    private readonly Dictionary<int, double> _ao = [];
    private readonly Dictionary<string, byte[]> _blocks = [];
    private readonly Dictionary<string, ulong> _handledSync = [];
    private readonly List<(string Command, string Status)> _axes = [];
    private readonly List<(string Path, MotionCommandId Command, double Speed, Dictionary<int, bool> Outputs)> _commands = [];
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

    /// <summary>AO 现在的输出；没写过是 -1。</summary>
    public double Ao(int index)
    {
        lock (_gate)
        {
            return _ao.TryGetValue(index, out double value) ? value : -1;
        }
    }

    /// <summary>轴报错 / 清错（驱动器报警）。</summary>
    public void SetAxisError(string statusPath, bool error)
    {
        var status = Read<MotionPlcToCSharpData>(statusPath);
        status.Is_Err = error ? (byte)1 : (byte)0;
        Put(statusPath, status);
    }

    public MotionCommandId? LastCommand(string path)
    {
        lock (_gate)
        {
            var found = _commands.LastOrDefault(item => item.Path == path);
            return found.Path is null ? null : found.Command;
        }
    }

    public double LastSpeed(string path)
    {
        lock (_gate)
        {
            return _commands.Last(item => item.Path == path).Speed;
        }
    }

    public IReadOnlyList<double> Speeds(string path)
    {
        lock (_gate)
        {
            return _commands.Where(item => item.Path == path).Select(item => item.Speed).ToList();
        }
    }

    /// <summary>第一条这个速度的指令发出那一刻，某个 DO 通没通。</summary>
    public bool OutputWhen(string path, double speed, int index)
    {
        lock (_gate)
        {
            return _commands.First(item => item.Path == path && item.Speed == speed).Outputs.GetValueOrDefault(index);
        }
    }

    public int CommandCount(string path)
    {
        lock (_gate)
        {
            return _commands.Count(item => item.Path == path);
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
        lock (_gate)
        {
            value = _ao.GetValueOrDefault(index);
            return _connected;
        }
    }

    public bool WriteAo(int index, double value)
    {
        lock (_gate)
        {
            if (!_connected)
            {
                return false;
            }

            _ao[index] = value;
            return true;
        }
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
            if (_handledSync.ContainsKey(path))
            {
                var command = MemoryMarshal.Read<MotionCSharpToPlcCommand>(data);
                _commands.Add((path, (MotionCommandId)command.Axis_Command, command.Param2, new Dictionary<int, bool>(_do)));
            }

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

                    case MotionCommandId.MoveBy:
                        status.Current_Position += command.Param1;
                        status.Is_In_Position = 1;
                        status.Is_Stopped = 1;
                        break;

                    // 跟仿真器一样：连续转、点动期间 PLC 报忙
                    case MotionCommandId.Spin:
                    case MotionCommandId.Jog:
                        status.Current_Speed = command.Param2;
                        status.Is_Stopped = 0;
                        status.Is_Busy = 1;
                        break;

                    case MotionCommandId.Stop:
                    case MotionCommandId.EStop:
                        status.Current_Speed = 0;
                        status.Is_Stopped = 1;
                        status.Is_Busy = 0;
                        status.Is_Servo_On = command.Axis_Servo;
                        break;

                    case MotionCommandId.Reset:
                        status.Is_Err = 0;
                        break;
                }

                byte[] bytes = new byte[Marshal.SizeOf<MotionPlcToCSharpData>()];
                MemoryMarshal.Write(bytes, in status);
                _blocks[statusPath] = bytes;
            }
        }
    }
}
