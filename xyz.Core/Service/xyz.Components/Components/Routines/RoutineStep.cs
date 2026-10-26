using System.Diagnostics;

namespace xyz.Components.Components;

/// <summary>
/// 动作里的一步：名字 + 进这一步时做一次（Run）+ 之后每拍判一次做完没有（Check）。只有正在走的这一步能判失败，原因自动带上第几步、在等什么
/// </summary>
public abstract class RoutineStep
{
    private ModuleRoutine? _routine;
    private long _startedAt;

    protected RoutineStep(string name)
    {
        Name = name;
    }

    /// <summary>
    /// 步骤名（日志用），如"Lift 升""取片：站点交接"
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 这一步属于哪个动作
    /// </summary>
    public ModuleRoutine Routine
    {
        get
        {
            if (_routine is null)
            {
                throw new InvalidOperationException($"步骤 {Name} 还没加进动作");
            }

            return _routine;
        }
    }

    /// <summary>
    /// 这一步做了多久，ms
    /// </summary>
    public long ElapsedMs => (long)Stopwatch.GetElapsedTime(_startedAt).TotalMilliseconds;

    /// <summary>
    /// 在等什么（超时、失败时写进原因），比如"Chamber.Chamber1.Arm2.Lift"；没在等为空
    /// </summary>
    public virtual string Waiting => string.Empty;

    /// <summary>
    /// 这一步手上的别家动作收完尾了没有（等别家动作的步骤用）；动作停下以后，搬运管理据此放资源
    /// </summary>
    protected internal virtual bool IsSettled => true;

    /// <summary>
    /// 判这一步失败：整个动作停在这儿；code + args 给界面，reason 只进日志
    /// </summary>
    public void Fail(string code, string reason, params string[] args)
    {
        Routine.FailAt(code, reason, args);
    }

    internal void Attach(ModuleRoutine routine)
    {
        if (_routine is not null)
        {
            throw new InvalidOperationException($"步骤 {Name} 已经加进别的动作了");
        }

        _routine = routine;
    }

    internal void Begin()
    {
        _startedAt = Stopwatch.GetTimestamp();
    }

    /// <summary>
    /// 进这一步时做一次：发指令、起别家的动作；出错调 Fail
    /// </summary>
    protected internal abstract void Run();

    /// <summary>
    /// 之后每拍判一次：true = 这一步做完；出错调 Fail
    /// </summary>
    protected internal abstract bool Check();

    /// <summary>
    /// 动作被中止时，正在走的这一步收场：作废在途的指令、中止在等的别家动作
    /// </summary>
    protected internal virtual void OnAborted(string reason)
    {
    }
}
