using xyz.Components.Components;
using xyz.Components.Interfaces;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Tools;

namespace xyz.Service;

/// <summary>
/// 服务基类
/// </summary>
public abstract class BaseService
{
    /// <summary>
    /// 获取模块组件
    /// </summary>
    protected static TModule? FindModule<TModule>(string name) where TModule : class, IModule
    {
        return IocHelper.GetServices<TModule>()
            .FirstOrDefault(module => string.Equals(module.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 失败回包：错误码 + 参数（顺序见错误码的注释），能直接回 Task 的地方都用它
    /// </summary>
    protected static Task<RpcResponse> Fail(string code, params string[] args)
    {
        return Task.FromResult(RpcResponse.Fail(code, args));
    }

    /// <summary>
    /// 模块不存在（module.not_found）。
    /// </summary>
    protected static Task<RpcResponse> ModuleNotFound(string module)
    {
        return Fail(ErrorCodes.ModuleNotFound, module);
    }

    /// <summary>
    /// 请求参数不对回包（rpc.request_invalid）
    /// </summary>
    protected static Task<RpcResponse> InvalidRequest()
    {
        return Fail(ErrorCodes.RequestInvalid);
    }

    /// <summary>
    /// 组件没装回包
    /// </summary>
    protected static Task<RpcResponse> NotInstalled(string code)
    {
        return Fail(code);
    }

    /// <summary>
    /// 组件的结果翻成回包：成功回 Ok，失败照原样回错误码和参数
    /// </summary>
    protected static Task<RpcResponse> Reply(HandleResult result)
    {
        if (!result.IsSuccess)
        {
            return Fail(result.ErrorMessage, [.. result.Args]);
        }

        return Task.FromResult(RpcResponse.Ok());
    }

    protected static Task<RpcResponse> Reply<T>(HandleResult<T> result)
    {
        if (!result.IsSuccess)
        {
            return Fail(result.ErrorMessage, [.. result.Args]);
        }

        return Task.FromResult(RpcResponse.Ok());
    }

    protected static Task<RpcResponse> ReplyData(HandleResult result)
    {
        if (!result.IsSuccess)
        {
            return Fail(result.ErrorMessage, [.. result.Args]);
        }

        return Task.FromResult(RpcResponse.Ok(JsonHelper.Serialize(result.Result)));
    }

    /// <summary>
    /// 同上，结果带类型的
    /// </summary>
    protected static Task<RpcResponse> ReplyData<T>(HandleResult<T> result)
    {
        if (!result.IsSuccess)
        {
            return Fail(result.ErrorMessage, [.. result.Args]);
        }

        return Task.FromResult(RpcResponse.Ok(JsonHelper.Serialize(result.Result)));
    }

    /// <summary>
    /// 下发动作并同步等终态：被拒照原样回入口给的错误码 + 参数；等超时回 module.wait_timeout（动作还在做，不能据此判失败或重发）；到终态回 Ok 或动作自己的错误码
    /// </summary>
    protected static Task<RpcResponse> RunRoutine(HandleResult<ModuleRoutine> started, int timeout)
    {
        var routine = started.Result;
        if (!started.IsSuccess || routine is null)
        {
            return Fail(started.ErrorMessage, [.. started.Args]);
        }

        if (!routine.WaitReply(timeout))
        {
            return Fail(ErrorCodes.WaitTimeout, routine.Name, timeout.ToString());
        }

        if (routine.IsSuccess)
        {
            return Task.FromResult(RpcResponse.Ok());
        }

        return Task.FromResult(Failed(routine));
    }

    /// <summary>
    /// 动作没做成的回包：错误码 + 参数；不止一步的再带上停在第几步，界面加在原因前面
    /// </summary>
    protected static RpcResponse Failed(ModuleRoutine routine)
    {
        var response = RpcResponse.Fail(routine.Code, routine.ErrorArgs);
        if (routine.StepCount > 1)
        {
            response.StepNumber = routine.StepNumber;
            response.StepCount = routine.StepCount;
        }

        return response;
    }
}
