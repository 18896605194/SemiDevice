namespace xyz.Components.Enums;

/// <summary>
/// CJ 的 Cancel / Stop / Abort 带的 Action（E94.1 CPVAL）：它名下还在排队的 PJ 留着还是删掉。
/// </summary>
public enum ControlJobAction
{
    /// <summary>SAVEJOBS：排队的 PJ 留着（不再归这个 CJ）。</summary>
    SaveJobs = 0,

    /// <summary>REMOVEJOBS：排队的 PJ 一起删掉。</summary>
    RemoveJobs = 1,
}
