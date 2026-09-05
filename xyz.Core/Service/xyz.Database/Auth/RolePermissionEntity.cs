using SqlSugar;
using xyz.Database;

namespace xyz.Database.Auth;

/// <summary>
/// 角色-权限关系表（RBAC 模型）
/// </summary>
public class RolePermissionEntity : BaseEntity
{
    /// <summary>
    /// 角色 Id
    /// </summary>
    [SugarColumn]
    public long RoleId { get; set; }

    /// <summary>
    /// 权限 Id
    /// </summary>
    [SugarColumn]
    public long PermissionId { get; set; }
}
