using xyz.Components.Components;
using xyz.Drivers.Loadport;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;

namespace xyz.Modules;

public sealed class LoadPortCommandOperation : ModuleOperation<LoadPortCommandStep>
{
    private readonly Func<LoadPortCommand?> _send;
    private readonly Func<int> _timeoutMs;
    private readonly Func<LoadPortResponse, HandleResult>? _onSuccess;
    private LoadPortCommand? _command;

    /// <param name="name">动作名（错误参数、日志里用，如 "Load"）。</param>
    /// <param name="send">发指令：返回受理了的指令；被拒（没连上、同名指令在途）返回 null。</param>
    /// <param name="timeoutMs">超时，每拍现取（取模块的 EC，在线改了下一拍就生效）。</param>
    /// <param name="onSuccess">指令成功之后、动作算完成之前要做的事（比如 Load 查 Mapping 结果、落账）；
    /// 返回失败（错误码 + 参数）就按它判动作失败——设备做完了、但结果不能用。没有为 null。</param>
    public LoadPortCommandOperation(string name, Func<LoadPortCommand?> send, Func<int> timeoutMs,
        Func<LoadPortResponse, HandleResult>? onSuccess = null) : base(name, LoadPortCommandStep.SendCommand)
    {
        _send = send;
        _timeoutMs = timeoutMs;
        _onSuccess = onSuccess;
    }

    protected override void OnScan()
    {
        switch (Step)
        {
            case LoadPortCommandStep.SendCommand:
                SendCommand();
                break;

            case LoadPortCommandStep.WaitCommand:
                WaitCommand();
                break;

            default:
                Fail(ErrorCodes.OperationFaulted, $"未知步骤: {Step}", Name, Step.ToString());
                break;
        }
    }

    private void SendCommand()
    {
        _command = _send();
        if (_command is null)
        {
            Fail(ErrorCodes.CommandRejected, "指令被拒绝（未连接或在途）", Name);
            return;
        }

        SetStep(LoadPortCommandStep.WaitCommand);
    }

    private void WaitCommand()
    {
        var command = _command;
        if (command is null)
        {
            Fail(ErrorCodes.OperationFaulted, "等指令时没有指令", Name, Step.ToString());
            return;
        }

        if (command.IsCompleted)
        {
            var response = command.Response;
            if (response is not null && response.IsSuccess)
            {
                var handled = _onSuccess?.Invoke(response);
                if (handled is not null && !handled.IsSuccess)
                {
                    Fail(handled.ErrorMessage, $"{Name} 设备做完了，但结果不能用：{handled.ErrorMessage}", handled.Args.ToArray());
                    return;
                }

                Complete();
                return;
            }

            string error = response?.Error ?? string.Empty;
            Fail(ErrorCodes.DeviceFailed, error, Name, error);
            return;
        }

        int timeout = _timeoutMs();
        if (Watch.ElapsedMilliseconds > timeout)
        {
            Fail(ErrorCodes.Timeout, $"{Name} 动作超时（{timeout}ms）", Name, timeout.ToString());
        }
    }
}
