using ProtoBuf.Grpc;
using System.ServiceModel;
using xyz.Shared.Dtos;

namespace xyz.Shared.Services;

/// <summary>
/// 整机操作服务契约（主界面左上的系统操作）：全部初始化、切 Auto / Manual、整机停止、全部回片、切晶圆尺寸。
/// 动作开了就回，系统状态不用查：系统状态推送（SystemStateDto，留存）里带着，模式按它算。复位走 IAlarmService.ResetAllAsync（跟右上角的复位一样）。
/// 没配整机（machine.xml 的 System 节点）回 system.not_installed；当前状态不让做回 module.state_not_allowed [整机名, 状态码]。
/// </summary>
[ServiceContract]
public interface IEquipmentService
{
    /// <summary>
    /// 全部初始化：先机械手回原点，再一起初始化其余模块；未初始化、空闲时能做。做成了进空闲，没做成回未初始化（原因在日志里）。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> InitAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 切 Auto：要先全部初始化过（空闲）；开搬运管理的自动派单。没配搬运管理回 transfer.not_installed，搬运管理停用了回 transfer.disabled。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> AutoAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 切 Manual：关自动派单，已经在跑的那一趟跑完；回空闲。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> ManualAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 整机停止：关自动调度、撤掉搬运，在跑的 Job 走中止（等设备确认、核对片位），别的正在做动作的模块发中止；
    /// 整机进停止中，等这些动作都收完尾。停止中再按回 module.state_not_allowed。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> StopAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 全部回片：Manual 空闲时，机内（机械手手上、腔体等）的片回各自来源 LoadPort 的原槽；回不去的片记日志跳过。
    /// 要搬运管理（transfer.not_installed / transfer.disabled）和晶圆账（wafer.ledger_disabled）开着。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> ReturnAllAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 切晶圆尺寸：参数 <see cref="WaferSizeKey"/> = 8 或 12。整机没初始化（每个模块都是未初始化）、机台里（LoadPort 以外）没片才让切；
    /// 切了马上生效并写回 machine.xml，当前尺寸由设备总状态推送带给界面。跟现在一样直接回成功。
    /// 尺寸不对回 system.wafer_size_invalid，没配晶圆尺寸回 system.wafer_size_not_installed，有模块初始化过回 system.wafer_size_initialized，
    /// 机台里有片回 system.wafer_size_has_wafer，写 machine.xml 失败回 system.wafer_size_save_failed。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> SetWaferSizeAsync(RpcRequest request, CallContext context = default);

    /// <summary>切晶圆尺寸的参数名：值是 8 或 12。</summary>
    const string WaferSizeKey = "Size";
}
