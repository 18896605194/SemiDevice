using CommunityToolkit.Mvvm.ComponentModel;
using xyz.Shared.Dtos;

namespace xyz.Client.Setting.Models;

/// <summary>
/// EC 设置页右边表格的一行：一项 EC 的定义（后端按代码里的声明给的，运行期不变）+ 当前值 + 还没改进去的设定值。
/// 当前值跟设定值分开放：当前值跟着后端走（谁改了都推过来），不会冲掉正在输的数。
/// </summary>
public sealed class EcItemModel : ObservableObject
{
    private static readonly string[] BoolChoices = [bool.TrueString, bool.FalseString];

    private readonly EcItemDto _definition;

    public EcItemModel(EcItemDto dto)
    {
        _definition = dto;
        Key = dto.Key;

        // 键是"组件全路径.参数名"：最后一个点前面是组件，后面是参数。
        int dot = dto.Key.LastIndexOf('.');
        Path = dot > 0 ? dto.Key[..dot] : string.Empty;
        Name = dto.Key[(dot + 1)..];
        Description = dto.Description ?? string.Empty;
        Unit = dto.Unit ?? string.Empty;
        Default = dto.Default ?? string.Empty;
        Choices = ChoicesOf(dto);
        RangeText = RangeOf(dto, Choices);
        _value = dto.Value ?? string.Empty;
    }

    /// <summary>
    /// 键："组件全路径.参数名"（LoadPort1.LoadTimeout），改值、输入框取范围都按它。
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// 所在组件的全路径（Chamber1.Arm1）。
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// 参数名（HomeSpeed）。
    /// </summary>
    public string Name { get; }

    public string Description { get; }

    public string Unit { get; }

    public string Default { get; }

    /// <summary>
    /// 能填什么：数值是"下限 ~ 上限"，布尔、枚举是可选值；没限制为空。
    /// </summary>
    public string RangeText { get; }

    /// <summary>
    /// 可选值：布尔是 True / False，枚举是声明的那几个；数值、文本为空。
    /// </summary>
    public IReadOnlyList<string> Choices { get; }

    /// <summary>
    /// 布尔、枚举用下拉选，其余用输入框。
    /// </summary>
    public bool IsChoice => Choices.Count > 0;

    private string _value;

    /// <summary>
    /// 当前值（后端正在用的）。
    /// </summary>
    public string Value
    {
        get => _value;
        set
        {
            if (SetProperty(ref _value, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(CanSet));
            }
        }
    }

    private string _pendingValue = string.Empty;

    /// <summary>
    /// "设定值"里还没改进去的新值；空 = 没填。
    /// </summary>
    public string PendingValue
    {
        get => _pendingValue;
        set
        {
            if (SetProperty(ref _pendingValue, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(PendingChoice));
                OnPropertyChanged(nameof(CanSet));
            }
        }
    }

    /// <summary>
    /// 下拉框绑这个而不直接绑 PendingValue：表格滚动时行是复用的，换行那一下下拉框会往回写一次空，
    /// 直接绑的话会把这一行选好还没改进去的值冲掉。人在界面上清不掉下拉的选择，所以写回来的空一律不理。
    /// </summary>
    public string? PendingChoice
    {
        get => _pendingValue.Length == 0 ? null : _pendingValue;
        set
        {
            if (!string.IsNullOrEmpty(value))
            {
                PendingValue = value;
            }
        }
    }

    private bool _hasInputError;

    /// <summary>
    /// 输入框当前是不是错着（输入框按提交校验的结果往回写）。错着的时候 PendingValue 还是上一次的合法值，不能拿去改。
    /// </summary>
    public bool HasInputError
    {
        get => _hasInputError;
        set
        {
            if (SetProperty(ref _hasInputError, value))
            {
                OnPropertyChanged(nameof(CanSet));
            }
        }
    }

    /// <summary>
    /// "设置"按钮能不能点：填了、没错着、跟当前值不一样。
    /// </summary>
    public bool CanSet => _pendingValue.Length > 0
                          && !_hasInputError
                          && !string.Equals(_pendingValue, _value, StringComparison.Ordinal);

    /// <summary>
    /// 后端给的这一项，定义（格式、上下限、单位、默认值、说明、可选值）跟建这一行时比变了没有。
    /// </summary>
    public bool HasSameDefinition(EcItemDto dto)
    {
        return dto.Key == _definition.Key
               && dto.Format == _definition.Format
               && dto.Min == _definition.Min
               && dto.Max == _definition.Max
               && dto.Unit == _definition.Unit
               && dto.Default == _definition.Default
               && dto.Description == _definition.Description
               && dto.Options == _definition.Options;
    }

    private static IReadOnlyList<string> ChoicesOf(EcItemDto dto)
    {
        if (string.Equals(dto.Format, "Bool", StringComparison.OrdinalIgnoreCase))
        {
            return BoolChoices;
        }

        if (string.Equals(dto.Format, "Enum", StringComparison.OrdinalIgnoreCase))
        {
            return (dto.Options ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        return [];
    }

    private static string RangeOf(EcItemDto dto, IReadOnlyList<string> choices)
    {
        if (choices.Count > 0)
        {
            return string.Join(" / ", choices);
        }

        bool hasMin = !string.IsNullOrEmpty(dto.Min);
        bool hasMax = !string.IsNullOrEmpty(dto.Max);
        if (hasMin && hasMax)
        {
            return $"{dto.Min} ~ {dto.Max}";
        }

        return hasMin ? $"≥ {dto.Min}" : hasMax ? $"≤ {dto.Max}" : string.Empty;
    }
}
