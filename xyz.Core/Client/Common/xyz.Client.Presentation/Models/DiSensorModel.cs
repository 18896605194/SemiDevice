using CommunityToolkit.Mvvm.ComponentModel;
using xyz.Shared.Dtos;

namespace xyz.Client.Presentation.Models;

/// <summary>
/// 一个 DI 信号的显示模型，信号灯绑它
/// </summary>
public class DiSensorModel : ObservableObject
{
    private bool _isPresent;

    /// <summary>
    /// sc 里配了这个信号
    /// </summary>
    public bool IsPresent
    {
        get => _isPresent;
        private set => SetProperty(ref _isPresent, value);
    }

    private string _name = string.Empty;

    /// <summary>
    /// sc 里的节点名
    /// </summary>
    public string Name
    {
        get => _name;
        private set => SetProperty(ref _name, value);
    }

    private bool? _isActive;

    /// <summary>
    /// 有没有触发，读不到是 null
    /// </summary>
    public bool? IsActive
    {
        get => _isActive;
        private set => SetProperty(ref _isActive, value);
    }

    /// <summary>
    /// 用推送就地刷新；dto 为 null 是 sc 里没配
    /// </summary>
    public void Update(DiSensorDto? dto)
    {
        if (dto is null)
        {
            IsPresent = false;
            IsActive = null;
            return;
        }

        Name = dto.Name;
        IsActive = dto.IsActive;
        IsPresent = true;
    }
}
