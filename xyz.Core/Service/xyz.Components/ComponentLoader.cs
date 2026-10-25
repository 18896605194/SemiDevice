using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using xyz.Common.Log;
using xyz.Components.Attributes;
using xyz.Components.Models;
using xyz.Configs.Models;

namespace xyz.Components;

/// <summary>
/// 组件装载器：按 machine.xml 的 Setting/Value 树构建组件实例
/// </summary>
public static class ComponentLoader
{
    /// <summary>
    /// 开机第一步：根组件按 InitOrder、再按 machine.xml 先后做开机准备，子组件由基类递归带着做
    /// </summary>
    public static void InitAll(IEnumerable<ComponentBase> roots)
    {
        foreach (var root in roots.Where(root => root.IsEnabled).OrderBy(root => root.InitOrder))
        {
            if (!root.InitComponent())
            {
                LogHelper.Error(root, "组件初始化没全做成（驱动连接失败等，原因见前面的日志）");
            }
        }
    }

    /// <summary>
    /// 开机第二步：开机准备都做完、开始扫描之前，整棵树按 machine.xml 先后挨个 Bind
    /// </summary>
    public static void BindAll(IEnumerable<ComponentBase> roots)
    {
        foreach (var component in Walk(roots))
        {
            component.Bind();
        }
    }

    /// <summary>
    /// 开机第三步：初始化和依赖绑定完成后统一启动根组件。子组件已在父组件的扫描树中，不能重复起线程。
    /// </summary>
    public static void StartScanning(IEnumerable<ComponentBase> roots)
    {
        foreach (var root in roots)
        {
            if (root.IsEnabled && root.IsRun)
            {
                root.Start();
            }
        }
    }

    /// <summary>
    /// 开机第四步：都开始扫描以后，整棵树按 machine.xml 先后挨个 OnStarted
    /// </summary>
    public static void StartedAll(IEnumerable<ComponentBase> roots)
    {
        foreach (var component in Walk(roots))
        {
            component.OnStarted();
        }
    }

    /// <summary>
    /// 宿主退出：整棵树倒着挨个 OnStopping（后起来的先收）；一个出错不耽误后面的
    /// </summary>
    public static void StopAll(IEnumerable<ComponentBase> roots)
    {
        foreach (var component in Walk(roots).Reverse())
        {
            try
            {
                component.OnStopping();
            }
            catch (Exception exception)
            {
                LogHelper.Error(component, $"退出收尾出错：{exception.Message}");
            }
        }
    }

    /// <summary>
    /// 启用的组件按 machine.xml 先后（父在前、子在后）；停用的连同下面的都跳过
    /// </summary>
    private static IEnumerable<ComponentBase> Walk(IEnumerable<ComponentBase> components)
    {
        foreach (var component in components)
        {
            if (!component.IsEnabled)
            {
                continue;
            }

            yield return component;
            foreach (var child in Walk(component.Children))
            {
                yield return child;
            }
        }
    }

    /// <summary>
    /// 把 MachineXml.Load() 的配置树装载为组件实例树，返回根组件列表（含模块）。
    /// </summary>
    public static List<ComponentBase> Load(IEnumerable<ModuleConfig> settings)
    {
        var catalog = BuildCatalog();
        var roots = new List<ComponentBase>();

        foreach (var setting in settings)
        {
            Build(setting, catalog, parent: null, parentPath: string.Empty, roots);
        }

        // 构造时挂好的子组件（machine.xml 里不写 Type 的那种）还没有路径，这里接在父组件后面补上；
        // 顺带定日志的两层名字：最顶上那个组件的全路径是第一层（日志历史页的模块下拉就是这些）。
        foreach (var root in roots)
        {
            AssignFullPath(root, parentPath: string.Empty, modulePath: string.Empty);
        }

        // 虚拟点可分布于任意模块，整棵树装好后统一校验索引与依赖。
        VirtualInputRegistry.Attach(roots);
        return roots;
    }

    /// <summary>
    /// 组件进容器：整棵树照 machine.xml 先后（父在前、子在后，停用的连同下面的都跳过），每个组件按它实现的 xyz 接口登记成单例；
    /// 一类有好几个的（LoadPort、腔体）用的地方取全部，只有一个的（搬运管理、晶圆账）取一个。树本身也登记，宿主开机、收尾按它走
    /// </summary>
    public static void Register(IServiceCollection services, IReadOnlyList<ComponentBase> roots)
    {
        services.AddSingleton(roots);
        foreach (var component in Walk(roots))
        {
            foreach (var contract in component.GetType().GetInterfaces())
            {
                if (contract.Namespace is not null && contract.Namespace.StartsWith("xyz", StringComparison.Ordinal))
                {
                    services.AddSingleton(contract, component);
                }
            }
        }
    }

    private static void AssignFullPath(ComponentBase component, string parentPath, string modulePath)
    {
        if (component.FullPath.Length == 0)
        {
            component.FullPath = Join(parentPath, component.Name);
        }

        if (modulePath.Length == 0)
        {
            modulePath = component.FullPath;
        }

        component.AssignLogNames(modulePath);
        foreach (var child in component.Children)
        {
            AssignFullPath(child, component.FullPath, modulePath);
        }
    }

    private static string Join(string parent, string name)
    {
        return parent.Length == 0 ? name : parent + "." + name;
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

    /// <summary>
    /// 装一个节点和它下面的节点。parentPath 是上一级节点的路径：分组节点（不写 Type）不生成组件，
    /// 但名字照样进路径，组件全路径跟 machine.xml 的层级一样（Chamber.Chamber1.Door）。
    /// </summary>
    private static void Build(ModuleConfig setting, Dictionary<string, Type> catalog, ComponentBase? parent, string parentPath,
        List<ComponentBase> roots)
    {
        ComponentBase? component = null;
        string path = Join(parentPath, setting.Name);

        if (string.IsNullOrWhiteSpace(setting.Type))
        {
            // 无 Type：在父组件已有子组件中按名匹配灌值（RFID 这类构造时挂好的子组件）。
            if (parent is not null)
            {
                var existing = parent.Children.FirstOrDefault(c =>
                    string.Equals(c.Name, setting.Name, StringComparison.OrdinalIgnoreCase));
                if (existing is not null)
                {
                    AssignValues(existing, setting, path);
                    existing.OnSettingLoaded(setting);
                    component = existing;
                }
            }
        }
        else
        {
            if (!catalog.TryGetValue(setting.Type, out var type))
            {
                throw new InvalidOperationException(
                    $"machine.xml 节点 {setting.Name} 的 Type \"{setting.Type}\" 未找到组件类（程序集未加载或未标记 [Component]）。");
            }

            component = (ComponentBase)Activator.CreateInstance(type)!;
            component.Name = setting.Name;
            component.FullPath = path;
            if (setting.InitOrder > 0)
            {
                component.InitOrder = setting.InitOrder;
            }

            AssignValues(component, setting, path);
            component.OnSettingLoaded(setting);

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
            Build(child, catalog, component ?? parent, path, roots);
        }
    }

    /// <summary>
    /// 先给全部 [Machine] 属性灌默认值，再用同名 Value（忽略大小写）覆盖；
    /// 名字对不上的记一条错误日志、照常开机（写错的那一项还是默认值）
    /// </summary>
    private static void AssignValues(ComponentBase component, ModuleConfig setting, string path)
    {
        var properties = component.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite && p.GetCustomAttribute<MachineAttribute>() is not null)
            .ToArray();

        foreach (var property in properties)
        {
            var editor = property.GetCustomAttribute<MachineAttribute>()!;
            property.SetValue(component, ConvertValue(property.PropertyType, editor.DefaultValue));
        }

        foreach (var value in setting.Values)
        {
            var property = properties.FirstOrDefault(p =>
                string.Equals(p.Name, value.Name, StringComparison.OrdinalIgnoreCase));
            if (property is null)
            {
                LogHelper.Error(LogHelper.HostModule,
                    $"machine.xml 节点 {path} 的参数 {value.Name} 不认识：{component.GetType().Name} 没有这个参数（名字写错了，或者参数已经删了），这一条没用上");
                continue;
            }

            if (value.Value is null)
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
                    $"machine.xml 节点 {setting.Name} 的值 {value.Name}=\"{value.Value}\" 无法转换为 {property.PropertyType.Name}。", exception);
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

        throw new NotSupportedException($"{targetType.Name} 不支持的 machine.xml 配置类型。");
    }
}
