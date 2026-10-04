namespace xyz.Client.Recipe.Models;

/// <summary>
/// 工艺配方页能不能用：Ready 才显示列表和编辑区，其他情况正中给一句提示。
/// </summary>
public enum ProcessRecipePageState
{
    /// <summary>
    /// 没连上后端。
    /// </summary>
    Offline,

    /// <summary>
    /// 连上了，正在拉。
    /// </summary>
    Loading,

    /// <summary>
    /// 后端没装工艺配方库（sc.xml 没配 ProcessRecipe 节点）。
    /// </summary>
    NotInstalled,

    Ready,
}
