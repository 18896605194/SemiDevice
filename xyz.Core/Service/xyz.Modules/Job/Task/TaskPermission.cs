namespace xyz.Modules;

/// <summary>
/// 调度能对一行任务做什么：Job 组件按 PJ 的状态给。暂停、停止的效果就体现在这上面——调度不认识暂停、停止，只看许可。
/// </summary>
[Flags]
public enum TaskPermission
{
    /// <summary>什么都不做（没开始、等启动、暂停到位、中止中、结束了）。</summary>
    None = 0,

    /// <summary>走机内的片（站内任务、去下一站、回片）。</summary>
    Advance = 1,

    /// <summary>从载具投新片。</summary>
    Feed = 2,
}
