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

    /// <summary>
    /// 正在等的别家动作（搬运管理排的机械手扫片）
    /// </summary>
    private ModuleOperation? _waiting;

    public LoadPortCommandOperation(string name, Func<LoadPortCommand?> send, Func<int> timeoutMs,
        Func<LoadPortResponse, HandleResult>? onSuccess = null) : this(name, timeoutMs)
    {
        _stages.Add(Stage.Command(name, send, onSuccess));
    }

    /// <summary>
    /// 先不带指令，后面用 Then / ThenWait 串（第一段就要等机械手扫片时用）
    /// </summary>
    public LoadPortCommandOperation(string name, Func<int> timeoutMs) : base(name, LoadPortCommandStep.SendCommand)
    {
        _timeoutMs = timeoutMs;
    }

    public LoadPortCommandOperation Then(string stageName, Func<LoadPortCommand?> send,
        Func<LoadPortResponse, HandleResult>? onSuccess = null)
    {
        _stages.Add(Stage.Command(stageName, send, onSuccess));
        return this;
    }

    /// <summary>
    /// 串一段搬运管理排的机械手扫片：发起被拒就失败，等它做完、失败带它的错误码；
    /// 这一段不按端口的超时算（等机械手空出来的时间不算，机械手扫片动作有自己的超时）
    /// </summary>
    public LoadPortCommandOperation ThenWait(string stageName, Func<HandleResult<ModuleOperation>> start,
        Func<HandleResult>? onSuccess = null)
    {
        _stages.Add(Stage.Wait(stageName, start, onSuccess));
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

    /// <summary>
    /// 被中止：排着的机械手扫片也撤掉（机械手正在扫的让它扫完，结果不用）
    /// </summary>
    protected override void OnAborted(string reason)
    {
        _waiting?.AbortByHost(reason);
    }

    private void SendCommand()
    {
        if (_stages.Count == 0)
        {
            Fail(ErrorCodes.OperationFaulted, "动作里一条指令都没串", Name, Step.ToString());
            return;
        }

        var stage = _stages[_stageIndex];
        if (stage.Start is not null)
        {
            var started = stage.Start();
            var operation = started.Result;
            if (!started.IsSuccess || operation is null)
            {
                Fail(started.ErrorMessage, $"{stage.Name} 被拒：{started.ErrorMessage}", started.Args.ToArray());
                return;
            }

            _waiting = operation;
            SetStep(LoadPortCommandStep.WaitCommand);
            return;
        }

        _command = stage.Send?.Invoke();
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
        var stage = _stages[_stageIndex];
        if (stage.Start is not null)
        {
            WaitOperation(stage);
            return;
        }

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
                var handled = stage.OnResponse?.Invoke(response);
                if (handled is not null && !handled.IsSuccess)
                {
                    Fail(handled.ErrorMessage, $"{stage.Name} 设备做完了，但结果不能用：{handled.ErrorMessage}", handled.Args.ToArray());
                    return;
                }

                NextStage();
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
    /// 等别家动作做完：成了接着做完成后的事，没成原样带它的错误码
    /// </summary>
    private void WaitOperation(Stage stage)
    {
        var operation = _waiting;
        if (operation is null)
        {
            Fail(ErrorCodes.OperationFaulted, "等别的动作时没有动作", Name, Step.ToString());
            return;
        }

        if (!operation.IsTerminal)
        {
            return;
        }

        if (!operation.IsSuccess)
        {
            Fail(operation.Code, $"{stage.Name} 没成：{operation.Reason}", operation.ErrorArgs.ToArray());
            return;
        }

        var handled = stage.OnDone?.Invoke();
        if (handled is not null && !handled.IsSuccess)
        {
            Fail(handled.ErrorMessage, $"{stage.Name} 做完了，但结果不能用：{handled.ErrorMessage}", handled.Args.ToArray());
            return;
        }

        NextStage();
    }

    /// <summary>
    /// 还有串着的就接着发下一条，全做成才算完
    /// </summary>
    private void NextStage()
    {
        if (_stageIndex + 1 < _stages.Count)
        {
            _stageIndex++;
            _command = null;
            _waiting = null;
            SetStep(LoadPortCommandStep.SendCommand);
            return;
        }

        Complete();
    }

    /// <summary>
    /// 串着的一段：发一条端口指令（Send，做成之后 OnResponse），或者等一个别家动作（Start，做成之后 OnDone）；Name 只进日志
    /// </summary>
    private sealed record Stage(string Name, Func<LoadPortCommand?>? Send, Func<LoadPortResponse, HandleResult>? OnResponse,
        Func<HandleResult<ModuleOperation>>? Start, Func<HandleResult>? OnDone)
    {
        public static Stage Command(string name, Func<LoadPortCommand?> send, Func<LoadPortResponse, HandleResult>? onResponse)
        {
            return new Stage(name, send, onResponse, null, null);
        }

        public static Stage Wait(string name, Func<HandleResult<ModuleOperation>> start, Func<HandleResult>? onDone)
        {
            return new Stage(name, null, null, start, onDone);
        }
    }
}
