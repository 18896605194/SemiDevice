using Microsoft.Extensions.DependencyInjection;
using System.Windows.Controls;
using xyz.Client.DataModels.ViewModels;
using xyz.Client.Recipe.ViewModels;
using xyz.Client.Recipe.Views;

namespace xyz.Client.Recipe;

/// <summary>
/// Recipe 模块服务注册扩展。
/// </summary>
public static class ServiceExtensions
{
    public static IServiceCollection AddXyzRecipeServices(this IServiceCollection services)
    {
        services.AddSingleton<SequenceViewModel>();
        services.AddSingleton<BaseViewModel>(sp => sp.GetRequiredService<SequenceViewModel>());
        services.AddSingleton<ProcessRecipeViewModel>();
        services.AddSingleton<BaseViewModel>(sp => sp.GetRequiredService<ProcessRecipeViewModel>());

        services.AddKeyedSingleton<UserControl, SequenceView>("Recipe.Sequence");
        services.AddKeyedSingleton<UserControl, ProcessRecipeView>("Recipe.Process");

        return services;
    }
}
