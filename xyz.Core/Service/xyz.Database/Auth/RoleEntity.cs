using SqlSugar;
using xyz.Database;

namespace xyz.Database.Auth;

/// <summary>
/// 角色表：一个角色一行
/// </summary>
[SugarTable("account_role")]
public class RoleEntity : BaseEntity
{
    /// <summary>
    /// 角色名
    /// </summary>
    [SugarColumn(Length = 50)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 说明
    /// </summary>
    [SugarColumn(Length = 500, IsNullable = true)]
    public string? Description { get; set; }

    /// <summary>
    /// 种类：0 自己建的、1 内置管理员、2 内置访客（xyz.Shared 的 RoleKind）
    /// </summary>
    public int Kind { get; set; }
}
