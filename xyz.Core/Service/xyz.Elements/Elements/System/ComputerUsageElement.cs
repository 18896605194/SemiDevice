using System.Runtime.InteropServices;
using xyz.Elements.Attributes;
using xyz.Elements.Enums;

namespace xyz.Elements.Elements;

/// <summary>
/// 整台电脑的 CPU、内存占用率，线程数（所有程序合计，同任务管理器“性能”页）。挂在 Computer 下，每隔 SampleIntervalMs 取一次样。
/// </summary>
[Element(description: "整台电脑占用（CPU、内存、线程数）")]
public class ComputerUsageElement : ElementBase
{
    #region EC

    [VariableMark(VariableType.EC, ValueFormat.Int, "ms", "100", "60000", "1000", "多久取一次整台电脑的 CPU、内存和线程数；CPU 是这段时间里的平均值")]
    public int SampleIntervalMs
    {
        get { return GetEcInt(nameof(SampleIntervalMs)); }
        set { SetEcInt(nameof(SampleIntervalMs), value); }
    }

    #endregion

    #region SV

    /// <summary>
    /// CPU 占用率（所有核平均，0~100）
    /// </summary>
    [VariableMark(VariableType.SV, ValueFormat.Double, unit: "%", description: "整台电脑 CPU 占用率（所有核平均）")]
    public double? CpuUsage { get; private set; }

    /// <summary>
    /// 内存占用率（物理内存已用 / 总量，0~100）
    /// </summary>
    [VariableMark(VariableType.SV, ValueFormat.Double, unit: "%", description: "整台电脑内存占用率（物理内存）")]
    public double? MemoryUsage { get; private set; }

    /// <summary>
    /// 线程数（所有程序合计）
    /// </summary>
    [VariableMark(VariableType.SV, ValueFormat.Int, description: "整台电脑线程数（所有程序合计）")]
    public int? ThreadCount { get; private set; }

    #endregion

    #region 扫描

    /// <summary>
    /// 上一次取样时系统的空闲时间、总时间（内核 + 用户，内核里含空闲），100 ns 为单位。
    /// </summary>
    private long _lastIdle;

    private long _lastTotal;

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

        CpuUsage = SampleCpu();

        var info = new PerformanceInformation { Size = (uint)Marshal.SizeOf<PerformanceInformation>() };
        if (!K32GetPerformanceInfo(ref info, info.Size) || info.PhysicalTotal == 0)
        {
            MemoryUsage = null;
            ThreadCount = null;
            return;
        }

        MemoryUsage = Math.Round((double)(info.PhysicalTotal - info.PhysicalAvailable) * 100 / info.PhysicalTotal, 1);
        ThreadCount = (int)info.ThreadCount;
    }

    /// <summary>
    /// 跟上一次取样比：这段时间里不空闲的占比。第一次没有可比的给 null；取不到时丢掉基准，下一次重新起算。
    /// </summary>
    private double? SampleCpu()
    {
        if (!GetSystemTimes(out long idle, out long kernel, out long user))
        {
            _hasCpuBase = false;
            return null;
        }

        long total = kernel + user;
        bool hadBase = _hasCpuBase;
        long idleDelta = idle - _lastIdle;
        long totalDelta = total - _lastTotal;
        _lastIdle = idle;
        _lastTotal = total;
        _hasCpuBase = true;

        if (!hadBase || totalDelta <= 0)
        {
            return null;
        }

        return Math.Round(Math.Clamp((totalDelta - idleDelta) * 100.0 / totalDelta, 0, 100), 1);
    }

    #endregion

    #region Windows 接口

    [StructLayout(LayoutKind.Sequential)]
    private struct PerformanceInformation
    {
        public uint Size;
        public nuint CommitTotal;
        public nuint CommitLimit;
        public nuint CommitPeak;
        public nuint PhysicalTotal;
        public nuint PhysicalAvailable;
        public nuint SystemCache;
        public nuint KernelTotal;
        public nuint KernelPaged;
        public nuint KernelNonpaged;
        public nuint PageSize;
        public uint HandleCount;
        public uint ProcessCount;
        public uint ThreadCount;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool K32GetPerformanceInfo(ref PerformanceInformation information, uint size);

    #endregion
}
