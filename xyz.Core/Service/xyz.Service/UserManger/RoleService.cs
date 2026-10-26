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
            return NotInstalled(ErrorCodes.AccountNotInstalled);
        }

        return ReplyData(account.GetRoles());
    }

    public Task<RpcResponse> CreateRoleAsync(RpcRequest request, CallContext context = default)
    {
        var account = _account;
        if (account is null)
        {
            return NotInstalled(ErrorCodes.AccountNotInstalled);
        }

        if (!request.TryGet<string>(RpcParams.Name, out var name))
        {
            return InvalidRequest();
        }

        string description = request.Get(RpcParams.Description, string.Empty);
        return ReplyData(account.CreateRole(name, description));
    }

    public Task<RpcResponse> UpdateRoleAsync(RpcRequest request, CallContext context = default)
    {
        var account = _account;
        if (account is null)
        {
            return NotInstalled(ErrorCodes.AccountNotInstalled);
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
            return NotInstalled(ErrorCodes.AccountNotInstalled);
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
            return NotInstalled(ErrorCodes.AccountNotInstalled);
        }

        if (!request.TryGet<long>(RpcParams.Id, out var id))
        {
            return InvalidRequest();
        }

        return ReplyData(account.GetPermissions(id));
    }

    public Task<RpcResponse> SavePermissionsAsync(RpcRequest request, CallContext context = default)
    {
        var account = _account;
        if (account is null)
        {
            return NotInstalled(ErrorCodes.AccountNotInstalled);
        }

        if (!request.TryGet<long>(RpcParams.RoleId, out var roleId)
            || !request.TryGet<List<RolePermissionItem>>(RpcParams.Items, out var items))
        {
            return InvalidRequest();
        }

        return Reply(account.SavePermissions(roleId, items));
    }
}
