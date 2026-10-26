using System.Text;
using Grpc.Core;
using ProtoBuf.Grpc;
using xyz.Shared.Rpc;

namespace xyz.Service;

public static class RpcOperator
{
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
