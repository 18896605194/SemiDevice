using xyz.Common.Log;
using xyz.Shared.Dtos;
using xyz.Tools;

namespace xyz.Service;

/// <summary>
/// 操作日志拦截器
/// </summary>
public sealed class ActionFilter : IActionFilter
{
    /// <summary>
    /// 不是 Get / Query 开头、但也不记的：订阅推送、客户端往后端发事件
    /// </summary>
    private static readonly HashSet<string> Unrecorded = new(StringComparer.Ordinal)
    {
        "Subscribe",
        "Publish",
    };

    public ActionCall? Begin(string method, string peer, string operatorName, object? request)
    {
        string action = ActionOf(method);
        if (!IsRecorded(action))
        {
            return null;
        }

        var call = new ActionCall($"{operatorName}@{HostOf(peer)} {action}");
        LogHelper.Operation(NLog.LogLevel.Info, $"{call.Who} {Arguments(request)}");
        return call;
    }

    public void End(ActionCall call, object? response)
    {
        long elapsed = call.Watch.ElapsedMilliseconds;
        if (response is RpcResponse rpc && !rpc.Success)
        {
            LogHelper.Operation(NLog.LogLevel.Warn, $"{call.Who} → 失败 {rpc.Code} [{string.Join(", ", rpc.Args)}] {elapsed} ms");
            return;
        }

        LogHelper.Operation(NLog.LogLevel.Info, $"{call.Who} → 成功 {elapsed} ms");
    }

    public void Fault(ActionCall call, Exception exception)
    {
        LogHelper.Operation(NLog.LogLevel.Error, $"{call.Who} → 出错 {call.Watch.ElapsedMilliseconds} ms：{exception.Message}");
    }

    /// <summary>
    /// Get / Query 开头的、订阅推送、上行发布不记
    /// </summary>
    private static bool IsRecorded(string action)
    {
        int dot = action.LastIndexOf('.');
        string method = action.Substring(dot + 1);
        if (method.StartsWith("Get", StringComparison.Ordinal) || method.StartsWith("Query", StringComparison.Ordinal))
        {
            return false;
        }

        return !Unrecorded.Contains(method);
    }

    /// <summary>
    /// "/xyz.Shared.Services.LoadPortService/Load" → "LoadPort.Load"（服务名去掉命名空间、开头的 I、结尾的 Service，方法去掉结尾的 Async）
    /// </summary>
    private static string ActionOf(string fullMethod)
    {
        string text = fullMethod.Trim('/');
        int slash = text.LastIndexOf('/');
        string service = text.Substring(0, Math.Max(slash, 0));
        string method = text.Substring(slash + 1);

        service = service.Substring(service.LastIndexOf('.') + 1);
        if (service.Length > 1 && service[0] == 'I' && char.IsUpper(service[1]))
        {
            service = service.Substring(1);
        }

        if (service.EndsWith("Service", StringComparison.Ordinal) && service.Length > "Service".Length)
        {
            service = service.Substring(0, service.Length - "Service".Length);
        }

        if (method.EndsWith("Async", StringComparison.Ordinal) && method.Length > "Async".Length)
        {
            method = method.Substring(0, method.Length - "Async".Length);
        }

        return $"{service}.{method}";
    }

    /// <summary>
    /// "ipv4:192.168.1.20:52341" → "192.168.1.20"，"ipv6:[::1]:52341" → "::1"
    /// </summary>
    private static string HostOf(string peer)
    {
        string text = peer;
        int colon = text.IndexOf(':');
        if (colon >= 0)
        {
            text = text.Substring(colon + 1);
        }

        int port = text.LastIndexOf(':');
        if (port > 0)
        {
            text = text.Substring(0, port);
        }

        return text.Trim('[', ']');
    }

    /// <summary>
    /// 参数原样转 JSON
    /// </summary>
    private static string Arguments(object? request)
    {
        // 参数本来就是 JSON，客户端装的时候中文就是原样，直接记
        if (request is RpcRequest rpc)
        {
            return string.IsNullOrWhiteSpace(rpc.Parameters) ? "{}" : rpc.Parameters;
        }

        try
        {
            return JsonHelper.Serialize(request);
        }
        catch (Exception exception)
        {
            return $"（参数转不成 JSON：{exception.Message}）";
        }
    }
}
