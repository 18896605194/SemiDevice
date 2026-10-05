namespace xyz.Modules;

/// <summary>
/// 状态、命令照 SEMI 原文的叫法（日志、错误码参数里用）：界面上的中文叫法按状态值查语言包，这里只管跟标准对得上的英文名。
/// </summary>
public static class JobNames
{
    public static string Of(PrJobState? state)
    {
        return state switch
        {
            null => "(no state)",
            PrJobState.QueuedPooled => "QUEUED/POOLED",
            PrJobState.SettingUp => "SETTING UP",
            PrJobState.WaitingForStart => "WAITING FOR START",
            PrJobState.Processing => "PROCESSING",
            PrJobState.ProcessComplete => "PROCESS COMPLETE",
            PrJobState.Pausing => "PAUSING",
            PrJobState.Paused => "PAUSED",
            PrJobState.Stopping => "STOPPING",
            PrJobState.Aborting => "ABORTING",
            PrJobState.Stopped => "STOPPED",
            PrJobState.Aborted => "ABORTED",
            _ => state.Value.ToString(),
        };
    }

    public static string Of(CtrlJobState? state)
    {
        return state switch
        {
            null => "(no state)",
            CtrlJobState.Queued => "QUEUED",
            CtrlJobState.Selected => "SELECTED",
            CtrlJobState.WaitingForStart => "WAITINGFORSTART",
            CtrlJobState.Executing => "EXECUTING",
            CtrlJobState.Paused => "PAUSED",
            CtrlJobState.Completed => "COMPLETED",
            _ => state.Value.ToString(),
        };
    }

    public static string Of(CtrlJobCommand command)
    {
        return "CJ" + command;
    }

    public static string Of(PrJobCommand command)
    {
        return command.ToString().ToUpperInvariant();
    }
}
