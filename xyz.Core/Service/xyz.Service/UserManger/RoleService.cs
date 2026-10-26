using ProtoBuf.Grpc;
using xyz.Components.Interfaces;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Shared.Rpc;
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

    public Task<RpcResponse> CreateRoleAsync(RpcRequest request, CallContext context = default)
    {
        var account = _account;
        if (account is null)
        {
            return NotInstalled();
        }

        if (!request.TryGet<string>(RpcParams.Name, out var name))
        {
            return InvalidRequest();
        }

        string description = request.Get(RpcParams.Description, string.Empty);
        var result = account.CreateRole(name, description);
        if (!result.IsSuccess)
        {
            return Task.FromResult(RpcResponse.Fail(result.ErrorMessage, result.Args));
        }

        return Task.FromResult(RpcResponse.Ok(JsonHelper.Serialize(result.Result)));
    }

    public Task<RpcResponse> UpdateRoleAsync(RpcRequest request, CallContext context = default)
    {
        var account = _account;
        if (account is null)
        {
            return NotInstalled();
        }

        if (!request.TryGet<long>(RpcParams.Id, out var id)
            || !request.TryGet<string>(RpcParams.Name, out var name))
        {
            return InvalidRequest();
        }

        string description = request.Get(RpcParams.Description, string.Empty);
        return Reply(account.UpdateRole(id, name, description));
    }

    public Task<RpcResponse> DeleteRoleAsync(RpcRequest request, CallContext context = default)
    {
        var account = _account;
        if (account is null)
        {
            return NotInstalled();
        }

        if (!request.TryGet<long>(RpcParams.Id, out var id))
        {
            return InvalidRequest();
        }

        return Reply(account.DeleteRole(id));
    }

    public Task<RpcResponse> GetPermissionsAsync(RpcRequest request, CallContext context = default)
    {
        var account = _account;
        if (account is null)
        {
            return NotInstalled();
        }

        if (!request.TryGet<long>(RpcParams.Id, out var id))
        {
            return InvalidRequest();
        }

        var result = account.GetPermissions(id);
        if (!result.IsSuccess)
        {
            return Task.FromResult(RpcResponse.Fail(result.ErrorMessage, result.Args));
        }

        return Task.FromResult(RpcResponse.Ok(JsonHelper.Serialize(result.Result)));
    }

    public Task<RpcResponse> SavePermissionsAsync(RpcRequest request, CallContext context = default)
    {
        var account = _account;
        if (account is null)
        {
            return NotInstalled();
        }

        if (!request.TryGet<long>(RpcParams.RoleId, out var roleId)
            || !request.TryGet<List<RolePermissionItem>>(RpcParams.Items, out var items))
        {
            return InvalidRequest();
        }

        return Reply(account.SavePermissions(roleId, items));
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
