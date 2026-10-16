using xyz.Components.Enums;
using xyz.Shared.Dtos;

namespace xyz.Components.Interfaces;

/// <summary>
/// E94（CJ）的设备侧上报口
/// </summary>
public interface IE94Callback
{
    /// <summary>
    /// CJ 状态通知
    /// </summary>
    /// <param name="job"></param>
    /// <param name="transition"></param>
    void ControlJobStateChanged(ControlJobDto job, int transition);
}
