using xyz.Common.Log;
using xyz.Components;
using xyz.Modules;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;

namespace xyz.Service;

/// <summary>
/// 服务基类：组件树访问 + 模块动作「下发 → 等终态 → 回包」的公共流程。
/// 各动作服务只管两件事：找模块、调对应动作，错误码拼装与超时判定都在这里。
/// </summary>
public abstract class BaseService
{
    protected BaseService(IReadOnlyList<ComponentBase> roots)
    {
        Roots = roots;
    }

    /// <summary>
    /// 组件树根列表（按 sc.xml 装配）。
    /// </summary>
    protected IReadOnlyList<ComponentBase> Roots { get; }

    /// <summary>
    /// 按模块实例名查找模块（忽略大小写）；找不到返回 null。
    /// </summary>
    protected TModule? FindModule<TModule>(string name) where TModule : ComponentBase
    {
        return Roots.OfType<TModule>()
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
    /// 下发动作并同步等终态：
    /// 动作被拒（operation 为 null）→ module.action_rejected + 当前状态码；
    /// 等待超时 → module.wait_timeout + [操作名, 等待ms]（操作仍在执行，不能据此判失败或重发）；
    /// 到终态 → Ok 或操作自身错误码。
    /// </summary>
    protected static Task<RpcResponse> RunOperation(string module, BaseLoadPortModule port,
        ModuleOperation? operation, int timeout)
    {
        if (operation is null)
        {
            return Task.FromResult(RpcResponse.Fail(ErrorCodes.ActionRejected, [module, port.State.ToString()]));
        }

        LogHelper.Debug($"[LoadPort] {module} {operation.Name}: {operation.State}, Reason={operation.Reason}");

        if (!operation.WaitReply(timeout))
        {
            return Task.FromResult(RpcResponse.Fail(ErrorCodes.WaitTimeout, [operation.Name, timeout.ToString()]));
        }

        return Task.FromResult(operation.IsSuccess
            ? RpcResponse.Ok()
            : RpcResponse.Fail(operation.Code, operation.ErrorArgs));
    }
}
