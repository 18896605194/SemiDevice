namespace xyz.Components.Alarm;

/// <summary>
/// 平台通用报警管理契约。模块依赖此接口，不依赖具体的 AlarmComponent 实现。
/// 同一台设备应共用同一个报警管理实例。
/// </summary>
public interface IAlarmComponent
{
    /// <summary>
    /// 报警触发、确认或恢复后的快照。重复操作不发送变化通知；不保证在 UI 线程执行。
    /// </summary>
    event Action<AlarmItem>? AlarmChanged;

    /// <summary>
    /// 当前活动报警的快照，按触发时间排序。人工确认不代表故障恢复，确认后仍保留在此列表中。
    /// </summary>
    IReadOnlyList<AlarmItem> ActiveAlarms { get; }

    /// <summary>
    /// 初始化时读取组件公开实例字段或属性上的 AlarmAttribute 定义。
    /// 字符串成员值作为报警代码，sourcePath 用于区分组件实例；不递归注册子组件。
    /// </summary>
    void Register(string sourcePath, ComponentBase source);

    /// <summary>
    /// 触发已注册的报警；新增活动报警时返回 true，已活动时返回 false。
    /// </summary>
    bool Raise(string sourcePath, string alarmCode);

    /// <summary>
    /// 由故障检测方在故障恢复后调用。移出活动列表并通知，不自动确认报警。
    /// 成功恢复时返回 true，不存在活动报警时返回 false。
    /// </summary>
    bool Clear(string sourcePath, string alarmCode);

    /// <summary>
    /// 人工确认活动报警，不清除故障，也不控制蜂鸣器。
    /// 首次确认时返回 true，不存在活动报警或已经确认时返回 false。
    /// </summary>
    bool Acknowledge(string sourcePath, string alarmCode);
}
