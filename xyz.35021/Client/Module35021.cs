using Microsoft.Extensions.DependencyInjection;
using xyz.Client.Modules;

namespace xyz._35021.Client;

[ClientModule("35021", "35021 机型客户端模块")]
public sealed class Module35021 : IClientModule, IClientMenuProvider
{
    public void Register(IServiceCollection services)
    {
        services.AddXyz35021ClientServices();
    }

    public IReadOnlyList<ClientMenu> Menus { get; } = new[]
    {
        new ClientMenu("Manual", "LoadPort1 手动", "Manual.LoadPort1", 1),
        new ClientMenu("Manual", "LoadPort2 手动", "Manual.LoadPort2", 2),
    };
}
