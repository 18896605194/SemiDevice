namespace xyz.Modules;

/// <summary>提交给 PJ 状态机的动作。</summary>
public enum ProcessStateAction
{
    Queue,
    Setup,
    WaitForStart,
    Activate,
    Start,
    Complete,
    Finish,
    Pause,
    FinishPause,
    Resume,
    Stop,
    FinishStop,
    Abort,
    FinishAbort,
    Dequeue,
}
