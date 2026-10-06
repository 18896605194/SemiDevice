namespace xyz.Components.Enums;

/// <summary>
/// 命令是谁下的：本地界面、Host（EAP）、恢复。决定能不能下（Host 要在 Online Remote 下才收，以后接 E30 控制状态时用）。
/// </summary>
public enum JobCommandSource
{
    Local,
    Host,
    Recovery,
}
