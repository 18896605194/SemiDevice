using xyz.Elements.Models;
using xyz.Elements.Attributes;

namespace xyz.Elements.Interfaces;

/// <summary>
/// 实时曲线
/// </summary>
[Ioc]
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
    /// 最多同时画几条曲线（跟数据曲线同一个设置）
    /// </summary>
    int MaxSignals { get; }

    /// <summary>
    /// 截图存到哪个文件夹（跟数据曲线同一个设置）
    /// </summary>
    string ScreenshotDirectory { get; }

    /// <summary>
    /// 在采的信号
    /// </summary>
    IReadOnlyList<DataSignal> Signals { get; }

    /// <summary>
    /// 最近一段（最旧的在前）
    /// </summary>
    IReadOnlyList<DataRecord> Recent();
}
