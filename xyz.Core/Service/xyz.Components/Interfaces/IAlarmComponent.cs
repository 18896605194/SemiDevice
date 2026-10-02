using xyz.Components.Models;

namespace xyz.Components.Interfaces;

/// <summary>
/// 报警组件接口
/// </summary>
public interface IAlarmComponent
{
    /// <summary>
    /// 报警变化的快照
    /// </summary>
    event Action<AlarmItem>? AlarmChanged;

    /// <summary>
    /// 当前报警的快照，活跃的报警
    /// </summary>
    IReadOnlyList<AlarmItem> ActiveAlarms { get; }

    /// <summary>
    /// Reset 报警
    /// </summary>
    /// <param name="sourcePath"></param>
    /// <returns></returns>
    bool Reset(string sourcePath);

    /// <summary>
    /// 人工复位全部有报警的组件；返回复位了几个组件。
    /// </summary>
    int ResetAll();
}
