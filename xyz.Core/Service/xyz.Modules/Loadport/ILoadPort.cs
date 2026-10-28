using xyz.Elements.Elements;
using xyz.Elements.Enums;
using xyz.Elements.Interfaces;
using xyz.Shared.Dtos;
using xyz.Elements.Attributes;

namespace xyz.Modules;


[Ioc]
public interface ILoadPort : ILoadPortAccess, IJobAccess
{
    #region 动作

    /// <summary>
    /// 回原点
    /// </summary>
    HandleResult<ModuleRoutine> Home();

    /// <summary>
    /// 夹紧载具
    /// </summary>
    HandleResult<ModuleRoutine> Clamp();

    /// <summary>
    /// 松开载具
    /// </summary>
    HandleResult<ModuleRoutine> Unclamp();

    #endregion

    /// <summary>
    /// 取片顺序
    /// </summary>
    SlotPickOrder PickOrder { get; }

    /// <summary>
    /// Load 等到做完的上限（ms）
    /// </summary>
    int LoadTimeout { get; }

    /// <summary>
    /// Unload 等到做完的上限（ms）
    /// </summary>
    int UnloadTimeout { get; }

    /// <summary>
    /// 回原点等到做完的上限（ms）
    /// </summary>
    int HomeTimeout { get; }

    /// <summary>
    /// 复位等到做完的上限（ms）
    /// </summary>
    int ResetTimeout { get; }

    /// <summary>
    /// 中止等到做完的上限（ms）
    /// </summary>
    int AbortTimeout { get; }

    /// <summary>
    /// 手动 Unload
    /// </summary>
    HandleResult<ModuleRoutine> UnloadManually();

    /// <summary>
    /// 推给界面的状态
    /// </summary>
    LoadPortDto CreateStateDto();

}
