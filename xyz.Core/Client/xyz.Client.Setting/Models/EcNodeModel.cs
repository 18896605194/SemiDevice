using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace xyz.Client.Setting.Models;

/// <summary>
/// EC 设置页左边组件树的一个节点：组件全路径按点号分层（模块 → 部件 → 子部件），跟 sc.xml 的组件层级一个样子。
/// 点哪个节点，右边就列这个组件和它下级的全部参数；最上面那个"全部"节点（Path 为空）列整机的。
/// 列什么由页面定，这里只把"选中了"交出去（onSelected）。
/// </summary>
public sealed class EcNodeModel : ObservableObject
{
    private readonly Action<EcNodeModel> _onSelected;

    public EcNodeModel(string name, string path, Action<EcNodeModel> onSelected)
    {
        Name = name;
        Path = path;
        _onSelected = onSelected;
    }

    /// <summary>
    /// 这一级的名字（Chamber1、Arm1）。
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 组件全路径（Chamber1.Arm1）；"全部"节点为空串。
    /// </summary>
    public string Path { get; }

    public ObservableCollection<EcNodeModel> Children { get; } = [];

    private int _count;

    /// <summary>
    /// 这个组件和它下级一共多少项参数（名字后面那个小字）。
    /// </summary>
    public int Count
    {
        get => _count;
        set => SetProperty(ref _count, value);
    }

    private bool _isSelected;

    /// <summary>
    /// 树上选中（界面双向绑）：选中时告诉页面换右边的表。
    /// </summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value) && value)
            {
                _onSelected(this);
            }
        }
    }

    private bool _isExpanded;

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    /// <summary>
    /// 某个组件的参数归不归这个节点：是它自己的，或者是它下级的。
    /// </summary>
    public bool Contains(string componentPath)
    {
        return Path.Length == 0
               || string.Equals(componentPath, Path, StringComparison.OrdinalIgnoreCase)
               || componentPath.StartsWith(Path + ".", StringComparison.OrdinalIgnoreCase);
    }
}
