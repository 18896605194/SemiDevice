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
    /// </summary>
    [OperationContract]
    Task<RpcResponse> ProcessAsync(ChamberProcessRequest request);

    /// <summary>
    /// 部件手动动作：门、Bowl、Lift 开关，喷嘴出液 / 停液，摆臂回零 / 去工艺位，旋转电机转 / 停。
    /// 只在未初始化、空闲、报错时允许；同步等部件做完（上限是腔体的 EC PartActionTimeout）。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> PartActionAsync(ChamberPartActionRequest request);

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
