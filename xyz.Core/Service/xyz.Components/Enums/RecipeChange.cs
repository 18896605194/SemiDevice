namespace xyz.Components.Enums;

/// <summary>
/// 配方怎么变的（报给 Host 的"配方变了"事件里带的，值照 SEMI E30 的 PPChangeStatus）。
/// </summary>
public enum RecipeChange
{
    /// <summary>新建了。</summary>
    Created = 1,

    /// <summary>内容改了（保存、Host 覆盖）。</summary>
    Edited = 2,

    /// <summary>删掉了。</summary>
    Deleted = 3,
}
