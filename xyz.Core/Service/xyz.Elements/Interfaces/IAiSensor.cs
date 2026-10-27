namespace xyz.Elements.Interfaces;

/// <summary>
/// AI 值监控
/// </summary>
public interface IAiSensor : IDevice
{
    double? Value { get; }

    /// <summary>
    /// 是否超限
    /// </summary>
    bool IsOutOfRange { get; }

    /// <summary>
    /// 目标值
    /// </summary>
    double Target { get; }

    /// <summary>
    /// 给目标值
    /// </summary>
    /// <param name="target"></param>
    void SetTarget(double target);
}
