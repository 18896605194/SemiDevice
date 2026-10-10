using xyz.Components.Components;
using xyz.Drivers.Loadport;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;

namespace xyz.Modules;

/// <summary>
/// LoadPort 平台默认动作：发驱动指令 → 等完结 → 超时判失败。一般一个动作一条指令；
/// 设备没有一条指令就做完的（比如 FCD 没有带图卸载，要关好门再主动扫一遍图），用 <see cref="Then"/> 把后面的指令串上，
/// 前一条做成了才发下一条，全做成才算动作完成。
/// </summary>
public sealed class LoadPortCommandOperation : ModuleOperation<LoadPortCommandStep>
{
    private readonly List<Stage> _stages = [];
    private readonly Func<int> _timeoutMs;
    private int _stageIndex;
    private long _sentAtMs;
    private LoadPortCommand? _command;

    /// <param name="name">动作名（错误参数、日志里用，如 "Load"）。</param>
    /// <param name="send">发指令：返回受理了的指令；被拒（没连上、同名指令在途）返回 null。</param>
    /// <param name="timeoutMs">超时，每拍现取（取模块的 EC，在线改了下一拍就生效）；串了好几条的，每条各按它算。</param>
    /// <param name="onSuccess">指令成功之后、动作算完成之前要做的事（比如 Load 查 Mapping 结果、落账）；
    /// 返回失败（错误码 + 参数）就按它判动作失败——设备做完了、但结果不能用。没有为 null。</param>
    public LoadPortCommandOperation(string name, Func<LoadPortCommand?> send, Func<int> timeoutMs,
        Func<LoadPortResponse, HandleResult>? onSuccess = null) : base(name, LoadPortCommandStep.SendCommand)
    {
        _timeoutMs = timeoutMs;
        _stages.Add(new Stage(name, send, onSuccess));
    }

    /// <summary>
    /// 前一条指令做成了（连同它的成功后处理）再接着发一条；前一条没做成整个动作就判失败，后面的不发——比如 Unload 关门没成就不扫图。
    /// 错误码参数仍用动作名（界面、报警按动作认），stageName 只进日志，好分清是哪一条出的事。
    /// </summary>
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
