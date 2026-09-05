namespace xyz.Modules;

/// <summary>
/// 模块操作扩展：手动/同步调用方的等待糖。
/// </summary>
public static class ModuleOperationExtensions
{
    /// <summary>
    /// 等待操作到终态并返回是否成功；操作为 null（被拒）直接返回 false。
    /// 用于 RPC/工具等同步调用方；模块扫描线程不要用。
    /// </summary>
    public static bool Wait(this ModuleOperation? operation, int timeoutMilliseconds)
    {
        if (operation is null)
        {
            return false;
        }

        operation.WaitReply(timeoutMilliseconds);
        return operation.IsSuccess;
    }
}
