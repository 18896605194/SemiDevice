using xyz.Client.Common.Events;
using xyz.Client.Common.Log;
using xyz.Client.Common.Rpc;
using xyz.Client.Common.Session;
using xyz.Client.Presentation.Localization;
using xyz.Shared.Dtos;
using xyz.Shared.Rpc;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Client.Session;

/// <summary>
/// 登录、注销、刷新会话：调后端的登录服务，结果写进 ClientSession（都在界面线程上）
/// </summary>
public static class ClientAccount
{
    private const string LogModule = "Account";

    private static IDisposable? _subscription;

    /// <summary>
    /// 启动时调一次：settings 为 null（后端不在线）时先当要登录、什么都只看，连上以后再定；要登录的先取访客的会话，菜单按访客的档位排
    /// </summary>
    public static async Task InitializeAsync(SystemSettingsDto? settings)
    {
        ClientSession.SetLoginEnabled(settings is null || settings.IsLoginEnabled);
        _subscription ??= EventBus.Register<AccountChangedDto>(AccountChangedDto.EventToken, changed => _ = RefreshAsync());
        RemoteEventBus.ConnectionChanged -= OnConnectionChanged;
        RemoteEventBus.ConnectionChanged += OnConnectionChanged;
        if (settings is not null)
        {
            await RefreshAsync();
        }
    }

    /// <summary>
    /// 登录：成功换成这个人的会话、回 null；没成回给人看的原因
    /// </summary>
    public static async Task<string?> LoginAsync(string userName, string password)
    {
        try
        {
            var response = await GrpcClientFactory.Create<IAccountService>()
                .LoginAsync(RpcRequest.Of((RpcParams.UserName, userName), (RpcParams.Password, password)));
            if (!response.Success)
            {
                return ErrorText.Of(response);
            }

            var session = response.DeserializeData<SessionDto>();
            ClientSession.Apply(session);
            ClientLog.Info(LogModule, $"{session.UserName} 登录（{RoleText.Of(session.RoleKind, session.RoleName)}）");
            return null;
        }
        catch (Exception exception)
        {
            return exception.Message;
        }
    }

    /// <summary>
    /// 注销：回到访客；isIdle = 闲置自动注销的
    /// </summary>
    public static async Task LogoutAsync(bool isIdle)
    {
        string userName = ClientSession.LoginName;
        if (userName.Length == 0)
        {
            return;
        }

        if (isIdle)
        {
            ClientLog.Info(LogModule, $"{ClientSession.IdleLogoutMinutes} 分钟没有操作，已自动注销（{userName}）");
        }
        else
        {
            ClientLog.Info(LogModule, $"{userName} 注销");
        }

        // 先在本地回到访客（档位先全按"只看"），取到访客的会话再换上
        ClientSession.Apply(new SessionDto { RoleKind = RoleKind.Guest });
        try
        {
            await GrpcClientFactory.Create<IAccountService>()
                .LogoutAsync(RpcRequest.Of((RpcParams.UserName, userName), (RpcParams.IsIdle, isIdle)));
        }
        catch
        {
            // 后端不在线：注销只是记一行日志，本地照样回访客
        }

        await RefreshAsync();
    }

    /// <summary>
    /// 改自己的密码：成功回 null，没成回给人看的原因
    /// </summary>
    public static async Task<string?> ChangePasswordAsync(string oldPassword, string newPassword)
    {
        try
        {
            var response = await GrpcClientFactory.Create<IAccountService>().ChangePasswordAsync(RpcRequest.Of(
                (RpcParams.UserName, ClientSession.LoginName),
                (RpcParams.OldPassword, oldPassword),
                (RpcParams.NewPassword, newPassword)));
            if (!response.Success)
            {
                return ErrorText.Of(response);
            }

            ClientLog.Info(LogModule, $"{ClientSession.LoginName} 改了自己的密码");
            return null;
        }
        catch (Exception exception)
        {
            return exception.Message;
        }
    }

    /// <summary>
    /// 重新取当前的会话（权限变了、重连以后）：登录着的这个人被删了、停用了就回访客
    /// </summary>
    public static async Task RefreshAsync()
    {
        if (!ClientSession.IsLoginEnabled)
        {
            return;
        }

        string userName = ClientSession.LoginName;
        try
        {
            var response = await GrpcClientFactory.Create<IAccountService>()
                .GetSessionAsync(RpcRequest.Of((RpcParams.UserName, userName)));
            if (response.Success)
            {
                ClientSession.Apply(response.DeserializeData<SessionDto>());
                return;
            }

            if (userName.Length > 0)
            {
                ClientLog.Warn(LogModule, $"{userName} 不能再用（{ErrorText.Of(response)}），回到访客");
                ClientSession.Apply(new SessionDto { RoleKind = RoleKind.Guest });
                await RefreshAsync();
                return;
            }

            ClientLog.Warn(LogModule, $"取访客的权限没成：{ErrorText.Of(response)}");
        }
        catch (Exception exception)
        {
            // 后端不在线：留着现在的会话，连上以后再取
            ClientLog.Warn(LogModule, $"取会话没成：{exception.Message}");
        }
    }

    /// <summary>
    /// 连上后端（含重连）：开机时没问到要不要登录的这时候问；再取一次会话（断线期间权限可能变了）
    /// </summary>
    private static async void OnConnectionChanged(bool connected)
    {
        if (!connected)
        {
            return;
        }

        try
        {
            var response = await GrpcClientFactory.Create<ISystemService>().GetSettingsAsync(new RpcRequest());
            if (response.Success)
            {
                ClientSession.SetLoginEnabled(response.DeserializeData<SystemSettingsDto>().IsLoginEnabled);
            }
        }
        catch
        {
            // 问不到就按原来的
        }

        await RefreshAsync();
    }
}
