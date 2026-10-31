using System.Globalization;
using xyz.Shared.Dtos;

namespace xyz.Elements.Elements;

/// <summary>
/// 一步：等条件成立；等太久按给的错误码失败
/// </summary>
internal sealed class UntilStep : RoutineStep
{
    private readonly Func<bool> _condition;
    private readonly Func<int> _timeoutMs;
    private readonly Func<HandleResult> _onTimeout;

    public UntilStep(string name, Func<bool> condition, Func<int> timeoutMs, Func<HandleResult> onTimeout) : base(name)
    {
        _condition = condition;
        _timeoutMs = timeoutMs;
        _onTimeout = onTimeout;
    }

    protected internal override void Send()
    {
    }

    protected internal override bool Check()
    {
        if (_condition())
        {
            return true;
        }

        // 上限 0 就是不等：判一次不成立就失败
        int timeout = _timeoutMs();
        if (ElapsedMs >= timeout)
        {
            var result = _onTimeout();
            Fail(result.ErrorMessage, $"等了 {timeout.ToString(CultureInfo.InvariantCulture)} ms 没等到", result.Args.ToArray());
        }

        return false;
    }
}
