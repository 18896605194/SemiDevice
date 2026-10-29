using System.Diagnostics;
using xyz.Elements.Attributes;
using xyz.Elements.Enums;

namespace xyz.Elements.Elements;

/// <summary>
/// 本程序（后端）占用的 CPU、内存，线程数。挂在 Computer 下，每隔 SampleIntervalMs 取一次样。
/// </summary>
[Element(description: "本程序占用（CPU、内存、线程数）")]
public class ProgramUsageElement : ElementBase
{
    #region EC

    [VariableMark(VariableType.EC, ValueFormat.Int, "ms", "100", "60000", "1000", "多久取一次本程序的 CPU、内存和线程数；CPU 是这段时间里的平均值")]
    public int SampleIntervalMs
    {
        get { return GetEcInt(nameof(SampleIntervalMs)); }
        set { SetEcInt(nameof(SampleIntervalMs), value); }
    }

    #endregion

    #region SV

    /// <summary>
    /// CPU 占用率：占整台电脑的（0~100，跟任务管理器一样）
    /// </summary>
    [VariableMark(VariableType.SV, ValueFormat.Double, unit: "%", description: "本程序 CPU 占用率（占整台电脑的）")]
    public double? CpuUsage { get; private set; }

    /// <summary>
    /// 内存占用（工作集）
    /// </summary>
    [VariableMark(VariableType.SV, ValueFormat.Double, unit: "MB", description: "本程序内存占用（工作集）")]
    public double? MemoryUsage { get; private set; }

    /// <summary>
    /// 线程数
    /// </summary>
    [VariableMark(VariableType.SV, ValueFormat.Int, description: "本程序线程数")]
    public int? ThreadCount { get; private set; }

    #endregion

    #region 扫描

    /// <summary>
    /// 上一次取样的时刻和本程序累计用掉的 CPU 时间：CPU 占用率 = 两次之间用掉的 CPU 时间 / (经过的时间 × 核数)。
    /// </summary>
    private long _lastTimestamp;

    private TimeSpan _lastProcessorTime;

    private bool _hasCpuBase;

    /// <summary>
    /// 下一次该取样的时刻：扫描线程 10 ms 一拍，没到点这一拍什么都不做
    /// </summary>
    private long _nextSampleAt;

    protected override void OnScan()
    {
        long now = Environment.TickCount64;
        if (now < _nextSampleAt)
        {
            return;
        }

        _nextSampleAt = now + SampleIntervalMs;

        using var process = Process.GetCurrentProcess();
        CpuUsage = SampleCpu(process.TotalProcessorTime);
        MemoryUsage = Math.Round(process.WorkingSet64 / 1024.0 / 1024.0, 1);
        ThreadCount = process.Threads.Count;
    }

    /// <summary>
    /// 跟上一次取样比；第一次没有可比的，给 null。
    /// </summary>
    private double? SampleCpu(TimeSpan processorTime)
    {
        long timestamp = Stopwatch.GetTimestamp();
        bool hadBase = _hasCpuBase;
        TimeSpan used = processorTime - _lastProcessorTime;
        TimeSpan elapsed = Stopwatch.GetElapsedTime(_lastTimestamp, timestamp);
        _lastTimestamp = timestamp;
        _lastProcessorTime = processorTime;
        _hasCpuBase = true;

        if (!hadBase || elapsed <= TimeSpan.Zero)
        {
            return null;
        }

        return Math.Round(Math.Clamp(used / (elapsed * Environment.ProcessorCount) * 100, 0, 100), 1);
    }

    #endregion
}
