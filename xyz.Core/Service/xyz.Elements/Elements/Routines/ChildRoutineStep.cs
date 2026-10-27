using xyz.Shared.Dtos;
using xyz.Shared.Errors;

namespace xyz.Elements.Elements;

/// <summary>
/// 一步：起一个别家的动作、等它收完尾。被拒、没做成就失败（给了收场办法先交给它，没判就原样带它的错误码）；本动作被中止时它跟着中止
/// </summary>
internal sealed class ChildRoutineStep : RoutineStep
{
    private readonly Func<HandleResult<ModuleRoutine>> _start;
    private readonly Func<HandleResult>? _onSuccess;
    private readonly Action<RoutineStep, ModuleRoutine>? _onFailed;

    /// <summary>
    /// 起来的那个别家动作；还没起为 null
    /// </summary>
    private ModuleRoutine? _child;

    public ChildRoutineStep(string name, Func<HandleResult<ModuleRoutine>> start, Func<HandleResult>? onSuccess,
        Action<RoutineStep, ModuleRoutine>? onFailed) : base(name)
    {
        _start = start;
        _onSuccess = onSuccess;
        _onFailed = onFailed;
    }

    /// <summary>
    /// 在等谁的什么动作
    /// </summary>
    public override string Waiting
    {
        get
        {
            var child = _child;
            if (child is null || child.IsSettled)
            {
                return string.Empty;
            }

            return $"{child.Module} 的 {child.Name}";
        }
    }

    protected internal override bool IsSettled
    {
        get
        {
            var child = _child;
            return child is null || child.IsSettled;
        }
    }

    protected internal override void Run()
    {
        var started = _start();
        var child = started.Result;
        if (!started.IsSuccess || child is null)
        {
            string code = started.ErrorMessage;
            if (code.Length == 0)
            {
                code = ErrorCodes.ActionRejected;
            }

            Fail(code, $"被拒：{code}", started.Args.ToArray());
            return;
        }

        _child = child;
    }

    protected internal override bool Check()
    {
        var child = _child;
        if (child is null || !child.IsSettled)
        {
            return false;
        }

        if (!child.IsSuccess)
        {
            if (_onFailed is not null)
            {
                _onFailed(this, child);
            }

            if (!Routine.IsTerminal)
            {
                Fail(child.Code, $"{child.Module} 的 {child.Name} 没成（{child.Reason}）", child.ErrorArgs.ToArray());
            }

            return false;
        }

        var check = _onSuccess;
        if (check is not null)
        {
            var handled = check();
            if (!handled.IsSuccess)
            {
                Fail(handled.ErrorMessage, $"做完了，但结果不能用：{handled.ErrorMessage}", handled.Args.ToArray());
                return false;
            }
        }

        return true;
    }

    protected internal override void OnAborted(string reason)
    {
        _child?.AbortByHost(reason);
    }
}
