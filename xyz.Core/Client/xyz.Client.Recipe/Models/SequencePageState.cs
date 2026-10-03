namespace xyz.Client.Recipe.Models;

/// <summary>
/// 页面能不能用：Ready 才显示列表和编辑区，其他情况正中给一句提示。
/// </summary>
public enum SequencePageState
{
    /// <summary>没连上后端。</summary>
    Offline,

    /// <summary>连上了，还没拉到列表。</summary>
    Loading,

    /// <summary>后端没装流程配方库（sc.xml 没配 Sequence 节点）。</summary>
    NotInstalled,

    Ready,
}
