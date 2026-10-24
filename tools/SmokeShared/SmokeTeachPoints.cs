using System.Globalization;
using xyz.Components;
using xyz.Components.Interfaces;
using xyz.Configs.Models;

/// <summary>
/// 冒烟造机械手示教点：跟 machine.xml 一样写 Size12 / Size8 分组和示教点节点，用 ComponentLoader 照样建出来。各冒烟工程链接这一个文件
/// </summary>
static class SmokeTeachPoints
{
    /// <summary>
    /// 示教点组件的类型全名（machine.xml 的 Type）
    /// </summary>
    public const string Type = "xyz.Components.Components.TeachPointComponent";

    /// <summary>
    /// 一张示教点表：Size12 / Size8 分组节点，下面挂示教点
    /// </summary>
    public static ModuleConfig Table(string name, params ModuleConfig[] points)
    {
        return new ModuleConfig { Name = name, Children = [.. points] };
    }

    /// <summary>
    /// 一个示教点节点：名字是站点名，参数照 machine.xml 写（Number、Y、Direction、PickArms、PlaceArms）
    /// </summary>
    public static ModuleConfig Point(string name, params (string Name, string Value)[] values)
    {
        var node = new ModuleConfig { Name = name, Type = Type };
        foreach (var value in values)
        {
            node.Values.Add(new ValueConfig { Name = value.Name, Value = value.Value });
        }

        return node;
    }

    /// <summary>
    /// 照这几张表建示教点组件：全路径从分组名起（Size12.LoadPort1），配错跟开机一样当场抛
    /// </summary>
    public static List<ComponentBase> Build(params ModuleConfig[] tables)
    {
        return ComponentLoader.Load(tables);
    }

    /// <summary>
    /// 假机械手用的一张表：这些站点按先后编站号 1、2、3……，朝南、两只手都能取放；键是站点名
    /// </summary>
    public static Dictionary<string, ITeachPoint> Numbered(params string[] stations)
    {
        var points = new List<ModuleConfig>();
        for (int index = 0; index < stations.Length; index++)
        {
            string number = (index + 1).ToString(CultureInfo.InvariantCulture);
            points.Add(Point(stations[index], ("Number", number), ("Direction", "South"), ("PickArms", "1,2"), ("PlaceArms", "1,2")));
        }

        var table = new Dictionary<string, ITeachPoint>(StringComparer.OrdinalIgnoreCase);
        foreach (var point in Build(Table("Size12", [.. points])).OfType<ITeachPoint>())
        {
            table[point.Name] = point;
        }

        return table;
    }

    /// <summary>
    /// 建好挂到机械手下面：机械手组件初始化时按挂在哪一组收进示教点表
    /// </summary>
    public static void AddTo(ComponentBase robot, params ModuleConfig[] tables)
    {
        foreach (var point in Build(tables))
        {
            robot.AddChild(point);
        }
    }
}
