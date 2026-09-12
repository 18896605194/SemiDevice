using Microsoft.Extensions.DependencyInjection;
using xyz.Client.Modules;

namespace xyz._35021.Client;

/// <summary>
/// 35021 机型客户端模块：由平台壳扫描加载，只负责本机型的页面与菜单声明。
/// 本工程是类库，不是客户端入口；入口始终是平台 xyz.Client.exe。
/// </summary>
[ClientModule("35021", "35021 机型客户端模块")]
public sealed class Module35021 : IClientModule, IClientMenuProvider
{
    /// <inheritdoc />
    public void Register(IServiceCollection services)
    {
        services.AddXyz35021ClientServices();
    }

    /// <inheritdoc />
    public IReadOnlyList<ClientMenu> Menus { get; } = new[]
    {
        new ClientMenu("Manual", "LoadPort 手动", "Manual.LoadPorts", 1),
    };

    /// <inheritdoc />
    public IReadOnlyList<string> RetiredMenuCodes { get; } = new[]
    {
        // 旧版本一个 LoadPort 一个菜单项，改成大手动界面后清理掉。
        "Manual.LoadPort1",
        "Manual.LoadPort2",
    };
}
