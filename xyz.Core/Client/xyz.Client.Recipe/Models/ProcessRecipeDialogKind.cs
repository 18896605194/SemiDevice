namespace xyz.Client.Recipe.Models;

/// <summary>
/// 工艺配方页正开着的确认框。
/// </summary>
public enum ProcessRecipeDialogKind
{
    None,

    /// <summary>
    /// 新建：填名称。
    /// </summary>
    Create,

    /// <summary>
    /// 重命名：改名称。
    /// </summary>
    Rename,

    /// <summary>
    /// 删除确认。
    /// </summary>
    Delete,

    /// <summary>
    /// 改了没保存时点别的编号：问要不要放弃修改。
    /// </summary>
    Discard,
}
