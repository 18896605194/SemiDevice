using CommunityToolkit.Mvvm.ComponentModel;
using xyz.Shared.Dtos;

namespace xyz.Client.Presentation.Models;

/// <summary>
/// 一路喷嘴的显示模型：腔体三维图绑它画出液。
/// </summary>
public class ChamberNozzleModel : ObservableObject
{
    public ChamberNozzleModel(PartDto dto)
    {
        Path = dto.Path;
        Update(dto);
    }

    /// <summary>组件全路径，如 "Chamber1.Arm1.Nozzle_DIW"（部件组成没变就不会变）。</summary>
    public string Path { get; }

    private bool _isOn;

    /// <summary>在出液。</summary>
    public bool IsOn
    {
        get => _isOn;
        private set => SetProperty(ref _isOn, value);
    }

    /// <summary>用推送就地刷新（界面线程调用）。</summary>
    public void Update(PartDto dto)
    {
        IsOn = dto.GetBool(PartValueNames.IsOn);
    }
}
