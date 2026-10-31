using xyz.Shared.Dtos;

namespace xyz.Client.Presentation.Models;

/// <summary>
/// 机型自己加的状态码怎么显示：平台认不出那个码才问它，机型客户端模块注册时挂上
/// </summary>
public interface IMachineModuleStates
{
    /// <summary>
    /// 状态字（走机型自己的语言包）；不认识这个码返回 null
    /// </summary>
    string? TextOf(StationKind kind, int state);

    /// <summary>
    /// 状态徽标的色调；不认识这个码返回 null
    /// </summary>
    ModuleStateTone? ToneOf(StationKind kind, int state);
}
