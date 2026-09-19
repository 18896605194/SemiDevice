namespace xyz.Components.Alarm;

/// <summary>
/// 报警管理契约，给界面用：看当前有哪些报警、人工复位。
/// 报警由各组件经 ComponentBase 的 RaiseAlarm / CheckAlarm 自己报，不走这里；报出去以后只能人工复位清。
/// </summary>
public interface IAlarmComponent
{
    /// <summary>
    /// 报警变化的快照：报出、清除各推一条；重复报不推。不保证在 UI 线程执行。
    /// </summary>
    event Action<AlarmItem>? AlarmChanged;

    /// <summary>
    /// 当前报警的快照，按报出时间排序。
    /// </summary>
    IReadOnlyList<AlarmItem> ActiveAlarms { get; }

    /// <summary>
    /// 人工复位某个报警来源（连同它的子组件）：走那个组件的 Reset，组件能复位就清掉它的报警。
    /// 条件还在的报警清了以后，下个扫描周期会重新报出来。没报过报警的来源返回 false。
    /// </summary>
    bool Reset(string sourcePath);

    /// <summary>
    /// 人工复位全部有报警的组件；返回复位了几个组件。
    /// </summary>
    int ResetAll();
}
