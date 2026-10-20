namespace xyz.Components.Interfaces;

/// <summary>
/// 部件寿命（时间）
/// </summary>
public interface ITimeLifetime : IDeviceComponent
{
    TimeSpan UsedTime { get; }

    int WarnMinutes { get; }

    int AlarmMinutes { get; }

    void Clear();
}
