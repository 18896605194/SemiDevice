namespace xyz.Client.Recipe.Models;

/// <summary>
/// 页面里正开着的确认框；None 时关着。
/// </summary>
public enum SequenceDialogKind
{
    None,

    /// <summary>新建：填名称。</summary>
    Create,

    /// <summary>重命名：改名称。</summary>
    Rename,

    /// <summary>删除确认。</summary>
    Delete,

    /// <summary>有没保存的修改，切到别的编号前问一声。</summary>
    Discard,
}
