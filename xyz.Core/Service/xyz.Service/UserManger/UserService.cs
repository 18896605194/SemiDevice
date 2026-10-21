using ProtoBuf.Grpc;
using xyz.Components.Interfaces;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
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

    public Task<RpcResponse> CreateUserAsync(UserCreateRequest request, CallContext context = default)
    {
        var account = _account;
        if (account is null)
        {
            return NotInstalled();
        }

        var result = account.CreateUser(request);
        if (!result.IsSuccess)
        {
            return Task.FromResult(RpcResponse.Fail(result.ErrorMessage, result.Args));
        }

        return Task.FromResult(RpcResponse.Ok(JsonHelper.Serialize(result.Result)));
    }

    public Task<RpcResponse> UpdateUserAsync(UserUpdateRequest request, CallContext context = default)
    {
        var account = _account;
        if (account is null)
        {
            return NotInstalled();
        }

        return Reply(account.UpdateUser(request));
    }

    public Task<RpcResponse> ResetPasswordAsync(UserPasswordResetRequest request, CallContext context = default)
    {
        var account = _account;
        if (account is null)
        {
            return NotInstalled();
        }

        return Reply(account.ResetPassword(request));
    }

    public Task<RpcResponse> SetEnabledAsync(UserEnableRequest request, CallContext context = default)
    {
        var account = _account;
        if (account is null)
        {
            return NotInstalled();
        }

        return Reply(account.SetUserEnabled(request));
    }

    public Task<RpcResponse> DeleteUserAsync(AccountIdRequest request, CallContext context = default)
    {
        var account = _account;
        if (account is null)
        {
            return NotInstalled();
        }

        return Reply(account.DeleteUser(request.Id));
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
