using Grpc.Core;
using Grpc.Core.Interceptors;
using xyz.Service;

namespace xyz.GrpcHost;

/// <summary>
/// 操作日志的 gRPC 这一层：客户端每次调后端都过这里，取出方法名、来路、请求头里的操作人交给 IActionFilter，
/// 回包、抛异常也告诉它；记不记、怎么记都在 IActionFilter 里。在 AddCodeFirstGrpc 里挂上，IActionFilter 由容器注入
/// </summary>
public sealed class ActionFilter : Interceptor
{
    private readonly IActionFilter _log;

    public ActionFilter(IActionFilter log)
    {
        _log = log;
    }

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(TRequest request, ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        var call = _log.Begin(context.Method, context.Peer, RpcOperator.Of(context), request);
        if (call is null)
        {
            return await continuation(request, context).ConfigureAwait(false);
        }

        try
        {
            var response = await continuation(request, context).ConfigureAwait(false);
            _log.End(call, response);
            return response;
        }
        catch (Exception exception)
        {
            _log.Fault(call, exception);
            throw;
        }
    }

    public override async Task ServerStreamingServerHandler<TRequest, TResponse>(TRequest request, IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context, ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        var call = _log.Begin(context.Method, context.Peer, RpcOperator.Of(context), request);
        if (call is null)
        {
            await continuation(request, responseStream, context).ConfigureAwait(false);
            return;
        }

        try
        {
            await continuation(request, responseStream, context).ConfigureAwait(false);
            _log.End(call, null);
        }
        catch (Exception exception)
        {
            _log.Fault(call, exception);
            throw;
        }
    }
}
