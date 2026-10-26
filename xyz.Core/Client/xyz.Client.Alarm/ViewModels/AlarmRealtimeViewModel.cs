using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using xyz.Client.Common.Alarms;
using xyz.Client.Common.Events;
using xyz.Client.Common.Log;
using xyz.Client.Common.Rpc;
using xyz.Client.DataModels.ViewModels;
using xyz.Client.Presentation.Dialogs;
using xyz.Client.Presentation.Localization;
using xyz.Client.Presentation.Models;
using xyz.Shared.Dtos;
using xyz.Shared.Rpc;
using xyz.Shared.Services;

namespace xyz.Client.Alarm.ViewModels;

/// <summary>
/// 实时报警页 ViewModel：跟着客户端当前报警（ClientAlarms）走，最新的在最上面。
/// 后端配了 AI 时多一条工具栏：选中一条报警点"AI 分析"，结果弹窗显示（只给建议，不动设备）。
/// </summary>
public class AlarmRealtimeViewModel : BaseViewModel
{
    private const string LogModule = "Alarm";

    private const double MillisecondsPerSecond = 1000;

    #region Column

    /// <summary>
    /// 当前报警，最新的在最前。
    /// </summary>
    public ObservableCollection<AlarmModel> Alarms { get; } = [];

    private AlarmModel? _selectedAlarm;

    /// <summary>
    /// 表格里选中的报警（AI 分析的就是它）
    /// </summary>
    public AlarmModel? SelectedAlarm
    {
        get => _selectedAlarm;
        set
        {
            if (SetProperty(ref _selectedAlarm, value))
            {
                ExplainCommand.NotifyCanExecuteChanged();
            }
        }
    }

    private bool _isAiInstalled;

    /// <summary>
    /// 后端配了 AI 没有：配了才显示 AI 工具栏
    /// </summary>
    public bool IsAiInstalled
    {
        get => _isAiInstalled;
        private set
        {
            if (SetProperty(ref _isAiInstalled, value))
            {
                ExplainCommand.NotifyCanExecuteChanged();
            }
        }
    }

    #endregion

    #region Command

    /// <summary>
    /// 让 AI 解释选中的报警
    /// </summary>
    public IAsyncRelayCommand ExplainCommand { get; }

    #endregion

    #region Service

    private readonly IAiService _aiService;

    #endregion

    public AlarmRealtimeViewModel()
    {
        _aiService = GrpcClientFactory.Create<IAiService>();
        ExplainCommand = new AsyncRelayCommand(DoExplain, CanExplain);
    }

    public override void Init()
    {
        ClientAlarms.Changed -= Refresh;
        ClientAlarms.Changed += Refresh;
        Refresh();

        RemoteEventBus.ConnectionChanged -= OnConnectionChanged;
        RemoteEventBus.ConnectionChanged += OnConnectionChanged;
        OnConnectionChanged(RemoteEventBus.IsConnected);
    }

    /// <summary>
    /// 报警一变整表重建；选中的那条还在就接着选中它，不然一来新报警选中就丢了
    /// </summary>
    private void Refresh()
    {
        var selected = SelectedAlarm;
        AlarmModel? keep = null;
        Alarms.Clear();
        foreach (var dto in ClientAlarms.Active)
        {
            var alarm = AlarmModel.From(dto);
            Alarms.Add(alarm);
            if (selected is not null && alarm.Source == selected.Source && alarm.Code == selected.Code)
            {
                keep = alarm;
            }
        }

        SelectedAlarm = keep;
    }

    /// <summary>
    /// 连上后端时问一次配没配 AI（后端重启时 machine.xml 可能改了）
    /// </summary>
    private void OnConnectionChanged(bool connected)
    {
        if (connected)
        {
            _ = LoadAiStatus();
        }
    }

    private async Task LoadAiStatus()
    {
        try
        {
            var response = await _aiService.GetStatusAsync(new RpcRequest());
            IsAiInstalled = response.Success;
        }
        catch (Exception)
        {
            // 后端是没有这个服务的旧版本，或者刚好又断了：当没配，不报
            IsAiInstalled = false;
        }
    }

    private bool CanExplain()
    {
        return IsAiInstalled && SelectedAlarm is not null;
    }

    /// <summary>
    /// 让 AI 解释选中的报警：要等几秒到几十秒（等的时候按钮点不了），结果弹窗显示，下面列出它看了哪些数据；没做成记到日志栏
    /// </summary>
    private async Task DoExplain()
    {
        var alarm = SelectedAlarm;
        if (alarm is null)
        {
            return;
        }

        ClientLog.Info(LogModule, L10n.Get("alarmview.ai_running", alarm.Source, alarm.Text));
        try
        {
            var response = await _aiService.ExplainAlarmAsync(RpcRequest.Of((RpcParams.Source, alarm.Source), (RpcParams.Code, alarm.Code)));
            if (!response.Success)
            {
                string reason = string.IsNullOrEmpty(response.Code) ? response.Message : L10n.Get(response.Code, response.Args);
                ClientLog.Error(LogModule, L10n.Get("alarmview.ai_failed", reason));
                return;
            }

            var answer = response.DeserializeData<AiAnswerDto>();
            string basis = L10n.Get("alarmview.ai_basis", answer.LogCount, answer.Signals.Count, answer.Model, answer.ElapsedMs / MillisecondsPerSecond);
            DialogService.ShowConfirm(
                L10n.Get("alarmview.ai_title", alarm.Source, alarm.Text),
                answer.Text + Environment.NewLine + Environment.NewLine + basis,
                L10n.Get("common.ok"),
                danger: false,
                showCancel: false);
        }
        catch (Exception exception)
        {
            ClientLog.Error(LogModule, L10n.Get("alarmview.ai_failed", exception.Message));
        }
    }
}
