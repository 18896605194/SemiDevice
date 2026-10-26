namespace xyz.Shared.Dtos;

/// <summary>
/// 账号
/// </summary>
public class UserDto
{
    public long Id { get; set; }

    /// <summary>
    /// 登录名
    /// </summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>
    /// 显示名
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// 角色名（库里存的；内置角色界面按种类取语言包显示）
    /// </summary>
    public string RoleName { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;

    public long RoleId { get; set; }

    public RoleKind RoleKind { get; set; }

    /// <summary>
    /// 内置账号（admin）：不能删、不能停用、不能改角色
    /// </summary>
    public bool IsBuiltIn { get; set; }

    /// <summary>
    /// 最后一次登录的时间；没登录过为空
    /// </summary>
    public DateTime? LastLoginTime { get; set; }
}
