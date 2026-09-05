using SqlSugar;
using xyz.Database;

namespace xyz.Database.Auth;

/// <summary>
/// 菜单实体（RBAC 模型）
/// </summary>
[SugarTable("Menu")]
public class MenuEntity : BaseEntity
{
    /// <summary>
    /// 菜单名称，例如：系统管理
    /// </summary>
    [SugarColumn(Length = 100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 菜单编码，例如：system:menu
    /// </summary>
    [SugarColumn(Length = 100)]
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 路由地址，例如：/system/user
    /// </summary>
    [SugarColumn(Length = 200, IsNullable = true)]
    public string? Path { get; set; }

    /// <summary>
    /// 菜单图标
    /// </summary>
    [SugarColumn(Length = 100, IsNullable = true)]
    public string? Icon { get; set; }

    /// <summary>
    /// 父级菜单 Id，为空表示顶级菜单
    /// </summary>
    [SugarColumn(IsNullable = true)]
    public long? ParentId { get; set; }

    /// <summary>
    /// 排序号，越小越靠前
    /// </summary>
    public int Sort { get; set; }

    /// <summary>
    /// 关联权限 Id（可为空）
    /// </summary>
    [SugarColumn(IsNullable = true)]
    public long? PermissionId { get; set; }
}
