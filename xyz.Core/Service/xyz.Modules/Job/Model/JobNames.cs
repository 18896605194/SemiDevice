using xyz.Components.Enums;

namespace xyz.Modules;

public static class JobNames
{
    public static string Of(ProcessJobState? state)
    {
        if (state is null)
        {
            return "(no state)";
        }

        switch (state.Value)
        {
            case ProcessJobState.QueuedPooled:
                return "QUEUED/POOLED";
            case ProcessJobState.SettingUp:
                return "SETTING UP";
            case ProcessJobState.WaitingForStart:
                return "WAITING FOR START";
            case ProcessJobState.Processing:
                return "PROCESSING";
            case ProcessJobState.ProcessComplete:
                return "PROCESS COMPLETE";
            case ProcessJobState.Pausing:
                return "PAUSING";
            case ProcessJobState.Paused:
                return "PAUSED";
            case ProcessJobState.Stopping:
                return "STOPPING";
            case ProcessJobState.Aborting:
                return "ABORTING";
            case ProcessJobState.Stopped:
                return "STOPPED";
            case ProcessJobState.Aborted:
                return "ABORTED";
            default:
                return state.Value.ToString();
        }
    }

    public static string Of(ControlJobState? state)
    {
        if (state is null)
        {
            return "(no state)";
        }

        switch (state.Value)
        {
            case ControlJobState.Create:
                return "CREATE";
            case ControlJobState.Queued:
                return "QUEUED";
            case ControlJobState.Selected:
                return "SELECTED";
            case ControlJobState.WaitingForStart:
                return "WAITINGFORSTART";
            case ControlJobState.Executing:
                return "EXECUTING";
            case ControlJobState.Paused:
                return "PAUSED";
            case ControlJobState.Completed:
                return "COMPLETED";
            default:
                return state.Value.ToString();
        }
    }

    public static string Of(ControlJobCommand command)
    {
        return "CJ" + command;
    }

    public static string Of(ProcessJobCommand command)
    {
        return command.ToString().ToUpperInvariant();
    }
}
