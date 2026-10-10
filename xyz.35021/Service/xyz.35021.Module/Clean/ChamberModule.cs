using xyz.Components.Attributes;
using xyz.Modules;

namespace xyz._35021.Module.Clean;

/// <summary>
/// 35021 机台清洗腔模块：回零、复位、中止、按工艺配方做工艺、手动页的部件动作、状态推送都用平台的（BaseChamberModule），
/// 照 sc.xml 里这个腔体下挂的门、Bowl、卡盘、摆臂（Lift、喷嘴）去发轴和 IO。
/// 这个类留着放 35021 独有的东西：哪个动作跟平台不一样，就重写对应的方法（Home、ResetDevice、AbortDevice、CreateProcessOperation）；
/// 平台没有的设备、动作在这儿扩展。连接不在这儿开——几个腔共用一个 PLC，连接归那个 PLC 组件，腔体只管按地址读写。
/// </summary>
[Component(description: "35021 清洗腔模块")]
public class ChamberModule : BaseChamberModule
{
}
