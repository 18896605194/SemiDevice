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
/// 主界面 ViewModel：左边系统操作（系统状态、当前模式、报警条数、晶圆尺寸，Auto / Manual / Stop / Reset / 初始化 / 全部回片），
/// 右边一个 LoadPort 一个页签（载具、槽图、LotID、Sequence、槽位表、创建 / 启动 Job）。中间的整机调度是另一块（默认是公共控件 DispatchMap，机型可换），不归这里。
/// 状态全靠订推送：系统状态、设备总状态（晶圆尺寸）、各 LoadPort 状态、Job 全貌都是留存消息，订上就补发、重连后重放；报警条数跟着客户端的当前报警（ClientAlarms）。
/// 系统操作的按钮按系统状态开关（跟后端整机的状态表一致），后端照样再查一遍。
/// 创建 Job 把当前页签的 LotID 和各槽选的 Sequence 交给后端（建好等启动）；启动 Job 启动这个 LoadPort 上等启动的 CJ（要在 Auto 下）。
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
    /// 跟后端连着没有：没连上（或连上了还没收到系统状态）时系统状态是"未连接"，系统操作的按钮都点不了。
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
    /// 连着、而且收到了系统状态：系统操作的按钮这时候才能点。
    /// </summary>
    private bool IsReady => IsConnected && _system is not null;

    /// <summary>
    /// 当前系统状态码；不知道（没连上、还没收到）时为 -1，哪个状态都对不上。
    /// </summary>
    private int SystemState => _system?.State ?? -1;

    private string _systemStateText = string.Empty;

    /// <summary>
    /// 系统状态字（状态徽标显示）：未连接，或整机的状态（未初始化、初始化中、空闲、停止中、报警、自动空闲、自动运行、回片中）。
    /// </summary>
    public string SystemStateText
    {
        get => _systemStateText;
        private set => SetProperty(ref _systemStateText, value);
    }

    private ModuleStateTone _systemStateTone;

    /// <summary>
    /// 系统状态色调：灰 未连接 / 未初始化、蓝 初始化中 / 自动运行 / 回片中、绿 空闲 / 自动空闲、黄 停止中、红 报警。
    /// </summary>
    public ModuleStateTone SystemStateTone
    {
        get => _systemStateTone;
        private set => SetProperty(ref _systemStateTone, value);
    }

    private bool _isAuto;

    /// <summary>
    /// 整机模式：true = Auto（系统状态是自动空闲或自动运行）。
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

    private string _waferSizeText = string.Empty;

    /// <summary>
    /// 整机做几寸片："12 寸"；没连上写"—"。
    /// </summary>
    public string WaferSizeText
    {
        get => _waferSizeText;
        private set => SetProperty(ref _waferSizeText, value);
    }

    /// <summary>
    /// 右栏的页签：machine.xml 里配了几个 LoadPort 就几个，先后跟 machine.xml 一样。
    /// </summary>
    public ObservableCollection<LoadPortJobModel> LoadPorts { get; }

    /// <summary>
    /// 有没有 LoadPort：没有（启动时没连上后端，或 machine.xml 没配）时右栏显示一句提示。
    /// </summary>
    public bool HasLoadPorts => LoadPorts.Count > 0;

    private LoadPortJobModel? _selectedLoadPort;

    /// <summary>
    /// 当前页签上的 LoadPort。
    /// </summary>
    public LoadPortJobModel? SelectedLoadPort
    {
        get => _selectedLoadPort;
        set
        {
            if (SetProperty(ref _selectedLoadPort, value))
            {
                RefreshJobCommands();
            }
        }
    }

    /// <summary>
    /// 能选的 Sequence（流程配方库里的），Sequence 框和 ⊕ 弹窗都用它。
    /// </summary>
    public ObservableCollection<SequenceOptionModel> SequenceOptions { get; } = [];

    #endregion

    #region 系统状态码（后端 xyz.Modules 的 ModuleState / SystemState；按钮照整机状态表开关）

    private const int StateNotInit = 10;
    private const int StateIdle = 30;
    private const int StateAborting = 35;
    private const int StateError = 40;

    #endregion

    #region Command

    /// <summary>切到 Auto（开自动派单）：要初始化过、现在空闲（Manual）才能点。</summary>
    public IAsyncRelayCommand AutoCommand { get; }

    /// <summary>切到 Manual（关自动派单）：现在是 Auto（自动空闲、自动运行）才能点。</summary>
    public IAsyncRelayCommand ManualCommand { get; }

    /// <summary>整机停止：关自动派单，中止所有在做的动作；停止中不能再点。</summary>
    public IAsyncRelayCommand StopCommand { get; }

    /// <summary>复位全部有报警的来源（跟右上角的复位一样）。</summary>
    public IAsyncRelayCommand ResetCommand { get; }

    /// <summary>全部初始化：未初始化、空闲、报警时能点。</summary>
    public IAsyncRelayCommand InitCommand { get; }

    /// <summary>全部回片：空闲（Manual）时能点。</summary>
    public IAsyncRelayCommand ReturnAllCommand { get; }

    /// <summary>切晶圆尺寸（8 寸、12 寸来回切）：连上了能点，能不能切由后端查（整机没初始化、机台里没片），不行的原因写顶栏日志。</summary>
    public IAsyncRelayCommand SwitchWaferSizeCommand { get; }

    /// <summary>⊕：给这一片单独选一个 Sequence（公共选择弹窗）。</summary>
    public IRelayCommand<JobSlotModel> PickSlotSequenceCommand { get; }

    /// <summary>⊖：清空这一片的 Sequence（这片不做）。</summary>
    public IRelayCommand<JobSlotModel> ClearSlotSequenceCommand { get; }

    /// <summary>创建 Job：当前页签有要做的片、这个 LoadPort 上还没有 Job 才能点；建好等启动。</summary>
    public IAsyncRelayCommand CreateJobCommand { get; }

    /// <summary>启动 Job：Auto 下、这个 LoadPort 上的 CJ 在等启动才能点。</summary>
    public IAsyncRelayCommand StartJobCommand { get; }

    #endregion

    #region Service

    private readonly IEquipmentService _equipmentService;
    private readonly IAlarmService _alarmService;
    private readonly ISequenceService _sequenceService;
    private readonly IJobService _jobService;

    private IDisposable? _systemSubscription;
    private IDisposable? _statusSubscription;
    private IDisposable? _sequenceSubscription;
    private IDisposable? _jobSubscription;
    private readonly List<IDisposable> _loadPortSubscriptions = [];

    /// <summary>最近一次收到的系统状态；断开后清空，重连时后端补发当前值。</summary>
    private SystemStateDto? _system;

    /// <summary>最近一次收到的设备总状态（这一页只用它的晶圆尺寸）；断开后清空，重连时后端补发当前值。</summary>
    private EquipmentStatusDto? _status;

    /// <summary>拉 Sequence 列表的序号：连着发了两次，只认最后一次的结果。</summary>
    private int _sequenceVersion;

    #endregion

    public MainPageViewModel(IReadOnlyList<string> loadPorts)
    {
        _equipmentService = GrpcClientFactory.Create<IEquipmentService>();
        _alarmService = GrpcClientFactory.Create<IAlarmService>();
        _sequenceService = GrpcClientFactory.Create<ISequenceService>();
        _jobService = GrpcClientFactory.Create<IJobService>();

        LoadPorts = new ObservableCollection<LoadPortJobModel>(loadPorts.Select(name => new LoadPortJobModel(name)));
        _selectedLoadPort = LoadPorts.FirstOrDefault();

        AutoCommand = new AsyncRelayCommand(DoAuto, () => IsReady && SystemState == StateIdle);
        ManualCommand = new AsyncRelayCommand(DoManual, () => IsReady && IsAuto);
        StopCommand = new AsyncRelayCommand(DoStop, () => IsReady && SystemState != StateAborting);
        ResetCommand = new AsyncRelayCommand(DoReset, () => IsReady);
        InitCommand = new AsyncRelayCommand(DoInit,
            () => IsReady && (SystemState == StateNotInit || SystemState == StateIdle || SystemState == StateError));
        ReturnAllCommand = new AsyncRelayCommand(DoReturnAll, () => IsReady && SystemState == StateIdle);
        SwitchWaferSizeCommand = new AsyncRelayCommand(DoSwitchWaferSize, () => IsConnected && _status is not null);
        PickSlotSequenceCommand = new RelayCommand<JobSlotModel>(DoPickSlotSequence);
        ClearSlotSequenceCommand = new RelayCommand<JobSlotModel>(DoClearSlotSequence);
        CreateJobCommand = new AsyncRelayCommand(DoCreateJob, CanCreateJob);
        StartJobCommand = new AsyncRelayCommand(DoStartJob, CanStartJob);

        // 要做的片、这个 LoadPort 上的 Job 变了，两个按钮跟着变
        foreach (var port in LoadPorts)
        {
            port.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(LoadPortJobModel.HasSelection) or nameof(LoadPortJobModel.ControlJob))
                {
                    RefreshJobCommands();
                }
            };
        }

        RefreshSystem();
    }

    public override void Init()
    {
        // 系统状态、设备总状态、各 LoadPort 状态都是留存消息：订上就补发当前值，重连后重放，不用另外拉。
        _systemSubscription?.Dispose();
        _systemSubscription = RemoteEventBus.Register<SystemStateDto>(SystemStateDto.EventToken, OnSystemChanged);

        _statusSubscription?.Dispose();
        _statusSubscription = RemoteEventBus.Register<EquipmentStatusDto>(EquipmentStatusDto.EventToken, OnStatusChanged);

        foreach (var subscription in _loadPortSubscriptions)
        {
            subscription.Dispose();
        }

        _loadPortSubscriptions.Clear();
        foreach (var port in LoadPorts)
        {
            _loadPortSubscriptions.Add(RemoteEventBus.Register<LoadPortDto>(port.Name, port.Update));
        }

        // Sequence 列表：连上后端时拉，流程配方库变了（新建、改名、删除）再拉
        _sequenceSubscription?.Dispose();
        _sequenceSubscription = RemoteEventBus.Register<SequenceChangedDto>(SequenceListDto.EventToken, changed => _ = LoadSequences());

        // Job 全貌：每个页签认自己 LoadPort 上的 CJ
        _jobSubscription?.Dispose();
        _jobSubscription = RemoteEventBus.Register<JobListDto>(JobListDto.EventToken, OnJobsChanged);

        ClientAlarms.Changed -= OnAlarmsChanged;
        ClientAlarms.Changed += OnAlarmsChanged;
        OnAlarmsChanged();

        RemoteConnection.ConnectionChanged -= OnConnectionChanged;
        RemoteConnection.ConnectionChanged += OnConnectionChanged;
        OnConnectionChanged(RemoteConnection.IsConnected);
    }

    private void OnConnectionChanged(bool connected)
    {
        IsConnected = connected;
        if (!connected)
        {
            // 断开后不知道设备现在是什么状态、有哪些 Job，等重连后后端重放
            _system = null;
            _status = null;
            OnJobsChanged(null);
            RefreshSystem();
            return;
        }

        _ = LoadSequences();
    }

    private void OnSystemChanged(SystemStateDto system)
    {
        _system = system;
        RefreshSystem();
    }

    private void OnStatusChanged(EquipmentStatusDto status)
    {
        _status = status;
        RefreshSystem();
    }

    private void OnJobsChanged(JobListDto? jobs)
    {
        foreach (var port in LoadPorts)
        {
            port.UpdateJob(jobs);
        }
    }

    private void OnAlarmsChanged()
    {
        AlarmCount = ClientAlarms.Active.Count;
    }

    /// <summary>
    /// 系统状态、模式按连接和系统状态推送重算：没连上（或还没收到）→ 未连接；否则照整机的状态码显示，自动空闲、自动运行算 Auto。
    /// 晶圆尺寸按设备总状态推送。
    /// </summary>
    private void RefreshSystem()
    {
        var system = _system;
        if (!IsConnected || system is null)
        {
            SystemStateText = L10n.Get("main.state.disconnected");
            SystemStateTone = ModuleStateTone.Inactive;
            IsAuto = false;
            ModeText = L10n.Get("main.none");
        }
        else
        {
            SystemStateText = ModuleStates.SystemText(system.State);
            SystemStateTone = ModuleStates.SystemTone(system.State);
            IsAuto = ModuleStates.IsSystemAuto(system.State);
            ModeText = L10n.Get(IsAuto ? "main.mode.auto" : "main.mode.manual");
        }

        var status = _status;
        WaferSizeText = IsConnected && status is not null ? L10n.Get("main.wafer_size.value", status.WaferSize) : L10n.Get("main.none");

        AutoCommand.NotifyCanExecuteChanged();
        ManualCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        ResetCommand.NotifyCanExecuteChanged();
        InitCommand.NotifyCanExecuteChanged();
        ReturnAllCommand.NotifyCanExecuteChanged();
        SwitchWaferSizeCommand.NotifyCanExecuteChanged();
        RefreshJobCommands();
    }

    private void RefreshJobCommands()
    {
        CreateJobCommand.NotifyCanExecuteChanged();
        StartJobCommand.NotifyCanExecuteChanged();
    }

    private bool CanCreateJob()
    {
        var port = SelectedLoadPort;
        return IsReady && port is not null && port.HasSelection && port.ControlJob is null;
    }

    private bool CanStartJob()
    {
        var job = SelectedLoadPort?.ControlJob;
        return IsReady && IsAuto && job is not null && job.State == ControlJobDto.StateWaitingForStart;
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
                ClientLog.Error(LogModule, L10n.Get("main.auto_failed", SystemReason(response)));
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
                ClientLog.Error(LogModule, L10n.Get("main.manual_failed", SystemReason(response)));
                return;
            }

            ClientLog.Info(LogModule, L10n.Get("main.manual_done"));
        }
        catch (Exception exception)
        {
            ClientLog.Error(LogModule, L10n.Get("main.manual_failed", exception.Message));
        }
    }

    /// <summary>
    /// 切到另一种尺寸（现在 8 寸就切 12 寸，别的都切 8 寸）；切成了设备总状态推送会带回新尺寸，这里只写日志。
    /// </summary>
    private async Task DoSwitchWaferSize()
    {
        var status = _status;
        if (status is null)
        {
            return;
        }

        int target = status.WaferSize == 8 ? 12 : 8;
        try
        {
            var request = RpcRequest.Of((RpcParams.Size, target));
            var response = await _equipmentService.SetWaferSizeAsync(request);
            if (!response.Success)
            {
                ClientLog.Error(LogModule, L10n.Get("main.wafer_size_failed", ReasonOf(response)));
                return;
            }

            ClientLog.Info(LogModule, L10n.Get("main.wafer_size_done", target));
        }
        catch (Exception exception)
        {
            ClientLog.Error(LogModule, L10n.Get("main.wafer_size_failed", exception.Message));
        }
    }

    private async Task DoStop()
    {
        try
        {
            var response = await _equipmentService.StopAsync(new RpcRequest());
            if (!response.Success)
            {
                ClientLog.Error(LogModule, L10n.Get("main.stop_failed", SystemReason(response)));
                return;
            }

            ClientLog.Warn(LogModule, L10n.Get("main.stopped"));
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
    /// 全部初始化：开了就回，做没做成看系统状态（空闲 = 做成，回未初始化 = 没做成，原因在后端日志里）。
    /// </summary>
    private async Task DoInit()
    {
        try
        {
            var response = await _equipmentService.InitAsync(new RpcRequest());
            if (!response.Success)
            {
                ClientLog.Error(LogModule, L10n.Get("main.init_failed", SystemReason(response)));
                return;
            }

            ClientLog.Info(LogModule, L10n.Get("main.init_started"));
        }
        catch (Exception exception)
        {
            ClientLog.Error(LogModule, L10n.Get("main.init_failed", exception.Message));
        }
    }

    /// <summary>
    /// 全部回片：开了就回，回完系统状态回空闲；回不去的片和原因在后端日志里。
    /// </summary>
    private async Task DoReturnAll()
    {
        try
        {
            var response = await _equipmentService.ReturnAllAsync(new RpcRequest());
            if (!response.Success)
            {
                ClientLog.Error(LogModule, L10n.Get("main.return_all_failed", SystemReason(response)));
                return;
            }

            ClientLog.Info(LogModule, L10n.Get("main.return_all_started"));
        }
        catch (Exception exception)
        {
            ClientLog.Error(LogModule, L10n.Get("main.return_all_failed", exception.Message));
        }
    }

    /// <summary>
    /// 创建 Job：当前页签选了 Sequence 的片交给后端（同一个 Sequence 的片一个 PJ，整篮一个 CJ，名字用 LotID），启动方式由设备 machine.xml 配置决定。
    /// 建没建成看回包；之后的进展看 Job 推送。
    /// </summary>
    private async Task DoCreateJob()
    {
        var port = SelectedLoadPort;
        if (port is null)
        {
            return;
        }

        var request = RpcRequest.Of(
            (RpcParams.LoadPort, port.Name),
            (RpcParams.LotId, port.LotId.Trim()),
            (RpcParams.Slots, port.SelectedSlots.Select(slot => new JobSlotDto { Slot = slot.Slot, Sequence = slot.Sequence }).ToList()));
        try
        {
            var response = await _jobService.CreateAsync(request);
            if (!response.Success)
            {
                ClientLog.Error(LogModule, L10n.Get("main.create_job_failed", port.Name, ReasonOf(response)));
                return;
            }

            var created = response.DeserializeData<JobCreatedDto>();
            ClientLog.Info(LogModule, L10n.Get("main.job_created", port.Name, created.ControlJob, string.Join(", ", created.ProcessJobs)));
        }
        catch (Exception exception)
        {
            ClientLog.Error(LogModule, L10n.Get("main.create_job_failed", port.Name, exception.Message));
        }
    }

    /// <summary>
    /// 启动 Job：给当前页签 LoadPort 上等启动的 CJ 发 Start（E94 CJStart）。
    /// </summary>
    private async Task DoStartJob()
    {
        var job = SelectedLoadPort?.ControlJob;
        if (job is null)
        {
            return;
        }

        try
        {
            var response = await _jobService.ControlJobCommandAsync(RpcRequest.Of(
                (RpcParams.JobId, job.Id),
                (RpcParams.Command, IJobService.ControlJobStart)));
            if (!response.Success)
            {
                ClientLog.Error(LogModule, L10n.Get("main.start_job_failed", job.Id, ReasonOf(response)));
                return;
            }

            ClientLog.Info(LogModule, L10n.Get("main.job_started", job.Id));
        }
        catch (Exception exception)
        {
            ClientLog.Error(LogModule, L10n.Get("main.start_job_failed", job.Id, exception.Message));
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

    /// <summary>
    /// 系统操作被拒的原因：状态不允许时把状态码翻成整机的状态字。
    /// </summary>
    private static string SystemReason(RpcResponse response)
    {
        return ErrorText.Of(response, ModuleStates.SystemText);
    }
}
