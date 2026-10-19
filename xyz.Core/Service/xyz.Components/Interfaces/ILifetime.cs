namespace xyz.Components.Interfaces;

/// <summary>
/// 部件寿命
/// </summary>
public interface ILifetime : IDeviceComponent
{
    /// <summary>
    /// 已用次数
    /// </summary>
    long UsedCount { get; }

    int WarnCount { get; }

    int AlarmCount { get; }


    void Clear();
}
