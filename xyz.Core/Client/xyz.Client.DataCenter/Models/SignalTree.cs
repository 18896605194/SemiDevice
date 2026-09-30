using xyz.Shared.Dtos;

namespace xyz.Client.DataCenter.Models;

/// <summary>
/// 信号勾选树：按信号名的点号分层长出来，数据曲线、实时曲线两页共用。
/// 同一层里先放属性（叶子）再放部件，各自按第一次出现的先后——后端给的顺序就是 SV 编号表、点表的顺序，基本跟 sc.xml 一致。
/// </summary>
public static class SignalTree
{
    public static List<SignalNodeModel> Build(IEnumerable<DataChartSignalDto> signals, Action<SignalNodeModel, bool> onToggle)
    {
        var roots = new List<SignalNodeModel>();
        var groups = new Dictionary<string, SignalNodeModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var signal in signals)
        {
            var parts = signal.Name.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                continue;
            }

            SignalNodeModel? parent = null;
            for (int level = 0; level < parts.Length - 1; level++)
            {
                string path = string.Join('.', parts, 0, level + 1);
                if (!groups.TryGetValue(path, out var group))
                {
                    group = new SignalNodeModel(parts[level], path, parent, null, onToggle);
                    groups[path] = group;
                    AddTo(parent, roots, group);
                }

                parent = group;
            }

            AddTo(parent, roots, new SignalNodeModel(parts[^1], signal.Name, parent, signal, onToggle));
        }

        foreach (var group in groups.Values)
        {
            var ordered = group.Children.Where(child => child.IsLeaf).Concat(group.Children.Where(child => !child.IsLeaf)).ToList();
            group.Children.Clear();
            foreach (var child in ordered)
            {
                group.Children.Add(child);
            }
        }

        return roots;
    }

    /// <summary>
    /// 叶子的勾选定了以后，中间节点跟着算：全勾 = 勾，一个没勾 = 不勾，其余半勾。
    /// </summary>
    public static void RefreshGroups(IEnumerable<SignalNodeModel> roots)
    {
        foreach (var root in roots)
        {
            Refresh(root);
        }
    }

    /// <summary>
    /// 按名字过滤（全路径里包含就算，不分大小写）：匹配的节点和它的上级显示、并展开；清空过滤全部显示。
    /// </summary>
    public static void Filter(IEnumerable<SignalNodeModel> roots, string text)
    {
        foreach (var root in roots)
        {
            Filter(root, text.Trim());
        }
    }

    public static IEnumerable<SignalNodeModel> AllLeaves(IEnumerable<SignalNodeModel> roots)
    {
        return roots.SelectMany(root => root.Leaves());
    }

    private static void AddTo(SignalNodeModel? parent, List<SignalNodeModel> roots, SignalNodeModel node)
    {
        if (parent is null)
        {
            roots.Add(node);
        }
        else
        {
            parent.Children.Add(node);
        }
    }

    private static bool? Refresh(SignalNodeModel node)
    {
        if (node.IsLeaf)
        {
            return node.IsChecked;
        }

        var states = node.Children.Select(Refresh).ToList();
        bool? state = states.Count == 0 ? false
            : states.All(item => item == true) ? true
            : states.All(item => item == false) ? false
            : null;
        node.SetChecked(state);
        return state;
    }

    private static bool Filter(SignalNodeModel node, string text)
    {
        if (text.Length == 0)
        {
            foreach (var child in node.Children)
            {
                Filter(child, text);
            }

            node.IsShown = true;
            return true;
        }

        bool selfMatch = node.Path.Contains(text, StringComparison.OrdinalIgnoreCase);
        bool childMatch = false;
        foreach (var child in node.Children)
        {
            // 上级名字对上了，下面整枝都显示。
            childMatch |= selfMatch ? Filter(child, string.Empty) : Filter(child, text);
        }

        node.IsShown = selfMatch || childMatch;
        if (childMatch && !selfMatch)
        {
            node.IsExpanded = true;
        }

        return node.IsShown;
    }
}
