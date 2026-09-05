using SqlSugar;
using xyz.Database;

namespace xyz.Database.Auth;

/// <summary>
/// 用户实体（RBAC 模型）
/// </summary>
[SugarTable("User")]
public class UserEntity : BaseEntity
{
    /// <summary>
    /// 登录名
    /// </summary>
    [SugarColumn(Length = 50)]
    public string UserName { get; set; } = string.Empty;

    /// <summary>
    /// 密码哈希，不存明文
    /// </summary>
    [SugarColumn(Length = 200)]
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>
    /// 显示名称
    /// </summary>
    [SugarColumn(Length = 100, IsNullable = true)]
    public string? DisplayName { get; set; }

    /// <summary>
    /// 所属角色名称
    /// </summary>
    [SugarColumn(Length = 100, IsNullable = true)]
    public string? RoleName { get; set; }

    /// <summary>
    /// 邮箱
    /// </summary>
    [SugarColumn(Length = 100, IsNullable = true)]
    public string? Email { get; set; }

    /// <summary>
    /// 手机号
    /// </summary>
    [SugarColumn(Length = 20, IsNullable = true)]
    public string? Phone { get; set; }

    /// <summary>
    /// 最后登录时间
    /// </summary>
    [SugarColumn(IsNullable = true)]
    public DateTime? LastLoginTime { get; set; }
}
