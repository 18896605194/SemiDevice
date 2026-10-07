namespace xyz.Modules;

/// <summary>提交给 CJ 状态机的动作。</summary>
public enum ControlStateAction
{
    Queue,
    Select,
    Activate,
    Deselect,
    Resume,
    Pause,
    Complete,
    Abort,
    FinishAbort,
    Rollback,

    // 本设备的手动启动、Stop 和 E94 删除动作。
    WaitForStart,
    FinishStop,
    Dequeue,
    Delete,
}
