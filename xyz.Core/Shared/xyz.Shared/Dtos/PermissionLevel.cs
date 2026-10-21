namespace xyz.Shared.Dtos;

/// <summary>
/// 权限档位
/// </summary>
public enum PermissionLevel
{
    /// <summary>
    /// 隐藏：菜单里看不到
    /// </summary>
    Hidden = 0,

    /// <summary>
    /// 只看：能进页面、能查询，改东西的按钮灰掉；功能项就是"不允许"
    /// </summary>
    View = 1,

    /// <summary>
    /// 能操作；功能项就是"允许"
    /// </summary>
    Operate = 2,
}
