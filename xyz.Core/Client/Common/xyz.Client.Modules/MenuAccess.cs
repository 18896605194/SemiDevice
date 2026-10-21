namespace xyz.Client.Modules;

/// <summary>
/// 菜单按权限怎么管
/// </summary>
public enum MenuAccess
{
    /// <summary>
    /// 隐藏 / 只看 / 能操作
    /// </summary>
    Normal = 0,

    /// <summary>
    /// 这一页没有改东西的操作（报警、数据中心）：只分隐藏 / 只看
    /// </summary>
    ViewOnly = 1,

    /// <summary>
    /// 不能隐藏（主界面）：只分只看 / 能操作
    /// </summary>
    NoHide = 2,

    /// <summary>
    /// 固定只给管理员（用户管理、角色管理）
    /// </summary>
    AdminOnly = 3,
}
