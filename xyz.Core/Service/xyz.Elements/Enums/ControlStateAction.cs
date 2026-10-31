namespace xyz.Elements.Enums;

/// <summary>
/// 提交给 CJ 状态表的动作名
/// </summary>
public class ControlStateAction
{
    public const string Queue = "Queue";

    public const string Select = "Select";

    public const string Activate = "Activate";

    public const string Deselect = "Deselect";

    public const string Resume = "Resume";

    public const string Pause = "Pause";

    public const string Complete = "Complete";

    public const string Abort = "Abort";

    public const string FinishAbort = "FinishAbort";

    public const string Rollback = "Rollback";

    #region 本设备的手动启动、Stop 和 E94 删除动作

    public const string WaitForStart = "WaitForStart";

    public const string FinishStop = "FinishStop";

    public const string Dequeue = "Dequeue";

    public const string Delete = "Delete";

    #endregion
}
