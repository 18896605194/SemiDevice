using xyz.Components.Enums;
using xyz.Shared.Dtos;

namespace xyz.Components.Interfaces;

/// <summary>
/// E40（PJ）的设备侧上报口
/// </summary>
public interface IE40Callback
{
    void ProcessJobStateChanged(ProcessJobDto job, int transition);
}
