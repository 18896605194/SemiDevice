namespace xyz.Elements.Enums;

/// <summary>
/// 提交给 PJ 状态表的动作名
/// </summary>
public class ProcessStateAction
{
    public const string Queue = "Queue";

    public const string Setup = "Setup";

    public const string WaitForStart = "WaitForStart";

    public const string Activate = "Activate";

    public const string Start = "Start";

    public const string Complete = "Complete";

    public const string Finish = "Finish";

    public const string Pause = "Pause";

    public const string FinishPause = "FinishPause";

    public const string Resume = "Resume";

    public const string Stop = "Stop";

    public const string FinishStop = "FinishStop";

    public const string Abort = "Abort";

    public const string FinishAbort = "FinishAbort";

    public const string Dequeue = "Dequeue";
}
