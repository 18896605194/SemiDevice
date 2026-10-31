namespace xyz.Elements.Enums;

/// <summary>
/// CJ 内部状态，与参考工程一致；EAP 单独转换成 E94 状态值。
/// 数值是跨边界契约（JobDto.State 就是它），不能改
/// </summary>
public class ControlJobState
{
    public const int Created = 0;

    public const int Queued = 1;

    public const int Selected = 2;

    public const int Executing = 3;

    public const int Paused = 4;

    public const int Aborting = 5;

    public const int Aborted = 6;

    public const int Completed = 7;

    public const int WaitingForStart = 8;
}
