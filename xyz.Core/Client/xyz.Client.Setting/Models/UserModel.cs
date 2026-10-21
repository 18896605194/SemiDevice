using xyz.Shared.Dtos;

namespace xyz.Client.Setting.Models;

/// <summary>
/// 用户页表格的一行
/// </summary>
public class UserModel
{
    public long Id { get; set; }

    public string UserName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public long RoleId { get; set; }

    public RoleKind RoleKind { get; set; }

    /// <summary>
    /// 库里存的角色名
    /// </summary>
    public string RoleName { get; set; } = string.Empty;

    /// <summary>
    /// 角色显示名：内置的按语言包
    /// </summary>
    public string RoleText { get; set; } = string.Empty;

    public bool IsEnabled { get; set; }

    /// <summary>
    /// 内置账号（admin）：不能删、不能停用、不能改角色
    /// </summary>
    public bool IsBuiltIn { get; set; }

    /// <summary>
    /// 启用 / 停用的字
    /// </summary>
    public string StatusText { get; set; } = string.Empty;

    /// <summary>
    /// 最后登录；没登录过为空
    /// </summary>
    public string LastLoginText { get; set; } = string.Empty;

    /// <summary>
    /// 说明：内置账号、停用的写一句
    /// </summary>
    public string Note { get; set; } = string.Empty;
}
