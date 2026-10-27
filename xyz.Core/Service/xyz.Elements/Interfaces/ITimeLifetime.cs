namespace xyz.Elements.Interfaces;

/// <summary>
/// 部件寿命（时间）
/// </summary>
public interface ITimeLifetime : IDevice
{
    TimeSpan UsedTime { get; }

    int WarnMinutes { get; }

    int AlarmMinutes { get; }

    void Clear();
}
