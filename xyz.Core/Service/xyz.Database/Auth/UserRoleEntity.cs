using SqlSugar;
using xyz.Database;

namespace xyz.Database.Auth;

/// <summary>
/// 用户-角色关系表（RBAC 模型）
/// </summary>
public class UserRoleEntity : BaseEntity
{
    /// <summary>
    /// 用户 Id
    /// </summary>
    [SugarColumn]
    public long UserId { get; set; }

    /// <summary>
    /// 角色 Id
    /// </summary>
    [SugarColumn]
    public long RoleId { get; set; }
}
