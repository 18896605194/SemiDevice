namespace xyz.Components.Wafers;

/// <summary>
/// 片的工艺状态，由工艺模块在开始/结束时改。
/// </summary>
public enum WaferProcessState
{
    /// <summary>还没做。</summary>
    Idle = 0,

    /// <summary>工艺中。</summary>
    InProcess = 1,

    /// <summary>做完了。</summary>
    Completed = 2,

    /// <summary>做失败了。</summary>
    Failed = 3,

    /// <summary>中途被中止。</summary>
    Aborted = 4,
}
