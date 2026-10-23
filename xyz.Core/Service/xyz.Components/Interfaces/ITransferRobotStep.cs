using xyz.Components.Components;
using xyz.Shared.Dtos;

namespace xyz.Components.Interfaces;

/// <summary>
/// 分段取放的机械手
/// </summary>
public interface ITransferRobotStep : ITransferRobot
{
    /// <summary>
    /// 伸出取
    /// </summary>
    /// <param name="arm"></param>
    /// <param name="station"></param>
    /// <param name="slot"></param>
    /// <returns></returns>
    HandleResult<ModuleRoutine> ExtendPick(int arm, string station, int slot);

    /// <summary>
    /// 伸出缩回
    /// </summary>
    /// <param name="arm"></param>
    /// <param name="station"></param>
    /// <param name="slot"></param>
    /// <returns></returns>
    HandleResult<ModuleRoutine> RetractPick(int arm, string station, int slot);

    /// <summary>
    /// 伸出放
    /// </summary>
    /// <param name="arm"></param>
    /// <param name="station"></param>
    /// <param name="slot"></param>
    /// <returns></returns>
    HandleResult<ModuleRoutine> ExtendPlace(int arm, string station, int slot);

    /// <summary>
    /// 缩回放
    /// </summary>
    /// <param name="arm"></param>
    /// <param name="station"></param>
    /// <param name="slot"></param>
    /// <returns></returns>
    HandleResult<ModuleRoutine> RetractPlace(int arm, string station, int slot);
}
