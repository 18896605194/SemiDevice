using ProtoBuf.Grpc;
using xyz.Components.Interfaces;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Shared.Rpc;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Service.UserManger;

/// <summary>
/// 角色管理
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
        if (_account is null)
        {
            return NotInstalled(ErrorCodes.AccountNotInstalled);
        }

        return ReplyData(_account.GetRoles());
    }

    public Task<RpcResponse> CreateRoleAsync(RpcRequest request, CallContext context = default)
    {
        if (_account is null)
        {
            return NotInstalled(ErrorCodes.AccountNotInstalled);
        }

        if (!request.TryGet<string>(RpcParams.Name, out var name))
        {
            return InvalidRequest();
        }

        string description = request.Get(RpcParams.Description, string.Empty);
        return ReplyData(_account.CreateRole(name, description));
    }

    public Task<RpcResponse> UpdateRoleAsync(RpcRequest request, CallContext context = default)
    {
        if (_account is null)
        {
            return NotInstalled(ErrorCodes.AccountNotInstalled);
        }

        if (!request.TryGet<long>(RpcParams.Id, out var id)
            || !request.TryGet<string>(RpcParams.Name, out var name))
        {
            return InvalidRequest();
        }

        string description = request.Get(RpcParams.Description, string.Empty);
        return Reply(_account.UpdateRole(id, name, description));
    }

    public Task<RpcResponse> DeleteRoleAsync(RpcRequest request, CallContext context = default)
    {
        if (_account is null)
        {
            return NotInstalled(ErrorCodes.AccountNotInstalled);
        }

        if (!request.TryGet<long>(RpcParams.Id, out var id))
        {
            return InvalidRequest();
        }

        return Reply(_account.DeleteRole(id));
    }

    public Task<RpcResponse> GetPermissionsAsync(RpcRequest request, CallContext context = default)
    {
        if (_account is null)
        {
            return NotInstalled(ErrorCodes.AccountNotInstalled);
        }

        if (!request.TryGet<long>(RpcParams.Id, out var id))
        {
            return InvalidRequest();
        }

        return ReplyData(_account.GetPermissions(id));
    }

    public Task<RpcResponse> SavePermissionsAsync(RpcRequest request, CallContext context = default)
    {
        if (_account is null)
        {
            return NotInstalled(ErrorCodes.AccountNotInstalled);
        }

        if (!request.TryGet<long>(RpcParams.RoleId, out var roleId)
            || !request.TryGet<List<RolePermissionItem>>(RpcParams.Items, out var items))
        {
            return InvalidRequest();
        }

        return Reply(_account.SavePermissions(roleId, items));
    }
}
