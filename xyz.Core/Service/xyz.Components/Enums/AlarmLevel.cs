namespace xyz.Components.Enums;

/// <summary>
/// 报警等级，后续会根据等级做相应动作
/// </summary>
public enum AlarmLevel
{
    /// <summary>
    /// 警告
    /// </summary>
    Warn = 1,

    /// <summary>
    /// 轻报警
    /// </summary>
    Light = 2,

    /// <summary>
    /// 重报警
    /// </summary>
    Heavy = 3,

    /// <summary>
    /// 致命
    /// </summary>
    Fatal = 4
}
