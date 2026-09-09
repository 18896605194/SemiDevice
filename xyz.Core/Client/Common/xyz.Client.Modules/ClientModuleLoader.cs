using System.IO;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace xyz.Client.Modules;

/// <summary>
/// 客户端机型模块装载器：扫描运行目录及 Modules 子目录下的 DLL，
/// 把带 <see cref="ClientModuleAttribute"/> 的模块注册进 DI。
/// 扫描方式与后端 xyz.Components.ComponentLoader 保持一致：机型 DLL 只拷贝部署也能被发现。
/// </summary>
public static class ClientModuleLoader
{
    /// <summary>
    /// 发现并注册所有机型模块，返回模块实例列表（供菜单同步等后续步骤使用）。
    /// </summary>
    public static IReadOnlyList<IClientModule> Load(IServiceCollection services)
    {
        var modules = Discover();

        foreach (var module in modules)
        {
            module.Register(services);
        }

        return modules;
    }

    /// <summary>
    /// 扫描目录发现机型模块；同一程序集只加载一次，按模块键排序保证顺序稳定。
    /// </summary>
    public static IReadOnlyList<IClientModule> Discover(string? baseDirectory = null)
    {
        var root = baseDirectory ?? AppContext.BaseDirectory;
        var modules = new List<IClientModule>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in EnumerateAssemblies(root))
        {
            if (!visited.Add(Path.GetFullPath(path)))
            {
                continue;
            }

            Assembly assembly;
            try
            {
                assembly = Assembly.LoadFrom(path);
            }
            catch (Exception exception) when (exception is BadImageFormatException or FileLoadException)
            {
                continue;
            }

            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                types = exception.Types.Where(type => type is not null).Cast<Type>().ToArray();
            }

            foreach (var type in types)
            {
                if (type.IsAbstract
                    || !typeof(IClientModule).IsAssignableFrom(type)
                    || type.GetCustomAttribute<ClientModuleAttribute>() is null)
                {
                    continue;
                }

                if (Activator.CreateInstance(type) is IClientModule module)
                {
                    modules.Add(module);
                }
            }
        }

        return modules
            .OrderBy(GetModuleKey, StringComparer.Ordinal)
            .ToList();
    }

    private static string GetModuleKey(IClientModule module)
    {
        var type = module.GetType();
        return type.GetCustomAttribute<ClientModuleAttribute>()?.Key ?? type.FullName ?? type.Name;
    }

    /// <summary>
    /// 运行目录顶层 DLL + Modules 目录（含子目录）DLL。机型可按 Modules\&lt;机型&gt;\ 分目录部署。
    /// </summary>
    private static IEnumerable<string> EnumerateAssemblies(string root)
    {
        foreach (var path in Directory.EnumerateFiles(root, "*.dll"))
        {
            yield return path;
        }

        var modulesDirectory = Path.Combine(root, "Modules");
        if (!Directory.Exists(modulesDirectory))
        {
            yield break;
        }

        foreach (var path in Directory.EnumerateFiles(modulesDirectory, "*.dll", SearchOption.AllDirectories))
        {
            yield return path;
        }
    }
}
