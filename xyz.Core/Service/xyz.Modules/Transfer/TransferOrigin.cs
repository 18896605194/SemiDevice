namespace xyz.Modules;

/// <summary>
/// 搬运操作的调用来源
/// </summary>
public enum TransferOrigin
{
    /// <summary>
    /// 手动
    /// </summary>
    Manual,

    /// <summary>
    /// 自动调度
    /// </summary>
    Auto,

    /// <summary>
    /// 恢复
    /// </summary>
    Recovery,
}
