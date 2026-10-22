using xyz.Client.Modules;
using xyz.Shared.Permissions;

namespace xyz.Client.Menus;

/// <summary>
/// 平台菜单：固定的一级菜单和平台页面的二级菜单，写在代码里，不进数据库。
/// 框架页面的二级菜单都在这里声明（含 Manual 下的手动页——单片类机台长得都一样），机型模块只写对应的界面。
/// 真有机型独有的页面时，才由机型实现 IClientMenuProvider 挂到这里的一级菜单下。
/// 显示名在语言包里配 menu.{Code}；菜单 Code 就是权限项编码（PermissionCodes），角色页、预置角色认同一份。
///
/// 按模块分的二级菜单是例外：照后端 sc.xml 里实际装的模块生成，机台配了几个就有几项，显示名就是模块名——
/// IO 下一个模块一项，Manual 下一个腔体一项（四个腔体一模一样也是四项）；sc.xml 里没配 Chamber3，菜单里就不会有 Chamber3。
/// 它们在权限上各合成一项：每个腔体的手动页都算 Manual.Chamber，每个模块的 IO 页都算 Io。
/// </summary>
public sealed class PlatformMenuProvider : IClientMenuProvider
{
    public PlatformMenuProvider(IReadOnlyList<string> modules, IReadOnlyList<string> chambers)
    {
        Menus =
        [
            // 主界面不能隐藏
            new ClientMenu(null, PermissionCodes.Main, 1) { Access = MenuAccess.NoHide },
            new ClientMenu(null, PermissionCodes.Manual, 2),
            new ClientMenu(null, PermissionCodes.Recipe, 3),
            new ClientMenu(null, PermissionCodes.Alarm, 4),
            new ClientMenu(null, PermissionCodes.DataCenter, 5),
            new ClientMenu(null, PermissionCodes.Setting, 6),
            new ClientMenu(null, PermissionCodes.Io, 7),

            new ClientMenu(PermissionCodes.Manual, PermissionCodes.ManualLoadPorts, 1),
            new ClientMenu(PermissionCodes.Manual, PermissionCodes.ManualRobot, 2),
            .. chambers.Select((chamber, index) => new ClientMenu(PermissionCodes.Manual, $"Manual.{chamber}", index + 3, chamber)
            {
                PermissionCode = PermissionCodes.ManualChamber
            }),

            // 配方：流程配方（片在设备里怎么走）、工艺配方（片在一个腔里怎么做：每一步的时间、转速、摆臂出液）
            new ClientMenu(PermissionCodes.Recipe, PermissionCodes.RecipeSequence, 1),
            new ClientMenu(PermissionCodes.Recipe, PermissionCodes.RecipeProcess, 2),

            // 报警、数据中心这几页没有改东西的操作（报警复位单独是功能项）
            new ClientMenu(PermissionCodes.Alarm, PermissionCodes.AlarmRealtime, 1) { Access = MenuAccess.ViewOnly },
            new ClientMenu(PermissionCodes.Alarm, PermissionCodes.AlarmHistory, 2) { Access = MenuAccess.ViewOnly },

            new ClientMenu(PermissionCodes.DataCenter, PermissionCodes.DataCenterLogRealtime, 1) { Access = MenuAccess.ViewOnly },
            new ClientMenu(PermissionCodes.DataCenter, PermissionCodes.DataCenterLogHistory, 2) { Access = MenuAccess.ViewOnly },
            new ClientMenu(PermissionCodes.DataCenter, PermissionCodes.DataCenterDataChart, 3) { Access = MenuAccess.ViewOnly },
            new ClientMenu(PermissionCodes.DataCenter, PermissionCodes.DataCenterRealChart, 4) { Access = MenuAccess.ViewOnly },
            new ClientMenu(PermissionCodes.DataCenter, PermissionCodes.DataCenterJobHistory, 5) { Access = MenuAccess.ViewOnly },

            new ClientMenu(PermissionCodes.Setting, PermissionCodes.SettingEc, 1),
            new ClientMenu(PermissionCodes.Setting, PermissionCodes.SettingWaferLedger, 2),
            // 用户管理、角色管理固定只给管理员：给了别人，谁都能把自己改成管理员
            new ClientMenu(PermissionCodes.Setting, PermissionCodes.SettingUser, 3) { Access = MenuAccess.AdminOnly },
            new ClientMenu(PermissionCodes.Setting, PermissionCodes.SettingRole, 4) { Access = MenuAccess.AdminOnly },

            .. modules.Select((module, index) => new ClientMenu(PermissionCodes.Io, $"Io.{module}", index + 1, module)
            {
                PermissionCode = PermissionCodes.Io
            }),
        ];

        Functions =
        [
            new ClientFunction(PermissionCodes.Main, PermissionCodes.MainSystem, 1),
            new ClientFunction(PermissionCodes.Alarm, PermissionCodes.AlarmReset, 1),
        ];
    }

    /// <inheritdoc />
    public IReadOnlyList<ClientMenu> Menus { get; }

    /// <inheritdoc />
    public IReadOnlyList<ClientFunction> Functions { get; }
}
