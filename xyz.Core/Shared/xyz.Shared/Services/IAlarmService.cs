using ProtoBuf.Grpc;
using System.ServiceModel;
using xyz.Shared.Dtos;

namespace xyz.Shared.Services;

/// <summary>
/// 报警服务契约：当前报警、人工复位、报警历史查询。
/// 报警的报出与清除实时走事件流（EventBus token = AlarmDto.EventToken）。
/// </summary>
[ServiceContract]
public interface IAlarmService
{
    /// <summary>
    /// 当前报警（还没被人工清除的），按报出时间升序。Data 为 AlarmDto 数组的 JSON。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> GetActiveAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 人工复位一个报警来源（连同它的子组件），走那个组件的 Reset。Parameters["Source"] = 来源路径。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> ResetAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 人工复位全部有报警的来源。Data 为复位了几个来源。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> ResetAllAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 按时间段查报警记录。Data 为 HistoryResult&lt;AlarmHistoryDto&gt; 的 JSON（最新的在前）。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> QueryHistoryAsync(AlarmHistoryQuery query, CallContext context = default);
}
