namespace xyz.Components.Components;

/// <summary>
/// 动作的等待糖：手动、同步调用方用（扫描线程里不能用）
/// </summary>
public static class ModuleRoutineExtensions
{
    /// <summary>
    /// 等动作收完尾，返回做没做成；被拒（null）回 false，等超时抛 TimeoutException
    /// </summary>
    public static bool Wait(this ModuleRoutine? routine, int timeoutMilliseconds, CancellationToken cancellationToken = default)
    {
        if (routine is null)
        {
            return false;
        }

        if (!routine.WaitReply(timeoutMilliseconds, cancellationToken))
        {
            throw new TimeoutException($"等动作 {routine.Name} 的结果超时（{timeoutMilliseconds}ms）。");
        }

        return routine.IsSuccess;
    }
}
