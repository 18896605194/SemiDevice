namespace xyz.Client.Setting.Models;

/// <summary>
/// 菜单模型。
/// </summary>
public class MenuModel
{
    /// <summary>
    /// 菜单名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 菜单编码，对应数据库 Menu.Code，用于路由/页面定位。
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 子菜单。
    /// </summary>
    public List<MenuModel> Children { get; set; } = new();
}
