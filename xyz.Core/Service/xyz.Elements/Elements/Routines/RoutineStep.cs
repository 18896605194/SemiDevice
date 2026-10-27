using System.Diagnostics;

namespace xyz.Elements.Elements;

/// <summary>
/// 动作里的一步
/// </summary>
public abstract class RoutineStep
{
    #region 字段

    /// <summary>
    /// 这一步加进了哪个动作；还没加为 null
    /// </summary>
    private ModuleRoutine? _routine;

    /// <summary>
    /// 开始的时间戳
    /// </summary>
    private long _startedAt;

    #endregion

    protected RoutineStep(string name)
    {
        Name = name;
    }

    #region 这一步的信息（动作、日志读）

    /// <summary>
    /// 步骤的名字
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

    #endregion

    #region 出错

    /// <summary>
    /// 判这一步失败：整个动作停在这儿；code + args 给界面，reason 只进日志
    /// </summary>
    public void Fail(string code, string reason, params string[] args)
    {
        Routine.FailAt(code, reason, args);
    }

    #endregion

    #region 推进（动作调）

    /// <summary>
    /// 加进动作时挂上：一个步骤只能加进一个动作
    /// </summary>
    internal void Attach(ModuleRoutine routine)
    {
        if (_routine is not null)
        {
            throw new InvalidOperationException($"步骤 {Name} 已经加进别的动作了");
        }

        _routine = routine;
    }

    /// <summary>
    /// 轮到这一步时记下开始的时刻
    /// </summary>
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

    #endregion
}
