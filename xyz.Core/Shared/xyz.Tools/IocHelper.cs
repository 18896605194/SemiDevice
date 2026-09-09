using Microsoft.Extensions.DependencyInjection;

namespace xyz.Tools;

/// <summary>
/// 统一 IoC 容器获取入口，前后端共用（原先前后端各有一份完全相同的实现）。
/// 启动时由宿主赋值 <see cref="ServiceProvider"/>：客户端 App.OnStartup / 后端 Program。
/// </summary>
public static class IocHelper
{
    /// <summary>
    /// 微软 IOC 容器服务提供者。
    /// </summary>
    public static IServiceProvider? ServiceProvider { get; set; }

    /// <summary>
    /// 通过接口/类型获取服务，找不到会抛异常。
    /// </summary>
    public static T GetRequiredService<T>() where T : class
    {
        return GetProvider().GetRequiredService<T>();
    }

    /// <summary>
    /// 通过 Type 获取服务，找不到会抛异常。
    /// </summary>
    public static object GetRequiredService(Type serviceType)
    {
        return GetProvider().GetRequiredService(serviceType);
    }

    /// <summary>
    /// 通过接口/类型 + key 获取服务，找不到会抛异常。
    /// </summary>
    public static T GetRequiredKeyedService<T>(string key) where T : class
    {
        return GetProvider().GetRequiredKeyedService<T>(key);
    }

    /// <summary>
    /// 通过 Type + key 获取服务，找不到会抛异常。
    /// </summary>
    public static object GetRequiredKeyedService(Type serviceType, string key)
    {
        return GetProvider().GetRequiredKeyedService(serviceType, key);
    }

    private static IServiceProvider GetProvider()
    {
        return ServiceProvider
            ?? throw new InvalidOperationException("IocHelper.ServiceProvider 未初始化");
    }
}
