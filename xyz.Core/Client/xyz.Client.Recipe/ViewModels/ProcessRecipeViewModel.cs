using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using xyz.Client.Common.Events;
using xyz.Client.Common.Log;
using xyz.Client.Common.Rpc;
using xyz.Client.Common.Session;
using xyz.Client.DataModels.ViewModels;
using xyz.Client.Presentation.Localization;
using xyz.Client.Recipe.Models;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Shared.Rpc;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Client.Recipe.ViewModels;

/// <summary>
/// 工艺配方页 ViewModel（配方 → 工艺配方）：左边编号 1~N 的列表（个数由后端给），右边选中那一个的基本信息、工艺步骤。
/// 每一步：时间、转速，然后摆臂 → 这条摆臂上的药液 → 流量 → 方式（Time / Scan）→ 位置；摆臂、药液来自后端 sc.xml，原样显示，
/// 各项范围也由后端给。新建、重命名、删除是列表上的操作，马上生效；说明和步骤改完点"保存"才存，带上打开时的版本，别处改过就存不进去。
/// 后端推"变了"通知，这里攒一下再重拉；页面不在前台时只记一笔，切回来再拉。
/// </summary>
public class ProcessRecipeViewModel : BaseViewModel
{
    private const string LogModule = "ProcessRecipe";

    /// <summary>
    /// 变更通知攒多久再拉：一次保存可能连着来几条，合成一次。
    /// </summary>
    private static readonly TimeSpan ReloadDelay = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// 编号至少显示两位（01）；个数上百、上千时跟着变宽。
    /// </summary>
    private const int MinIndexDigits = 2;

    private const string TimeFormat = "yyyy-MM-dd HH:mm:ss";

    /// <summary>
    /// 提示里数字的写法：最多两位小数，不带多余的 0（跟后端错误码里的写法一样）。
    /// </summary>
    private const string NumberFormat = "0.##";

    #region Column

    /// <summary>
    /// 左边列表：编号 1~个数，用了的带名称。
    /// </summary>
    public ObservableCollection<ProcessRecipeSlotModel> Slots { get; } = [];

    private ProcessRecipeSlotModel? _selectedSlot;

    /// <summary>
    /// 左边选中的编号。有没保存的修改时先弹确认框问一声，列表的选中先退回去，确认放弃了再切过去。
    /// </summary>
    public ProcessRecipeSlotModel? SelectedSlot
    {
        get => _selectedSlot;
        set
        {
            if (value is null || ReferenceEquals(value, _selectedSlot))
            {
                return;
            }

            if (IsDirty)
            {
                _pendingSlot = value;
                OpenDialog(ProcessRecipeDialogKind.Discard);

                // 列表那边已经选到新的一行了：等这次选中变化走完，再把选中退回到正在编辑的那一行
                Application.Current?.Dispatcher.BeginInvoke(() => OnPropertyChanged(nameof(SelectedSlot)));
                return;
            }

            SetProperty(ref _selectedSlot, value);
            NotifyHeader();
            RefreshCommands();
            _ = Open(value);
        }
    }

    private ProcessRecipeDto? _current;

    /// <summary>
    /// 右边打开的工艺配方（打开时从后端拉的那一份，保存、改名后换成新的）；空编号为 null。
    /// </summary>
    public ProcessRecipeDto? Current
    {
        get => _current;
        private set
        {
            if (SetProperty(ref _current, value))
            {
                NotifyHeader();
                RefreshCommands();
            }
        }
    }

    public bool HasRecipe => Current is not null;

    public string IndexText => SelectedSlot?.IndexText ?? string.Empty;

    public string NameText => Current?.Name ?? string.Empty;

    public string CreatedBy => Current?.CreatedBy ?? string.Empty;

    public string CreatedAtText => Current?.CreatedAt.ToString(TimeFormat, CultureInfo.InvariantCulture) ?? string.Empty;

    public string ModifiedBy => Current?.ModifiedBy ?? string.Empty;

    public string ModifiedAtText => Current?.ModifiedAt.ToString(TimeFormat, CultureInfo.InvariantCulture) ?? string.Empty;

    public string RevisionText => Current?.Revision.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    /// <summary>
    /// 选中空编号时基本信息里的那句话。
    /// </summary>
    public string EmptyText => SelectedSlot is null ? string.Empty : L10n.Get("recipe.process.empty_slot", SelectedSlot.IndexText);

    private string _description = string.Empty;

    /// <summary>
    /// 说明（输入框提交后才写进来）；改了算"有没保存的修改"。
    /// </summary>
    public string Description
    {
        get => _description;
        set
        {
            if (SetProperty(ref _description, value ?? string.Empty) && !_applying)
            {
                MarkDirty();
            }
        }
    }

    /// <summary>
    /// 工艺步骤。
    /// </summary>
    public ObservableCollection<ProcessRecipeStepModel> Steps { get; } = [];

    private ProcessRecipeStepModel? _selectedStep;

    /// <summary>
    /// 选中的步骤："添加"插在它后面，"删除"删它。点行、点进这一行的输入框或下拉框都会选中那一行。
    /// </summary>
    public ProcessRecipeStepModel? SelectedStep
    {
        get => _selectedStep;
        set
        {
            if (SetProperty(ref _selectedStep, value))
            {
                DeleteStepCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// 摆臂下拉框的选项："不出液" + 后端给的摆臂（sc.xml 原样）。
    /// </summary>
    public ObservableCollection<ProcessRecipeChoiceModel> ArmChoices { get; } = [];

    /// <summary>
    /// 方式下拉框的选项：Time / Scan。
    /// </summary>
    public IReadOnlyList<ProcessRecipeChoiceModel> ModeChoices { get; }

    private string _positionTip = string.Empty;

    /// <summary>
    /// 位置那一列表头后面的说明（晶圆坐标两头是几，Scan 怎么扫）。
    /// </summary>
    public string PositionTip
    {
        get => _positionTip;
        private set => SetProperty(ref _positionTip, value);
    }

    private string _totalText = string.Empty;

    /// <summary>
    /// 合计时长（工艺步骤标题条右边）。
    /// </summary>
    public string TotalText
    {
        get => _totalText;
        private set => SetProperty(ref _totalText, value);
    }

    private bool _isDirty;

    /// <summary>
    /// 说明或步骤改了还没保存。
    /// </summary>
    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (SetProperty(ref _isDirty, value))
            {
                if (_selectedSlot is not null)
                {
                    _selectedSlot.IsDirty = value;
                }

                RefreshCommands();
            }
        }
    }

    private string _validationText = string.Empty;

    /// <summary>
    /// 步骤检查没过的地方（工艺步骤标题条上的红字）；没问题是空的。
    /// </summary>
    public string ValidationText
    {
        get => _validationText;
        private set => SetProperty(ref _validationText, value);
    }

    private bool _isValid = true;

    public bool IsValid
    {
        get => _isValid;
        private set => SetProperty(ref _isValid, value);
    }

    private bool _isConnected;

    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (SetProperty(ref _isConnected, value))
            {
                RefreshCommands();
            }
        }
    }

    private ProcessRecipePageState _pageState = ProcessRecipePageState.Offline;

    /// <summary>
    /// 页面能不能用：Ready 才显示列表和编辑区，其他情况正中给一句提示（PageHint）。
    /// </summary>
    public ProcessRecipePageState PageState
    {
        get => _pageState;
        private set
        {
            if (SetProperty(ref _pageState, value))
            {
                OnPropertyChanged(nameof(IsReady));
                OnPropertyChanged(nameof(IsPageHintVisible));
                OnPropertyChanged(nameof(PageHint));
                RefreshCommands();
            }
        }
    }

    public bool IsReady => PageState == ProcessRecipePageState.Ready;

    /// <summary>
    /// 不能用时正中给一句提示（没连上、正在拉、没装）。
    /// </summary>
    public bool IsPageHintVisible => !IsReady;

    public string PageHint => PageState switch
    {
        ProcessRecipePageState.Offline => L10n.Get("recipe.process.offline"),
        ProcessRecipePageState.Loading => L10n.Get("recipe.process.loading"),
        ProcessRecipePageState.NotInstalled => L10n.Get(ErrorCodes.ProcessRecipeNotInstalled),
        _ => string.Empty,
    };

    private ProcessRecipeDialogKind _dialog;

    /// <summary>
    /// 正开着的确认框；None 时关着。
    /// </summary>
    public ProcessRecipeDialogKind Dialog
    {
        get => _dialog;
        private set
        {
            if (SetProperty(ref _dialog, value))
            {
                OnPropertyChanged(nameof(IsDialogOpen));
                OnPropertyChanged(nameof(IsNaming));
                RefreshCommands();
            }
        }
    }

    public bool IsDialogOpen => Dialog != ProcessRecipeDialogKind.None;

    /// <summary>
    /// 确认框里要填名称（新建、重命名）。
    /// </summary>
    public bool IsNaming => Dialog is ProcessRecipeDialogKind.Create or ProcessRecipeDialogKind.Rename;

    private string _dialogTitle = string.Empty;

    public string DialogTitle
    {
        get => _dialogTitle;
        private set => SetProperty(ref _dialogTitle, value);
    }

    private string _dialogSubject = string.Empty;

    /// <summary>
    /// 确认框里的主角（大字）：删除、放弃修改时是要动的那一个（编号和名称）；新建、重命名没有——编号写在标题里，
    /// 名称就在下面的输入框里，不再重复显示一遍。
    /// </summary>
    public string DialogSubject
    {
        get => _dialogSubject;
        private set
        {
            if (SetProperty(ref _dialogSubject, value))
            {
                OnPropertyChanged(nameof(HasDialogSubject));
            }
        }
    }

    public bool HasDialogSubject => DialogSubject.Length > 0;

    private string _dialogMessage = string.Empty;

    /// <summary>
    /// 确认框里的说明（删除、放弃修改时有）；没有是空的。
    /// </summary>
    public string DialogMessage
    {
        get => _dialogMessage;
        private set => SetProperty(ref _dialogMessage, value);
    }

    private string _dialogName = string.Empty;

    /// <summary>
    /// 新建、重命名时填的名称。
    /// </summary>
    public string DialogName
    {
        get => _dialogName;
        set
        {
            if (SetProperty(ref _dialogName, value ?? string.Empty))
            {
                DialogError = string.Empty;
            }
        }
    }

    private string _dialogError = string.Empty;

    /// <summary>
    /// 后端拒了的原因（名称重了、不合规……），显示在确认框里，框不关。
    /// </summary>
    public string DialogError
    {
        get => _dialogError;
        private set => SetProperty(ref _dialogError, value);
    }

    private string _confirmText = string.Empty;

    public string ConfirmText
    {
        get => _confirmText;
        private set => SetProperty(ref _confirmText, value);
    }

    private string _cancelText = string.Empty;

    public string CancelText
    {
        get => _cancelText;
        private set => SetProperty(ref _cancelText, value);
    }

    private bool _isDangerConfirm;

    /// <summary>
    /// 确认按钮用红色（删除、放弃修改）。
    /// </summary>
    public bool IsDangerConfirm
    {
        get => _isDangerConfirm;
        private set => SetProperty(ref _isDangerConfirm, value);
    }

    #endregion

    #region Command

    /// <summary>
    /// 新建：选中的是空编号才能点，弹框填名称。
    /// </summary>
    public IRelayCommand CreateCommand { get; }

    /// <summary>
    /// 重命名：选中的编号用了才能点，弹框改名称。
    /// </summary>
    public IRelayCommand RenameCommand { get; }

    /// <summary>
    /// 删除：选中的编号用了才能点，弹框确认。
    /// </summary>
    public IRelayCommand DeleteCommand { get; }

    /// <summary>
    /// 保存说明和步骤：改过、检查通过才能点。
    /// </summary>
    public IAsyncRelayCommand SaveCommand { get; }

    /// <summary>
    /// 添加一步：插在选中那一步后面，内容按后端给的默认（不出液、转着）。
    /// </summary>
    public IRelayCommand AddStepCommand { get; }

    /// <summary>
    /// 删除选中的那一步（至少留一步）。
    /// </summary>
    public IRelayCommand DeleteStepCommand { get; }

    /// <summary>
    /// 确认框里的确认（确定、确认删除、放弃修改）。
    /// </summary>
    public IAsyncRelayCommand DialogConfirmCommand { get; }

    /// <summary>
    /// 确认框里的取消（Esc 也是）。
    /// </summary>
    public IRelayCommand DialogCancelCommand { get; }

    #endregion

    #region Service

    private readonly IProcessRecipeService _service;

    /// <summary>
    /// 攒变更通知用：第一条来了起表，到点拉一次。
    /// </summary>
    private readonly DispatcherTimer _reloadTimer;

    private IDisposable? _subscription;

    private bool _isPageVisible;

    /// <summary>
    /// 列表可能变了还没拉（页面不在前台、没连上时攒着）。
    /// </summary>
    private bool _needsReload = true;

    /// <summary>
    /// 第几轮拉列表；拉回来时已经有更新的一轮就扔掉，以新的为准。
    /// </summary>
    private int _loadVersion;

    /// <summary>
    /// 第几次打开工艺配方；连着点几个编号，只认最后一次。
    /// </summary>
    private int _openVersion;

    /// <summary>
    /// 拉到过列表没有（没拉到过之前不说"没装"）。
    /// </summary>
    private bool _isLoaded;

    /// <summary>
    /// 后端装了工艺配方库。
    /// </summary>
    private bool _isInstalled = true;

    /// <summary>
    /// 后端给的选项：摆臂和药液、各项范围、新加一步的默认值。
    /// </summary>
    private ProcessRecipeOptionsDto _options = new();

    private ProcessRecipeLimitsModel _limits = new(new ProcessRecipeOptionsDto());

    /// <summary>
    /// 有没保存的修改时点的那个编号，确认放弃后切过去。
    /// </summary>
    private ProcessRecipeSlotModel? _pendingSlot;

    /// <summary>
    /// 正在把后端的数据摆到界面上：这时候的改动不算人改的。
    /// </summary>
    private bool _applying;

    #endregion

    public ProcessRecipeViewModel()
    {
        _service = GrpcClientFactory.Create<IProcessRecipeService>();
        ModeChoices =
        [
            new ProcessRecipeChoiceModel(ProcessArmMode.Time, L10n.Get("recipe.process.mode_time")),
            new ProcessRecipeChoiceModel(ProcessArmMode.Scan, L10n.Get("recipe.process.mode_scan")),
        ];

        CreateCommand = new RelayCommand(DoCreate, () => IsConnected && IsReady && !IsDialogOpen && _selectedSlot is not null && !_selectedSlot.IsUsed);
        RenameCommand = new RelayCommand(DoRename, () => IsConnected && !IsDialogOpen && Current is not null);
        DeleteCommand = new RelayCommand(DoDelete, () => IsConnected && !IsDialogOpen && Current is not null);
        SaveCommand = new AsyncRelayCommand(DoSave, () => IsConnected && !IsDialogOpen && Current is not null && IsDirty && IsValid);
        AddStepCommand = new RelayCommand(DoAddStep, () => !IsDialogOpen && Current is not null);
        DeleteStepCommand = new RelayCommand(DoDeleteStep, CanDeleteStep);
        DialogConfirmCommand = new AsyncRelayCommand(DoDialogConfirm, () => IsDialogOpen && (IsConnected || Dialog == ProcessRecipeDialogKind.Discard));
        DialogCancelCommand = new RelayCommand(DoDialogCancel);

        _reloadTimer = new DispatcherTimer { Interval = ReloadDelay };
        _reloadTimer.Tick += (_, _) =>
        {
            _reloadTimer.Stop();
            _ = Reload();
        };
    }

    /// <summary>
    /// 订变更通知、跟着连接走：连上就拉列表（页面在前台时）。
    /// </summary>
    public override void Init()
    {
        _subscription?.Dispose();
        _subscription = EventBus.Register<ProcessRecipeChangedDto>(ProcessRecipeListDto.EventToken, _ => RequestReload());

        RemoteEventBus.ConnectionChanged -= OnConnectionChanged;
        RemoteEventBus.ConnectionChanged += OnConnectionChanged;
        OnConnectionChanged(RemoteEventBus.IsConnected);
    }

    /// <summary>
    /// 页面显示 / 隐藏（View 的 IsVisibleChanged 调）：不在前台时变更只记一笔不拉，切回来有变化再拉。
    /// </summary>
    public void SetPageVisible(bool visible)
    {
        _isPageVisible = visible;
        if (visible && _needsReload && IsConnected)
        {
            _reloadTimer.Stop();
            _ = Reload();
        }
    }

    private void OnConnectionChanged(bool connected)
    {
        IsConnected = connected;
        if (connected)
        {
            RequestReload();
        }
        else
        {
            // 断开期间会不会变不知道：连回来整个重拉。改了没保存的留着，连回来还能存。
            _needsReload = true;
        }

        UpdatePageState();
    }

    /// <summary>
    /// 要重拉：记一笔；页面在前台、连着才起表，一串通知只拉一次。
    /// </summary>
    private void RequestReload()
    {
        _needsReload = true;
        if (!_isPageVisible || !IsConnected || _reloadTimer.IsEnabled)
        {
            return;
        }

        _reloadTimer.Start();
    }

    /// <summary>
    /// 拉列表和选项（摆臂、药液、范围）。打开着的那个没改过就跟着重拉（别处可能改了、删了）；
    /// 改了没保存的留着人的修改，保存时版本对不上后端会说。
    /// </summary>
    private async Task Reload()
    {
        int version = ++_loadVersion;
        _needsReload = false;
        try
        {
            var listResponse = await _service.GetListAsync(new RpcRequest());
            if (!listResponse.Success)
            {
                if (version != _loadVersion)
                {
                    return;
                }

                _isLoaded = true;
                _isInstalled = listResponse.Code != ErrorCodes.ProcessRecipeNotInstalled;
                if (_isInstalled)
                {
                    _needsReload = true;
                    ClientLog.Error(LogModule, L10n.Get("recipe.process.load_failed", ReasonOf(listResponse)));
                }

                UpdatePageState();
                return;
            }

            var list = listResponse.DeserializeData<ProcessRecipeListDto>();
            var options = (await _service.GetOptionsAsync(new RpcRequest())).DeserializeData<ProcessRecipeOptionsDto>();
            if (version != _loadVersion)
            {
                return;
            }

            _isLoaded = true;
            _isInstalled = true;
            ApplyOptions(options);
            ApplyList(list);
            UpdatePageState();

            var slot = _selectedSlot;
            if (slot is not null && !IsDirty)
            {
                await Open(slot);
            }
        }
        catch (Exception exception)
        {
            if (version == _loadVersion)
            {
                _needsReload = true;
                ClientLog.Error(LogModule, L10n.Get("recipe.process.load_failed", exception.Message));
            }
        }
    }

    /// <summary>
    /// 换上后端给的选项：摆臂下拉框的选项、各项范围、位置那一列的说明。
    /// </summary>
    private void ApplyOptions(ProcessRecipeOptionsDto options)
    {
        _options = options;
        _limits = new ProcessRecipeLimitsModel(options);
        ArmChoices.Clear();
        ArmChoices.Add(new ProcessRecipeChoiceModel(string.Empty, L10n.Get("recipe.process.no_arm")));
        foreach (var arm in options.Arms)
        {
            ArmChoices.Add(new ProcessRecipeChoiceModel(arm.Name, arm.Name));
        }

        PositionTip = L10n.Get("recipe.process.position_tip", Format(options.MinPosition), Format(options.MaxPosition));
    }

    /// <summary>
    /// 摆列表：个数变了才重建（选中的编号找回来，找不到选第一个），名称每次都对一遍。
    /// </summary>
    private void ApplyList(ProcessRecipeListDto list)
    {
        int digits = Math.Max(MinIndexDigits, list.Capacity.ToString(CultureInfo.InvariantCulture).Length);
        if (Slots.Count != list.Capacity || (Slots.Count > 0 && Slots[0].IndexText.Length != digits))
        {
            int? keep = _selectedSlot?.Index;
            Slots.Clear();
            for (int index = 1; index <= list.Capacity; index++)
            {
                Slots.Add(new ProcessRecipeSlotModel(index, digits));
            }

            // 直接换选中（不走切换确认）：列表重建了，打开的那个后面会重拉
            _selectedSlot = Slots.FirstOrDefault(slot => slot.Index == keep) ?? Slots.FirstOrDefault();
            OnPropertyChanged(nameof(SelectedSlot));
        }

        var names = list.Items.ToDictionary(item => item.Index, item => item.Name);
        foreach (var slot in Slots)
        {
            slot.Name = names.TryGetValue(slot.Index, out string? name) ? name : null;
        }

        if (_selectedSlot is not null)
        {
            _selectedSlot.IsDirty = IsDirty;
        }

        NotifyHeader();
        RefreshCommands();
    }

    /// <summary>
    /// 打开一个编号：用了的从后端拉内容，空的清空右边。连着点几个编号只认最后一次。
    /// </summary>
    private async Task Open(ProcessRecipeSlotModel slot)
    {
        int version = ++_openVersion;
        if (!slot.IsUsed)
        {
            ShowRecipe(null);
            return;
        }

        try
        {
            var response = await _service.GetAsync(new ProcessRecipeIndexRequest { Index = slot.Index });
            if (version != _openVersion || !ReferenceEquals(slot, _selectedSlot))
            {
                return;
            }

            if (!response.Success)
            {
                if (response.Code == ErrorCodes.ProcessRecipeNotFound)
                {
                    // 刚被别处删了：当空编号
                    slot.Name = null;
                    ShowRecipe(null);
                    return;
                }

                ClientLog.Error(LogModule, L10n.Get("recipe.process.load_failed", ReasonOf(response)));
                return;
            }

            ShowRecipe(response.DeserializeData<ProcessRecipeDto>());
        }
        catch (Exception exception)
        {
            ClientLog.Error(LogModule, L10n.Get("recipe.process.load_failed", exception.Message));
        }
    }

    /// <summary>
    /// 把一个工艺配方摆到右边（null = 空编号），选中第 selectPosition 步；摆完不算"有没保存的修改"。
    /// </summary>
    private void ShowRecipe(ProcessRecipeDto? dto, int selectPosition = 0)
    {
        _applying = true;
        try
        {
            Current = dto;
            Description = dto?.Description ?? string.Empty;
            Steps.Clear();
            if (dto is not null)
            {
                foreach (var step in dto.Steps)
                {
                    Steps.Add(CreateStep(step));
                }
            }

            Renumber();
            SelectedStep = Steps.Count == 0 ? null : Steps[Math.Clamp(selectPosition, 0, Steps.Count - 1)];
        }
        finally
        {
            _applying = false;
        }

        IsDirty = false;
        Validate();
    }

    private ProcessRecipeStepModel CreateStep(ProcessRecipeStepDto dto)
    {
        return new ProcessRecipeStepModel(dto, _limits, _options.Arms, OnStepChanged);
    }

    /// <summary>
    /// 一步改了：选中这一行、记"有没保存的修改"、重新检查。
    /// </summary>
    private void OnStepChanged(ProcessRecipeStepModel step)
    {
        if (_applying)
        {
            return;
        }

        SelectedStep = step;
        MarkDirty();
        Validate();
    }

    private void MarkDirty()
    {
        if (Current is not null)
        {
            IsDirty = true;
        }
    }

    private void Renumber()
    {
        for (int position = 0; position < Steps.Count; position++)
        {
            Steps[position].Number = position + 1;
        }
    }

    /// <summary>
    /// 检查步骤（跟后端保存时的检查一样，提示文字也用同一套）：时间、转速在范围里；出液的摆臂还在、选了药液、药液在这条摆臂上、
    /// 流量和位置在范围里，Scan 两头不一样、速度在范围里；合计不超过腔体的工艺超时。
    /// 输入框自己报了错的（格式不对、超了范围，还没提交进来）也算没过。有问题的行标红，标题条上列出来；全过了才能保存。
    /// </summary>
    private void Validate()
    {
        var messages = new List<string>();
        var limits = _limits;
        double total = 0;
        foreach (var step in Steps)
        {
            var problems = CheckStep(step, limits, out bool chemicalError);
            messages.AddRange(problems);
            step.HasError = problems.Count > 0;
            step.HasChemicalError = chemicalError;
            if (ProcessRecipeStepModel.TryNumber(step.SecondsText, out double seconds) && seconds > 0)
            {
                total += seconds;
            }
        }

        if (Steps.Count == 0 && Current is not null)
        {
            messages.Add(L10n.Get(ErrorCodes.ProcessRecipeNoSteps));
        }

        if (limits.MaxTotalSeconds > 0 && total > limits.MaxTotalSeconds)
        {
            messages.Add(L10n.Get(ErrorCodes.ProcessRecipeTotalTooLong, Format(total), Format(limits.MaxTotalSeconds)));
        }

        TotalText = Current is null ? string.Empty : L10n.Get("recipe.process.total", Format(total));
        ValidationText = string.Join(L10n.Get("recipe.process.error_separator"), messages);
        IsValid = messages.Count == 0;
        RefreshCommands();
    }

    private List<string> CheckStep(ProcessRecipeStepModel step, ProcessRecipeLimitsModel limits, out bool chemicalError)
    {
        var problems = new List<string>();
        chemicalError = false;
        string number = step.Number.ToString(CultureInfo.InvariantCulture);
        if (step.HasSecondsError || !InRange(step.SecondsText, limits.MinSeconds, limits.MaxSeconds))
        {
            problems.Add(L10n.Get(ErrorCodes.ProcessRecipeTimeOutOfRange, number, Format(limits.MinSeconds), Format(limits.MaxSeconds)));
        }

        if (step.HasRpmError || !IsWhole(step.RpmText) || !InRange(step.RpmText, limits.MinRpm, limits.MaxRpm))
        {
            problems.Add(L10n.Get(ErrorCodes.ProcessRecipeRpmOutOfRange, number, Format(limits.MaxRpm)));
        }

        if (!step.IsDispensing)
        {
            return problems;
        }

        string armName = step.Arm ?? string.Empty;
        string chemical = step.Chemical ?? string.Empty;
        var arm = _options.Arms.FirstOrDefault(item => string.Equals(item.Name, armName, StringComparison.OrdinalIgnoreCase));
        if (arm is null)
        {
            problems.Add(L10n.Get(ErrorCodes.ProcessRecipeArmNotFound, number, armName));
        }
        else if (chemical.Length == 0)
        {
            chemicalError = true;
            problems.Add(L10n.Get(ErrorCodes.ProcessRecipeChemicalRequired, number, arm.Name));
        }
        else if (!arm.Chemicals.Contains(chemical, StringComparer.OrdinalIgnoreCase))
        {
            chemicalError = true;
            problems.Add(L10n.Get(ErrorCodes.ProcessRecipeChemicalNotOnArm, number, chemical, arm.Name));
        }

        if (step.HasChemical && (step.HasFlowError || !InRange(step.FlowText, limits.MinFlow, limits.MaxFlow)))
        {
            problems.Add(L10n.Get(ErrorCodes.ProcessRecipeFlowOutOfRange, number, Format(limits.MinFlow), Format(limits.MaxFlow)));
        }

        bool positionBad = step.HasPositionError || !InRange(step.PositionText, limits.MinPosition, limits.MaxPosition)
            || (step.IsScan && (step.HasScanToError || !InRange(step.ScanToText, limits.MinPosition, limits.MaxPosition)));
        if (positionBad)
        {
            problems.Add(L10n.Get(ErrorCodes.ProcessRecipePositionOutOfRange, number, Format(limits.MinPosition), Format(limits.MaxPosition)));
        }
        else if (step.IsScan && ProcessRecipeStepModel.TryNumber(step.PositionText, out double from)
            && ProcessRecipeStepModel.TryNumber(step.ScanToText, out double to) && from == to)
        {
            problems.Add(L10n.Get(ErrorCodes.ProcessRecipeScanSamePosition, number));
        }

        if (step.IsScan && (step.HasScanSpeedError || !InRange(step.ScanSpeedText, limits.MinScanSpeed, limits.MaxScanSpeed)))
        {
            problems.Add(L10n.Get(ErrorCodes.ProcessRecipeScanSpeedOutOfRange, number, Format(limits.MinScanSpeed), Format(limits.MaxScanSpeed)));
        }

        return problems;
    }

    private void DoCreate()
    {
        OpenDialog(ProcessRecipeDialogKind.Create);
    }

    private void DoRename()
    {
        OpenDialog(ProcessRecipeDialogKind.Rename);
    }

    private void DoDelete()
    {
        OpenDialog(ProcessRecipeDialogKind.Delete);
    }

    /// <summary>
    /// 添加一步：插在选中那一步后面（没选中就加在最后），内容按后端给的默认（不出液、转着），选中新的一行。
    /// </summary>
    private void DoAddStep()
    {
        if (Current is null)
        {
            return;
        }

        int selected = SelectedStep is null ? -1 : Steps.IndexOf(SelectedStep);
        int at = selected < 0 ? Steps.Count : selected + 1;
        var step = CreateStep(new ProcessRecipeStepDto { Seconds = _options.NewStepSeconds, Rpm = _options.NewStepRpm });
        Steps.Insert(at, step);
        Renumber();
        OnStepChanged(step);
    }

    /// <summary>
    /// 至少留一步。
    /// </summary>
    private bool CanDeleteStep()
    {
        return !IsDialogOpen && Current is not null && SelectedStep is not null && Steps.Count > 1;
    }

    private void DoDeleteStep()
    {
        var step = SelectedStep;
        if (!CanDeleteStep() || step is null)
        {
            return;
        }

        int position = Steps.IndexOf(step);
        Steps.RemoveAt(position);
        Renumber();
        OnStepChanged(Steps[Math.Min(position, Steps.Count - 1)]);
    }

    /// <summary>
    /// 保存说明和步骤：带上打开时的版本；成了换成后端存好的那一份（版本加 1），没成把原因记到日志栏。
    /// </summary>
    private async Task DoSave()
    {
        var current = Current;
        if (current is null || !IsValid)
        {
            return;
        }

        var request = new ProcessRecipeSaveRequest
        {
            Index = current.Index,
            Revision = current.Revision,
            Description = Description.Trim(),
            Steps = Steps.Select(step => step.ToDto()).ToList(),
            Operator = ClientSession.UserName,
        };

        var response = await Call(() => _service.SaveAsync(request), "recipe.process.save_failed");
        if (response is null)
        {
            return;
        }

        var saved = response.DeserializeData<ProcessRecipeDto>();
        int position = SelectedStep is null ? 0 : Steps.IndexOf(SelectedStep);
        ShowRecipe(saved, position);
        ClientLog.Info(LogModule, L10n.Get("recipe.process.saved", IndexText, saved.Name, saved.Revision));
    }

    /// <summary>
    /// 开确认框：标题、主角、说明、按钮字按要做的事摆好。
    /// </summary>
    private void OpenDialog(ProcessRecipeDialogKind kind)
    {
        string subject = Current is null ? IndexText : $"{IndexText}  {Current.Name}";
        DialogError = string.Empty;
        DialogMessage = string.Empty;
        ConfirmText = L10n.Get("common.ok");
        CancelText = L10n.Get("common.cancel");
        IsDangerConfirm = false;
        switch (kind)
        {
            case ProcessRecipeDialogKind.Create:
                DialogTitle = L10n.Get("recipe.process.dialog_create", IndexText);
                DialogSubject = string.Empty;
                DialogName = string.Empty;
                break;
            case ProcessRecipeDialogKind.Rename:
                DialogTitle = L10n.Get("recipe.process.dialog_rename", IndexText);
                DialogSubject = string.Empty;
                DialogName = Current?.Name ?? string.Empty;
                break;
            case ProcessRecipeDialogKind.Delete:
                DialogTitle = L10n.Get("recipe.process.dialog_delete");
                DialogSubject = subject;
                DialogMessage = L10n.Get("recipe.process.delete_hint");
                ConfirmText = L10n.Get("recipe.process.confirm_delete");
                IsDangerConfirm = true;
                break;
            case ProcessRecipeDialogKind.Discard:
                DialogTitle = L10n.Get("recipe.process.dialog_discard");
                DialogSubject = subject;
                DialogMessage = L10n.Get("recipe.process.discard_hint");
                ConfirmText = L10n.Get("recipe.process.discard");
                CancelText = L10n.Get("recipe.process.keep_editing");
                IsDangerConfirm = true;
                break;
        }

        DialogError = string.Empty;
        Dialog = kind;
    }

    private void DoDialogCancel()
    {
        _pendingSlot = null;
        Dialog = ProcessRecipeDialogKind.None;
    }

    private async Task DoDialogConfirm()
    {
        switch (Dialog)
        {
            case ProcessRecipeDialogKind.Create:
                await ConfirmCreate();
                break;
            case ProcessRecipeDialogKind.Rename:
                await ConfirmRename();
                break;
            case ProcessRecipeDialogKind.Delete:
                await ConfirmDelete();
                break;
            case ProcessRecipeDialogKind.Discard:
                ConfirmDiscard();
                break;
        }
    }

    /// <summary>
    /// 新建：成了列表里带上名称、右边打开它；名称不合规、重了等原因写在确认框里，框不关。
    /// </summary>
    private async Task ConfirmCreate()
    {
        var slot = _selectedSlot;
        if (slot is null || slot.IsUsed)
        {
            Dialog = ProcessRecipeDialogKind.None;
            return;
        }

        var response = await CallInDialog(() => _service.CreateAsync(new ProcessRecipeCreateRequest
        {
            Index = slot.Index,
            Name = DialogName.Trim(),
            Operator = ClientSession.UserName,
        }));
        if (response is null)
        {
            return;
        }

        var created = response.DeserializeData<ProcessRecipeDto>();
        slot.Name = created.Name;
        Dialog = ProcessRecipeDialogKind.None;
        if (ReferenceEquals(slot, _selectedSlot))
        {
            ShowRecipe(created);
        }

        ClientLog.Info(LogModule, L10n.Get("recipe.process.created", slot.IndexText, created.Name));
    }

    /// <summary>
    /// 重命名：成了列表和基本信息换上新名称、新版本；改了没保存的步骤留着（保存时用新版本，不会被当成别处改过）。
    /// </summary>
    private async Task ConfirmRename()
    {
        var slot = _selectedSlot;
        var current = Current;
        if (slot is null || current is null)
        {
            Dialog = ProcessRecipeDialogKind.None;
            return;
        }

        var response = await CallInDialog(() => _service.RenameAsync(new ProcessRecipeRenameRequest
        {
            Index = current.Index,
            Name = DialogName.Trim(),
            Operator = ClientSession.UserName,
        }));
        if (response is null)
        {
            return;
        }

        var renamed = response.DeserializeData<ProcessRecipeDto>();
        slot.Name = renamed.Name;
        Dialog = ProcessRecipeDialogKind.None;
        if (IsDirty)
        {
            Current = renamed;
        }
        else
        {
            int position = SelectedStep is null ? 0 : Steps.IndexOf(SelectedStep);
            ShowRecipe(renamed, position);
        }

        ClientLog.Info(LogModule, L10n.Get("recipe.process.renamed", slot.IndexText, renamed.Name));
    }

    /// <summary>
    /// 删除：成了这个编号空出来，右边清空。
    /// </summary>
    private async Task ConfirmDelete()
    {
        var slot = _selectedSlot;
        var current = Current;
        if (slot is null || current is null)
        {
            Dialog = ProcessRecipeDialogKind.None;
            return;
        }

        var response = await CallInDialog(() => _service.DeleteAsync(new ProcessRecipeDeleteRequest
        {
            Index = current.Index,
            Operator = ClientSession.UserName,
        }));
        if (response is null)
        {
            return;
        }

        slot.Name = null;
        Dialog = ProcessRecipeDialogKind.None;
        ShowRecipe(null);
        ClientLog.Info(LogModule, L10n.Get("recipe.process.deleted", slot.IndexText, current.Name));
    }

    /// <summary>
    /// 放弃修改：丢掉没保存的，切到刚才点的那个编号。
    /// </summary>
    private void ConfirmDiscard()
    {
        var pending = _pendingSlot;
        _pendingSlot = null;
        IsDirty = false;
        Dialog = ProcessRecipeDialogKind.None;
        if (pending is not null)
        {
            SelectedSlot = pending;
        }
    }

    /// <summary>
    /// 发一次请求：成了返回回包；没成把原因（按错误码翻成当前语言）记到日志栏，返回 null。
    /// </summary>
    private static async Task<RpcResponse?> Call(Func<Task<RpcResponse>> call, string failedKey)
    {
        try
        {
            var response = await call();
            if (response.Success)
            {
                return response;
            }

            ClientLog.Error(LogModule, L10n.Get(failedKey, ReasonOf(response)));
            return null;
        }
        catch (Exception exception)
        {
            ClientLog.Error(LogModule, L10n.Get(failedKey, exception.Message));
            return null;
        }
    }

    /// <summary>
    /// 确认框里发请求：没成把原因写在框里（框不关，改了再点），返回 null。
    /// </summary>
    private async Task<RpcResponse?> CallInDialog(Func<Task<RpcResponse>> call)
    {
        try
        {
            var response = await call();
            if (response.Success)
            {
                return response;
            }

            DialogError = ReasonOf(response);
            return null;
        }
        catch (Exception exception)
        {
            DialogError = exception.Message;
            return null;
        }
    }

    private static string ReasonOf(RpcResponse response)
    {
        return string.IsNullOrEmpty(response.Code) ? response.Message : L10n.Get(response.Code, response.Args);
    }

    /// <summary>
    /// 文字读成数并且在 [min, max] 里。
    /// </summary>
    private static bool InRange(string text, double min, double max)
    {
        return ProcessRecipeStepModel.TryNumber(text, out double value) && value >= min && value <= max;
    }

    /// <summary>
    /// 是整数（转速只收整数）。
    /// </summary>
    private static bool IsWhole(string text)
    {
        return int.TryParse(text.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _);
    }

    private static string Format(double value)
    {
        return value.ToString(NumberFormat, CultureInfo.InvariantCulture);
    }

    private void UpdatePageState()
    {
        if (_isInstalled && Slots.Count > 0)
        {
            PageState = ProcessRecipePageState.Ready;
        }
        else if (!IsConnected)
        {
            PageState = ProcessRecipePageState.Offline;
        }
        else if (!_isLoaded)
        {
            PageState = ProcessRecipePageState.Loading;
        }
        else
        {
            PageState = _isInstalled ? ProcessRecipePageState.Loading : ProcessRecipePageState.NotInstalled;
        }
    }

    private void NotifyHeader()
    {
        OnPropertyChanged(nameof(HasRecipe));
        OnPropertyChanged(nameof(IndexText));
        OnPropertyChanged(nameof(NameText));
        OnPropertyChanged(nameof(CreatedBy));
        OnPropertyChanged(nameof(CreatedAtText));
        OnPropertyChanged(nameof(ModifiedBy));
        OnPropertyChanged(nameof(ModifiedAtText));
        OnPropertyChanged(nameof(RevisionText));
        OnPropertyChanged(nameof(EmptyText));
    }

    private void RefreshCommands()
    {
        CreateCommand.NotifyCanExecuteChanged();
        RenameCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
        AddStepCommand.NotifyCanExecuteChanged();
        DeleteStepCommand.NotifyCanExecuteChanged();
        DialogConfirmCommand.NotifyCanExecuteChanged();
    }
}
