using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace xyz.Client.Recipe.Models;

/// <summary>
/// 流程配方列表的一行：一个编号；用了就有名称，没用名称为 null。
/// </summary>
public sealed class SequenceSlotModel : ObservableObject
{
    public SequenceSlotModel(int index, int digits)
    {
        Index = index;
        IndexText = index.ToString("D" + digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }

    public int Index { get; }

    /// <summary>
    /// 显示用的编号（01、02……位数跟着个数走）。
    /// </summary>
    public string IndexText { get; }

    private string? _name;

    /// <summary>
    /// 名称；这个编号没用为 null。
    /// </summary>
    public string? Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value))
            {
                OnPropertyChanged(nameof(IsUsed));
            }
        }
    }

    public bool IsUsed => Name is not null;

    private bool _isDirty;

    /// <summary>
    /// 右边打开的就是它、并且改了还没保存：名称后面带个点。
    /// </summary>
    public bool IsDirty
    {
        get => _isDirty;
        set => SetProperty(ref _isDirty, value);
    }
}
