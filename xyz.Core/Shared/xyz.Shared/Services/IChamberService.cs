using System.ServiceModel;
using xyz.Shared.Dtos;

namespace xyz.Shared.Services;

[ServiceContract]
public interface IChamberService
{
    /// <summary>
    /// 回零
    /// </summary>
    [OperationContract]
    Task<RpcResponse> HomeAsync(RpcRequest request);

    /// <summary>
    /// 复位
    /// </summary>
    [OperationContract]
    Task<RpcResponse> ResetAsync(RpcRequest request);

    /// <summary>
    /// 急停
    /// </summary>
    [OperationContract]
    Task<RpcResponse> AbortAsync(RpcRequest request);

    /// <summary>
    /// 按配方起工艺
    /// </summary>
    [OperationContract]
    Task<RpcResponse> ProcessAsync(RpcRequest request);

    // 设备手动动作（腔体手动页）：轴、气缸按设备推送里的 Path 找。
    // 回零、移动、步进、复位、升、降只在未初始化、空闲、报错时允许，同步等设备做完（上限是腔体的 EC DeviceActionTimeout），执行中腔体在"手动中"；
    // 停止腔体正忙也照发、发出去就回；点动按下发一次就回，松手发 AxisStopAsync 才停，中间不发别的。
    // 腔体下没有这根轴 / 这个气缸回 chamber.device_not_found，指令没发出去回 chamber.device_command_rejected，没做成回 chamber.device_action_failed。

    /// <summary>
    /// 轴回零
    /// </summary>
    [OperationContract]
    Task<RpcResponse> AxisHomeAsync(RpcRequest request);

    /// <summary>
    /// 轴走到绝对位置
    /// </summary>
    [OperationContract]
    Task<RpcResponse> AxisMoveAsync(RpcRequest request);

    /// <summary>
    /// 轴步进
    /// </summary>
    [OperationContract]
    Task<RpcResponse> AxisStepAsync(RpcRequest request);

    /// <summary>
    /// 轴点动
    /// </summary>
    [OperationContract]
    Task<RpcResponse> AxisJogAsync(RpcRequest request);

    /// <summary>
    /// 轴停止
    /// </summary>
    [OperationContract]
    Task<RpcResponse> AxisStopAsync(RpcRequest request);

    /// <summary>
    /// 轴驱动器复位清错
    /// </summary>
    [OperationContract]
    Task<RpcResponse> AxisResetAsync(RpcRequest request);

    /// <summary>
    /// 气缸升
    /// </summary>
    [OperationContract]
    Task<RpcResponse> CylinderUpAsync(RpcRequest request);

    /// <summary>
    /// 气缸降
    /// </summary>
    [OperationContract]
    Task<RpcResponse> CylinderDownAsync(RpcRequest request);

    /// <summary>
    /// 切换生产模式
    /// </summary>
    [OperationContract]
    Task<RpcResponse> SetProductionAsync(RpcRequest request);

    /// <summary>
    /// 切换维护模式
    /// </summary>
    [OperationContract]
    Task<RpcResponse> SetMaintenanceAsync(RpcRequest request);

    /// <summary>
    /// 查询当前状态（ChamberDto）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> GetStateAsync(RpcRequest request);
}
