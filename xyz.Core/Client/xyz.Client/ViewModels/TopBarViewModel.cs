using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using xyz.Client.Common.Events;
using xyz.Client.Common.Log;
using xyz.Client.Common.Rpc;
using xyz.Client.DataModels.ViewModels;
using xyz.Client.Presentation.Localization;
using xyz.Shared.Dtos;
using xyz.Shared.Rpc;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Client.ViewModels;

/// <summary>
/// 主界面顶栏右侧：四色灯、当前时间、整机复位。
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

    #endregion

    #region Service

    private readonly IAlarmService _alarmService;
    private readonly DispatcherTimer _clock;
    private IDisposable? _statusSubscription;

    /// <summary>
    /// 最近一次收到的设备总状态；断开后清空，重连时后端补发当前值。
    /// </summary>
    private EquipmentStatusDto? _status;

    #endregion

    public TopBarViewModel()
    {
        _alarmService = GrpcClientFactory.Create<IAlarmService>();

        ResetCommand = new AsyncRelayCommand(DoReset, () => IsConnected);
        BuzzerOffCommand = new RelayCommand(DoBuzzerOff);

        _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clock.Tick += (_, _) => Now = DateTime.Now;
    }

    public override void Init()
    {
        Now = DateTime.Now;
        _clock.Start();

        // 设备总状态是留存消息：连上（含重连）就先收到当前值，之后变化才推。
        _statusSubscription?.Dispose();
        _statusSubscription = EventBus.Register<EquipmentStatusDto>(EquipmentStatusDto.EventToken, OnStatusChanged);

        RemoteEventBus.ConnectionChanged -= OnConnectionChanged;
        RemoteEventBus.ConnectionChanged += OnConnectionChanged;
        OnConnectionChanged(RemoteEventBus.IsConnected);
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
