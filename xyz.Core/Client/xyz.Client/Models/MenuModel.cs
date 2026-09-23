namespace xyz.Client.Models;

/// <summary>
/// 底部导航的菜单项：由代码里声明的菜单（IClientMenuProvider）合成，不来自数据库。
/// </summary>
public class MenuModel
{
    /// <summary>
    /// 显示名：按 Code 从当前语言包取的 menu.{Code}。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 菜单编码，也是页面在 DI 里的 keyed 注册键。
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 子菜单。
    /// </summary>
    public List<MenuModel> Children { get; set; } = new();
}
