using CommunityToolkit.Mvvm.ComponentModel;
using xyz.Shared.Dtos;

namespace xyz.Client.Presentation.Models;

/// <summary>
/// 一个双作用气缸（门、Bowl、Lift）的显示模型：腔体三维图绑它画开关 / 升降。sc.xml 里没配时 IsPresent 为 false。
/// </summary>
public class ChamberCylinderModel : ObservableObject
{
    private string _path = string.Empty;

    /// <summary>组件全路径，如 "Chamber1.Door"；手动动作按它找部件。</summary>
    public string Path
    {
        get => _path;
        private set => SetProperty(ref _path, value);
    }

    private bool _isPresent;

    /// <summary>sc.xml 里配了这个部件。</summary>
    public bool IsPresent
    {
        get => _isPresent;
        private set => SetProperty(ref _isPresent, value);
    }

    private bool _isOpen;

    /// <summary>指令在开侧（门开、Bowl 升、Lift 升）：指令一发出去就变，三维图据此开始动。</summary>
    public bool IsOpen
    {
        get => _isOpen;
        private set => SetProperty(ref _isOpen, value);
    }

    private bool _isMoving;

    /// <summary>正在走（指令侧还没到位），三维图据此高亮。</summary>
    public bool IsMoving
    {
        get => _isMoving;
        private set => SetProperty(ref _isMoving, value);
    }

    /// <summary>
    /// 用推送就地刷新（界面线程调用）；dto 为 null 表示 sc.xml 里没配这个部件。
    /// </summary>
    public void Update(ChamberCylinderDto? dto)
    {
        if (dto is null)
        {
            IsPresent = false;
            Path = string.Empty;
            IsOpen = false;
            IsMoving = false;
            return;
        }

        IsPresent = true;
        Path = dto.Path;
        IsOpen = dto.IsOpen;
        IsMoving = dto.IsMoving;
    }
}
