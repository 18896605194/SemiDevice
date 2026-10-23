using CommunityToolkit.Mvvm.ComponentModel;
using xyz.Client.Presentation.Localization;
using xyz.Shared.Dtos;

namespace xyz.Client.Presentation.Models;

/// <summary>
/// 顶栏模块行上的一个模块：名字照 sc、状态色调和状态字、生产 / 维护
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

    private bool _isProduction;

    /// <summary>
    /// 生产模式：小框边框亮
    /// </summary>
    public bool IsProduction
    {
        get => _isProduction;
        private set => SetProperty(ref _isProduction, value);
    }

    private bool _hasMode;

    /// <summary>
    /// 分不分生产 / 维护：供液这类没有，小框不画边框
    /// </summary>
    public bool HasMode
    {
        get => _hasMode;
        private set => SetProperty(ref _hasMode, value);
    }

    /// <summary>
    /// 用整机推来的这个模块刷新（界面线程调用）
    /// </summary>
    public void Update(ModuleStatusDto dto)
    {
        StateText = ModuleStates.TextOf(dto.Kind, dto.State);
        StateTone = ModuleStates.ToneOf(dto.Kind, dto.State);
        HasMode = dto.Mode is not null;
        IsProduction = dto.Mode == ModuleMode.Production;
    }

    /// <summary>
    /// 跟后端断开：不知道模块现在怎样，显示未连接
    /// </summary>
    public void SetDisconnected()
    {
        StateText = L10n.Get("module.state.disconnected");
        StateTone = ModuleStateTone.Inactive;
        IsProduction = false;
    }
}
