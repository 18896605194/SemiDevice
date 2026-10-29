using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using xyz.Elements;
using xyz.Elements.Elements;
using xyz.Elements.Interfaces;
using xyz.Configs.Models;
using xyz.Tools;

/// <summary>
/// 冒烟用的容器：跟宿主一样元件照树进容器（ElementLoader.Register），IocHelper 从它取；
/// 冒烟里换树、登记假件、撤掉某个单例都在这里，改一次重建一次。各冒烟工程链接这一个文件
/// </summary>
static class SmokeIoc
{
    private static IReadOnlyList<ElementBase> _tree = [];

    /// <summary>
    /// 单独登记的（假件、直接 new 的、撤掉的）：按登记先后，盖过树上的同类；null 是撤掉
    /// </summary>
    private static readonly List<(Type Type, object? Instance)> _uses = [];

    /// <summary>
    /// 晶圆尺寸是必配元件（宿主开机查过，一定取得到）：树上没有、也没单独登记的，补这个 12 寸的；Reset 时换一个新的
    /// </summary>
    private static WaferSizeElement _defaultWaferSize = new();

    /// <summary>
    /// 冒烟开头：空容器（没有树、没有单独登记的，只有默认的晶圆尺寸）
    /// </summary>
    public static void Reset()
    {
        _tree = [];
        _uses.Clear();
        _defaultWaferSize = new WaferSizeElement();
        Rebuild();
    }

    /// <summary>
    /// 照 machine.xml 装一棵树（宿主开机装配那一步），装出来的元件都进容器
    /// </summary>
    public static List<ElementBase> Load(IEnumerable<ModuleConfig> settings)
    {
        var roots = ElementLoader.Load(settings);
        UseTree(roots);
        return roots;
    }

    /// <summary>
    /// 整棵树换成这些：树上的元件按接口进容器，单独登记过的同类让给树上的（后装的生效）
    /// </summary>
    public static void Tree(params ElementBase[] roots)
    {
        UseTree(roots);
    }

    private static void UseTree(IReadOnlyList<ElementBase> roots)
    {
        var contracts = new HashSet<Type>();
        CollectContracts(roots, contracts);
        _uses.RemoveAll(use => contracts.Contains(use.Type));
        _tree = roots;
        Rebuild();
    }

    /// <summary>
    /// 单独登记一个（后登记的生效）；传 null 是撤掉，树上的同类也一起撤
    /// </summary>
    public static void Use<T>(T? instance) where T : class
    {
        _uses.RemoveAll(use => use.Type == typeof(T));
        _uses.Add((typeof(T), instance));
        Rebuild();
    }

    private static void CollectContracts(IEnumerable<ElementBase> elements, HashSet<Type> contracts)
    {
        foreach (var element in elements)
        {
            foreach (var contract in element.GetType().GetInterfaces())
            {
                contracts.Add(contract);
            }

            CollectContracts(element.Children, contracts);
        }
    }

    private static void Rebuild()
    {
        var services = new ServiceCollection();
        ElementLoader.Register(services, _tree);
        foreach (var (type, instance) in _uses)
        {
            services.RemoveAll(type);
            if (instance is not null)
            {
                services.AddSingleton(type, instance);
            }
        }

        if (!services.Any(service => service.ServiceType == typeof(IWaferSize)))
        {
            services.AddSingleton<IWaferSize>(_defaultWaferSize);
        }

        IocHelper.ServiceProvider = services.BuildServiceProvider();
    }
}
