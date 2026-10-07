using xyz.Components.Enums;

namespace xyz.Components.Interfaces;

/// <summary>
/// E30（工艺程序管理）的设备侧上报口：流程配方库、工艺配方库各自变了就调这里自己的那个方法，EAP 侧据此报"配方变了"事件（PPChangeName / PPChangeStatus）。
/// 实现由 EAP 侧提供并挂到 ISequenceComponent.E30Callback、IProcessRecipeComponent.E30Callback；没接 EAP 时为 null。
/// 所有上报都在 EAP 的派发组件（EapNotifierComponent）一条线程上按发生顺序串行调用：实现里可以慢，但不要死等。
/// </summary>
public interface IE30Callback
{
    /// <summary>流程配方建了、改了、删了（改名报两次：旧名删了、新名建了）。</summary>
    void SequenceChanged(string name, ChangeKind change);

    /// <summary>工艺配方建了、改了、删了（改名报两次：旧名删了、新名建了）。</summary>
    void ProcessRecipeChanged(string name, ChangeKind change);
}
