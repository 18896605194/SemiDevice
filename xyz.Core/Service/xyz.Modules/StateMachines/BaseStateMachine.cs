using xyz.Common.Log;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;

namespace xyz.Modules.StateMachines;

/// <summary>按 (当前状态, 动作) 查表，执行检查、动作及状态通知。</summary>
public abstract class BaseStateMachine<TState, TAction> where TState : struct, Enum where TAction : struct, Enum
{
    private readonly object _stateLock = new();
    private TState _currentState;

    public Dictionary<(TState, TAction), StateTransition<TState>> Transitions { get; set; } = [];

    public event Action<TState, TState>? OnStateChanged;

    public TState CurrentState
    {
        get { return _currentState; }
        set
        {
            lock (_stateLock)
            {
                var previous = _currentState;
                _currentState = value;
                OnStateChanged?.Invoke(previous, value);
            }
        }
    }

    protected virtual TState? GetErrorState()
    {
        return null;
    }

    protected HandleResult ExecuteTransition(Dictionary<(TState, TAction), StateTransition<TState>> transitions,
        TAction action, params object[] parameters)
    {
        lock (_stateLock)
        {
            if (!transitions.TryGetValue((CurrentState, action), out var transition))
            {
                return HandleResult.Fail(ErrorCodes.JobCommandNotAllowed,
                    GetType().Name, action.ToString(), CurrentState.ToString());
            }

            HandleResult? result = null;
            try
            {
                transition.OnEntry?.Invoke(parameters);
                if (transition.PreCheck is not null && !transition.PreCheck(parameters))
                {
                    return HandleResult.Fail(ErrorCodes.JobCommandNotAllowed,
                        GetType().Name, action.ToString(), CurrentState.ToString());
                }

                if (transition.ProcessState.HasValue)
                {
                    CurrentState = transition.ProcessState.Value;
                }

                if (transition.Execute is not null)
                {
                    result = transition.Execute(parameters);
                    if (!result.IsSuccess)
                    {
                        var errorState = GetErrorState();
                        if (errorState.HasValue)
                        {
                            CurrentState = errorState.Value;
                        }

                        transition.ErrorHandler?.Invoke(result, parameters);
                        return result;
                    }
                }

                CurrentState = transition.TargetState;
                if (transition.OnExit is not null)
                {
                    var exitResult = transition.OnExit(parameters);
                    if (!exitResult.IsSuccess)
                    {
                        transition.ErrorHandler?.Invoke(exitResult, parameters);
                        return exitResult;
                    }
                }

                return result ?? HandleResult.Success();
            }
            catch (Exception exception)
            {
                var errorState = GetErrorState();
                if (errorState.HasValue)
                {
                    CurrentState = errorState.Value;
                }

                LogHelper.Error(GetType().Name, exception.Message);
                var failure = HandleResult.Fail(ErrorCodes.OperationFaulted, GetType().Name, exception.Message);
                transition.ErrorHandler?.Invoke(failure, parameters);
                return failure;
            }
        }
    }

    public virtual HandleResult StateChange(TAction action, params object[] parameters)
    {
        return ExecuteTransition(Transitions, action, parameters);
    }
}
