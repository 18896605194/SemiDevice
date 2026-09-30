using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using xyz.Shared.Dtos;

namespace xyz.Client.DataCenter.Models;

/// <summary>
/// 信号勾选树的一个节点：信号名按点号分层（模块 → 部件 → 属性），跟 sc.xml 的组件层级一个样子。
/// 叶子是一个信号（带单位、来源、说明）；中间节点三态勾选：下面全勾 = 勾，部分勾 = 半勾。
/// 勾选由页面决定（最多几条、勾哪些），这里只把"用户点了"交出去（onToggle），状态由页面用 SetChecked 回写。
/// </summary>
public sealed class SignalNodeModel : ObservableObject
{
    private readonly Action<SignalNodeModel, bool> _onToggle;

    public SignalNodeModel(string name, string path, SignalNodeModel? parent, DataChartSignalDto? signal,
        Action<SignalNodeModel, bool> onToggle)
    {
        Name = name;
        Path = path;
        Parent = parent;
        Signal = signal;
        _onToggle = onToggle;
    }

    /// <summary>
    /// 这一级的名字（Chamber1、Arm1、CurrentPosition）。
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 到这一级的全路径；叶子就是信号全名。
    /// </summary>
    public string Path { get; }

    public SignalNodeModel? Parent { get; }

    /// <summary>
    /// 叶子的信号；中间节点为 null。
    /// </summary>
    public DataChartSignalDto? Signal { get; }

    public bool IsLeaf => Signal is not null;

    public ObservableCollection<SignalNodeModel> Children { get; } = [];

    /// <summary>
    /// 叶子名字后面那行小字：IO 是点位地址 + 单位（AI0 · MPa）；SV 只写单位（%），没有单位就什么都不写——不标"SV"。
    /// </summary>
    public string Detail => Signal is null
        ? string.Empty
        : string.Join(" · ", new[] { Signal.Address, Signal.Unit }.Where(part => !string.IsNullOrEmpty(part)));

    /// <summary>
    /// 悬停提示：全名 + 说明（IO 是点表里的点名和描述）。
    /// </summary>
    public string ToolTip => Signal is null || string.IsNullOrEmpty(Signal.Description)
        ? Path
        : $"{Path}\n{Signal.Description}";

    private bool? _isChecked = false;

    /// <summary>
    /// 界面勾选框绑这个：用户一点就交给页面处理（可能因为超过条数被拒），页面再用 SetChecked 定下来。
    /// </summary>
    public bool? IsChecked
    {
        get => _isChecked;
        set
        {
            _onToggle(this, value == true);
            // 页面可能没照办（超过条数），勾选框要按真实状态再读一次。
            OnPropertyChanged();
        }
    }

    private bool _isExpanded;

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    private bool _isShown = true;

    /// <summary>
    /// 搜索过滤：自己或下面有匹配的才显示。
    /// </summary>
    public bool IsShown
    {
        get => _isShown;
        set => SetProperty(ref _isShown, value);
    }

    /// <summary>
    /// 页面定下的勾选状态（不再回调 onToggle）。
    /// </summary>
    public void SetChecked(bool? value)
    {
        if (_isChecked != value)
        {
            _isChecked = value;
            OnPropertyChanged(nameof(IsChecked));
        }
    }

    /// <summary>
    /// 自己（叶子）或下面全部叶子。
    /// </summary>
    public IEnumerable<SignalNodeModel> Leaves()
    {
        if (IsLeaf)
        {
            yield return this;
            yield break;
        }

        foreach (var child in Children)
        {
            foreach (var leaf in child.Leaves())
            {
                yield return leaf;
            }
        }
    }
}
