using SqlSugar;
using xyz.Database;

namespace xyz.Database.Auth;

/// <summary>
/// 角色实体（RBAC 模型）
/// </summary>
[SugarTable("Role")]
public class RoleEntity : BaseEntity
{
    /// <summary>
    /// 角色名称，例如：管理员
    /// </summary>
    [SugarColumn(Length = 100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 角色编码，例如：admin
    /// </summary>
    [SugarColumn(Length = 100)]
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 角色描述
    /// </summary>
    [SugarColumn(Length = 500, IsNullable = true)]
    public string? Description { get; set; }
}
