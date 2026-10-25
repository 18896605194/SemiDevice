using System.Text;
using Grpc.Core;
using ProtoBuf.Grpc;
using xyz.Shared.Rpc;

namespace xyz.Service;

/// <summary>
/// 读请求头里的操作人（客户端每次调用都带）：账单调整、配方的创建人修改人、建 Job、操作日志都从这儿取
/// </summary>
public static class RpcOperator
{
    /// <summary>
    /// 没带操作人的调用（冒烟测试这类直接调服务的）记成这个
    /// </summary>
    public const string Unknown = "Unknown";

    public static string Of(CallContext context)
    {
        return Of(context.ServerCallContext);
    }

    public static string Of(ServerCallContext? context)
    {
        if (context is null)
        {
            return Unknown;
        }

        var entry = context.RequestHeaders.Get(RpcHeaders.Operator);
        if (entry is null || !entry.IsBinary)
        {
            return Unknown;
        }

        string name = Encoding.UTF8.GetString(entry.ValueBytes).Trim();
        if (name.Length == 0)
        {
            return Unknown;
        }

        return name;
    }
}
