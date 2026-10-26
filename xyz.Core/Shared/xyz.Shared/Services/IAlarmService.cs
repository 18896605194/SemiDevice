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
    /// 人工复位一个报警来源（连同它的子组件），走那个组件的 Reset。参数：Source（string，来源路径）
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
    /// 参数：Start（DateTime，含）、End（DateTime，不含）、Level（string，可不带，空 = 全部）、Keyword（string，可不带）、MaxCount（int，可不带，&lt;=0 按后端配置）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> QueryHistoryAsync(RpcRequest request, CallContext context = default);
}
