using xyz.Components.Attributes;
using xyz.Components.Components;
using xyz.Modules;
using xyz.Modules.Enums;
using xyz._35021.Module.Robot.Operation;

namespace xyz._35021.Module.Robot;

/// <summary>
/// 35021 机台 _robot 模块：只提供机型动作（操作类在 Operation 文件夹）。
/// 设备状态轮询（报错 / 伺服 / 速度 / 轴位 / 手指在位订阅）在平台 BaseRobotModule，机型不用写；
/// 品牌指令由 _driver 组件选（sc.xml 换 Type 即换品牌），机型代码不碰具体品牌。
/// </summary>
[Component(description: "35021 _robot 模块")]
public class RobotModule : BaseRobotModule, IRobot
{
    #region Action

    public override ModuleOperation? Home()
    {
        return Begin(RobotAction.Home, new HomeOperation(this));
    }

    protected override ModuleOperation? ResetDevice()
    {
        return Begin(RobotAction.Reset, new ResetOperation(this));
    }

    protected override ModuleOperation? AbortDevice()
    {
        return Begin(RobotAction.Abort, new AbortOperation(this));
    }

    protected override ModuleOperation CreatePickOperation(int arm, int stationNumber, int slot)
    {
        return new PickOperation(this, arm, stationNumber, slot);
    }

    protected override ModuleOperation CreatePlaceOperation(int arm, int stationNumber, int slot)
    {
        return new PlaceOperation(this, arm, stationNumber, slot);
    }

    public override ModuleOperation? PowerOn()
    {
        return Begin(RobotAction.PowerOn, new PowerOnOperation(this));
    }

    public override ModuleOperation? PowerOff()
    {
        return Begin(RobotAction.PowerOff, new PowerOffOperation(this));
    }

    #endregion
}
