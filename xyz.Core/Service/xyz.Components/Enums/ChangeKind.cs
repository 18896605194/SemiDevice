namespace xyz.Components.Enums;

/// <summary>
/// 流程配方、工艺配方怎么被修改的
/// </summary>
public enum ChangeKind
{
    /// <summary>
    /// 新建
    /// </summary>
    Created = 1,

   /// <summary>
   /// 编辑
   /// </summary>
    Edited = 2,

    /// <summary>
    /// 删除
    /// </summary>
    Deleted = 3,
}
