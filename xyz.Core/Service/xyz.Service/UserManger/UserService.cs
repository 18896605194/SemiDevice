using ProtoBuf.Grpc;
using xyz.Components.Interfaces;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Shared.Rpc;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Service.UserManger;

/// <summary>
/// 用户管理 gRPC 服务：转给账号组件，只翻结果
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
        var account = _account;
        if (account is null)
        {
            return NotInstalled();
        }

        var result = account.GetUsers();
        if (!result.IsSuccess)
        {
            return Task.FromResult(RpcResponse.Fail(result.ErrorMessage, result.Args));
        }

        return Task.FromResult(RpcResponse.Ok(JsonHelper.Serialize(result.Result)));
    }

    public Task<RpcResponse> CreateUserAsync(RpcRequest request, CallContext context = default)
    {
        var account = _account;
        if (account is null)
        {
            return NotInstalled();
        }

        if (!request.TryGet<string>(RpcParams.UserName, out var userName)
            || !request.TryGet<long>(RpcParams.RoleId, out var roleId)
            || !request.TryGet<string>(RpcParams.Password, out var password))
        {
            return InvalidRequest();
        }

        string displayName = request.Get(RpcParams.DisplayName, string.Empty);
        var result = account.CreateUser(userName, displayName, roleId, password);
        if (!result.IsSuccess)
        {
            return Task.FromResult(RpcResponse.Fail(result.ErrorMessage, result.Args));
        }

        return Task.FromResult(RpcResponse.Ok(JsonHelper.Serialize(result.Result)));
    }

    public Task<RpcResponse> UpdateUserAsync(RpcRequest request, CallContext context = default)
    {
        var account = _account;
        if (account is null)
        {
            return NotInstalled();
        }

        if (!request.TryGet<long>(RpcParams.Id, out var id)
            || !request.TryGet<long>(RpcParams.RoleId, out var roleId))
        {
            return InvalidRequest();
        }

        string displayName = request.Get(RpcParams.DisplayName, string.Empty);
        return Reply(account.UpdateUser(id, displayName, roleId));
    }

    public Task<RpcResponse> ResetPasswordAsync(RpcRequest request, CallContext context = default)
    {
        var account = _account;
        if (account is null)
        {
            return NotInstalled();
        }

        if (!request.TryGet<long>(RpcParams.Id, out var id)
            || !request.TryGet<string>(RpcParams.Password, out var password))
        {
            return InvalidRequest();
        }

        return Reply(account.ResetPassword(id, password));
    }

    public Task<RpcResponse> SetEnabledAsync(RpcRequest request, CallContext context = default)
    {
        var account = _account;
        if (account is null)
        {
            return NotInstalled();
        }

        if (!request.TryGet<long>(RpcParams.Id, out var id)
            || !request.TryGet<bool>(RpcParams.IsEnabled, out var isEnabled))
        {
            return InvalidRequest();
        }

        return Reply(account.SetUserEnabled(id, isEnabled));
    }

    public Task<RpcResponse> DeleteUserAsync(RpcRequest request, CallContext context = default)
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

        return Reply(account.DeleteUser(id));
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
