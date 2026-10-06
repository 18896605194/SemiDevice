using xyz.Components.Attributes;
using xyz.Modules;

namespace xyz._35021.Module.Job;

/// <summary>
/// 35021 机台的任务组件：任务表的生成规则、出错处理都用平台的（BaseTaskComponent）——
/// 来源 LoadPort 取片 → 腔体放片 → 工艺 → 腔体取片 → 回片 LoadPort 放片。
/// 这个类留着放 35021 独有的规则：以后加了对准器、冷却台这类站点、规则跟平台不一样，就在这儿重写 BuildTasks / TasksAt。
/// </summary>
[Component(description: "35021 任务组件：照流程配方给每片生成任务表")]
public class TaskComponent : BaseTaskComponent
{
}
