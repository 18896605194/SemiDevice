using ProtoBuf.Grpc;
using System.ServiceModel;
using xyz.Shared.Dtos;

namespace xyz.Shared.Services;

/// <summary>
/// 整机操作服务契约（主界面左上的系统操作）：切 Auto / Manual、整机停止。
/// 当前是什么模式、系统状态不用查：设备总状态推送（EquipmentStatusDto，留存）里带着。复位走 IAlarmService.ResetAllAsync（跟右上角的复位一样）。
/// </summary>
[ServiceContract]
public interface IEquipmentService
{
    /// <summary>
    /// 切 Auto：开搬运管理的自动派单。没配搬运管理回 transfer.not_installed，搬运管理停用了回 transfer.disabled。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> AutoAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 切 Manual：关自动派单，已经在跑的那一趟跑完。没配搬运管理时本来就是 Manual，直接回成功。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> ManualAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 整机停止：关自动派单、撤搬运单，在跑的 Job 走中止（等设备确认、核对片位）；
    /// 别的正在执行手动动作的模块发中止（不等中止做完）。Data 为这里直接发了中止的模块个数。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> StopAsync(RpcRequest request, CallContext context = default);
}
