using System.Reflection;
using System.ServiceModel;
using Microsoft.Extensions.DependencyInjection;
using xyz.Common.Log;
using xyz.Elements;
using xyz.Elements.Collectors;
using xyz.Elements.Interfaces;
using xyz.Configs;
using xyz.Configs.Models;
using xyz.Modules;
using xyz.Tools;

namespace xyz.Service;

/// <summary>
/// 后端开机：装配元件树、元件和 gRPC 服务进容器
/// </summary>
public static class ServiceExtensions
{
    /// <summary>
    /// 装进容器（宿主 Build 之前）：照 machine.xml 装元件树，再登记 gRPC 服务和操作日志
    /// </summary>
    public static IServiceCollection AddXyzServices(this IServiceCollection services)
    {
        #region 装元件树

        var settings = MachineLoader.Load();//装机文件加载
        services.AddSingleton<IReadOnlyList<ModuleConfig>>(settings);

        var roots = ElementLoader.Load(settings);
        ElementLoader.Register(services, roots);
        services.AddSingleton(new GemCollectors());

        #endregion

        #region gRPC 服务

        // gRPC 服务：实现了契约的类自动认
        foreach (var (contract, implementation) in GrpcServices())
        {
            services.AddTransient(contract, implementation);
        }

        #endregion

        #region 操作日志

        // 操作日志：宿主的 ActionFilter 每个调用经它记
        services.AddSingleton<IActionFilter, ActionFilter>();

        #endregion

        return services;
    }

    /// <summary>
    /// 开机（容器建好以后、gRPC 开始监听之前）：查必配的元件、合并 EC、编 GEM 编号表，再按阶段把元件起来：准备 → 绑定 → 开始扫描 → 都起来以后
    /// </summary>
    public static void StartElements(this IServiceProvider provider)
    {
        #region 接上容器、查必配的元件

        IocHelper.ServiceProvider = provider;
        var roots = IocHelper.GetRequiredService<IReadOnlyList<ElementBase>>();
        Require<ISystemModule>("System", "整机");
        Require<IMapper>("Mapper", "对象映射（报警、EC、曲线推给界面都靠它转）");
        Require<ILogElement>("Log", "日志（转给界面的实时日志、最近日志）");
        Require<IAlarm>("Alarm", "报警");
        Require<IIo>("Io", "IO 点表");
        Require<IWaferSize>("WaferSize", "晶圆尺寸，配方坐标、示教点表按它取");

        #endregion

        #region 合并 EC、编 GEM 编号表

        // 元件开机准备就要读 EC，所以排在开机阶段前面
        var ec = IocHelper.GetService<IEc>();
        if (ec is not null)
        {
            ec.Merge(roots);
        }
        else
        {
            LogHelper.Warn(LogHelper.HostModule, "machine.xml 没配 EC 节点：EC 参数全按代码默认值走，改了也不落盘");
        }

        IocHelper.GetRequiredService<GemCollectors>().Merge(roots, MachineLoader.ConfigDirectory);

        #endregion

        #region 开机各阶段：准备 → 绑定 → 开始扫描 → 都起来以后 → 开机推送

        ElementLoader.InitAll(roots);
        ElementLoader.BindAll(roots);
        ElementLoader.StartScanning(roots);
        ElementLoader.NotifyStartedAll(roots);
        ElementLoader.PublishStartupAll(roots);

        #endregion

        #region 开机汇总日志

        var scanningRoots = roots.Where(root => root.IsEnabled && root.IsRun).ToList();
        LogHelper.Info(LogHelper.HostModule, $"根元件装配 {roots.Count} 个，模块 {IocHelper.GetServices<IModule>().Count} 个，启动独立扫描 {scanningRoots.Count} 个："+ string.Join(", ", scanningRoots.Select(root => root.FullPath)));

        #endregion
    }

    /// <summary>
    /// 宿主退出：各元件自己收尾,提前注册好的
    /// </summary>
    public static void StopElements()
    {
        ElementLoader.StopAll(IocHelper.GetRequiredService<IReadOnlyList<ElementBase>>());
    }

    #region 开机用到的小方法

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

    /// <summary>
    /// 程序集里能加载的类型：有的类型依赖没装的 DLL，跳过它们、别让整个程序集都认不了
    /// </summary>
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
    /// 平台必配的元件：没配开机就报
    /// </summary>
    private static void Require<T>(string node, string what) where T : class
    {
        if (IocHelper.GetService<T>() is null)
        {
            throw new InvalidOperationException($"machine.xml 没配 {node} 节点（{what}），这是必配的");
        }
    }

    #endregion
}
