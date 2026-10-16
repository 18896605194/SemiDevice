namespace xyz.Components.Interfaces;

/// <summary>AI 值监控（流量计、压力、温度这类）：别的组件、模块按它用 AI 传感器组件。</summary>
public interface IAiSensor : IDeviceComponent
{
    /// <summary>最近一次读数（工程值）；IO 没接或读失败为 null。</summary>
    double? Value { get; }

    /// <summary>是否超限（防抖后）。</summary>
    bool IsOutOfRange { get; }

    /// <summary>目标值；0 = 没给。</summary>
    double Target { get; }

    /// <summary>给目标值（喷嘴按配方流量给流量计）；0 = 清掉。</summary>
    void SetTarget(double target);
}
