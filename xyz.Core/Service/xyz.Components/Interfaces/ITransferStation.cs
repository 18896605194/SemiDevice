using xyz.Components.Components;
using xyz.Components.Models;
using xyz.Shared.Dtos;

namespace xyz.Components.Interfaces;

/// <summary>
/// 机械手可服务工位的搬运交互契约。
/// </summary>
public interface ITransferStation : IModule
{
    int SlotCount { get; }

    /// <summary>
    /// 是否可以准备传输
    /// </summary>
    bool CanPrepareTransfer { get; }

    /// <summary>
    /// 只查询，不占用工位
    /// </summary>
    /// <returns></returns>
    HandleResult? CheckPrepareTransfer();

    /// <summary>
    /// 准备阶段一：申请并占用本站本轮交接
    /// </summary>
    /// <returns></returns>
    ModuleOperation? PrepareTransfer(TransferContext context);

    /// <summary>
    /// 准备阶段二：完成本站本轮取片或放片所需的机构准备。
    /// </summary>
    /// <returns></returns>
    ModuleOperation? PrepareTransfer2(TransferContext context);

    /// <summary>
    /// 传输进行中
    /// </summary>
    bool Transferring(TransferContext context);

    #region 分段

    /// <summary>
    /// 分段
    /// </summary>
    /// <param name="context"></param>
    /// <returns></returns>
    ModuleOperation? PrepareNextLeg(TransferContext context);

    /// <summary>
    /// 取片要站点中途交接：机械手伸到位先停下
    /// </summary>
    bool HandoffOnPick { get; }

    /// <summary>
    /// 放片要站点中途交接：机械手伸到位先停下
    /// </summary>
    bool HandoffOnPlace { get; }

    /// <summary>
    /// 取片交接：机械手伸到位停下后调；只认本轮带取片的上下文、交互中、没有没收尾的动作，不受理返回 null
    /// </summary>
    ModuleOperation? HandoffPick(TransferContext context);

    /// <summary>
    /// 放片交接：机械手伸到位停下后调；只认本轮带放片的上下文、交互中、没有没收尾的动作，不受理返回 null
    /// </summary>
    ModuleOperation? HandoffPlace(TransferContext context);

    #endregion

    /// <summary>
    /// 完成
    /// </summary>
    /// <param name="context"></param>
    /// <returns></returns>
    bool TransferComplete(TransferContext context);

    /// <summary>
    /// 取消传输
    /// </summary>
    /// <param name="context"></param>
    /// <returns></returns>
    bool CancelTransfer(TransferContext context);
}
