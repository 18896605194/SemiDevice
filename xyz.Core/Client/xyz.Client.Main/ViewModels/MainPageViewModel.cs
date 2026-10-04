using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using xyz.Client.Common.Alarms;
using xyz.Client.Common.Events;
using xyz.Client.Common.Log;
using xyz.Client.Common.Rpc;
using xyz.Client.DataModels.ViewModels;
using xyz.Client.Main.Models;
using xyz.Client.Presentation.Controls;
using xyz.Client.Presentation.Dialogs;
using xyz.Client.Presentation.Localization;
using xyz.Client.Presentation.Models;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Shared.Rpc;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Client.Main.ViewModels;

/// <summary>
/// 主界面 ViewModel：左边系统操作（系统状态、当前模式、报警条数，Auto / Manual / Stop / Reset），
/// 右边一个 LoadPort 一个页签（载具、槽图、LotID、Sequence、槽位表）。中间的整机调度是另一块（默认是公共控件 DispatchMap，机型可换），不归这里。
/// 状态全靠订推送：设备总状态、各 LoadPort 状态都是留存消息，订上就补发、重连后重放；报警条数跟着客户端的当前报警（ClientAlarms）。
/// Job 和按 Job 自动调度还没做：创建 / 启动 Job 在界面上灰着，⊕ ⊖ 和 Sequence 框只改界面上的选择。
/// </summary>
public class MainPageViewModel : BaseViewModel
{
    private const string LogModule = "Main";

    /// <summary>
    /// 选 Sequence 弹窗里编号列的宽度（名称列占满剩下的）。
    /// </summary>
    private const double IndexColumnWidth = 80;

    #region Column

    private bool _isConnected;

    /// <summary>
    /// 跟后端连着没有：没连上（或连上了还没收到设备总状态）时系统状态是"未连接"，四个按钮都点不了。
    /// </summary>
    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (SetProperty(ref _isConnected, value))
            {
                RefreshSystem();
            }
        }
    }

    /// <summary>
    /// 连着、而且收到了设备总状态（知道现在是什么模式）：四个按钮这时候才能点。
    /// </summary>
    private bool IsReady => IsConnected && _status is not null;

    private string _systemStateText = string.Empty;

    /// <summary>
    /// 系统状态字（状态徽标显示）：未连接 / 报警 / 运行中 / 警告 / 空闲。
    /// </summary>
    public string SystemStateText
    {
        get => _systemStateText;
        private set => SetProperty(ref _systemStateText, value);
    }

    private ModuleStateTone _systemStateTone;

    /// <summary>
    /// 系统状态色调：灰 未连接、红 报警、蓝 运行中、黄 警告、绿 空闲。
    /// </summary>
    public ModuleStateTone SystemStateTone
    {
        get => _systemStateTone;
        private set => SetProperty(ref _systemStateTone, value);
    }

    private bool _isAuto;

    /// <summary>
    /// 整机模式：true = Auto（自动派单开着）。
    /// </summary>
    public bool IsAuto
    {
        get => _isAuto;
        private set => SetProperty(ref _isAuto, value);
    }

    private string _modeText = string.Empty;

    /// <summary>
    /// 当前模式字：Auto / Manual；没连上写"—"。
    /// </summary>
    public string ModeText
    {
        get => _modeText;
        private set => SetProperty(ref _modeText, value);
    }

    private int _alarmCount;

    /// <summary>
    /// 当前报警条数（还没被人工复位的）。
    /// </summary>
    public int AlarmCount
    {
        get => _alarmCount;
        private set
        {
            if (SetProperty(ref _alarmCount, value))
            {
                OnPropertyChanged(nameof(HasAlarms));
            }
        }
    }

    /// <summary>
    /// 有没有报警（条数变红）。
    /// </summary>
    public bool HasAlarms => AlarmCount > 0;

    /// <summary>
    /// 右栏的页签：sc.xml 里配了几个 LoadPort 就几个，先后跟 sc.xml 一样。
    /// </summary>
    public ObservableCollection<LoadPortJobModel> LoadPorts { get; }

    /// <summary>
    /// 有没有 LoadPort：没有（启动时没连上后端，或 sc.xml 没配）时右栏显示一句提示。
    /// </summary>
    public bool HasLoadPorts => LoadPorts.Count > 0;

    private LoadPortJobModel? _selectedLoadPort;

    /// <summary>
    /// 当前页签上的 LoadPort。
    /// </summary>
    public LoadPortJobModel? SelectedLoadPort
    {
        get => _selectedLoadPort;
        set => SetProperty(ref _selectedLoadPort, value);
    }

    /// <summary>
    /// 能选的 Sequence（流程配方库里的），Sequence 框和 ⊕ 弹窗都用它。
    /// </summary>
    public ObservableCollection<SequenceOptionModel> SequenceOptions { get; } = [];

    #endregion

    #region Command

    /// <summary>切到 Auto（开自动派单）：连上了、现在是 Manual 才能点。</summary>
    public IAsyncRelayCommand AutoCommand { get; }

    /// <summary>切到 Manual（关自动派单）：连上了、现在是 Auto 才能点。</summary>
    public IAsyncRelayCommand ManualCommand { get; }

    /// <summary>整机停止：关自动派单，中止所有在做的动作。</summary>
    public IAsyncRelayCommand StopCommand { get; }

    /// <summary>复位全部有报警的来源（跟右上角的复位一样）。</summary>
    public IAsyncRelayCommand ResetCommand { get; }

    /// <summary>⊕：给这一片单独选一个 Sequence（公共选择弹窗）。</summary>
    public IRelayCommand<JobSlotModel> PickSlotSequenceCommand { get; }

    /// <summary>⊖：清空这一片的 Sequence（这片不做）。</summary>
    public IRelayCommand<JobSlotModel> ClearSlotSequenceCommand { get; }

    #endregion

    #region Service

    private readonly IEquipmentService _equipmentService;
    private readonly IAlarmService _alarmService;
    private readonly ISequenceService _sequenceService;

    private IDisposable? _statusSubscription;
    private IDisposable? _sequenceSubscription;
    private readonly List<IDisposable> _loadPortSubscriptions = [];

    /// <summary>最近一次收到的设备总状态；断开后清空，重连时后端补发当前值。</summary>
    private EquipmentStatusDto? _status;

    /// <summary>拉 Sequence 列表的序号：连着发了两次，只认最后一次的结果。</summary>
    private int _sequenceVersion;

    #endregion

    public MainPageViewModel(IReadOnlyList<string> loadPorts)
    {
        _equipmentService = GrpcClientFactory.Create<IEquipmentService>();
        _alarmService = GrpcClientFactory.Create<IAlarmService>();
        _sequenceService = GrpcClientFactory.Create<ISequenceService>();

        LoadPorts = new ObservableCollection<LoadPortJobModel>(loadPorts.Select(name => new LoadPortJobModel(name)));
        _selectedLoadPort = LoadPorts.FirstOrDefault();

        AutoCommand = new AsyncRelayCommand(DoAuto, () => IsReady && !IsAuto);
        ManualCommand = new AsyncRelayCommand(DoManual, () => IsReady && IsAuto);
        StopCommand = new AsyncRelayCommand(DoStop, () => IsReady);
        ResetCommand = new AsyncRelayCommand(DoReset, () => IsReady);
        PickSlotSequenceCommand = new RelayCommand<JobSlotModel>(DoPickSlotSequence);
        ClearSlotSequenceCommand = new RelayCommand<JobSlotModel>(DoClearSlotSequence);

        RefreshSystem();
    }

    public override void Init()
    {
        // 设备总状态、各 LoadPort 状态都是留存消息：订上就补发当前值，重连后重放，不用另外拉。
        _statusSubscription?.Dispose();
        _statusSubscription = EventBus.Register<EquipmentStatusDto>(EquipmentStatusDto.EventToken, OnStatusChanged);

        foreach (var subscription in _loadPortSubscriptions)
        {
            subscription.Dispose();
        }

        _loadPortSubscriptions.Clear();
        foreach (var port in LoadPorts)
        {
            _loadPortSubscriptions.Add(EventBus.Register<LoadPortDto>(port.Name, port.Update));
        }

        // Sequence 列表：连上后端时拉，流程配方库变了（新建、改名、删除）再拉
        _sequenceSubscription?.Dispose();
        _sequenceSubscription = EventBus.Register<SequenceChangedDto>(SequenceListDto.EventToken, changed => _ = LoadSequences());

        ClientAlarms.Changed -= OnAlarmsChanged;
        ClientAlarms.Changed += OnAlarmsChanged;
        OnAlarmsChanged();

        RemoteEventBus.ConnectionChanged -= OnConnectionChanged;
        RemoteEventBus.ConnectionChanged += OnConnectionChanged;
        OnConnectionChanged(RemoteEventBus.IsConnected);
    }

    private void OnConnectionChanged(bool connected)
    {
        IsConnected = connected;
        if (!connected)
        {
            // 断开后不知道设备现在是什么状态，等重连后后端重放
            _status = null;
            RefreshSystem();
            return;
        }

        _ = LoadSequences();
    }

    private void OnStatusChanged(EquipmentStatusDto status)
    {
        _status = status;
        RefreshSystem();
    }

    private void OnAlarmsChanged()
    {
        AlarmCount = ClientAlarms.Active.Count;
    }

    /// <summary>
    /// 系统状态、模式按连接和设备总状态重算：没连上 → 未连接；有报警 → 报警；有模块在动 → 运行中；只有警告 → 警告；否则空闲。
    /// </summary>
    private void RefreshSystem()
    {
        var status = _status;
        if (!IsConnected || status is null)
        {
            SystemStateText = L10n.Get("main.state.disconnected");
            SystemStateTone = ModuleStateTone.Inactive;
            IsAuto = false;
            ModeText = L10n.Get("main.none");
        }
        else
        {
            if (status.HasAlarm)
            {
                SystemStateText = L10n.Get("main.state.alarm");
                SystemStateTone = ModuleStateTone.Alarm;
            }
            else if (status.IsRunning)
            {
                SystemStateText = L10n.Get("main.state.running");
                SystemStateTone = ModuleStateTone.Busy;
            }
            else if (status.HasWarning)
            {
                SystemStateText = L10n.Get("main.state.warning");
                SystemStateTone = ModuleStateTone.Warning;
            }
            else
            {
                SystemStateText = L10n.Get("main.state.idle");
                SystemStateTone = ModuleStateTone.Ready;
            }

            IsAuto = status.IsAuto;
            ModeText = L10n.Get(status.IsAuto ? "main.mode.auto" : "main.mode.manual");
        }

        AutoCommand.NotifyCanExecuteChanged();
        ManualCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        ResetCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// 拉 Sequence 列表；没装流程配方库（sequence.not_installed）就是空的，其他原因没拉到的保留上一次的，记一笔日志。
    /// </summary>
    private async Task LoadSequences()
    {
        int version = ++_sequenceVersion;
        try
        {
            var response = await _sequenceService.GetListAsync(new RpcRequest());
            if (version != _sequenceVersion)
            {
                return;
            }

            if (response.Success)
            {
                SequenceOptions.Clear();
                foreach (var option in SequenceOptionModel.From(response.DeserializeData<SequenceListDto>()))
                {
                    SequenceOptions.Add(option);
                }
            }
            else if (response.Code == ErrorCodes.SequenceNotInstalled)
            {
                SequenceOptions.Clear();
            }
            else
            {
                ClientLog.Error(LogModule, L10n.Get("main.sequences_failed", ReasonOf(response)));
            }
        }
        catch (Exception exception)
        {
            ClientLog.Error(LogModule, L10n.Get("main.sequences_failed", exception.Message));
        }
    }

    private async Task DoAuto()
    {
        try
        {
            var response = await _equipmentService.AutoAsync(new RpcRequest());
            if (!response.Success)
            {
                ClientLog.Error(LogModule, L10n.Get("main.auto_failed", ReasonOf(response)));
                return;
            }

            ClientLog.Info(LogModule, L10n.Get("main.auto_done"));
        }
        catch (Exception exception)
        {
            ClientLog.Error(LogModule, L10n.Get("main.auto_failed", exception.Message));
        }
    }

    private async Task DoManual()
    {
        try
        {
            var response = await _equipmentService.ManualAsync(new RpcRequest());
            if (!response.Success)
            {
                ClientLog.Error(LogModule, L10n.Get("main.manual_failed", ReasonOf(response)));
                return;
            }

            ClientLog.Info(LogModule, L10n.Get("main.manual_done"));
        }
        catch (Exception exception)
        {
            ClientLog.Error(LogModule, L10n.Get("main.manual_failed", exception.Message));
        }
    }

    private async Task DoStop()
    {
        try
        {
            var response = await _equipmentService.StopAsync(new RpcRequest());
            if (!response.Success)
            {
                ClientLog.Error(LogModule, L10n.Get("main.stop_failed", ReasonOf(response)));
                return;
            }

            ClientLog.Warn(LogModule, L10n.Get("main.stopped", response.DeserializeData<int>()));
        }
        catch (Exception exception)
        {
            ClientLog.Error(LogModule, L10n.Get("main.stop_failed", exception.Message));
        }
    }

    private async Task DoReset()
    {
        try
        {
            var response = await _alarmService.ResetAllAsync(new RpcRequest());
            if (!response.Success)
            {
                ClientLog.Error(LogModule, L10n.Get("main.reset_failed", ReasonOf(response)));
                return;
            }

            ClientLog.Info(LogModule, L10n.Get("main.reset_done", response.DeserializeData<int>()));
        }
        catch (Exception exception)
        {
            ClientLog.Error(LogModule, L10n.Get("main.reset_failed", exception.Message));
        }
    }

    /// <summary>
    /// ⊕：公共选择弹窗选一个 Sequence 给这一片；弹窗里先选中这片现在的那个，取消就不变。
    /// </summary>
    private void DoPickSlotSequence(JobSlotModel? slot)
    {
        if (slot is null || !slot.CanAssign)
        {
            return;
        }

        var columns = new[]
        {
            new PickerColumn { HeaderKey = "recipe.sequence.index", Path = nameof(SequenceOptionModel.No), Width = IndexColumnWidth },
            new PickerColumn { HeaderKey = "recipe.sequence.name", Path = nameof(SequenceOptionModel.Name) },
        };
        var picked = DialogService.ShowPicker(
            L10n.Get("main.pick_slot_sequence", slot.SlotText),
            columns,
            SequenceOptions,
            slot.Sequence,
            nameof(SequenceOptionModel.Name));
        if (picked is SequenceOptionModel option)
        {
            slot.Sequence = option.Name;
        }
    }

    /// <summary>
    /// ⊖：清空这一片的 Sequence，这片不做。
    /// </summary>
    private void DoClearSlotSequence(JobSlotModel? slot)
    {
        if (slot is null)
        {
            return;
        }

        slot.Sequence = string.Empty;
    }

    /// <summary>
    /// 后端拒了的原因：有错误码就按语言包写成句子，老接口没码才用 Message。
    /// </summary>
    private static string ReasonOf(RpcResponse response)
    {
        return string.IsNullOrEmpty(response.Code) ? response.Message : L10n.Get(response.Code, response.Args);
    }
}
