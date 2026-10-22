using CommunityToolkit.Mvvm.ComponentModel;
using xyz.Client.Presentation.Localization;
using xyz.Shared.Dtos;

namespace xyz.Client.Presentation.Models;

/// <summary>
/// 顶栏模块行上的一个模块：名字照 sc、状态色调和状态字、在不在 Auto
/// </summary>
public sealed class ModuleStatusModel : ObservableObject
{
    public string Name { get; init; } = string.Empty;

    private string _stateText = string.Empty;

    /// <summary>
    /// 状态字（悬停提示用）
    /// </summary>
    public string StateText
    {
        get => _stateText;
        private set => SetProperty(ref _stateText, value);
    }

    private ModuleStateTone _stateTone;

    /// <summary>
    /// 状态色调（小框里圆点的颜色按它）
    /// </summary>
    public ModuleStateTone StateTone
    {
        get => _stateTone;
        private set => SetProperty(ref _stateTone, value);
    }

    private bool _isAuto;

    /// <summary>
    /// 在线（参与自动调度）算 Auto，小框边框亮
    /// </summary>
    public bool IsAuto
    {
        get => _isAuto;
        private set => SetProperty(ref _isAuto, value);
    }

    /// <summary>
    /// 用整机推来的这个模块刷新（界面线程调用）
    /// </summary>
    public void Update(ModuleStatusDto dto)
    {
        StateText = ModuleStates.TextOf(dto.Kind, dto.State);
        StateTone = ModuleStates.ToneOf(dto.Kind, dto.State);
        IsAuto = dto.Mode == ModuleMode.Online;
    }

    /// <summary>
    /// 跟后端断开：不知道模块现在怎样，显示未连接
    /// </summary>
    public void SetDisconnected()
    {
        StateText = L10n.Get("module.state.disconnected");
        StateTone = ModuleStateTone.Inactive;
        IsAuto = false;
    }
}
