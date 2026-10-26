using System.ServiceModel;
using xyz.Shared.Dtos;

namespace xyz.Shared.Services;

[ServiceContract]
public interface IChamberService
{
    /// <summary>
    /// 回零。参数：Module（string）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> HomeAsync(RpcRequest request);

    /// <summary>
    /// 复位。参数：Module（string）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> ResetAsync(RpcRequest request);

    /// <summary>
    /// 急停，可顶替在途动作（被顶替动作的调用方收到 module.action_aborted）。参数：Module（string）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> AbortAsync(RpcRequest request);

    /// <summary>
    /// 按配方起工艺：只在空闲时允许，同步等工艺做完（上限是配方头的超时时间）。
    /// 配方没填回 chamber.recipe_required；装了工艺配方库而配方不在库里回 chamber.recipe_not_found。参数：Module（string）、Recipe（string，配方名）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> ProcessAsync(RpcRequest request);

    // 设备手动动作（腔体手动页）：轴、气缸按设备推送里的 Path 找。
    // 回零、移动、步进、复位、升、降只在未初始化、空闲、报错时允许，同步等设备做完（上限是腔体的 EC DeviceActionTimeout），执行中腔体在"手动中"；
    // 停止腔体正忙也照发、发出去就回；点动按下发一次就回，松手发 AxisStopAsync 才停，中间不发别的。
    // 腔体下没有这根轴 / 这个气缸回 chamber.device_not_found，指令没发出去回 chamber.device_command_rejected，没做成回 chamber.device_action_failed。

    /// <summary>
    /// 轴回零。参数：Module（string）、Device（string，设备全路径，如 Chamber1.Arm1）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> AxisHomeAsync(RpcRequest request);

    /// <summary>
    /// 轴走到绝对位置；位置不是有限数、速度是负的回 chamber.device_args_invalid。
    /// 参数：Module（string）、Axis（string，轴的全路径）、Position（double）、Speed（double，可不带，0 = 按这根轴的 EC MoveSpeed）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> AxisMoveAsync(RpcRequest request);

    /// <summary>
    /// 轴走一段（步进）；步距是 0 也算参数不对。
    /// 参数：Module（string）、Axis（string）、Distance（double，正负是方向）、Speed（double，可不带，0 = 按这根轴的 EC MoveSpeed）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> AxisStepAsync(RpcRequest request);

    /// <summary>
    /// 轴点动：按下发一次就回，腔体进"手动中"，一直走到松手发 <see cref="AxisStopAsync"/>。速度是 0 算参数不对。
    /// 参数：Module（string）、Axis（string）、Speed（double，正负是方向）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> AxisJogAsync(RpcRequest request);

    /// <summary>
    /// 轴停止：腔体正忙也照发、发出去就回；正按着的点动算松手。参数：Module（string）、Device（string）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> AxisStopAsync(RpcRequest request);

    /// <summary>
    /// 轴驱动器复位清错。参数：Module（string）、Device（string）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> AxisResetAsync(RpcRequest request);

    /// <summary>
    /// 气缸升（开侧：门开、Bowl 升、Lift 升）。参数：Module（string）、Device（string）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> CylinderUpAsync(RpcRequest request);

    /// <summary>
    /// 气缸降（关侧）。参数：Module（string）、Device（string）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> CylinderDownAsync(RpcRequest request);

    /// <summary>
    /// 切生产模式：交给调度用（只改 Mode，不经设备）。参数：Module（string）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> SetProductionAsync(RpcRequest request);

    /// <summary>
    /// 切维护模式：调度不再派新活（只改 Mode，不经设备）。参数：Module（string）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> SetMaintenanceAsync(RpcRequest request);

    /// <summary>
    /// 查询当前状态；Data 为 ChamberDto（或其数组）的 JSON。参数：Module（string，可不带，空 = 全部腔体）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> GetStateAsync(RpcRequest request);
}
