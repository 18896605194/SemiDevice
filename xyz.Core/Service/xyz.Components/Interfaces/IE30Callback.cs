using xyz.Components.Enums;

namespace xyz.Components.Interfaces;

/// <summary>
/// E30的设备侧上报口：流程配方库、工艺配方库各自变了就调这里自己的那个方法
/// </summary>
public interface IE30Callback
{
    void SequenceChanged(string name, ChangeKind change);

    void ProcessRecipeChanged(string name, ChangeKind change);
}
