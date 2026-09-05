using SqlSugar;
using xyz.Database;

namespace xyz.Database.Auth;

/// <summary>
/// 权限实体（RBAC 模型）
/// </summary>
public class PermissionEntity : BaseEntity
{
    /// <summary>
    /// 权限名称，例如：用户管理
    /// </summary>
    [SugarColumn(Length = 100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 权限编码，例如：system:user:create
    /// </summary>
    [SugarColumn(Length = 100)]
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 权限描述
    /// </summary>
    [SugarColumn(Length = 500, IsNullable = true)]
    public string? Description { get; set; }

    /// <summary>
    /// 父级权限 Id，用于树形权限（为空表示顶级权限）
    /// </summary>
    [SugarColumn(IsNullable = true)]
    public long? ParentId { get; set; }
}
