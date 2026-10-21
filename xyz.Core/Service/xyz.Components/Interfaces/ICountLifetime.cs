namespace xyz.Components.Interfaces;

/// <summary>
/// 部件寿命（次数）
/// </summary>
public interface ICountLifetime : IDevice
{
    /// <summary>
    /// 已用次数
    /// </summary>
    long UsedCount { get; }

    int WarnCount { get; }

    int AlarmCount { get; }


    void Clear();
}
