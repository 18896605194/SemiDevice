using xyz.Client.Common.Session;
using xyz.Shared.Dtos;

namespace xyz.Client.Modules;

/// <summary>
/// 菜单对当前会话是几档：按菜单的种类收好（只看的页最多只看、主界面至少只看、只给管理员的别人隐藏）
/// </summary>
public static class MenuPermissions
{
    /// <summary>
    /// 这个菜单现在几档
    /// </summary>
    public static PermissionLevel LevelOf(ClientMenu menu)
    {
        PermissionLevel level;
        switch (menu.Access)
        {
            case MenuAccess.AdminOnly:
                return ClientSession.IsAdmin ? PermissionLevel.Operate : PermissionLevel.Hidden;
            case MenuAccess.ViewOnly:
                level = ClientSession.LevelOf(menu.PermissionCode);
                return level == PermissionLevel.Hidden ? PermissionLevel.Hidden : PermissionLevel.View;
            case MenuAccess.NoHide:
                level = ClientSession.LevelOf(menu.PermissionCode);
                return level == PermissionLevel.Hidden ? PermissionLevel.View : level;
            default:
                return ClientSession.LevelOf(menu.PermissionCode);
        }
    }

    /// <summary>
    /// 这一页要不要标"只看"：能看、不能操作，而且这一页本来有改东西的操作
    /// </summary>
    public static bool IsViewOnly(ClientMenu menu)
    {
        return menu.Access != MenuAccess.ViewOnly && LevelOf(menu) == PermissionLevel.View;
    }
}
