using xyz.Components.Attributes;
using xyz.Components.Enums;

namespace xyz.Components.Components;

/// <summary>
/// 电脑：下面挂 Program（本程序）、Total（整台电脑）两个子节点，各记 CPU、内存、线程数
/// 根组件，开机由装配层 Start；每隔 SampleIntervalMs 扫一次子节点，子节点扫一次取一次样。
/// </summary>
[Component(description: "电脑（下挂本程序、整台电脑的 CPU、内存、线程数）")]
public class ComputerComponent : ComponentBase
{
    #region EC

    [VariableMark(VariableType.EC, ValueFormat.Int, "ms", "100", "60000", "1000", "多久取一次 CPU、内存和线程数；CPU 是这段时间里的平均值")]
    public int SampleIntervalMs
    {
        get { return GetEcInt(nameof(SampleIntervalMs)); }
        set { SetEcInt(nameof(SampleIntervalMs), value); }
    }

    #endregion

    #region 扫描

    private long _nextSampleAt;

    /// <summary>
    /// 扫描线程 10 ms 一拍，到了取样时间才扫子节点：第一拍就扫，之后每隔 SampleIntervalMs 扫一次。
    /// </summary>
    protected override void OnScan()
    {
        long now = Environment.TickCount64;
        if (now < _nextSampleAt)
        {
            return;
        }

        _nextSampleAt = now + SampleIntervalMs;
        base.OnScan();
    }

    #endregion
}
