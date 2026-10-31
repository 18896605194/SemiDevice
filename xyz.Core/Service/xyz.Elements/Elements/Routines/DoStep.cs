using xyz.Shared.Dtos;

namespace xyz.Elements.Elements;

/// <summary>
/// 一步：当场做一下（检查、记账），返回不成功就失败
/// </summary>
internal sealed class DoStep : RoutineStep
{
    private readonly Func<HandleResult> _run;

    public DoStep(string name, Func<HandleResult> run) : base(name)
    {
        _run = run;
    }

    protected internal override void Send()
    {
        var result = _run();
        if (!result.IsSuccess)
        {
            Fail(result.ErrorMessage, $"不过：{result.ErrorMessage}", result.Args.ToArray());
        }
    }

    protected internal override bool Check()
    {
        return true;
    }
}
