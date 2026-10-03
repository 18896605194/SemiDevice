using CommunityToolkit.Mvvm.ComponentModel;

namespace xyz.Client.Recipe.Models;

/// <summary>
/// 一步里的一个可选站点（勾选块）：模块名照 sc.xml 原样显示；勾上、去掉都告诉这一步。
/// </summary>
public sealed class StationOptionModel : ObservableObject
{
    private readonly Action _changed;

    public StationOptionModel(string name, bool isChecked, Action changed)
    {
        Name = name;
        _isChecked = isChecked;
        _changed = changed;
    }

    public string Name { get; }

    private bool _isChecked;

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (SetProperty(ref _isChecked, value))
            {
                _changed();
            }
        }
    }
}
