using SqlSugar;
using xyz.Database;

namespace xyz.Database.Auth;

/// <summary>
/// 角色权限表：一个角色对一项权限一行（没配过的项没有行）
/// </summary>
[SugarTable("account_role_permission")]
public class RolePermissionEntity : BaseEntity
{
    /// <summary>
    /// 角色编号
    /// </summary>
    public long RoleId { get; set; }

    /// <summary>
    /// 权限项编码（菜单 Code 或功能项编码）
    /// </summary>
    [SugarColumn(Length = 100)]
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 档位：0 隐藏、1 只看、2 能操作（xyz.Shared 的 PermissionLevel）
    /// </summary>
    public int Level { get; set; }
}
