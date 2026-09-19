using xyz.Client.Modules;

namespace xyz.Client.Menus;

/// <summary>
/// 平台菜单：固定的一级菜单和平台页面的二级菜单，写在代码里，不进数据库。
/// 机型的二级菜单由机型模块自己声明（IClientMenuProvider），挂到这里的一级菜单下。
/// 显示名在语言包里配 menu.{Code}。
/// </summary>
public sealed class PlatformMenuProvider : IClientMenuProvider
{
    /// <inheritdoc />
    public IReadOnlyList<ClientMenu> Menus { get; } = new[]
    {
        new ClientMenu(null, "Main", 1),
        new ClientMenu(null, "Manual", 2),
        new ClientMenu(null, "Recipe", 3),
        new ClientMenu(null, "Alarm", 4),
        new ClientMenu(null, "DataCenter", 5),
        new ClientMenu(null, "Setting", 6),
        new ClientMenu(null, "Io", 7),

        new ClientMenu("Alarm", "Alarm.Realtime", 1),
        new ClientMenu("Alarm", "Alarm.History", 2),

        new ClientMenu("DataCenter", "DataCenter.LogRealtime", 1),
        new ClientMenu("DataCenter", "DataCenter.LogHistory", 2),

        new ClientMenu("Setting", "Setting.User", 1),
        new ClientMenu("Setting", "Setting.Role", 2),
    };
}
