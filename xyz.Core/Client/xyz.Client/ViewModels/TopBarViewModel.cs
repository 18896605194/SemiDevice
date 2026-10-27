using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using xyz.Client.Common.Events;
using xyz.Client.Common.Log;
using xyz.Client.Common.Rpc;
using xyz.Client.Common.Session;
using xyz.Client.DataModels.ViewModels;
using xyz.Client.Presentation.Dialogs;
using xyz.Client.Presentation.Localization;
using xyz.Client.Session;
using xyz.Shared.Dtos;
using xyz.Shared.Rpc;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Client.ViewModels;

/// <summary>
/// 主界面顶栏右侧：四色灯、当前时间、当前用户（能点：登录、换人、改密码）、注销、整机复位。
/// 红 = 报警、黄 = 警告、绿 = 运行，跟后端推来的设备总状态（EquipmentStatusDto）亮；蓝 = 和后端的通讯。
/// </summary>
public class TopBarViewModel : BaseViewModel
{
    #region Column

    private bool _isAlarmOn;

    /// <summary>
    /// 红灯：有报警。
    /// </summary>
    public bool IsAlarmOn
    {
        get => _isAlarmOn;
        private set => SetProperty(ref _isAlarmOn, value);
    }

    private bool _isWarningOn;

    /// <summary>
    /// 黄灯：有警告。
    /// </summary>
    public bool IsWarningOn
    {
        get => _isWarningOn;
        private set => SetProperty(ref _isWarningOn, value);
    }

    private bool _isRunningOn;

    /// <summary>
    /// 绿灯：有模块在执行动作。
    /// </summary>
    public bool IsRunningOn
    {
        get => _isRunningOn;
        private set => SetProperty(ref _isRunningOn, value);
    }

    private bool _isConnected;

    /// <summary>
    /// 蓝灯：和后端连着。
    /// </summary>
    public bool IsConnected
    {
        get => _isConnected;
        private set => SetProperty(ref _isConnected, value);
    }

    private DateTime _now = DateTime.Now;

    /// <summary>
    /// 当前时间，每秒走一次。
    /// </summary>
    public DateTime Now
    {
        get => _now;
        private set => SetProperty(ref _now, value);
    }

    private string _userText = ClientSession.DefaultUserName;

    /// <summary>
    /// 用户块上面一行：显示名；没登录是"访客"，没启用登录是 Admin
    /// </summary>
    public string UserText
    {
        get => _userText;
        private set => SetProperty(ref _userText, value);
    }

    private string _roleCaption = string.Empty;

    /// <summary>
    /// 用户块下面一行：角色；没登录是"点这里登录"，没启用登录是"没启用登录"
    /// </summary>
    public string RoleCaption
    {
        get => _roleCaption;
        private set => SetProperty(ref _roleCaption, value);
    }

    private bool _isGuest;

    /// <summary>
    /// 启用了登录、现在没登录（访客）：用户块灰字，小菜单里只有"登录"
    /// </summary>
    public bool IsGuest
    {
        get => _isGuest;
        private set => SetProperty(ref _isGuest, value);
    }

    private bool _isLoggedIn;

    /// <summary>
    /// 登录着：小菜单里是"换人登录""改密码"，注销能点
    /// </summary>
    public bool IsLoggedIn
    {
        get => _isLoggedIn;
        private set => SetProperty(ref _isLoggedIn, value);
    }

    private bool _isLoginEnabled;

    /// <summary>
    /// 启用了登录：用户块才有小菜单（带小箭头）
    /// </summary>
    public bool IsLoginEnabled
    {
        get => _isLoginEnabled;
        private set => SetProperty(ref _isLoginEnabled, value);
    }

    #endregion

    #region Command

    /// <summary>
    /// 整机复位：复位全部有报警的来源（各组件走自己的 Reset，模块顺带发设备复位）。没连上后端时不可点。
    /// </summary>
    public IAsyncRelayCommand ResetCommand { get; }

    /// <summary>
    /// 关闭蜂鸣器的声音（右上角 Off 按钮）。
    /// </summary>
    public IRelayCommand BuzzerOffCommand { get; }

    /// <summary>
    /// 登录 / 换人登录：弹登录框
    /// </summary>
    public IRelayCommand LoginCommand { get; }

    /// <summary>
    /// 改自己的密码：登录着才能点
    /// </summary>
    public IRelayCommand ChangePasswordCommand { get; }

    /// <summary>
    /// 注销回访客：登录着才能点
    /// </summary>
    public IAsyncRelayCommand LogoutCommand { get; }

    #endregion

    #region Service

    private readonly IAlarmService _alarmService;
    private readonly DispatcherTimer _clock;
    private IDisposable? _statusSubscription;

    /// <summary>
    /// 最近一次收到的设备总状态；断开后清空，重连时后端补发当前值。
    /// </summary>
    private EquipmentStatusDto? _status;

    /// <summary>
    /// 登录框开着（开机的、点了登录 / 换人的、闲置退出弹的）：别再叠一个
    /// </summary>
    private bool _loginOpen;

    #endregion

    public TopBarViewModel()
    {
        _alarmService = GrpcClientFactory.Create<IAlarmService>();

        ResetCommand = new AsyncRelayCommand(DoReset, () => IsConnected);
        BuzzerOffCommand = new RelayCommand(DoBuzzerOff);
        LoginCommand = new RelayCommand(DoLogin);
        ChangePasswordCommand = new RelayCommand(DoChangePassword, () => ClientSession.IsLoggedIn);
        LogoutCommand = new AsyncRelayCommand(DoLogout, () => ClientSession.IsLoggedIn);

        _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clock.Tick += (_, _) => Now = DateTime.Now;
    }

    public override void Init()
    {
        Now = DateTime.Now;
        _clock.Start();

        // 设备总状态是留存消息：连上（含重连）就先收到当前值，之后变化才推。
        _statusSubscription?.Dispose();
        _statusSubscription = RemoteEventBus.Register<EquipmentStatusDto>(EquipmentStatusDto.EventToken, OnStatusChanged);

        RemoteConnection.ConnectionChanged -= OnConnectionChanged;
        RemoteConnection.ConnectionChanged += OnConnectionChanged;
        OnConnectionChanged(RemoteConnection.IsConnected);

        ClientSession.Changed -= UpdateUser;
        ClientSession.Changed += UpdateUser;
        UpdateUser();

        IdleLogout.TimedOut -= OnIdleTimedOut;
        IdleLogout.TimedOut += OnIdleTimedOut;
    }

    /// <summary>
    /// 开机弹的那次登录框（启用了登录、还没登录才弹）：多一行"取消就当访客"的说明
    /// </summary>
    public void ShowLoginAtStartup()
    {
        if (!ClientSession.IsLoginEnabled || ClientSession.IsLoggedIn)
        {
            return;
        }

        OpenLogin(string.Empty, true, null);
    }

    /// <summary>
    /// 闲置到点已经退出了：弹登录框要重新登录，写明为什么，登录名填好刚才那个人；取消就按访客（跟开机一样）
    /// </summary>
    private void OnIdleTimedOut(string userName, int minutes)
    {
        OpenLogin(userName, true, L10n.Get("account.login.idle_notice", minutes));
    }

    /// <summary>
    /// 弹登录框；已经开着一个就不再叠（比如有人点了换人登录就走开了，闲置到点时那个框还在）
    /// </summary>
    private void OpenLogin(string userName, bool showGuestHint, string? notice)
    {
        if (_loginOpen)
        {
            return;
        }

        _loginOpen = true;
        try
        {
            DialogService.ShowLogin(userName, showGuestHint, notice, ClientAccount.LoginAsync);
        }
        finally
        {
            _loginOpen = false;
        }
    }

    private void UpdateUser()
    {
        if (!ClientSession.IsLoginEnabled)
        {
            UserText = ClientSession.DefaultUserName;
            RoleCaption = L10n.Get("account.login_disabled");
        }
        else if (ClientSession.IsLoggedIn)
        {
            UserText = ClientSession.DisplayName;
            RoleCaption = RoleText.Of(ClientSession.RoleKind, ClientSession.RoleName);
        }
        else
        {
            UserText = L10n.Get("account.guest");
            RoleCaption = L10n.Get("account.click_to_login");
        }

        IsGuest = ClientSession.IsLoginEnabled && !ClientSession.IsLoggedIn;
        IsLoggedIn = ClientSession.IsLoggedIn;
        IsLoginEnabled = ClientSession.IsLoginEnabled;
        ChangePasswordCommand.NotifyCanExecuteChanged();
        LogoutCommand.NotifyCanExecuteChanged();
    }

    private void OnStatusChanged(EquipmentStatusDto status)
    {
        _status = status;
        UpdateLamps();
    }

    private void OnConnectionChanged(bool connected)
    {
        IsConnected = connected;
        if (!connected)
        {
            // 断开后不知道设备现在是什么状态，红黄绿先灭。
            _status = null;
        }

        UpdateLamps();
        ResetCommand.NotifyCanExecuteChanged();
    }

    private void UpdateLamps()
    {
        IsAlarmOn = _status?.HasAlarm == true;
        IsWarningOn = _status?.HasWarning == true;
        IsRunningOn = _status?.IsRunning == true;
    }

    private void DoBuzzerOff()
    {
        // 后端关蜂鸣器的接口还没做，先留空。
    }

    /// <summary>
    /// 登录、换人登录：登录名先填上现在这个人的，换人时改掉就行
    /// </summary>
    private void DoLogin()
    {
        OpenLogin(ClientSession.LoginName, false, null);
    }

    private void DoChangePassword()
    {
        string subtitle = $"{ClientSession.LoginName} · {RoleText.Of(ClientSession.RoleKind, ClientSession.RoleName)}";
        DialogService.ShowPassword(L10n.Get("account.password.change_title"), subtitle, true, ClientAccount.ChangePasswordAsync);
    }

    private Task DoLogout()
    {
        return ClientAccount.LogoutAsync(false);
    }

    private async Task DoReset()
    {
        try
        {
            var response = await _alarmService.ResetAllAsync(new RpcRequest());
            if (!response.Success)
            {
                ClientLog.Error("Alarm", $"复位失败：{L10n.Get(response.Code, response.Args)}");
                return;
            }

            ClientLog.Info("Alarm", $"已复位 {response.DeserializeData<int>()} 个报警来源");
        }
        catch (Exception exception)
        {
            ClientLog.Error("Alarm", $"复位失败：{exception.Message}");
        }
    }
}
