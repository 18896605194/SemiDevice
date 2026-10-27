using xyz.Elements.Models;

namespace xyz.Elements.Interfaces;

/// <summary>
/// 报警元件接口
/// </summary>
public interface IAlarm
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
    /// 人工复位全部有报警的元件；返回复位了几个元件。
    /// </summary>
    int ResetAll();

    /// <summary>
    /// 元件报警（只给 ElementBase.RaiseAlarm 用）
    /// </summary>
    internal bool Raise(ElementBase source, string alarmCode);

    /// <summary>
    /// 清掉这个元件的报警（只给 ElementBase.Reset 用）
    /// </summary>
    internal int Clear(ElementBase source);

    /// <summary>
    /// 这个路径下面有没有报警
    /// </summary>
    internal bool HasAlarmUnder(string path);

    /// <summary>
    /// 报警历史一次最多查几条
    /// </summary>
    int HistoryQueryMaxCount { get; }

    /// <summary>
    /// 报警历史落哪个库
    /// </summary>
    string HistoryDatabase { get; }
}
