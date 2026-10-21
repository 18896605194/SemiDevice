using xyz.Shared.Dtos;

namespace xyz.Client.Setting.Models;

/// <summary>
/// 角色：角色页左边一行、用户页选角色的候选
/// </summary>
public class RoleModel
{
    public long Id { get; set; }

    /// <summary>
    /// 库里存的名字
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 显示名：内置的按语言包，自己建的就是库里的名字
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public RoleKind Kind { get; set; }

    /// <summary>
    /// 内置的（管理员、访客）：不能删、不能改名
    /// </summary>
    public bool IsBuiltIn => Kind != RoleKind.Normal;

    /// <summary>
    /// 右边那一小格：几个账号在用；访客是"没登录时"
    /// </summary>
    public string CountText { get; set; } = string.Empty;
}
