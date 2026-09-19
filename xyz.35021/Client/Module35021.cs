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
    public string? PresentationAssembly => "xyz.35021.Client.Presentation";

    /// <summary>
    /// 本机型用到的框架页面，挂在平台的 Manual 一级菜单下；菜单名是框架的，配在平台语言包（menu.{Code}）。
    /// </summary>
    public IReadOnlyList<ClientMenu> Menus { get; } = new[]
    {
        new ClientMenu("Manual", "Manual.LoadPorts", 1),
        new ClientMenu("Manual", "Manual.Transfer", 2),
    };
}
