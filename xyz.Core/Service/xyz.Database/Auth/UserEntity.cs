using SqlSugar;
using xyz.Database;

namespace xyz.Database.Auth;

/// <summary>
/// 账号表：一个账号一行
/// </summary>
[SugarTable("account_user")]
public class UserEntity : BaseEntity
{
    /// <summary>
    /// 登录名
    /// </summary>
    [SugarColumn(Length = 50)]
    public string UserName { get; set; } = string.Empty;

    /// <summary>
    /// 显示名
    /// </summary>
    [SugarColumn(Length = 100, IsNullable = true)]
    public string? DisplayName { get; set; }

    /// <summary>
    /// 密码的加盐哈希，不存原文
    /// </summary>
    [SugarColumn(Length = 200)]
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>
    /// 角色编号
    /// </summary>
    public long RoleId { get; set; }

    /// <summary>
    /// 内置账号（admin）：不能删、不能停用、不能改角色
    /// </summary>
    public bool IsBuiltIn { get; set; }

    /// <summary>
    /// 最后一次登录的时间
    /// </summary>
    [SugarColumn(IsNullable = true)]
    public DateTime? LastLoginTime { get; set; }
}
