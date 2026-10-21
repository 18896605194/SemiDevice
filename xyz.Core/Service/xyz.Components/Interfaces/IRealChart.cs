using xyz.Components.Models;

namespace xyz.Components.Interfaces;

/// <summary>
/// 实时曲线
/// </summary>
public interface IRealChart
{
    /// <summary>
    /// 这次开机的会话号（界面按它认是不是同一段数据）
    /// </summary>
    long Session { get; }

    /// <summary>
    /// 采样周期（毫秒）
    /// </summary>
    int SampleIntervalMs { get; }

    /// <summary>
    /// 内存里留最近多少秒
    /// </summary>
    int WindowSeconds { get; }

    /// <summary>
    /// 在采的信号
    /// </summary>
    IReadOnlyList<DataSignal> Signals { get; }

    /// <summary>
    /// 最近一段（最旧的在前）
    /// </summary>
    IReadOnlyList<DataRecord> Recent();
}
