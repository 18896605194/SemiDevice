using xyz.Elements.Models;
using xyz.Elements.Attributes;

namespace xyz.Elements.Interfaces;

/// <summary>
/// 数据曲线
/// </summary>
[Ioc]
public interface IDataChart
{
    /// <summary>
    /// 采样周期（毫秒）
    /// </summary>
    int SampleIntervalMs { get; }

    /// <summary>
    /// 在采的信号
    /// </summary>
    IReadOnlyList<DataSignal> Signals { get; }

    /// <summary>
    /// 每个采样周期一行
    /// </summary>
    IObservable<DataRecord> Records { get; }

    /// <summary>
    /// 数据曲线、实时曲线页最多同时画几条
    /// </summary>
    int QueryMaxSignals { get; }

    /// <summary>
    /// 曲线截图存哪个文件夹
    /// </summary>
    string ScreenshotDirectory { get; }

    /// <summary>
    /// 按名字查 [start, end)（本机时间）的曲线
    /// </summary>
    DataQueryResult Query(IReadOnlyList<string> names, DateTime start, DateTime end, CancellationToken token);

    /// <summary>
    /// 按名字导出 [start, end)（本机时间）的原始行，一段一段给
    /// </summary>
    IEnumerable<DataExport> Export(IReadOnlyList<string> names, DateTime start, DateTime end, CancellationToken token);

}
