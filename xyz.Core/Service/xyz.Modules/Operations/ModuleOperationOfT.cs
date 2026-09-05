namespace xyz.Modules;

/// <summary>
/// 带步骤枚举的操作基类：每个操作定义自己的 TStep，OnScan 里 switch 推进。
/// </summary>
public abstract class ModuleOperation<TStep> : ModuleOperation where TStep : struct, Enum
{
    /// <summary>
    /// 当前步骤。
    /// </summary>
    public TStep Step { get; private set; }

    protected ModuleOperation(string name, TStep initialStep) : base(name)
    {
        Step = initialStep;
    }

    /// <summary>
    /// 走到下一步。
    /// </summary>
    protected void SetStep(TStep step)
    {
        Step = step;
    }
}
