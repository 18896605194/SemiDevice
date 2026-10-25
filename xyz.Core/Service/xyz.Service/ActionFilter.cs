using System.Diagnostics;
using System.Text.RegularExpressions;
using Grpc.Core;
using Grpc.Core.Interceptors;
using xyz.Common.Log;
using xyz.Shared.Dtos;
using xyz.Tools;

namespace xyz.Service;

/// <summary>
/// 操作日志：客户端每次调后端都过这里，记谁（请求头里的操作人）、哪台电脑、调了什么、参数，回包再记结果和用时。
/// 只读的（Get / Query 开头）和自动反复发的（续点动、订阅推送、上行发布）不记；参数里的密码写成 ***。宿主在 AddCodeFirstGrpc 里挂上
/// </summary>
public sealed class ActionFilter : Interceptor
{
    /// <summary>
    /// 不是 Get / Query 开头、但也不记的：续点动（按住点动按钮时客户端自动反复发）、订阅推送、客户端往后端发事件
    /// </summary>
    private static readonly HashSet<string> Unrecorded = new(StringComparer.Ordinal)
    {
        "AxisJogRenew",
        "Subscribe",
        "Publish",
    };

    /// <summary>
    /// 参数 JSON 里名字带 Password 的字段（登录、改密码、重置密码、建账号）
    /// </summary>
    private static readonly Regex PasswordField = new(
        "\"(?<name>[A-Za-z]*Password[A-Za-z]*)\"\\s*:\\s*\"(?:[^\"\\\\]|\\\\.)*\"",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(TRequest request, ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        var call = Begin(request, context);
        if (call is null)
        {
            return await continuation(request, context).ConfigureAwait(false);
        }

        try
        {
            var response = await continuation(request, context).ConfigureAwait(false);
            End(call, response);
            return response;
        }
        catch (Exception exception)
        {
            Fault(call, exception);
            throw;
        }
    }

    public override async Task ServerStreamingServerHandler<TRequest, TResponse>(TRequest request, IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context, ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        var call = Begin(request, context);
        if (call is null)
        {
            await continuation(request, responseStream, context).ConfigureAwait(false);
            return;
        }

        try
        {
            await continuation(request, responseStream, context).ConfigureAwait(false);
            End(call, null);
        }
        catch (Exception exception)
        {
            Fault(call, exception);
            throw;
        }
    }

    /// <summary>
    /// 请求进来：要记的记一条，回包时接着用；不记的返回 null
    /// </summary>
    private static Call? Begin<TRequest>(TRequest request, ServerCallContext context)
    {
        string action = ActionOf(context.Method);
        if (!IsRecorded(action))
        {
            return null;
        }

        var call = new Call($"{RpcOperator.Of(context)}@{HostOf(context.Peer)} {action}");
        LogHelper.Operation(NLog.LogLevel.Info, $"{call.Who} {Arguments(request)}");
        return call;
    }

    /// <summary>
    /// 回包：做成了记 Info，回的是失败（错误码 + 参数）记 Warn；推流的做完就算成功
    /// </summary>
    private static void End(Call call, object? response)
    {
        long elapsed = call.Watch.ElapsedMilliseconds;
        if (response is RpcResponse rpc && !rpc.Success)
        {
            LogHelper.Operation(NLog.LogLevel.Warn, $"{call.Who} → 失败 {rpc.Code} [{string.Join(", ", rpc.Args)}] {elapsed} ms");
            return;
        }

        LogHelper.Operation(NLog.LogLevel.Info, $"{call.Who} → 成功 {elapsed} ms");
    }

    private static void Fault(Call call, Exception exception)
    {
        LogHelper.Operation(NLog.LogLevel.Error, $"{call.Who} → 出错 {call.Watch.ElapsedMilliseconds} ms：{exception.Message}");
    }

    /// <summary>
    /// 方法名去掉 Get / Query 开头的、和那几个自动反复发的
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
    /// 参数原样转 JSON，密码写成 ***
    /// </summary>
    private static string Arguments<TRequest>(TRequest request)
    {
        try
        {
            string json = JsonHelper.ToLogText(request);
            return PasswordField.Replace(json, match => $"\"{match.Groups["name"].Value}\":\"***\"");
        }
        catch (Exception exception)
        {
            return $"（参数转不成 JSON：{exception.Message}）";
        }
    }

    /// <summary>
    /// 一次要记的调用：谁@哪台电脑 调了什么，和从进来开始的计时
    /// </summary>
    private sealed class Call(string who)
    {
        public string Who { get; } = who;

        public Stopwatch Watch { get; } = Stopwatch.StartNew();
    }
}
