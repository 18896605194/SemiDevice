using RejeRobotSimulator.Protocol;
using RejeRobotSimulator.Server;

namespace RejeRobotSimulator;

/// <summary>
/// 协议逻辑自测：不依赖 TCP 监听，直接调用 TcpServer.ProcessCommand 真实路由，
/// 校验解析归一化、三类指令的回帧格式、互斥与中止语义。结果全部写进日志。
/// </summary>
public class ProtocolTest
{
    private readonly Action<string> _write;
    private readonly TcpServer _server;

    private int _pass;
    private int _fail;

    public ProtocolTest(Action<string> write)
    {
        _write = write;
        _server = new TcpServer(0);
        _server.MotionHandler.SimulatedDelayMs = 0;   // 自测不等仿真耗时
        _server.OnLog += _ => { };                     // 静默内部日志
    }

    public void Run()
    {
        _pass = 0;
        _fail = 0;

        var st = _server.State;

        // ── 解析归一化 ──
        CheckParse("@XPos;", "AxisPos", "X");
        CheckParse("@Arm1Pos;", "AxisPos", "Arm1");
        CheckParse("@SetZPos700;", "SetAxisPos", "Z700");
        CheckParse("@ThetaWorkHome45;", "AxisWorkHome", "Theta45");
        CheckParse("@Arm3Home;", "Home", "Arm3");
        CheckParse("@Speed50;", "Speed", "50");
        CheckParse("@G10102;", "G", "10102");
        CheckParse("@OpenEMV 2;", "OpenEMV", "2");

        // ── 查询指令回帧格式 ──
        var (ack, rsp) = _server.ProcessCommand("@Status;");
        Check("Status ACK", ack == ">;");
        Check("Status 成功帧", rsp == ">00000000#The robot is OK@Status;");

        (_, rsp) = _server.ProcessCommand("@QOpMode;");
        Check("QOpMode 默认自动模式", rsp == ">00000000#2@QOpMode;");

        (_, rsp) = _server.ProcessCommand("@XPos;");
        Check("XPos 回显+F3", rsp == ">00000000#0.000@XPos;");

        // ── 设置指令 ──
        (_, rsp) = _server.ProcessCommand("@Speed150;");
        Check("Speed 越界拒绝", rsp == ">99990003#Speed must be 1-100@Speed;");

        (_, rsp) = _server.ProcessCommand("@Speed80;");
        Check("Speed 设置成功回显", rsp == ">00000000#OK@Speed80;" && st.SpeedPercent == 80);

        (_, rsp) = _server.ProcessCommand("@SubWafer1;");
        Check("SubWafer 订阅", rsp == ">00000000#OK@SubWafer1;" && st.WaferSubscribed);

        (_, rsp) = _server.ProcessCommand("@OpenEMV5;");
        Check("EMV 通道越界", rsp.StartsWith(">99990003#"));

        // ── 运动指令 ──
        (_, rsp) = _server.ProcessCommand("@G10102;");   // 手指1 工位0x01 层0x02
        Check("G 取片成功", rsp == ">00000000#OK@G10102;" && st.Arm1HasWafer);
        Check("G 更新臂位", st.PosArm1 == 12.0);         // 0x01*10 + 2

        (_, rsp) = _server.ProcessCommand("@P20102;");
        Check("P 放片成功", rsp == ">00000000#OK@P20102;" && !st.Arm2HasWafer);

        (_, rsp) = _server.ProcessCommand("@GAP1010220103;");
        Check("GAP 两帧结果", rsp == ">00000000#OK@G10102;\n>00000000#OK@P20103;");
        Check("GAP 取放语义", st.Arm1HasWafer && !st.Arm2HasWafer);

        (_, rsp) = _server.ProcessCommand("@Home;");
        Check("Home 全轴回原点", rsp == ">00000000#OK@Home;" && st.PosArm1 == 0);

        // ── 未使能拒绝运动 ──
        st.IsEnabled = false;
        (_, rsp) = _server.ProcessCommand("@G10102;");
        Check("未使能拒绝运动", rsp == ">99990020#Robot not enabled@G10102;");
        st.IsEnabled = true;

        // ── 未知指令 ──
        (_, rsp) = _server.ProcessCommand("@Whatever;");
        Check("未知指令 99990099", rsp == ">99990099#Unknown command@Whatever;");

        // ── 运动互斥 (动作中再收动作应回 busy) ──
        _server.MotionHandler.SimulatedDelayMs = 500;
        var slow = Task.Run(() => _server.ProcessCommand("@G10102;"));
        Thread.Sleep(100);                               // 等慢动作进入执行中
        (_, rsp) = _server.ProcessCommand("@G20102;");
        Check("运动中再收动作回 busy", rsp.StartsWith(">99990011#Robot busy"));
        _server.State.AbortSignal.Set();                 // 打断慢动作
        slow.Wait();
        Check("SStop 打断在途运动", slow.Result.response.StartsWith(">99990030#Motion aborted"));
        _server.MotionHandler.SimulatedDelayMs = 0;

        _write($"自测完成: {_pass} 通过, {_fail} 失败");
    }

    private void CheckParse(string raw, string wantName, string wantArgs)
    {
        var p = CommandParser.Parse(raw);
        Check($"解析 {raw}",
            p is not null
            && p.CommandName.Equals(wantName, StringComparison.OrdinalIgnoreCase)
            && p.Args.Equals(wantArgs, StringComparison.OrdinalIgnoreCase));
    }

    private void Check(string name, bool ok)
    {
        _write($"{(ok ? "  ✓ " : "  ✗ ")}{name}");
        if (ok) _pass++; else _fail++;
    }
}
