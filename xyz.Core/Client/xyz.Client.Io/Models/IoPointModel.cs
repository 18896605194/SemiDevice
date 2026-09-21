using CommunityToolkit.Mvvm.ComponentModel;

namespace xyz.Client.Io.Models;

/// <summary>
/// IO 点位显示模型，字段与 IoPointDto 对齐。
/// 值每包都在变，所以整行不重建、只改值——重建会让选中行和滚动位置每半秒跳一次。
/// </summary>
public class IoPointModel : ObservableObject
{
    /// <summary>点表里的装机信息，运行期不变，建行时灌一次。</summary>
    public int Index { get; init; }

    public string Name { get; init; } = string.Empty;

    /// <summary>归属部件（Door、Bowl1、Nozzle_DIW……）。</summary>
    public string Component { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string Unit { get; init; } = string.Empty;

    /// <summary>输出点（DO/AO），界面上只有它们给设值的入口。</summary>
    public bool IsOutput { get; init; }

    private bool _isOn;

    /// <summary>数字量当前状态，界面上那颗指示灯看它。</summary>
    public bool IsOn
    {
        get => _isOn;
        set => SetProperty(ref _isOn, value);
    }

    private string _value = string.Empty;

    /// <summary>显示值：数字量 0/1，模拟量是工程值。</summary>
    public string Value
    {
        get => _value;
        set => SetProperty(ref _value, value);
    }

    private bool _isValid;

    /// <summary>
    /// 这一包读到了没有。读不到时界面把值显示成"—"并压暗——
    /// 陈旧值看着跟实时值一样是最容易误判的。
    /// </summary>
    public bool IsValid
    {
        get => _isValid;
        set => SetProperty(ref _isValid, value);
    }
}
