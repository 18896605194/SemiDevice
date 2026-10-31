using xyz.Elements.Elements;
using xyz.Elements.Interfaces;

namespace xyz.Modules;

/// <summary>
/// 整机停止的"停止中"：等停的那一刻各模块手上的动作都收完尾（被中止的、归 Job 中止去停的都算），
/// 搬运管理撤掉的搬运也都收了场，才算停下来。Job 的中止不等（要核对片位、可能要人处理，进展看 Job 推送）。
/// 各项动作都有自己的超时，这里不另设。中止指令在系统模块锁外发，发完了 <see cref="Issue"/> 才开始等
/// </summary>
internal sealed class SystemStopStep : RoutineStep
{
    private readonly IReadOnlyList<ModuleRoutine> _inFlight;

    /// <summary>
    /// 搬运管理：撤掉的搬运要等收场；没装是 null
    /// </summary>
    private readonly ITransferManager? _transfers;

    private volatile bool _issued;

    /// <param name="name">步骤名</param>
    /// <param name="inFlight">停的那一刻各模块手上的动作</param>
    /// <param name="transfers">搬运管理；没装是 null</param>
    public SystemStopStep(string name, IReadOnlyList<ModuleRoutine> inFlight, ITransferManager? transfers) : base(name)
    {
        _inFlight = inFlight;
        _transfers = transfers;
    }

    /// <summary>
    /// 还没收完尾的动作
    /// </summary>
    public override string Waiting
    {
        get
        {
            var pending = new List<string>();
            foreach (var routine in _inFlight)
            {
                if (!routine.IsSettled)
                {
                    pending.Add($"{routine.Module} 的 {routine.Name}");
                }
            }

            return string.Join("、", pending);
        }
    }

    /// <summary>
    /// 中止指令都发完了，开始等
    /// </summary>
    public void Issue()
    {
        _issued = true;
    }

    protected internal override void Send()
    {
    }

    protected internal override bool Check()
    {
        if (!_issued)
        {
            return false;
        }

        foreach (var routine in _inFlight)
        {
            if (!routine.IsSettled)
            {
                return false;
            }
        }

        if (_transfers is not null)
        {
            foreach (var robot in _transfers.Robots)
            {
                if (_transfers.IsRobotInTransfer(robot.Name))
                {
                    return false;
                }
            }
        }

        return true;
    }
}
