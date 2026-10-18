using System.Diagnostics.CodeAnalysis;
using xyz.Components.Components;
using xyz.Components.Models;
using xyz.Shared.Dtos;

namespace xyz.Components.Interfaces;

/// <summary>
/// 机械手的公共搬运能力；与ITransferStation 配合
/// </summary>
public interface ITransferRobot : IModule
{
    /// <summary>
    /// 服务的站点
    /// </summary>
    IReadOnlyDictionary<string, RobotStation> Stations { get; }

    /// <summary>
    /// 查询站点
    /// </summary>
    bool TryGetStation(string station, [MaybeNullWhen(false)] out RobotStation config);

    /// <summary>
    /// robot取片能不能做
    /// </summary>
    HandleResult? CheckPick(int arm, string station, int slot);

    /// <summary>
    /// robot 放片能不能做
    /// </summary>
    HandleResult? CheckPlace(int arm, string station, int slot);

    /// <summary>
    /// 取片
    /// </summary>
    HandleResult<ModuleOperation> Pick(int arm, string station, int slot);

    /// <summary>
    /// 放片
    /// </summary>
    HandleResult<ModuleOperation> Place(int arm, string station, int slot);

    /// <summary>
    /// 换片
    /// </summary>
    /// <param name="pickArm"></param>
    /// <param name="placeArm"></param>
    /// <param name="station"></param>
    /// <param name="slot"></param>
    /// <returns></returns>
    ModuleOperation? Swap(int pickArm, int placeArm, string station, int slot);
}
