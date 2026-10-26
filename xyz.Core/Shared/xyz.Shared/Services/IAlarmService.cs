using ProtoBuf.Grpc;
using System.ServiceModel;
using xyz.Shared.Dtos;

namespace xyz.Shared.Services;


[ServiceContract]
public interface IAlarmService
{
    /// <summary>
    /// 当前报警（还没被人工清除的），按报出时间升序。Data 为 AlarmDto 数组的 JSON。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> GetActiveAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 人工复位报警来源
    /// </summary>
    [OperationContract]
    Task<RpcResponse> ResetAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 人工复位全部有报警的来源。Data 为复位了几个来源。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> ResetAllAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 按时间段查询报警记录（HistoryResult&lt;AlarmHistoryDto&gt;）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> QueryHistoryAsync(RpcRequest request, CallContext context = default);
}
