using ProtoBuf.Grpc;
using System.ServiceModel;
using xyz.Shared.Dtos;

namespace xyz.Shared.Services;

/// <summary>
/// AI 助手服务契约：只读设备数据、给建议，不动设备。
/// </summary>
[ServiceContract]
public interface IAiService
{
    /// <summary>
    /// 这台机配了 AI 没有。配了 Data 为 AiStatusDto 的 JSON；没配回 ai.not_installed。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> GetStatusAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 让 AI 解释一条当前报警（可能的原因、先查哪里），要等几秒到几十秒。成功时 Data 为 AiAnswerDto 的 JSON；
    /// 失败回 ai.not_installed / ai.alarm_not_found / ai.timeout / ai.failed / ai.empty_answer。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> ExplainAlarmAsync(AiAlarmRequest request, CallContext context = default);
}
