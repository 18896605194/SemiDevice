using Microsoft.Extensions.DependencyInjection;
using xyz.Client.DataModels.ViewModels;

namespace ChamberLayoutEditor;

public static class ServiceExtensions
{
    public static IServiceCollection AddChamberLayoutEditor(this IServiceCollection services)
    {
        services.AddSingleton<ChamberEditorViewModel>();
        services.AddSingleton<BaseViewModel>(sp => sp.GetRequiredService<ChamberEditorViewModel>());
        return services;
    }
}
