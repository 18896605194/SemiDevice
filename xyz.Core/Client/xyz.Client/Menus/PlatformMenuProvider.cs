using xyz.Client.Modules;

namespace xyz.Client.Menus;

/// <summary>
/// 平台菜单：固定的一级菜单和平台页面的二级菜单，写在代码里，不进数据库�?
/// 框架页面的二级菜单都在这里声明（�?Manual 下的手动页——单片类机台长得都一样），机型模块只写对应的界面�?
/// 真有机型独有的页面时，才由机型实�?IClientMenuProvider 挂到这里的一级菜单下�?
/// 显示名在语言包里�?menu.{Code}�?
///
/// IO 下按模块分的二级菜单是个例外：它照后�?sc.xml 里实际装的模块生成，
/// 机台配了几个模块就有几项——sc.xml 里没�?LoadPort1，菜单里就不会有 LoadPort1�?
/// </summary>
public sealed class PlatformMenuProvider : IClientMenuProvider
{
    public PlatformMenuProvider(IReadOnlyList<string> modules)
    {
        Menus =
        [
            new ClientMenu(null, "Main", 1),
            new ClientMenu(null, "Manual", 2),
            new ClientMenu(null, "Recipe", 3),
            new ClientMenu(null, "Alarm", 4),
            new ClientMenu(null, "DataCenter", 5),
            new ClientMenu(null, "Setting", 6),
            new ClientMenu(null, "Io", 7),

            new ClientMenu("Manual", "Manual.LoadPorts", 1),
            new ClientMenu("Manual", "Manual.Robot", 2),

            new ClientMenu("Alarm", "Alarm.Realtime", 1),
            new ClientMenu("Alarm", "Alarm.History", 2),

            new ClientMenu("DataCenter", "DataCenter.LogRealtime", 1),
            new ClientMenu("DataCenter", "DataCenter.LogHistory", 2),

            new ClientMenu("Setting", "Setting.User", 1),
            new ClientMenu("Setting", "Setting.Role", 2),

            .. modules.Select((module, index) => new ClientMenu("Io", $"Io.{module}", index + 1)),
        ];
    }

    /// <inheritdoc />
    public IReadOnlyList<ClientMenu> Menus { get; }
}
