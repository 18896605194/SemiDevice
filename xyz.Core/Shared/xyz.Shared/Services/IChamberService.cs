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

    // 设备手动动作（腔体手动页）：轴、气缸按设备推送里的 Path 找。
    // 回零、移动、步进、复位、升、降只在未初始化、空闲、报错时允许，同步等设备做完（上限是腔体的 EC DeviceActionTimeout），执行中腔体在"手动中"；
    // 停止腔体正忙也照发、发出去就回；点动发起就回，按住期间调 AxisJogRenewAsync 续，松手发 AxisStopAsync。
    // 腔体下没有这根轴 / 这个气缸回 chamber.device_not_found，指令没发出去回 chamber.device_command_rejected，没做成回 chamber.device_action_failed。

    /// <summary>轴回零。</summary>
    [OperationContract]
    Task<RpcResponse> AxisHomeAsync(ChamberDeviceRequest request);

    /// <summary>轴走到绝对位置；位置不是有限数、速度是负的回 chamber.device_args_invalid。</summary>
    [OperationContract]
    Task<RpcResponse> AxisMoveAsync(ChamberAxisMoveRequest request);

    /// <summary>轴走一段（步进）；步距是 0 也算参数不对。</summary>
    [OperationContract]
    Task<RpcResponse> AxisStepAsync(ChamberAxisStepRequest request);

    /// <summary>
    /// 轴点动：发起就回，腔体进"手动中"；界面按住期间隔一会儿调 <see cref="AxisJogRenewAsync"/> 续，
    /// 腔体的 EC HoldTimeoutMs 内没续上就自己停（界面断了也能停）。速度是 0 算参数不对。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> AxisJogAsync(ChamberAxisJogRequest request);

    /// <summary>续点动；没有在按住的点动回 chamber.jog_not_held（界面据此不用再续）。</summary>
    [OperationContract]
    Task<RpcResponse> AxisJogRenewAsync(ChamberDeviceRequest request);

    /// <summary>轴停止：腔体正忙也照发、发出去就回；正按着的点动算松手。</summary>
    [OperationContract]
    Task<RpcResponse> AxisStopAsync(ChamberDeviceRequest request);

    /// <summary>轴驱动器复位清错。</summary>
    [OperationContract]
    Task<RpcResponse> AxisResetAsync(ChamberDeviceRequest request);

    /// <summary>气缸升（开侧：门开、Bowl 升、Lift 升）。</summary>
    [OperationContract]
    Task<RpcResponse> CylinderUpAsync(ChamberDeviceRequest request);

    /// <summary>气缸降（关侧）。</summary>
    [OperationContract]
    Task<RpcResponse> CylinderDownAsync(ChamberDeviceRequest request);

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
