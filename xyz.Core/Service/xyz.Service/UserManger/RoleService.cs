using ProtoBuf.Grpc;
using xyz.Components.Interfaces;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Service.UserManger;

/// <summary>
/// 角色管理 gRPC 服务：转给账号组件，只翻结果
/// </summary>
public class RoleService : BaseService, IRoleService
{
    private readonly IAccount? _account;

    public RoleService()
    {
        _account = IocHelper.GetService<IAccount>();
    }

    public Task<RpcResponse> GetRolesAsync(RpcRequest request, CallContext context = default)
    {
        var account = _account;
        if (account is null)
        {
            return NotInstalled();
        }

        var result = account.GetRoles();
        if (!result.IsSuccess)
        {
            return Task.FromResult(RpcResponse.Fail(result.ErrorMessage, result.Args));
        }

        return Task.FromResult(RpcResponse.Ok(JsonHelper.Serialize(result.Result)));
    }

    public Task<RpcResponse> CreateRoleAsync(RoleSaveRequest request, CallContext context = default)
    {
        var account = _account;
        if (account is null)
        {
            return NotInstalled();
        }

        var result = account.CreateRole(request);
        if (!result.IsSuccess)
        {
            return Task.FromResult(RpcResponse.Fail(result.ErrorMessage, result.Args));
        }

        return Task.FromResult(RpcResponse.Ok(JsonHelper.Serialize(result.Result)));
    }

    public Task<RpcResponse> UpdateRoleAsync(RoleSaveRequest request, CallContext context = default)
    {
        var account = _account;
        if (account is null)
        {
            return NotInstalled();
        }

        return Reply(account.UpdateRole(request));
    }

    public Task<RpcResponse> DeleteRoleAsync(AccountIdRequest request, CallContext context = default)
    {
        var account = _account;
        if (account is null)
        {
            return NotInstalled();
        }

        return Reply(account.DeleteRole(request.Id));
    }

    public Task<RpcResponse> GetPermissionsAsync(AccountIdRequest request, CallContext context = default)
    {
        var account = _account;
        if (account is null)
        {
            return NotInstalled();
        }

        var result = account.GetPermissions(request.Id);
        if (!result.IsSuccess)
        {
            return Task.FromResult(RpcResponse.Fail(result.ErrorMessage, result.Args));
        }

        return Task.FromResult(RpcResponse.Ok(JsonHelper.Serialize(result.Result)));
    }

    public Task<RpcResponse> SavePermissionsAsync(RolePermissionsSaveRequest request, CallContext context = default)
    {
        var account = _account;
        if (account is null)
        {
            return NotInstalled();
        }

        return Reply(account.SavePermissions(request));
    }

    private static Task<RpcResponse> NotInstalled()
    {
        return Task.FromResult(RpcResponse.Fail(ErrorCodes.AccountNotInstalled, []));
    }

    private static Task<RpcResponse> Reply(HandleResult result)
    {
        return Task.FromResult(result.IsSuccess ? RpcResponse.Ok() : RpcResponse.Fail(result.ErrorMessage, result.Args));
    }
}
