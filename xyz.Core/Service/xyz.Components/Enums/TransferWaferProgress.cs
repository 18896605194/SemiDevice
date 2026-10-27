namespace xyz.Components.Enums;

/// <summary>
/// 一片在一趟执行里做到哪一步
/// </summary>
public enum TransferWaferProgress
{
    /// <summary>
    /// 还没碰到（片在原位）
    /// </summary>
    None,

    /// <summary>
    /// 取完了，片在机械手手上（账已经记过去）
    /// </summary>
    Picked,

    /// <summary>
    /// 放完了，片在目标位置（账已经记过去）
    /// </summary>
    Placed,
}
