using xyz.Components;
using xyz.Components.Attributes;

namespace xyz.Modules;

/// <summary>
/// 指挥层（sc.xml 的 Dispatch 节点）：Job 派单、搬运管理推工单挂在它下面，共用它这一条扫描线程——
/// 派单、推工单、收结果都在这一条线程上做，彼此之间没有线程交叉（工单自己没有线程，跟着搬运管理走）。
/// 设备（机械手、LoadPort、腔体）不挂在这里：各自在根上、各一条线程，指挥层经设备的动作入口下令，再看动作做完没有。
/// 一拍里按 sc.xml 的先后扫子组件：先 Job 后 Transfer，先收结果、派单，新派的单当拍就能走第一步。
/// </summary>
[Component(description: "指挥层：Job、搬运管理挂在下面，共用一条扫描线程")]
public class DispatchComponent : ComponentBase
{
    /// <summary>
    /// 扫描：子组件按挂上的先后逐个单独扫，不走基类的递归——基类那样一个子组件抛异常，这一拍排在后面的就都不扫了，
    /// Job 一直出错的话搬运管理就一直推不动工单。每个子组件的异常、慢扫描都记在它自己名下。
    /// </summary>
    protected override void OnScan()
    {
        foreach (var child in Children)
        {
            ScanChild(child);
        }
    }
}
