using xyz.Components.Enums;

namespace xyz.Components.Interfaces;

/// <summary>
/// E30（工艺程序管理）的设备侧上报口：流程配方库、工艺配方库各自变了就调这里自己的那个方法
/// </summary>
public interface IE30Callback
{
    /// <summary>流程配方建了、改了、删了（改名报两次：旧名删了、新名建了）。</summary>
    void SequenceChanged(string name, ChangeKind change);

    /// <summary>工艺配方建了、改了、删了（改名报两次：旧名删了、新名建了）。</summary>
    void ProcessRecipeChanged(string name, ChangeKind change);
}
