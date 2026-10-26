using ProtoBuf.Grpc;
using xyz.Components.Interfaces;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Shared.Rpc;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Service.UserManger;

/// <summary>
/// 登录 gRPC 服务：转给账号组件，只翻结果
/// </summary>
public class AccountService : BaseService, IAccountService
{
    private readonly IAccount? _account;

    public AccountService()
    {
        _account = IocHelper.GetService<IAccount>();
    }

    public Task<RpcResponse> LoginAsync(RpcRequest request, CallContext context = default)
    {
        var account = _account;
        if (account is null)
        {
            return NotInstalled();
        }

        if (!request.TryGet<string>(RpcParams.UserName, out var userName)
            || !request.TryGet<string>(RpcParams.Password, out var password))
        {
            return InvalidRequest();
        }

        return Reply(account.Login(userName, password));
    }

    public Task<RpcResponse> GetSessionAsync(RpcRequest request, CallContext context = default)
    {
        var account = _account;
        if (account is null)
        {
            return NotInstalled();
        }

        string userName = request.Get(RpcParams.UserName, string.Empty);
        return Reply(account.GetSession(userName));
    }

    public Task<RpcResponse> LogoutAsync(RpcRequest request, CallContext context = default)
    {
        var account = _account;
        if (account is null)
        {
            return NotInstalled();
        }

        if (!request.TryGet<string>(RpcParams.UserName, out var userName))
        {
            return InvalidRequest();
        }

        bool isIdle = request.Get(RpcParams.IsIdle, false);
        account.NoteLogout(userName, isIdle);
        return Task.FromResult(RpcResponse.Ok());
    }

    public Task<RpcResponse> ChangePasswordAsync(RpcRequest request, CallContext context = default)
    {
        var account = _account;
        if (account is null)
        {
            return NotInstalled();
        }

        if (!request.TryGet<string>(RpcParams.UserName, out var userName)
            || !request.TryGet<string>(RpcParams.OldPassword, out var oldPassword)
            || !request.TryGet<string>(RpcParams.NewPassword, out var newPassword))
        {
            return InvalidRequest();
        }

        var result = account.ChangePassword(userName, oldPassword, newPassword);
        return Task.FromResult(result.IsSuccess ? RpcResponse.Ok() : RpcResponse.Fail(result.ErrorMessage, result.Args));
    }

    private static Task<RpcResponse> NotInstalled()
    {
        return Task.FromResult(RpcResponse.Fail(ErrorCodes.AccountNotInstalled, []));
    }

    private static Task<RpcResponse> Reply(HandleResult<SessionDto> result)
    {
        if (!result.IsSuccess)
        {
            return Task.FromResult(RpcResponse.Fail(result.ErrorMessage, result.Args));
        }

        return Task.FromResult(RpcResponse.Ok(JsonHelper.Serialize(result.Result)));
    }
}
