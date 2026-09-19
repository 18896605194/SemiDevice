namespace xyz.Shared.Dtos;

/// <summary>
/// 历史查询结果（RpcResponse.Data 的 JSON）：查到的记录按时间倒序（最新的在前）。
/// </summary>
public class HistoryResult<T>
{
    public List<T> Items { get; set; } = [];

    /// <summary>
    /// 符合条件的比返回的多（只留了最新的一部分，条数按后端 sc.xml 配置），界面据此提示缩小时间段或加条件。
    /// </summary>
    public bool Truncated { get; set; }
}
