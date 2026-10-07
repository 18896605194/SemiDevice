using xyz.Components.Enums;

namespace xyz.Components.Interfaces;

/// <summary>
/// 配方库的上报口（SEMI E30 工艺程序管理）：配方建、改、删了由配方库调这里，EAP 侧据此报"配方变了"事件（PPChangeName / PPChangeStatus）。
/// 实现由 EAP 侧提供并挂到 IRecipeLibrary.E30RecipeCallback；没接 EAP 时为 null。
/// 所有上报都在 EAP 的派发组件（EapNotifierComponent）一条线程上按发生顺序串行调用：实现里可以慢，但不要死等。
/// </summary>
public interface IE30RecipeCallback
{
    /// <summary>哪个库的哪个配方怎么变了（改名报两次：旧名删了、新名建了）。</summary>
    void RecipeChanged(IRecipeLibrary library, string name, RecipeChange change);
}
