namespace xyz.Components.Enums;

/// <summary>CJ 内部状态，与参考工程一致；EAP 单独转换成 E94 状态值。</summary>
public enum ControlJobState
{
    Created = 0,
    Queued = 1,
    Selected = 2,
    Executing = 3,
    Paused = 4,
    Aborting = 5,
    Aborted = 6,
    Completed = 7,
    WaitingForStart = 8,
}
