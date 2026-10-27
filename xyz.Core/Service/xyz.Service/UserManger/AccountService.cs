using ProtoBuf.Grpc;
using xyz.Elements.Interfaces;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Shared.Rpc;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Service.UserManger;

public class AccountService : BaseService, IAccountService
{
    private readonly IAccount? _account;

    public AccountService()
    {
        _account = IocHelper.GetService<IAccount>();
    }

    public Task<RpcResponse> LoginAsync(RpcRequest request, CallContext context = default)
    {
        if (_account is null)
        {
            return NotInstalled(ErrorCodes.AccountNotInstalled);
        }

        if (!request.TryGet<string>(RpcParams.UserName, out var userName)|| !request.TryGet<string>(RpcParams.Password, out var password))
        {
            return InvalidRequest();
        }

        return ReplyData(_account.Login(userName, password));
    }

    public Task<RpcResponse> GetSessionAsync(RpcRequest request, CallContext context = default)
    {
        if (_account is null)
        {
            return NotInstalled(ErrorCodes.AccountNotInstalled);
        }

        string userName = request.Get(RpcParams.UserName, string.Empty);
        return ReplyData(_account.GetSession(userName));
    }

    public Task<RpcResponse> LogoutAsync(RpcRequest request, CallContext context = default)
    {
        if (_account is null)
        {
            return NotInstalled(ErrorCodes.AccountNotInstalled);
        }

        if (!request.TryGet<string>(RpcParams.UserName, out var userName))
        {
            return InvalidRequest();
        }

        bool isIdle = request.Get(RpcParams.IsIdle, false);
        _account.NoteLogout(userName, isIdle);
        return Task.FromResult(RpcResponse.Ok());
    }

    /// <summary>
    /// 修改密码
    /// </summary>
    /// <param name="request"></param>
    /// <param name="context"></param>
    /// <returns></returns>
    public Task<RpcResponse> ChangePasswordAsync(RpcRequest request, CallContext context = default)
    {
        if (_account is null)
        {
            return NotInstalled(ErrorCodes.AccountNotInstalled);
        }

        if (!request.TryGet<string>(RpcParams.UserName, out var userName)
            || !request.TryGet<string>(RpcParams.OldPassword, out var oldPassword)
            || !request.TryGet<string>(RpcParams.NewPassword, out var newPassword))
        {
            return InvalidRequest();
        }

        return Reply(_account.ChangePassword(userName, oldPassword, newPassword));
    }
}
