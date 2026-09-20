namespace xyz.Modules;

/// <summary>
/// 这一趟搬运是谁下的单。执行完全一样，卡控和失败收场按它分流。
/// </summary>
public enum TransferOrigin
{
    /// <summary>人工下单（手动传片页）：卡控看权限与路径，失败报给下单的那个人，不停机。</summary>
    Manual,

    /// <summary>系统派单（将来由 JobManager 按 PJ 展开）：卡控看工艺与模块 Online，失败停自动派单并报警。</summary>
    Auto,
}
