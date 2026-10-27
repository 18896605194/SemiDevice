namespace xyz.Components.Enums;

/// <summary>
/// E84 交接进展是哪一种（见 E84Report）
/// </summary>
public enum E84ReportKind
{
    HandoffStarted,
    HandoffCompleted,
    HandoffTimedOut,
    HandoffAborted,
    AvailabilityChanged,
}
