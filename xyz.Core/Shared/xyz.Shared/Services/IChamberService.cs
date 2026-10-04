using System.ServiceModel;
using xyz.Shared.Dtos;

namespace xyz.Shared.Services;

[ServiceContract]
public interface IChamberService
{
    [OperationContract]
    Task<RpcResponse> HomeAsync(string module);

    [OperationContract]
    Task<RpcResponse> ResetAsync(string module);

    /// <summary>
    /// 急停，可顶替在途动作（被顶替动作的调用方收到 module.action_aborted）。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> AbortAsync(string module);

    /// <summary>
    /// 按配方起工艺：只在空闲时允许，同步等工艺做完（上限是腔体的 EC ProcessTimeout）。
    /// 配方没填回 chamber.recipe_required；装了工艺配方库而配方不在库里回 chamber.recipe_not_found。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> ProcessAsync(ChamberProcessRequest request);

    /// <summary>
    /// 部件手动动作：调部件上标了 [ManualAction] 的方法（轴回零 / 移动 / 步进 / 点动 / 停止 / 复位，气缸升 / 降……）。
    /// 普通动作只在未初始化、空闲、报错时允许，同步等部件做完（上限是腔体的 EC PartActionTimeout）；
    /// 停止类（如轴停止）腔体正忙也照发、发出去就回；按住类（点动）发起就回，之后按住期间调 RenewPartActionAsync 续，松手发它的松手动作。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> PartActionAsync(PartActionRequest request);

    /// <summary>
    /// 续按住类动作（点动）：界面按住期间隔一会儿调一次；腔体的 EC HoldTimeoutMs 内没续上就自己发松手动作（界面断了也能停）。
    /// 没有在按住的这个动作回 chamber.part_not_held。只看 Module、Part、Action，不看参数。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> RenewPartActionAsync(PartActionRequest request);

    /// <summary>
    /// 上线：模块模式切 Online（只改 Mode，不经设备）。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> OnlineAsync(string module);

    /// <summary>
    /// 下线：模块模式切 Offline（只改 Mode，不经设备）。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> OfflineAsync(string module);

    /// <summary>
    /// 查询当前状态。module 为空返回全部腔体；Data 为 ChamberDto（或其数组）的 JSON。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> GetStateAsync(string module);
}
