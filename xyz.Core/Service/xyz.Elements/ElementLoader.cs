using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using xyz.Common;
using xyz.Common.Log;
using xyz.Elements.Attributes;
using xyz.Elements.Models;
using xyz.Configs.Models;
using xyz.Tools;

namespace xyz.Elements;

/// <summary>
/// Element装载器
/// </summary>
public static class ElementLoader
{
    /// <summary>
    /// 开始加载并且调用组件
    /// </summary>
    /// <param name="roots"></param>
    public static void InitAll(IEnumerable<ElementBase> roots)
    {
        foreach (var root in roots.Where(root => root.IsEnabled).OrderBy(root => root.InitOrder))
        {
            if (!root.InitElement())
            {
                LogHelper.Error(root, "元件初始化没全做成（驱动连接失败等，原因见前面的日志）");
            }
        }
    }

    /// <summary>
    /// 所有的绑定开始，bind里面会拿对象
    /// </summary>
    public static void BindAll(IEnumerable<ElementBase> roots)
    {
        foreach (var element in Walk(roots))
        {
            element.Bind();
        }
    }

    /// <summary>
    /// 找节点里面带IsRun并且是启用的
    /// </summary>
    /// <param name="roots"></param>
    public static void StartScanning(IEnumerable<ElementBase> roots)
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
    /// OnStarted  开启一些动作
    /// </summary>
    /// <param name="roots"></param>
    public static void NotifyStartedAll(IEnumerable<ElementBase> roots)
    {
        foreach (var element in Walk(roots))
        {
            element.OnStarted();
        }
    }

    /// <summary>
    /// 开机第五步：都起来以后，整棵树按 machine.xml 先后挨个 PublishOnStartup；一个出错不耽误后面的
    /// </summary>
    public static void PublishStartupAll(IEnumerable<ElementBase> roots)
    {
        foreach (var element in Walk(roots))
        {
            try
            {
                element.PublishOnStartup();
            }
            catch (Exception exception)
            {
                LogHelper.Error(element, $"开机推送出错：{exception.Message}");
            }
        }
    }

    /// <summary>
    /// 退出 停止扫描，还有断plc以及有一些需要落盘的数据操作
    /// </summary>
    public static void StopAll(IEnumerable<ElementBase> roots)
    {
        // 收尾要断链路、落库，扫描还在跑就会撞上；宿主随后还要释放容器，扫描里再取报警、EC 会报"对象已释放"
        var rootList = roots.ToList();
        foreach (var root in rootList)
        {
            root.StopScan(); //停止扫描
        }

        foreach (var element in Walk(rootList).Reverse())
        {
            try
            {
                element.OnStopping();
            }
            catch (Exception exception)
            {
                LogHelper.Error(element, $"退出收尾出错：{exception.Message}");
            }
        }
    }

    /// <summary>
    /// 启用的元件按 machine.xml 先后（父在前、子在后）；停用的连同下面的都跳过
    /// </summary>
    private static IEnumerable<ElementBase> Walk(IEnumerable<ElementBase> elements)
    {
        foreach (var element in elements)
        {
            if (!element.IsEnabled)
            {
                continue;
            }

            yield return element;
            foreach (var child in Walk(element.Children))
            {
                yield return child;
            }
        }
    }

    /// <summary>
    /// MachineLoader.Load() 里面用到的组件映射过来
    /// </summary>
    public static List<ElementBase> Load(IEnumerable<ModuleConfig> settings)
    {
        var catalog = BuildCatalog();
        var roots = new List<ElementBase>();

        foreach (var setting in settings)
        {
            Build(setting, catalog, parent: null, parentPath: string.Empty, roots);
        }

        foreach (var root in roots)
        {
            AssignFullPath(root, parentPath: string.Empty, modulePath: string.Empty);
        }

        // 虚拟点可分布于任意模块，整棵树装好后统一校验索引与依赖。
        VirtualInputRegistry.Attach(roots);
        return roots;
    }

    /// <summary>
    /// 元件进容器：只按打了 [Ioc] 的接口登记；模块内部的部件（阀、喷嘴、DI、轴）不打标记，由父元件在自己子树里找
    /// </summary>
    /// <param name="services"></param>
    /// <param name="roots"></param>
    public static void Register(IServiceCollection services, IReadOnlyList<ElementBase> roots)
    {
        services.AddSingleton(roots);
        foreach (var element in Walk(roots))
        {
            foreach (var contract in element.GetType().GetInterfaces())
            {
                if (contract.GetCustomAttribute<IocAttribute>() is not null)
                {
                    services.AddSingleton(contract, element);
                }
            }
        }
    }

    /// <summary>
    /// 整棵元件树里按类型或接口找全部，照 machine.xml 先后；给没打 [Ioc]、不进容器的部件用（轴、寿命、心跳）
    /// </summary>
    public static IReadOnlyList<T> FindAll<T>() where T : class
    {
        var roots = IocHelper.GetService<IReadOnlyList<ElementBase>>();
        if (roots is null)
        {
            return [];
        }

        var found = new List<T>();
        foreach (var element in Walk(roots))
        {
            if (element is T matched)
            {
                found.Add(matched);
            }
        }

        return found;
    }

    private static void AssignFullPath(ElementBase element, string parentPath, string modulePath)
    {
        if (element.FullPath.Length == 0)
        {
            element.FullPath = Join(parentPath, element.Name);
        }

        if (modulePath.Length == 0)
        {
            modulePath = element.FullPath;
        }

        element.AssignLogNames(modulePath);
        foreach (var child in element.Children)
        {
            AssignFullPath(child, element.FullPath, modulePath);
        }
    }

    private static string Join(string parent, string name)
    {
        return parent.Length == 0 ? name : parent + "." + name;
    }

    /// <summary>
    /// [Element] 类型目录：注册键（Key 或 CLR 全名）→ 类型。
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
                if (type.IsAbstract || !typeof(ElementBase).IsAssignableFrom(type))
                {
                    continue;
                }

                var attribute = type.GetCustomAttribute<ElementAttribute>();
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
    /// 装一个节点和它下面的节点。parentPath 是上一级节点的路径：分组节点（不写 Type）不生成元件，
    /// 但名字照样进路径，元件全路径跟 machine.xml 的层级一样（Chamber.Chamber1.Door）。
    /// </summary>
    private static void Build(ModuleConfig setting, Dictionary<string, Type> catalog, ElementBase? parent, string parentPath,
        List<ElementBase> roots)
    {
        ElementBase? element = null;
        string path = Join(parentPath, setting.Name);

        if (string.IsNullOrWhiteSpace(setting.Type))
        {
            // 无 Type：在父元件已有子元件中按名匹配灌值（RFID 这类构造时挂好的子元件）。
            if (parent is not null)
            {
                var existing = parent.Children.FirstOrDefault(c =>
                    string.Equals(c.Name, setting.Name, StringComparison.OrdinalIgnoreCase));
                if (existing is not null)
                {
                    AssignValues(existing, setting, path);
                    existing.OnSettingLoaded(setting);
                    element = existing;
                }
            }
        }
        else
        {
            if (!catalog.TryGetValue(setting.Type, out var type))
            {
                throw new InvalidOperationException(
                    $"machine.xml 节点 {setting.Name} 的 Type \"{setting.Type}\" 未找到元件类（程序集未加载或未标记 [Element]）。");
            }

            element = (ElementBase)Activator.CreateInstance(type)!;
            element.Name = setting.Name;
            element.FullPath = path;
            if (setting.InitOrder > 0)
            {
                element.InitOrder = setting.InitOrder;
            }

            AssignValues(element, setting, path);
            element.OnSettingLoaded(setting);

            if (parent is null)
            {
                roots.Add(element);
            }
            else
            {
                parent.AddChild(element);
            }
        }

        // 分组节（如 LoadPort 包装节）继续下钻子节点。
        foreach (var child in setting.Children)
        {
            Build(child, catalog, element ?? parent, path, roots);
        }
    }

    /// <summary>
    /// 先给全部 [Machine] 属性灌默认值，再用同名 Value（忽略大小写）覆盖；
    /// 名字对不上的记一条错误日志、照常开机（写错的那一项还是默认值）
    /// </summary>
    private static void AssignValues(ElementBase element, ModuleConfig setting, string path)
    {
        var properties = element.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite && p.GetCustomAttribute<MachineAttribute>() is not null)
            .ToArray();

        foreach (var property in properties)
        {
            var editor = property.GetCustomAttribute<MachineAttribute>()!;
            property.SetValue(element, ValueHelper.ConvertTo(property.PropertyType, editor.DefaultValue));
        }

        foreach (var value in setting.Values)
        {
            var property = properties.FirstOrDefault(p =>
                string.Equals(p.Name, value.Name, StringComparison.OrdinalIgnoreCase));
            if (property is null)
            {
                LogHelper.Error(LogHelper.HostModule,
                    $"machine.xml 节点 {path} 的参数 {value.Name} 不认识：{element.GetType().Name} 没有这个参数（名字写错了，或者参数已经删了），这一条没用上");
                continue;
            }

            if (value.Value is null)
            {
                continue;
            }

            try
            {
                property.SetValue(element, ValueHelper.ConvertTo(property.PropertyType, value.Value));
            }
            catch (Exception exception) when (
                exception is FormatException or ArgumentException or NotSupportedException)
            {
                throw new InvalidOperationException(
                    $"machine.xml 节点 {setting.Name} 的值 {value.Name}=\"{value.Value}\" 无法转换为 {property.PropertyType.Name}。", exception);
            }
        }
    }
}
