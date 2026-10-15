using xyz.Components.Components;

namespace xyz.Modules;

/// <summary>
/// 整机停止的"停止中"：等停的那一刻各模块手上的动作都收完尾（被中止的、归 Job 中止去停的都算），
/// 搬运管理撤掉的搬运也都收了场，才算停下来。Job 的中止不等（要核对片位、可能要人处理，进展看 Job 推送）。
/// 各项动作都有自己的超时，这里不另设。中止指令在系统模块锁外发，发完了 <see cref="Issue"/> 才开始等。
/// </summary>
internal sealed class SystemStopOperation : ModuleOperation
{
    private readonly IReadOnlyList<ModuleOperation> _inFlight;
    private volatile bool _issued;

    /// <param name="inFlight">停的那一刻各模块手上的动作。</param>
    public SystemStopOperation(IReadOnlyList<ModuleOperation> inFlight) : base("Abort")
    {
        _inFlight = inFlight;
    }

    /// <summary>中止指令都发完了，开始等。</summary>
    public void Issue()
    {
        _issued = true;
    }

    protected override void OnScan()
    {
        if (!_issued)
        {
            return;
        }

        foreach (var operation in _inFlight)
        {
            if (!operation.IsSettled)
            {
                return;
            }
        }

        var transfers = TransferManager.Current;
        if (transfers is not null)
        {
            foreach (var robot in transfers.Robots)
            {
                if (transfers.IsRobotInTransfer(robot.Name))
                {
                    return;
                }
            }
        }

        Complete();
    }
}
