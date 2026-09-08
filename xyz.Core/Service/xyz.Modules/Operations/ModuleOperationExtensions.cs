namespace xyz.Modules;

/// <summary>
/// 模块操作扩展：手动/同步调用方的等待糖。
/// </summary>
public static class ModuleOperationExtensions
{
    /// <summary>
    /// 等待操作到终态并返回是否成功；操作为 null（被拒）直接返回 false。
    /// 等待超时抛出 TimeoutException，不改变操作状态；取消只结束本次等待。
    /// 用于 RPC/工具等同步调用方；模块扫描线程不要用。
    /// </summary>
    public static bool Wait(this ModuleOperation? operation, int timeoutMilliseconds,
        CancellationToken cancellationToken = default)
    {
        if (operation is null)
        {
            return false;
        }

        if (!operation.WaitReply(timeoutMilliseconds, cancellationToken))
        {
            throw new TimeoutException($"等待操作 {operation.Name} 结果超时（{timeoutMilliseconds}ms）。");
        }

        return operation.IsSuccess;
    }
}
