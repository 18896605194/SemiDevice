using ProtoBuf.Grpc;
using xyz.Components.Interfaces;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
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

    public Task<RpcResponse> LoginAsync(LoginRequest request, CallContext context = default)
    {
        var account = _account;
        if (account is null)
        {
            return NotInstalled();
        }

        return Reply(account.Login(request.UserName, request.Password));
    }

    public Task<RpcResponse> GetSessionAsync(SessionRequest request, CallContext context = default)
    {
        var account = _account;
        if (account is null)
        {
            return NotInstalled();
        }

        return Reply(account.GetSession(request.UserName));
    }

    public Task<RpcResponse> LogoutAsync(LogoutRequest request, CallContext context = default)
    {
        var account = _account;
        if (account is null)
        {
            return NotInstalled();
        }

        account.NoteLogout(request.UserName, request.IsIdle);
        return Task.FromResult(RpcResponse.Ok());
    }

    public Task<RpcResponse> ChangePasswordAsync(PasswordChangeRequest request, CallContext context = default)
    {
        var account = _account;
        if (account is null)
        {
            return NotInstalled();
        }

        var result = account.ChangePassword(request.UserName, request.OldPassword, request.NewPassword);
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
