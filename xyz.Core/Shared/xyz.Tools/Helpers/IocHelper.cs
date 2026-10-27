using Microsoft.Extensions.DependencyInjection;

namespace xyz.Tools;

/// <summary>
/// 统一 IoC 容器获取入口，前后端共用
/// </summary>
public static class IocHelper
{
    public static IServiceProvider? ServiceProvider { get; set; }

    public static T GetRequiredService<T>() where T : class
    {
        return GetProvider().GetRequiredService<T>();
    }

    public static T? GetService<T>() where T : class
    {
        return GetProvider().GetService<T>();
    }

    public static IReadOnlyList<T> GetServices<T>() where T : class
    {
        return GetProvider().GetServices<T>().ToList();
    }

    public static object GetRequiredService(Type serviceType)
    {
        return GetProvider().GetRequiredService(serviceType);
    }

    public static T GetRequiredKeyedService<T>(string key) where T : class
    {
        return GetProvider().GetRequiredKeyedService<T>(key);
    }

    public static object GetRequiredKeyedService(Type serviceType, string key)
    {
        return GetProvider().GetRequiredKeyedService(serviceType, key);
    }

    private static IServiceProvider GetProvider()
    {
        return ServiceProvider ?? throw new InvalidOperationException("IocHelper.ServiceProvider 未初始化");
    }
}
