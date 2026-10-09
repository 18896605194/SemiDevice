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
using xyz.Modules;
using xyz.Modules.Enums;
using xyz.Service;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Tools;

// 腔体部件冒烟：部件清单照 sc 生成（标了 [PartKind] 的组件、[LiveValue] 的数据），气缸三态、喷嘴、旋转电机、摆臂 Reach，
// 有变化才推；部件手动动作（找不到、没有这个动作、参数不对、指令没发出去、执行中 Manual、在途拒绝、Abort 顶替、失败、停用、
// 停止类腔体忙也照发、点动按住 / 续 / 松手 / 没续上自己停，以及各部件真走一遍）。
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
    var bowl = Find<CylinderComponent>(chamber, "Bowl1");
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

    PartActionRequest Request(string module, string part, string action, string[] args)
    {
        return new PartActionRequest { Module = module, Part = part, Action = action, Args = [.. args] };
    }

    RpcResponse ActIn(string module, string part, string action, params string[] args)
    {
        return Finish(Start(() => service.PartActionAsync(Request(module, part, action, args))));
    }

    RpcResponse Act(string part, string action, params string[] args)
    {
        return ActIn("Chamber9", part, action, args);
    }

    RpcResponse Renew(string part, string action)
    {
        return service.RenewPartActionAsync(Request("Chamber9", part, action, [])).Result;
    }

    // 推送里某个部件的某项数据（原文）
    string Live(string path, string name)
    {
        var part = chamber.CreatePartsDto().Find(path);
        if (part is null)
        {
            throw new InvalidOperationException($"FAIL: 部件快照里应有 {path}");
        }

        return part.Get(name);
    }

    double Reach()
    {
        return double.Parse(Live("Chamber9.Arm1", "Reach"), CultureInfo.InvariantCulture);
    }

    double EdgeReach()
    {
        return double.Parse(Live("Chamber9.Arm1", "EdgeReach"), CultureInfo.InvariantCulture);
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

    // 1. 部件清单：sc 里标了种类的组件按 sc 先后（先父后子），种类、类名、数据都是组件自己声明的；没标种类的（WaferSensor）不算部件
    var parts = chamber.CreatePartsDto();
    Check(parts.Module == "Chamber9", "部件快照带模块名");
    Check(parts.Parts.Select(part => part.Path).SequenceEqual(["Chamber9.Door", "Chamber9.Bowl1", "Chamber9.SpinMotor", "Chamber9.Arm1",
        "Chamber9.Arm1.Lift", "Chamber9.Arm1.Nozzle_DIW", "Chamber9.Arm1.Nozzle_SC1"]), "部件照 sc 先后，WaferSensor 没标种类不在里面");
    var doorDto = parts.Find("chamber9.door");
    var armDto = parts.Find("Chamber9.Arm1");
    var spinDto = parts.Find("Chamber9.SpinMotor");
    var nozzleDto = parts.Find("Chamber9.Arm1.Nozzle_DIW");
    Check(doorDto is not null && doorDto.Kind == "TwoState" && doorDto.Type == "CylinderComponent", "门：双作用气缸，类名照组件类，按路径找忽略大小写");
    Check(armDto is not null && armDto.Kind == "Axis" && armDto.Type == "ArmAxisComponent", "摆臂：轴，类名 ArmAxisComponent（三维图据此认摆臂）");
    Check(spinDto is not null && spinDto.Kind == "Axis" && spinDto.Type == "SpinMotorComponent", "旋转电机：轴，类名 SpinMotorComponent");
    Check(nozzleDto is not null && nozzleDto.Kind == "OneState" && nozzleDto.Type == "NozzleComponent", "喷嘴：单线圈阀");
    string[] axisValues = ["HasPlcData", "CurrentPosition", "CurrentSpeed", "IsHomed", "IsInPosition", "IsBusy", "IsServoOn", "IsError"];
    Check(axisValues.All(name => armDto!.Values.ContainsKey(name) && spinDto!.Values.ContainsKey(name)), "轴都推位置、速度和五盏灯");
    Check(armDto!.Values.ContainsKey("Reach") && armDto.Values.ContainsKey("EdgeReach") && !spinDto!.Values.ContainsKey("Reach"),
        "摆臂另外推 Reach / EdgeReach，旋转电机没有");
    Check(spinDto!.Values.ContainsKey("IsSpinning") && doorDto!.Values.Keys.SequenceEqual(["Position"])
        && nozzleDto!.Values.Keys.SequenceEqual(["IsOn"]), "旋转电机推 IsSpinning；气缸只推 Position，喷嘴只推 IsOn");
    Check(armDto.Get("HasPlcData") == "True" && armDto.Get("CurrentPosition") == "0" && armDto.Get("IsError") == "False",
        "值是不变区域性字符串：布尔 True / False，数字没有多余的小数");

    // 2. 气缸三态：命令发到哪一侧就看那一侧到没到位，没到是未知；两个线圈都没通只看到位反馈；PLC 断了未知
    Check(Live("Chamber9.Door", "Position") == "Closed", "刚上电线圈都没通、关到位信号亮：关到位");
    plc.HoldCylinders = true;
    Check(door.Open(), "直接开门，线圈写得进去");
    Check(Live("Chamber9.Door", "Position") == "Unknown", "命令发到开侧、开到位信号还没亮：未知（关到位信号还亮着也算未知）");
    plc.HoldCylinders = false;
    Tick();
    Check(Live("Chamber9.Door", "Position") == "Opened", "开到位信号亮了：开到位");
    plc.WriteDo(0, false);
    Check(Live("Chamber9.Door", "Position") == "Opened", "两个线圈都断了只看到位反馈：还是开到位");
    Check(door.Close(), "直接关门");
    Tick();
    Check(Live("Chamber9.Door", "Position") == "Closed", "关到位");
    plc.IsConnected = false;
    Check(Live("Chamber9.Bowl1", "Position") == "Unknown", "PLC 断了：未知");
    plc.IsConnected = true;
    Tick(2);

    // 3. 喷嘴、旋转电机、摆臂 Reach
    Check(diw.On() && Live("Chamber9.Arm1.Nozzle_DIW", "IsOn") == "True", "不接反馈的喷嘴按输出回读算出液");
    Check(sc1.On() && Live("Chamber9.Arm1.Nozzle_SC1", "IsOn") == "False", "接了流量开关的喷嘴等反馈");
    Tick();
    Check(Live("Chamber9.Arm1.Nozzle_SC1", "IsOn") == "True", "反馈来了算出液");
    Check(diw.Off() && sc1.Off(), "关喷嘴");
    Tick();
    Check(Live("Chamber9.Arm1.Nozzle_DIW", "IsOn") == "False" && Live("Chamber9.Arm1.Nozzle_SC1", "IsOn") == "False", "都停液");

    SetSpinSpeed(50);
    Check(Live("Chamber9.SpinMotor", "IsSpinning") == "True" && Live("Chamber9.SpinMotor", "CurrentSpeed") == "50",
        "实际转速超出速度容差算在转，正负照推（界面按正负画转向）");
    SetSpinSpeed(-50);
    Check(Live("Chamber9.SpinMotor", "IsSpinning") == "True" && Live("Chamber9.SpinMotor", "CurrentSpeed") == "-50", "反转");
    SetSpinSpeed(2);
    Check(Live("Chamber9.SpinMotor", "IsSpinning") == "False", "速度容差以内不算在转");
    SetSpinSpeed(0);

    // 摆臂：回零后 0 位 = Home；示教位默认 Edge = 0（第一个边缘，跟 Home 重合）、Center = 150（晶圆中心）
    Check(Near(arm.Center, 150) && Near(arm.Edge, 0), "示教位默认 Center = 150、Edge = 0（轴位置按晶圆坐标走）");
    Check(Reach() == 0 && EdgeReach() == 0, "0 位是 Home；默认 Edge 跟 Home 重合，不分段");
    SetArm(75);
    Check(Near(Reach(), 0.5), "默认示教位下按 Center 线性换算：75 / 150 = 0.5");
    // 示教后（实际轴位置）：Edge = 100、Center = 200
    arm.Edge = 100;
    arm.Center = 200;
    SetArm(100);
    Check(Near(Reach(), 0.5) && Near(EdgeReach(), 0.5), "示教后轴在 Edge：Reach = 100 / 200，边缘在 Reach 上的位置 = Edge / Center");
    SetArm(150);
    Check(Near(Reach(), 0.75), "边缘到中心之间按轴位置线性：150 / 200");
    SetArm(150.0004);
    Check(Live("Chamber9.Arm1", "Reach") == "0.75" && Live("Chamber9.Arm1", "CurrentPosition") == "150",
        "推的值按 3 位小数取整：编码器在这一位以下抖不变");
    SetArm(152);
    Check(Near(Reach(), 0.76), "动了就跟着变");
    plc.IsConnected = false;
    Tick();
    Check(!arm.HasPlcData && Near(Reach(), 0.76) && Live("Chamber9.Arm1", "HasPlcData") == "False",
        "PLC 断了 Reach 停在断线前的值，HasPlcData 推 False（界面位置显示\"—\"）");
    plc.IsConnected = true;
    Tick(2);
    arm.Edge = 250;
    Check(EdgeReach() == 0, "Edge 不在 Home 和 Center 之间（示教错了）不分段");
    arm.Edge = 100;
    arm.Center = 0;
    SetArm(0);
    Check(Reach() == 0 && EdgeReach() == 0, "Center 被改到跟 0 位分不开：0 位算 Home，不分段");
    SetArm(10);
    Check(Reach() == 1, "Center 跟 0 位分不开时离开 0 位就算工艺位（只分两档）");
    arm.Center = 200;
    SetArm(0);
    Check(Reach() == 0 && Near(EdgeReach(), 0.5), "回到 0 位，示教位恢复");

    // 4. 推送：有变化才推；跟 ChamberDto 同 token、类型不同，各自留存互不覆盖
    int pushes = 0;
    ModulePartsDto? pushed = null;
    using var partsSubscription = EventBus.Register<ModulePartsDto>("Chamber9", dto =>
    {
        pushes++;
        pushed = dto;
    });
    ChamberDto? state = null;
    using var stateSubscription = EventBus.Register<ChamberDto>("Chamber9", dto => state = dto);
    Check(pushes == 1 && pushed is not null && pushed.Module == "Chamber9", "部件推送留存：订上就补发最后一条");
    Check(state is not null && state.Name == "Chamber9", "模块状态也还在：两种 DTO 同 token 不互相覆盖");
    Tick(3);
    Check(pushes == 1, "没变化不推");
    SetArm(0.0003);
    Check(pushes == 1, "变化在推的小数位以下不推");
    Check(bowl.Open(), "升 Bowl");
    Tick();
    Check(pushes == 2 && pushed is not null && pushed.Find("Chamber9.Bowl1")?.Get("Position") == "Opened", "变了推一次");
    Check(bowl.Close(), "降 Bowl");
    Tick();

    // 5. 部件动作出错：找不到部件（不是部件的组件也算找不到）、没有这个动作、参数不对、模块不存在
    var response = Act("Chamber9.Nope", "Open");
    Check(!response.Success && response.Code == ErrorCodes.ChamberPartNotFound
        && response.Args.SequenceEqual(["Chamber9", "Chamber9.Nope"]), "找不到部件：chamber.part_not_found [模块, 路径]");
    response = Act("Chamber9.WaferSensor", "Open");
    Check(!response.Success && response.Code == ErrorCodes.ChamberPartNotFound, "没标种类的组件不是部件");
    response = Act("Chamber9.Door", "Center");
    Check(!response.Success && response.Code == ErrorCodes.ChamberPartActionUnsupported
        && response.Args.SequenceEqual(["Chamber9.Door", "Center"]), "门上没有 Center：chamber.part_action_unsupported [路径, 动作]");
    response = Act("Chamber9.Arm1", "Spin", "10");
    Check(!response.Success && response.Code == ErrorCodes.ChamberPartActionUnsupported, "没标 [ManualAction] 的方法不能调（轴的 Spin）");
    response = Act("Chamber9.Arm1", "MoveTo", "abc");
    Check(!response.Success && response.Code == ErrorCodes.ChamberPartActionArgsInvalid
        && response.Args.SequenceEqual(["Chamber9.Arm1", "MoveTo"]), "数字写错：chamber.part_action_args_invalid [路径, 动作]");
    response = Act("Chamber9.Arm1", "MoveTo");
    Check(!response.Success && response.Code == ErrorCodes.ChamberPartActionArgsInvalid, "必填参数没给");
    response = Act("Chamber9.Arm1", "MoveTo", "1", "2", "3");
    Check(!response.Success && response.Code == ErrorCodes.ChamberPartActionArgsInvalid, "参数给多了");
    response = Act("Chamber9.Arm1", "MoveTo", "1e999");
    Check(!response.Success && response.Code == ErrorCodes.ChamberPartActionArgsInvalid, "不是有限数");
    response = ActIn("Chamber404", "Chamber9.Door", "Open");
    Check(!response.Success && response.Code == ErrorCodes.ModuleNotFound, "模块不存在");

    // 6. 指令没发出去（轴没回零不能定位）：直接回码，模块状态不动、不挂操作；动作名照方法名，大小写不论
    Check(chamber.State == ModuleState.NotInit, "还没初始化");
    response = Act("Chamber9.Arm1", "moveto", "10");
    Check(!response.Success && response.Code == ErrorCodes.ChamberPartCommandRejected
        && response.Args.SequenceEqual(["Chamber9.Arm1", "moveto"]), "轴没回零不能定位：chamber.part_command_rejected");
    Check(chamber.State == ModuleState.NotInit && chamber.CurrentOperation is null, "指令没发出去不改模块状态、不挂操作");

    // 7. 执行中落 Manual、在途拒绝别的动作、做完回原来的状态
    plc.HoldCylinders = true;
    var opening = Start(() => service.PartActionAsync(Request("Chamber9", "Chamber9.Door", "Open", [])));
    Tick(3);
    Check(!opening.IsCompleted && chamber.State == ChamberState.Manual, "门在走：模块落 Manual（120），RPC 还在等");
    response = Act("Chamber9.Bowl1", "Open");
    Check(!response.Success && response.Code == ErrorCodes.ActionRejected
        && response.Args.SequenceEqual(["Chamber9", ChamberState.Manual.ToString()]), "一个部件动作在途时别的动作被拒");
    Check(!plc.ReadDo(2), "被拒的动作没发指令（Bowl 线圈没通）");
    response = Act("Chamber9.SpinMotor", "Jog", "50");
    Check(!response.Success && response.Code == ErrorCodes.ActionRejected && !spin.IsSpinning, "在途时点动也被拒");
    response = Act("Chamber9.Arm1", "Stop");
    Check(response.Success && chamber.State == ChamberState.Manual, "停止类腔体正忙也照发、发出去就回，不动在途的门");
    plc.HoldCylinders = false;
    response = Finish(opening);
    Check(response.Success && chamber.State == ModuleState.NotInit && Live("Chamber9.Door", "Position") == "Opened",
        "门开到位：成功，回到原来的未初始化");

    // 8. Abort 顶替在途的部件动作
    plc.HoldCylinders = true;
    var lifting = Start(() => service.PartActionAsync(Request("Chamber9", "Chamber9.Arm1.Lift", "Open", [])));
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
    response = Act("Chamber9.Door", "Close");
    Check(!response.Success && response.Code == ErrorCodes.ChamberPartActionFailed
        && response.Args.SequenceEqual(["Chamber9.Door", "Close"]), "关门等不到位：chamber.part_action_failed [路径, 动作]");
    Check(chamber.State == ModuleState.Error, "部件动作失败模块落 Error");
    plc.HoldCylinders = false;
    response = Act("Chamber9.Door", "Close");
    Check(response.Success && chamber.State == ModuleState.Error, "Error 下手动关门成功，不顺带清错");
    response = Finish(Start(() => service.ResetAsync("Chamber9")));
    Check(response.Success && chamber.State == ModuleState.Idle, "复位回空闲");

    // 10. 轴真走一遍：回零、移动（带速度 / 速度空着用 EC）、步进、复位
    response = Act("Chamber9.Arm1", "Home");
    Check(response.Success && arm.IsHomed && chamber.State == ModuleState.Idle, "摆臂回零");
    response = Act("Chamber9.Arm1", "MoveTo", "200", "20");
    Check(response.Success && Near(arm.CurrentPosition, 200) && Reach() == 1, "摆臂移动到 200（= 示教的 Center），Reach = 1");
    response = Act("Chamber9.Arm1", "MoveBy", "-50", "20");
    Check(response.Success && Near(arm.CurrentPosition, 150), "步进 -50 到 150");
    response = Act("Chamber9.Arm1", "MoveBy", "0.5", "20");
    Check(response.Success && Near(arm.CurrentPosition, 150.5), "步距比到位容差（1）还小也照走：相对移动不按\"已经在目标\"省掉");
    response = Act("Chamber9.Arm1", "MoveTo", "150.2", "20");
    Check(response.Success && Near(arm.CurrentPosition, 150.5), "绝对定位的目标已在到位容差里就不发");
    response = Act("Chamber9.Arm1", "MoveTo", "0", "");
    Check(response.Success && Near(arm.CurrentPosition, 0) && Reach() == 0, "速度给空串按后端 EC MoveSpeed 走回 0");
    response = Act("Chamber9.Arm1", "ResetDrive");
    Check(response.Success && chamber.State == ModuleState.Idle, "驱动器复位");

    // 11. 点动（按住类）：发起就回、腔体进 Manual；按住期间续；松手发停止，停下来回原状态；松手后再续回 chamber.part_not_held
    response = Act("Chamber9.SpinMotor", "Jog", "50");
    Check(response.Success && chamber.State == ChamberState.Manual, "点动发起就回 Ok，腔体进手动中");
    Tick();
    Check(spin.IsSpinning && Live("Chamber9.SpinMotor", "CurrentSpeed") == "50", "点动按 50 转起来");
    Check(Renew("Chamber9.SpinMotor", "jog").Success, "按住期间续得上（动作名大小写不论）");
    response = Renew("Chamber9.SpinMotor", "Stop");
    Check(!response.Success && response.Code == ErrorCodes.ChamberPartNotHeld
        && response.Args.SequenceEqual(["Chamber9.SpinMotor", "Stop"]), "续的不是正按着的动作：chamber.part_not_held");
    Check(!Renew("Chamber9.Arm1", "Jog").Success, "续的不是正按着的部件");
    response = Act("Chamber9.Bowl1", "Open");
    Check(!response.Success && response.Code == ErrorCodes.ActionRejected, "点动按住期间别的普通动作被拒");
    response = Act("Chamber9.SpinMotor", "Stop");
    Check(response.Success, "松手发停止（停止类，腔体忙也照发）");
    WaitIdle("松手后等停下就退出手动中");
    Check(chamber.State == ModuleState.Idle && !spin.IsSpinning, "停下了，回到原来的空闲");
    Check(!Renew("Chamber9.SpinMotor", "Jog").Success, "松手后再续：没在按住");
    response = Act("Chamber9.Door", "Jog", "10");
    Check(!response.Success && response.Code == ErrorCodes.ChamberPartActionUnsupported, "气缸没有点动");
    response = Act("Chamber9.SpinMotor", "Jog");
    Check(!response.Success && response.Code == ErrorCodes.ChamberPartActionArgsInvalid, "点动没给速度");

    // 12. 没续上（界面断了、客户端退了）：过了 EC HoldTimeoutMs 模块自己发停止
    chamber.HoldTimeoutMs = 500;
    response = Act("Chamber9.SpinMotor", "Jog", "-50");
    Check(response.Success && chamber.State == ChamberState.Manual, "反向点动发起");
    Tick();
    Check(spin.IsSpinning && spin.CurrentSpeed < 0, "反向转起来");
    var held = Stopwatch.StartNew();
    WaitIdle("没续上应在保活超时后自己停");
    Check(held.ElapsedMilliseconds >= 400 && !spin.IsSpinning && chamber.State == ModuleState.Idle, "过了保活超时（500 ms）自己停，回到空闲");

    // 13. 气缸、喷嘴照方法名调：Lift 升降、喷嘴通断、Bowl 升
    response = Act("Chamber9.Arm1.Lift", "Open");
    Check(response.Success && Live("Chamber9.Arm1.Lift", "Position") == "Opened", "Lift 升");
    response = Act("Chamber9.Arm1.Lift", "Close");
    Check(response.Success && Live("Chamber9.Arm1.Lift", "Position") == "Closed", "Lift 降");
    response = Act("Chamber9.Arm1.Nozzle_SC1", "On");
    Check(response.Success && Live("Chamber9.Arm1.Nozzle_SC1", "IsOn") == "True", "喷嘴通（等到流量开关）");
    response = Act("Chamber9.Arm1.Nozzle_SC1", "Off");
    Check(response.Success && Live("Chamber9.Arm1.Nozzle_SC1", "IsOn") == "False", "喷嘴断");
    response = Act("Chamber9.Bowl1", "Open");
    Check(response.Success && Live("Chamber9.Bowl1", "Position") == "Opened" && chamber.State == ModuleState.Idle, "Bowl 升，做完回空闲");

    // 14. 停用的腔体不发部件动作
    var disabled = (SmokeChamber)ComponentLoader.Load([ChamberConfig("Chamber8", enabled: false)]).Single();
    var disabledService = new ChamberService([disabled]);
    var disabledResponse = disabledService.PartActionAsync(Request("Chamber8", "Chamber8.Door", "Open", [])).Result;
    Check(!disabledResponse.Success && disabledResponse.Code == ErrorCodes.ActionRejected, "停用的腔体部件动作被拒");

    // 15. 组件初始化（开机，不动硬件）与模块初始化：腔体开机只占晶圆账的槽位、一路递归到所有部件，不碰任何一根轴；
    //     模块初始化 InitModule 就是 Home（人或调度才调）；停用的腔体连账都不占
    var previousLedger = WaferManagerComponent.Current;
    var ledger = new WaferManagerComponent();
    try
    {
        var armBefore = arm.ActionState;
        var spinBefore = spin.ActionState;
        Check(chamber.InitComponent(), "腔体的组件初始化应成功（部件都没有自己的开机动作）");
        Check(ledger.IsRegistered("Chamber9") && ledger.GetSlots("Chamber9").Count == chamber.SlotCount,
            "腔体的组件初始化登记了晶圆账槽位");
        Check(arm.ActionState == armBefore && spin.ActionState == spinBefore && chamber.CurrentOperation is null,
            "组件初始化一路递归下去也不动轴：摆臂和旋转电机的动作状态没变，腔体没挂操作");
        Check(disabled.InitComponent() && !ledger.IsRegistered("Chamber8"), "停用的腔体：组件初始化连账都不占");

        var fresh = (SmokeChamber)ComponentLoader.Load([ChamberConfig("Chamber7", enabled: true)]).Single();
        var home = fresh.InitModule();
        Check(home is not null && fresh.State == ChamberState.Homing, "腔体的模块初始化 InitModule = Home：回原点操作挂上，进 Homing");
    }
    finally
    {
        WaferManagerComponent.Current = previousLedger;
    }

    Console.WriteLine($"PASS: {checks} chamber parts checks (generic part list from sc, cylinder tri-state, nozzle/spin/arm values, "
        + "push on change with retained replay, part actions: not found, unsupported, invalid args, command rejected, Manual state, "
        + "busy rejection, priority stop while busy, abort replacement, failure to Error, axis end to end, jog hold/renew/release, "
        + "hold timeout auto stop, cylinders and nozzles by method name, disabled chamber, component init registering the ledger "
        + "without moving any axis and module init = Home)");
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
        Node("Bowl1", typeof(CylinderComponent), Value("DoOpenIndex", "2"), Value("DoCloseIndex", "3"),
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

    protected override ModuleOperation? CreateProcessOperation(ProcessRequest request)
    {
        return new TimedProbe("Process", ActionMs);
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
/// 带反馈的阀照输出给反馈、轴照新命令回零 / 定位 / 步进 / 点动 / 转 / 停 / 复位。冒烟线程和 RPC 线程都会碰它，全部加锁。
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

                    case MotionCommandId.MoveBy:
                        status.Current_Position += command.Param1;
                        status.Is_In_Position = 1;
                        status.Is_Stopped = 1;
                        break;

                    case MotionCommandId.Spin:
                    case MotionCommandId.Jog:
                        status.Current_Speed = command.Param2;
                        status.Is_Stopped = 0;
                        break;

                    case MotionCommandId.Stop:
                    case MotionCommandId.EStop:
                        status.Current_Speed = 0;
                        status.Is_Stopped = 1;
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
