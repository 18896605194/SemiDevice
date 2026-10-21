using xyz.Shared.Dtos;

namespace xyz.Client.Presentation.Localization;

/// <summary>
/// 角色的叫法：内置的管理员、访客按语言包，自己建的就是库里的名字
/// </summary>
public static class RoleText
{
    /// <summary>
    /// 角色显示名
    /// </summary>
    public static string Of(RoleKind kind, string name)
    {
        switch (kind)
        {
            case RoleKind.Admin:
                return L10n.Get("account.role.admin");
            case RoleKind.Guest:
                return L10n.Get("account.role.guest");
            default:
                return name;
        }
    }
}
