using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace xyz.Client.Recipe.Models;

/// <summary>
/// 流程步骤的一行：步号、站点分组（sc.xml 里的分组名，原样显示）、这个分组的模块（勾几个 = 哪个空去哪个）、工艺配方（分组要的时候才有）。
/// 勾选、改配方都告诉页面（页面标记"有没保存的修改"、重新检查、重画路线预览）。检查结果由页面填回来。
/// </summary>
public sealed class SequenceStepModel : ObservableObject
{
    private readonly Action<SequenceStepModel> _changed;

    public SequenceStepModel(
        string group,
        IEnumerable<string> modules,
        IEnumerable<string> picked,
        string recipe,
        bool needsRecipe,
        bool isLoadPort,
        bool isKnownGroup,
        Action<SequenceStepModel> changed)
    {
        _changed = changed;
        Group = group;
        NeedsRecipe = needsRecipe;
        IsLoadPort = isLoadPort;
        IsKnownGroup = isKnownGroup;
        _recipe = recipe;
        var chosen = new HashSet<string>(picked, StringComparer.OrdinalIgnoreCase);
        foreach (string module in modules)
        {
            Options.Add(new StationOptionModel(module, chosen.Contains(module), () => _changed(this)));
        }
    }

    private int _number;

    /// <summary>
    /// 步号（从 1 起，跟后端提示里的步号一样）。
    /// </summary>
    public int Number
    {
        get => _number;
        set => SetProperty(ref _number, value);
    }

    public string Group { get; }

    /// <summary>
    /// 这一步要选工艺配方（工艺腔分组）。
    /// </summary>
    public bool NeedsRecipe { get; }

    /// <summary>
    /// 这一步是 LoadPort 分组。
    /// </summary>
    public bool IsLoadPort { get; }

    /// <summary>
    /// 分组还在可选分组里（sc.xml 改过、分组没了时为 false）。
    /// </summary>
    public bool IsKnownGroup { get; }

    public ObservableCollection<StationOptionModel> Options { get; } = [];

    private string _recipe;

    public string Recipe
    {
        get => _recipe;
        set
        {
            if (SetProperty(ref _recipe, value ?? string.Empty))
            {
                _changed(this);
            }
        }
    }

    /// <summary>
    /// 勾上的站点名，按 sc.xml 里的先后。
    /// </summary>
    public List<string> CheckedStations => Options.Where(option => option.IsChecked).Select(option => option.Name).ToList();

    private string _stationHint = string.Empty;

    /// <summary>
    /// 站点那一格后面的红字（一个都没勾、分组没了）；没问题是空的。
    /// </summary>
    public string StationHint
    {
        get => _stationHint;
        set
        {
            if (SetProperty(ref _stationHint, value))
            {
                OnPropertyChanged(nameof(HasStationHint));
            }
        }
    }

    public bool HasStationHint => StationHint.Length > 0;

    private bool _hasRecipeError;

    /// <summary>
    /// 工艺配方没选：选择框变红。
    /// </summary>
    public bool HasRecipeError
    {
        get => _hasRecipeError;
        set => SetProperty(ref _hasRecipeError, value);
    }

    private bool _hasError;

    /// <summary>
    /// 这一行有问题：整行淡红底。
    /// </summary>
    public bool HasError
    {
        get => _hasError;
        set => SetProperty(ref _hasError, value);
    }
}
