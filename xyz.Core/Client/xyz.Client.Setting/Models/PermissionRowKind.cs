namespace xyz.Client.Setting.Models;

/// <summary>
/// 角色页权限表里一行是什么
/// </summary>
public enum PermissionRowKind
{
    /// <summary>
    /// 一级菜单（下面有几项）：点哪档下面各项一起改
    /// </summary>
    Group = 0,

    /// <summary>
    /// 一级菜单本身就是一项（主界面、IO）
    /// </summary>
    Item = 1,

    /// <summary>
    /// 一级菜单下面的一项
    /// </summary>
    Child = 2,

    /// <summary>
    /// 功能项：允许 / 不允许
    /// </summary>
    Function = 3,

    /// <summary>
    /// 固定只给管理员（用户管理、角色管理）：不能配
    /// </summary>
    AdminOnly = 4,
}
