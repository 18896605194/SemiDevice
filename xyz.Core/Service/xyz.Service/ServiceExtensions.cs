using System.Reflection;
using System.ServiceModel;
using Microsoft.Extensions.DependencyInjection;
using xyz.Common.Log;
using xyz.Components;
using xyz.Components.Collectors;
using xyz.Components.Interfaces;
using xyz.Configs;
using xyz.Configs.Models;
using xyz.Modules;
using xyz.Tools;

namespace xyz.Service;

/// <summary>
/// 后端开机：装配组件树、组件和 gRPC 服务进容器；容器建好以后按阶段把组件起来、退出时收尾。
/// 这里只有框架自己的几步，不认识具体组件：组件开机要做的事写在它自己的开机阶段里（见 ComponentBase），加组件不用改这个文件。
/// </summary>
public static class ServiceExtensions
{
    /// <summary>
    /// 注册 xyz 后端：照 sc.xml 装组件树，组件、GEM 编号表、gRPC 服务进容器。不开机，组件等容器建好以后由 <see cref="StartComponents"/> 起
    /// </summary>
    public static IServiceCollection AddXyzServices(this IServiceCollection services)
    {
        var settings = SC.Load();
        services.AddSingleton<IReadOnlyList<ModuleConfig>>(settings);

        var roots = ComponentLoader.Load(settings);
        ComponentLoader.Register(services, roots);
        services.AddSingleton(new GemCollectors());

        // gRPC 服务：实现了契约的类自动认
        foreach (var (contract, implementation) in GrpcServices())
        {
            services.AddTransient(contract, implementation);
        }

        return services;
    }

    /// <summary>
    /// 开机（容器建好以后、gRPC 开始监听之前）：查必配的组件、合并 EC、编 GEM 编号表，再按阶段把组件起来：准备 → 绑定 → 开始扫描 → 都起来以后
    /// </summary>
    public static void StartComponents(this IServiceProvider provider)
    {
        IocHelper.ServiceProvider = provider;
        var roots = IocHelper.GetRequiredService<IReadOnlyList<ComponentBase>>();
        Require<ISystemModule>("System", "整机");
        Require<IMapperComponent>("Mapper", "对象映射（报警、EC、曲线推给界面都靠它转）");
        Require<ILogComponent>("Log", "日志（转给界面的实时日志、最近日志）");

        // 组件开机准备就要读 EC，所以排在开机阶段前面
        var ec = IocHelper.GetService<IEcComponent>();
        if (ec is not null)
        {
            ec.Merge(roots);
        }
        else
        {
            LogHelper.Warn("EC", "sc.xml 没配 EC 节点：EC 参数全按代码默认值走，改了也不落盘");
        }

        IocHelper.GetRequiredService<GemCollectors>().Merge(roots, SC.ConfigDirectory);

        ComponentLoader.InitAll(roots);
        ComponentLoader.BindAll(roots);
        ComponentLoader.StartScanning(roots);
        ComponentLoader.StartedAll(roots);

        var scanningRoots = roots.Where(root => root.IsEnabled && root.IsRun).ToList();
        LogHelper.Info($"根组件装配 {roots.Count} 个，模块 {IocHelper.GetServices<IModule>().Count} 个，启动独立扫描 {scanningRoots.Count} 个："
            + string.Join(", ", scanningRoots.Select(root => root.FullPath)));
    }

    /// <summary>
    /// 宿主退出：各组件自己收尾（EAP 断链路、数据曲线落库、断 PLC、晶圆账存盘），按开机的倒序
    /// </summary>
    public static void StopComponents()
    {
        ComponentLoader.StopAll(IocHelper.GetRequiredService<IReadOnlyList<ComponentBase>>());
    }

    /// <summary>
    /// 实现了 gRPC 契约（xyz.Shared 里带 [ServiceContract] 的接口）的类：在装进来的 xyz 程序集里找，宿主、机型 DLL 里的也算
    /// </summary>
    public static IReadOnlyList<(Type Contract, Type Implementation)> GrpcServices()
    {
        var found = new List<(Type Contract, Type Implementation)>();
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic || assembly.GetName().Name?.StartsWith("xyz.", StringComparison.OrdinalIgnoreCase) != true)
            {
                continue;
            }

            foreach (var type in LoadableTypes(assembly))
            {
                if (!type.IsClass || type.IsAbstract || !type.IsPublic)
                {
                    continue;
                }

                foreach (var contract in type.GetInterfaces())
                {
                    if (contract.GetCustomAttribute<ServiceContractAttribute>() is not null)
                    {
                        found.Add((contract, type));
                    }
                }
            }
        }

        return found;
    }

    private static Type[] LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.Where(type => type is not null).Select(type => type!).ToArray();
        }
    }

    /// <summary>
    /// 平台必配的组件：没配开机就报
    /// </summary>
    private static void Require<T>(string node, string what) where T : class
    {
        if (IocHelper.GetService<T>() is null)
        {
            throw new InvalidOperationException($"sc.xml 没配 {node} 节点（{what}），这是必配的");
        }
    }
}
