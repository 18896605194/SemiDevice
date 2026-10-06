using xyz.Components.Attributes;
using xyz.Components.Interfaces;
using xyz.Modules;

namespace xyz._35021.Module.Loadport;

/// <summary>
/// 35021 机台 LoadPort 模块：动作、状态查询、在位判断、断线重连、状态推送都用平台的（BaseLoadPortModule）。
/// 这个类留着放 35021 独有的东西：哪个动作跟平台不一样，就重写对应的方法（Load、Unload、Home、Clamp……）；
/// 平台没有的设备、动作在这儿扩展。品牌驱动是 sc.xml 挂在本模块下的 Driver 子节点，换 Type 即换品牌。
/// </summary>
[Component(description: "35021 LoadPort 模块")]
public class LoadPortModule : BaseLoadPortModule, ILoadPort
{
}
