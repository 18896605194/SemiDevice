namespace xyz.Shared.Dtos;

/// <summary>
/// 角色的种类
/// </summary>
public enum RoleKind
{
   /// <summary>
   /// 自己创建
   /// </summary>
    Normal = 0,

    /// <summary>
    /// 内置管理
    /// </summary>
    Admin = 1,

    /// <summary>
    /// 内置访客
    /// </summary>
    Guest = 2,
}
