using xyz.Components.Attributes;
using xyz.Modules;

namespace xyz._35021.Module.Robot;

/// <summary>
/// 35021 机台 _robot 模块：动作（Home、Pick、Place、上下使能、复位、急停）、动作入口检查、设备状态轮询、状态推送都用平台的（BaseRobotModule）。
/// 这个类留着放 35021 独有的东西：哪个动作跟平台不一样，就重写对应的 CreateXxxOperation；要加互锁就重写 XxxInterlock（先调 base）。
/// 品牌指令由 _driver 组件选（sc.xml 换 Type 即换品牌），机型代码不碰具体品牌。
/// </summary>
[Component(description: "35021 _robot 模块")]
public class RobotModule : BaseRobotModule, IRobot
{
}
