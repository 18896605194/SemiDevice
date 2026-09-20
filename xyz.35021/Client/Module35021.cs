using Microsoft.Extensions.DependencyInjection;
using xyz.Client.Modules;

namespace xyz._35021.Client;

[ClientModule("35021", "35021 机型客户端模块")]
public sealed class Module35021 : IClientModule
{
    /// <inheritdoc />
    public void Register(IServiceCollection services)
    {
        services.AddXyz35021ClientServices();
    }

    /// <inheritdoc />
    public string? PresentationAssembly => "xyz.35021.Client.Presentation";
}
