using ProtoBuf.Grpc;
using xyz.Components.Interfaces;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Shared.Rpc;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Service.UserManger;

/// <summary>
/// 用户管理
/// </summary>
public class UserService : BaseService, IUserService
{
    private readonly IAccount? _account;

    public UserService()
    {
        _account = IocHelper.GetService<IAccount>();
    }

    public Task<RpcResponse> GetUsersAsync(RpcRequest request, CallContext context = default)
    {
        if (_account is null)
        {
            return NotInstalled(ErrorCodes.AccountNotInstalled);
        }

        return ReplyData(_account.GetUsers());
    }

    public Task<RpcResponse> CreateUserAsync(RpcRequest request, CallContext context = default)
    {
        if (_account is null)
        {
            return NotInstalled(ErrorCodes.AccountNotInstalled);
        }

        if (!request.TryGet<string>(RpcParams.UserName, out var userName)
            || !request.TryGet<long>(RpcParams.RoleId, out var roleId)
            || !request.TryGet<string>(RpcParams.Password, out var password))
        {
            return InvalidRequest();
        }

        string displayName = request.Get(RpcParams.DisplayName, string.Empty);
        return ReplyData(_account.CreateUser(userName, displayName, roleId, password));
    }

    public Task<RpcResponse> UpdateUserAsync(RpcRequest request, CallContext context = default)
    {
        if (_account is null)
        {
            return NotInstalled(ErrorCodes.AccountNotInstalled);
        }

        if (!request.TryGet<long>(RpcParams.Id, out var id)
            || !request.TryGet<long>(RpcParams.RoleId, out var roleId))
        {
            return InvalidRequest();
        }

        string displayName = request.Get(RpcParams.DisplayName, string.Empty);
        return Reply(_account.UpdateUser(id, displayName, roleId));
    }

    public Task<RpcResponse> ResetPasswordAsync(RpcRequest request, CallContext context = default)
    {
        if (_account is null)
        {
            return NotInstalled(ErrorCodes.AccountNotInstalled);
        }

        if (!request.TryGet<long>(RpcParams.Id, out var id)
            || !request.TryGet<string>(RpcParams.Password, out var password))
        {
            return InvalidRequest();
        }

        return Reply(_account.ResetPassword(id, password));
    }

    public Task<RpcResponse> SetEnabledAsync(RpcRequest request, CallContext context = default)
    {
        if (_account is null)
        {
            return NotInstalled(ErrorCodes.AccountNotInstalled);
        }

        if (!request.TryGet<long>(RpcParams.Id, out var id)
            || !request.TryGet<bool>(RpcParams.IsEnabled, out var isEnabled))
        {
            return InvalidRequest();
        }

        return Reply(_account.SetUserEnabled(id, isEnabled));
    }

    public Task<RpcResponse> DeleteUserAsync(RpcRequest request, CallContext context = default)
    {
        if (_account is null)
        {
            return NotInstalled(ErrorCodes.AccountNotInstalled);
        }

        if (!request.TryGet<long>(RpcParams.Id, out var id))
        {
            return InvalidRequest();
        }

        return Reply(_account.DeleteUser(id));
    }
}
