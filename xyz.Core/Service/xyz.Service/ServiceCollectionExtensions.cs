using Microsoft.Extensions.DependencyInjection;
using xyz.Components;
using xyz.Configs;
using xyz.Configs.Models;
using xyz.Modules;
using xyz.Service.UserManger;
using xyz.Shared.Services;

namespace xyz.Service;

/// <summary>
/// 业务服务注册扩展。
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 注册 xyz 后端业务服务。
    /// </summary>
    public static IServiceCollection AddXyzServices(this IServiceCollection services)
    {
        #region 模块装配（SC 配置 → 组件实例 → EC 合并 → 启动扫描线程）

        var settings = SC.Load();
        services.AddSingleton<IReadOnlyList<ModuleConfig>>(settings);

        // 驱动接入后在此处补 Open（先连接后启动）。
        var roots = ComponentLoader.Load(settings);
        services.AddSingleton<IReadOnlyList<ComponentBase>>(roots);

        // 把组件树 [VariableMark(EC)] 声明合并进 ec.xml（缺的补建，已有值不动）。
        EcMerger.Merge(roots);

        var modules = roots.OfType<BaseModule>().Where(m => m.IsEnabled).ToList();

        // 先连接后启动：模块在此打开驱动连接。
        foreach (var module in modules)
        {
            if (!module.Open())
            {
                Console.WriteLine($"模块 {module.Name} 驱动连接失败");
            }
        }

        foreach (var module in modules)
        {
            module.Start();
        }

        Console.WriteLine($"组件装配 {roots.Count} 个，启动模块 {modules.Count} 个：{string.Join(", ", modules.Select(m => m.Name))}");

        #endregion

        #region gRPC 服务注册

        RoleMappingConfig.Register();

        services.AddTransient<IRoleService, RoleService>();
        services.AddTransient<IUserService, UserService>();
        services.AddTransient<IMenuService, MenuService>();
        services.AddTransient<ILoadPortService, LoadPortService>();

        #endregion

        return services;
    }
}
