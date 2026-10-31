namespace xyz.Elements.Elements;

/// <summary>
/// 一步：自己写的 run / check（模块里的方法），数据放模块字段或小状态类
/// </summary>
internal sealed class CustomStep : RoutineStep
{
    private readonly Action<RoutineStep>? _run;
    private readonly Func<RoutineStep, bool>? _check;

    public CustomStep(string name, Action<RoutineStep>? run, Func<RoutineStep, bool>? check) : base(name)
    {
        _run = run;
        _check = check;
    }

    protected internal override void Send()
    {
        _run?.Invoke(this);
    }

    protected internal override bool Check()
    {
        if (_check is null)
        {
            return true;
        }

        return _check(this);
    }
}
