using System.Diagnostics.CodeAnalysis;
using xyz.Components.Components;
using xyz.Shared.Dtos;

namespace xyz.Components.Interfaces;

/// <summary>
/// 机械手的公共搬运能力；与ITransferStation 配合
/// </summary>
public interface ITransferRobot : IModule
{
    /// <summary>
    /// 能去的站点和怎么去：整机现在几寸就是哪张示教点表（键是站点名）
    /// </summary>
    IReadOnlyDictionary<string, ITeachPoint> TeachPoints { get; }

    /// <summary>
    /// 某种晶圆尺寸的示教点表：开机检查要两种尺寸都查
    /// </summary>
    IReadOnlyDictionary<string, ITeachPoint> TeachPointsOf(int size);

    /// <summary>
    /// 这台机械手用不用扫片（sc 开关）：开了，它示教点表里的 LoadPort 由它扫
    /// </summary>
    bool MapEnabled { get; }

    /// <summary>
    /// 查这台机械手去某个站点的示教点；表里没有就是到不了
    /// </summary>
    bool TryGetTeachPoint(string station, [MaybeNullWhen(false)] out ITeachPoint point);

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
    HandleResult<ModuleRoutine> Pick(int arm, string station, int slot);

    /// <summary>
    /// 放片
    /// </summary>
    HandleResult<ModuleRoutine> Place(int arm, string station, int slot);

    /// <summary>
    /// 换片
    /// </summary>
    /// <param name="pickArm"></param>
    /// <param name="placeArm"></param>
    /// <param name="station"></param>
    /// <param name="slot"></param>
    /// <returns></returns>
    ModuleRoutine? Swap(int pickArm, int placeArm, string station, int slot);
}
