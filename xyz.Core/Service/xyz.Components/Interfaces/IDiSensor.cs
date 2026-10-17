namespace xyz.Components.Interfaces;


public interface IDiSensor : IDeviceComponent
{
    /// <summary>
    /// 有没有触发
    /// </summary>
    bool? IsActive { get; }

    /// <summary>
    /// 配了 DI 点没有（DiIndex 配成 -1 就是没配，当没有这个信号）
    /// </summary>
    bool IsConfigured { get; }
}
