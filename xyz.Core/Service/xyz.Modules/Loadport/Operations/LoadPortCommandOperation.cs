using xyz.Components.Components;
using xyz.Drivers.Loadport;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;

namespace xyz.Modules;

public sealed class LoadPortCommandOperation : ModuleOperation<LoadPortCommandStep>
{
    private readonly List<Stage> _stages = [];
    private readonly Func<int> _timeoutMs;
    private int _stageIndex;
    private long _sentAtMs;
    private LoadPortCommand? _command;

    public LoadPortCommandOperation(string name, Func<LoadPortCommand?> send, Func<int> timeoutMs,
        Func<LoadPortResponse, HandleResult>? onSuccess = null) : base(name, LoadPortCommandStep.SendCommand)
    {
        _timeoutMs = timeoutMs;
        _stages.Add(new Stage(name, send, onSuccess));
    }

    public LoadPortCommandOperation Then(string stageName, Func<LoadPortCommand?> send,
        Func<LoadPortResponse, HandleResult>? onSuccess = null)
    {
        _stages.Add(new Stage(stageName, send, onSuccess));
        return this;
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
        var stage = _stages[_stageIndex];
        _command = stage.Send();
        if (_command is null)
        {
            Fail(ErrorCodes.CommandRejected, $"{stage.Name} 指令被拒绝（未连接或在途）", Name);
            return;
        }

        // 第一条照旧从动作发起算超时；串着的后一条从它自己发出去算——Watch 是整个动作共用的表，不能把前一条用掉的时间也算进去。
        if (_stageIndex > 0)
        {
            _sentAtMs = Watch.ElapsedMilliseconds;
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

        var stage = _stages[_stageIndex];
        if (command.IsCompleted)
        {
            var response = command.Response;
            if (response is not null && response.IsSuccess)
            {
                var handled = stage.OnSuccess?.Invoke(response);
                if (handled is not null && !handled.IsSuccess)
                {
                    Fail(handled.ErrorMessage, $"{stage.Name} 设备做完了，但结果不能用：{handled.ErrorMessage}", handled.Args.ToArray());
                    return;
                }

                // 还有串着的就接着发下一条，全做成才算完。
                if (_stageIndex + 1 < _stages.Count)
                {
                    _stageIndex++;
                    _command = null;
                    SetStep(LoadPortCommandStep.SendCommand);
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
        if (Watch.ElapsedMilliseconds - _sentAtMs > timeout)
        {
            Fail(ErrorCodes.Timeout, $"{stage.Name} 动作超时（{timeout}ms）", Name, timeout.ToString());
        }
    }

    /// <summary>
    /// 串着的一条指令：叫什么（只进日志）、怎么发、做成之后要做的事。
    /// </summary>
    private sealed record Stage(string Name, Func<LoadPortCommand?> Send, Func<LoadPortResponse, HandleResult>? OnSuccess);
}
