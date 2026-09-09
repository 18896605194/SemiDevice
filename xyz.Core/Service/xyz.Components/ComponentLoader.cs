using System.Globalization;
using System.Reflection;
using xyz.Components.Attributes;
using xyz.Configs.Models;

namespace xyz.Components;

/// <summary>
/// 组件装载器：按 sc.xml 的 Setting/Value 树构建组件实例
/// </summary>
public static class ComponentLoader
{
    /// <summary>
    /// 把 SC.Load() 的配置树装载为组件实例树，返回根组件列表（含模块）。
    /// </summary>
    public static List<ComponentBase> Load(IEnumerable<ModuleConfig> settings)
    {
        var catalog = BuildCatalog();
        var roots = new List<ComponentBase>();

        foreach (var setting in settings)
        {
            Build(setting, catalog, parent: null, roots);
        }

        return roots;
    }

    /// <summary>
    /// [Component] 类型目录：注册键（Key 或 CLR 全名）→ 类型。
    /// 扫描运行目录及 Modules 子目录的所有 DLL——机型层 DLL 即使未被宿主引用（仅拷贝部署）也能被发现。
    /// </summary>
    private static Dictionary<string, Type> BuildCatalog()
    {
        var catalog = new Dictionary<string, Type>(StringComparer.Ordinal);

        foreach (var path in EnumerateAssemblies())
        {
            Assembly assembly;
            try
            {
                assembly = Assembly.LoadFrom(path);
            }
            catch (BadImageFormatException)
            {
                continue;
            }

            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException)
            {
                continue;
            }

            foreach (var type in types)
            {
                if (type.IsAbstract || !typeof(ComponentBase).IsAssignableFrom(type))
                {
                    continue;
                }

                var attribute = type.GetCustomAttribute<ComponentAttribute>();
                if (attribute is null)
                {
                    continue;
                }

                catalog[attribute.Key ?? type.FullName!] = type;
            }
        }

        return catalog;
    }

    /// <summary>
    /// 运行目录顶层 DLL + Modules 目录（含子目录）DLL；机型按 Modules\&lt;机型&gt;\ 部署。
    /// </summary>
    private static IEnumerable<string> EnumerateAssemblies()
    {
        foreach (var path in Directory.EnumerateFiles(AppContext.BaseDirectory, "*.dll"))
        {
            yield return path;
        }

        var modulesDirectory = Path.Combine(AppContext.BaseDirectory, "Modules");
        if (!Directory.Exists(modulesDirectory))
        {
            yield break;
        }

        foreach (var path in Directory.EnumerateFiles(modulesDirectory, "*.dll", SearchOption.AllDirectories))
        {
            yield return path;
        }
    }

    private static void Build(ModuleConfig setting, Dictionary<string, Type> catalog, ComponentBase? parent, List<ComponentBase> roots)
    {
        ComponentBase? component = null;

        if (string.IsNullOrWhiteSpace(setting.Type))
        {
            // 无 Type：在父组件已有子组件中按名匹配灌值（RFID 这类构造时挂好的子组件）。
            if (parent is not null
                && parent.Children.FirstOrDefault(c =>
                    string.Equals(c.Name, setting.Name, StringComparison.OrdinalIgnoreCase)) is { } existing)
            {
                AssignValues(existing, setting);
                component = existing;
            }
        }
        else
        {
            if (!catalog.TryGetValue(setting.Type, out var type))
            {
                throw new InvalidOperationException(
                    $"sc.xml 节点 {setting.Name} 的 Type \"{setting.Type}\" 未找到组件类（程序集未加载或未标记 [Component]）。");
            }

            component = (ComponentBase)Activator.CreateInstance(type)!;
            component.Name = setting.Name;
            component.FullPath = parent is null ? setting.Name : $"{parent.FullPath}.{setting.Name}";
            if (setting.InitOrder > 0)
            {
                component.InitOrder = setting.InitOrder;
            }

            AssignValues(component, setting);

            if (parent is null)
            {
                roots.Add(component);
            }
            else
            {
                parent.AddChild(component);
            }
        }

        // 分组节（如 LoadPort 包装节）继续下钻子节点。
        foreach (var child in setting.Children)
        {
            Build(child, catalog, component ?? parent, roots);
        }
    }

    /// <summary>
    /// 先给全部 [SCEditor] 属性灌默认值，再用同名 Value（忽略大小写）覆盖。
    /// </summary>
    private static void AssignValues(ComponentBase component, ModuleConfig setting)
    {
        var properties = component.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite && p.GetCustomAttribute<SCEditorAttribute>() is not null)
            .ToArray();

        foreach (var property in properties)
        {
            var editor = property.GetCustomAttribute<SCEditorAttribute>()!;
            property.SetValue(component, ConvertValue(property.PropertyType, editor.DefaultValue));
        }

        foreach (var value in setting.Values)
        {
            var property = properties.FirstOrDefault(p =>
                string.Equals(p.Name, value.Name, StringComparison.OrdinalIgnoreCase));
            if (property is null || value.Value is null)
            {
                continue;
            }

            try
            {
                property.SetValue(component, ConvertValue(property.PropertyType, value.Value));
            }
            catch (Exception exception) when (
                exception is FormatException or ArgumentException or NotSupportedException)
            {
                throw new InvalidOperationException(
                    $"sc.xml 节点 {setting.Name} 的值 {value.Name}=\"{value.Value}\" 无法转换为 {property.PropertyType.Name}。", exception);
            }
        }
    }

    private static object ConvertValue(Type targetType, string value)
    {
        if (targetType == typeof(int))
        {
            return int.Parse(value, CultureInfo.InvariantCulture);
        }

        if (targetType == typeof(double))
        {
            return double.Parse(value, CultureInfo.InvariantCulture);
        }

        if (targetType == typeof(bool))
        {
            return bool.Parse(value);
        }

        if (targetType == typeof(string))
        {
            return value;
        }

        if (targetType.IsEnum)
        {
            return Enum.Parse(targetType, value, ignoreCase: true);
        }

        throw new NotSupportedException($"{targetType.Name} 不支持的 SC 配置类型。");
    }
}
