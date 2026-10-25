using xyz.Components.Components;
using xyz.Components.Interfaces;
using xyz.Modules;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Tools;

namespace xyz.Service;

/// <summary>
/// 服务基类：找模块（按接口从容器里取）+ 模块动作「下发 → 等终态 → 回包」的公共流程。
/// 各动作服务只管两件事：找模块、调对应动作，错误码拼装与超时判定都在这里。
/// </summary>
public abstract class BaseService
{
    /// <summary>
    /// 按模块实例名查找模块（忽略大小写）；找不到返回 null。
    /// </summary>
    protected static TModule? FindModule<TModule>(string name) where TModule : class, IModule
    {
        return IocHelper.GetServices<TModule>()
            .FirstOrDefault(module => string.Equals(module.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 模块不存在回包（module.not_found）。
    /// </summary>
    protected static Task<RpcResponse> ModuleNotFound(string module)
    {
        return Task.FromResult(RpcResponse.Fail(ErrorCodes.ModuleNotFound, [module]));
    }

    /// <summary>
    /// 切生产模式：只在这类模块里按名字找（LoadPort 的服务不会切到同名的别类模块），只改 Mode、不经设备，置位即成功
    /// </summary>
    protected Task<RpcResponse> SetProduction<TModule>(string module) where TModule : class, IModule
    {
        var target = FindModule<TModule>(module) as IMode;
        if (target is null)
        {
            return ModuleNotFound(module);
        }

        target.SetProduction();
        return Task.FromResult(RpcResponse.Ok());
    }

    /// <summary>
    /// 切维护模式：同 <see cref="SetProduction{TModule}"/>
    /// </summary>
    protected Task<RpcResponse> SetMaintenance<TModule>(string module) where TModule : class, IModule
    {
        var target = FindModule<TModule>(module) as IMode;
        if (target is null)
        {
            return ModuleNotFound(module);
        }

        target.SetMaintenance();
        return Task.FromResult(RpcResponse.Ok());
    }

    /// <summary>
    /// 下发 LoadPort 动作并同步等终态（回包规则见私有重载）。
    /// </summary>
    protected static Task<RpcResponse> RunRoutine(string module, ILoadPort port,
        ModuleRoutine? routine, int timeout)
    {
        return RunRoutine(module, port.State, routine, timeout);
    }

    /// <summary>
    /// 下发已经改成"状态 → 资源 → 互锁"入口的 LoadPort 动作：被拒照原样回入口给的错误码 + 参数，发起了就同步等终态（回包规则见私有重载）。
    /// </summary>
    protected static Task<RpcResponse> RunRoutine(string module, ILoadPort port,
        HandleResult<ModuleRoutine> started, int timeout)
    {
        if (!started.IsSuccess)
        {
            return Task.FromResult(RpcResponse.Fail(started.ErrorMessage, [.. started.Args]));
        }

        return RunRoutine(module, port.State, started.Result, timeout);
    }

    /// <summary>
    /// 下发 LoadPort 的复位、中止：这两个跟着组件基类返回动作或 null，被拒了再问端口原因（whyRejected，就是端口的 CheckReset / CheckAbort）回给界面；
    /// 问的时候状态已经变了、问不出原因的，回笼统的 module.action_rejected。发起了就同步等终态（回包规则见私有重载）。
    /// </summary>
    protected static Task<RpcResponse> RunRoutine(string module, ILoadPort port,
        ModuleRoutine? routine, Func<HandleResult?> whyRejected, int timeout)
    {
        if (routine is null)
        {
            var rejected = whyRejected();
            if (rejected is not null)
            {
                return Task.FromResult(RpcResponse.Fail(rejected.ErrorMessage, [.. rejected.Args]));
            }
        }

        return RunRoutine(module, port.State, routine, timeout);
    }

    /// <summary>
    /// 下发 _robot 的动作（"状态 → 资源 → 互锁"入口，跟 LoadPort 一样）：被拒照原样回入口给的错误码 + 参数，发起了就同步等终态（回包规则见私有重载）。
    /// </summary>
    protected static Task<RpcResponse> RunRoutine(string module, IRobot robot,
        HandleResult<ModuleRoutine> started, int timeout)
    {
        if (!started.IsSuccess)
        {
            return Task.FromResult(RpcResponse.Fail(started.ErrorMessage, [.. started.Args]));
        }

        return RunRoutine(module, robot.State, started.Result, timeout);
    }

    /// <summary>
    /// 下发 _robot 的复位、急停：跟着组件基类返回动作或 null，被拒了再问原因（whyRejected，就是机械手的 CheckReset / CheckAbort）回给界面；
    /// 问不出原因的回笼统的 module.action_rejected。发起了就同步等终态（回包规则见私有重载）。
    /// </summary>
    protected static Task<RpcResponse> RunRoutine(string module, IRobot robot,
        ModuleRoutine? routine, Func<HandleResult?> whyRejected, int timeout)
    {
        if (routine is null)
        {
            var rejected = whyRejected();
            if (rejected is not null)
            {
                return Task.FromResult(RpcResponse.Fail(rejected.ErrorMessage, [.. rejected.Args]));
            }
        }

        return RunRoutine(module, robot.State, routine, timeout);
    }

    /// <summary>
    /// 下发腔体的动作（"状态 → 资源 → 互锁"入口，跟 LoadPort 一样）：被拒照原样回入口给的错误码 + 参数，发起了就同步等终态（回包规则见私有重载）。
    /// </summary>
    protected static Task<RpcResponse> RunRoutine(string module, IChamber chamber,
        HandleResult<ModuleRoutine> started, int timeout)
    {
        if (!started.IsSuccess)
        {
            return Task.FromResult(RpcResponse.Fail(started.ErrorMessage, [.. started.Args]));
        }

        return RunRoutine(module, chamber.State, started.Result, timeout);
    }

    /// <summary>
    /// 下发腔体的复位、中止：跟着组件基类返回动作或 null，被拒了再问原因（whyRejected，就是腔体的 CheckReset / CheckAbort）回给界面；
    /// 问不出原因的回笼统的 module.action_rejected。发起了就同步等终态（回包规则见私有重载）。
    /// </summary>
    protected static Task<RpcResponse> RunRoutine(string module, IChamber chamber,
        ModuleRoutine? routine, Func<HandleResult?> whyRejected, int timeout)
    {
        if (routine is null)
        {
            var rejected = whyRejected();
            if (rejected is not null)
            {
                return Task.FromResult(RpcResponse.Fail(rejected.ErrorMessage, [.. rejected.Args]));
            }
        }

        return RunRoutine(module, chamber.State, routine, timeout);
    }

    /// <summary>
    /// 下发动作并同步等终态：
    /// 动作被拒（routine 为 null）→ module.action_rejected + 当前状态码（调用方在发起动作之后读取）；
    /// 等待超时 → module.wait_timeout + [动作名, 等待ms]（动作还在做，不能据此判失败或重发）；
    /// 到终态 → Ok 或动作自身的错误码。
    /// </summary>
    private static Task<RpcResponse> RunRoutine(string module, int state,
        ModuleRoutine? routine, int timeout)
    {
        if (routine is null)
        {
            return Task.FromResult(RpcResponse.Fail(ErrorCodes.ActionRejected, [module, state.ToString()]));
        }


        if (!routine.WaitReply(timeout))
        {
            return Task.FromResult(RpcResponse.Fail(ErrorCodes.WaitTimeout, [routine.Name, timeout.ToString()]));
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
