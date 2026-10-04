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
using xyz.Client.Presentation.Controls;
using xyz.Client.Presentation.Dialogs;
using xyz.Client.Presentation.Localization;
using xyz.Client.Presentation.Models;
using xyz.Client.Recipe.Models;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Shared.Rpc;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Client.Recipe.ViewModels;

/// <summary>
/// 流程配方页 ViewModel（配方 → 流程配方）：左边编号 1~N 的列表（个数由后端给），右边选中那一个的基本信息、流程步骤、路线预览。
/// 新建、重命名、删除是列表上的操作，马上生效；说明和步骤改完点"保存"才存，带上打开时的版本，别处改过就存不进去。
/// 站点分组、模块名都来自后端 sc.xml，原样显示。后端推"变了"通知，这里攒一下再重拉；页面不在前台时只记一笔，切回来再拉。
/// </summary>
public class SequenceViewModel : BaseViewModel
{
    private const string LogModule = "Sequence";

    /// <summary>
    /// 变更通知攒多久再拉：一次保存可能连着来几条，合成一次。
    /// </summary>
    private static readonly TimeSpan ReloadDelay = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// 编号至少显示两位（01）；个数上百、上千时跟着变宽。
    /// </summary>
    private const int MinIndexDigits = 2;

    /// <summary>
    /// 最少几步：第 1 步取片、中间至少一步、最后一步放片（跟后端的检查一样）。
    /// </summary>
    private const int MinSteps = 3;

    /// <summary>
    /// "添加"弹窗里分组那一列的宽度，剩下的给模块列。
    /// </summary>
    private const double GroupColumnWidth = 180;

    private const string TimeFormat = "yyyy-MM-dd HH:mm:ss";

    #region Column

    /// <summary>
    /// 左边列表：编号 1~个数，用了的带名称。
    /// </summary>
    public ObservableCollection<SequenceSlotModel> Slots { get; } = [];

    private SequenceSlotModel? _selectedSlot;

    /// <summary>
    /// 左边选中的编号。有没保存的修改时先弹确认框问一声，列表的选中先退回去，确认放弃了再切过去。
    /// </summary>
    public SequenceSlotModel? SelectedSlot
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
                OpenDialog(SequenceDialogKind.Discard);

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

    private SequenceDto? _current;

    /// <summary>
    /// 右边打开的流程配方（打开时从后端拉的那一份，保存、改名后换成新的）；空编号为 null。
    /// </summary>
    public SequenceDto? Current
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

    public bool HasSequence => Current is not null;

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
    public string EmptyText => SelectedSlot is null ? string.Empty : L10n.Get("recipe.sequence.empty_slot", SelectedSlot.IndexText);

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
    /// 流程步骤：第 1 步、最后一步是 LoadPort（取片、放片），中间是片要经过的站点。
    /// </summary>
    public ObservableCollection<SequenceStepModel> Steps { get; } = [];

    private SequenceStepModel? _selectedStep;

    /// <summary>
    /// 选中的步骤："添加"插在它后面，"删除"删它。点行、勾站点、改配方都会选中那一行。
    /// </summary>
    public SequenceStepModel? SelectedStep
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
    /// 路线预览：一步一列，按步骤自动生成。
    /// </summary>
    public ObservableCollection<RouteColumnModel> RouteColumns { get; } = [];

    /// <summary>
    /// 选工艺配方弹窗里列的工艺配方（工艺配方库，跟着后端的变更通知重拉）。
    /// </summary>
    public ObservableCollection<ProcessRecipeOptionModel> ProcessRecipes { get; } = [];

    private bool _canTypeRecipe;

    /// <summary>
    /// 工艺配方框能不能手输：后端没装工艺配方库时才让手输（库都没有，只能填名字；后端那边也不查）；装了就只能从库里选。
    /// </summary>
    public bool CanTypeRecipe
    {
        get => _canTypeRecipe;
        private set => SetProperty(ref _canTypeRecipe, value);
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
    /// 步骤检查没过的地方（流程步骤标题条右边的红字）；没问题是空的。
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

    private SequencePageState _pageState = SequencePageState.Offline;

    /// <summary>
    /// 页面能不能用：Ready 才显示列表和编辑区，其他情况正中给一句提示（PageHint）。
    /// </summary>
    public SequencePageState PageState
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

    public bool IsReady => PageState == SequencePageState.Ready;

    /// <summary>
    /// 不能用时正中给一句提示（没连上、正在拉、没装）。
    /// </summary>
    public bool IsPageHintVisible => !IsReady;

    public string PageHint => PageState switch
    {
        SequencePageState.Offline => L10n.Get("recipe.sequence.offline"),
        SequencePageState.Loading => L10n.Get("recipe.sequence.loading"),
        SequencePageState.NotInstalled => L10n.Get(ErrorCodes.SequenceNotInstalled),
        _ => string.Empty,
    };

    private SequenceDialogKind _dialog;

    /// <summary>
    /// 正开着的确认框；None 时关着。
    /// </summary>
    public SequenceDialogKind Dialog
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

    public bool IsDialogOpen => Dialog != SequenceDialogKind.None;

    /// <summary>
    /// 确认框里要填名称（新建、重命名）。
    /// </summary>
    public bool IsNaming => Dialog is SequenceDialogKind.Create or SequenceDialogKind.Rename;

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
    /// 添加一步：先在公共选择弹窗里选站点分组，插在选中那一步后面。
    /// </summary>
    public IRelayCommand AddStepCommand { get; }

    /// <summary>
    /// 删除选中的那一步（第 1 步、最后一步删不了，中间至少留一步）。
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

    private readonly ISequenceService _service;

    private readonly IProcessRecipeService _recipeService;

    /// <summary>
    /// 攒变更通知用：第一条来了起表，到点拉一次。
    /// </summary>
    private readonly DispatcherTimer _reloadTimer;

    private IDisposable? _subscription;

    private IDisposable? _recipeSubscription;

    /// <summary>
    /// 后端装了工艺配方库：装了才查"工艺配方在不在库里"。
    /// </summary>
    private bool _hasRecipeLibrary;

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
    /// 第几次打开流程配方；连着点几个编号，只认最后一次。
    /// </summary>
    private int _openVersion;

    /// <summary>
    /// 拉到过列表没有（没拉到过之前不说"没装"）。
    /// </summary>
    private bool _isLoaded;

    /// <summary>
    /// 后端装了流程配方库。
    /// </summary>
    private bool _isInstalled = true;

    /// <summary>
    /// 可选的站点分组（来自后端 sc.xml）。
    /// </summary>
    private List<StationGroupModel> _groups = [];

    /// <summary>
    /// 有没保存的修改时点的那个编号，确认放弃后切过去。
    /// </summary>
    private SequenceSlotModel? _pendingSlot;

    /// <summary>
    /// 正在把后端的数据摆到界面上：这时候的改动不算人改的。
    /// </summary>
    private bool _applying;

    #endregion

    public SequenceViewModel()
    {
        _service = GrpcClientFactory.Create<ISequenceService>();
        _recipeService = GrpcClientFactory.Create<IProcessRecipeService>();

        CreateCommand = new RelayCommand(DoCreate, () => IsConnected && IsReady && !IsDialogOpen && _selectedSlot is not null && !_selectedSlot.IsUsed);
        RenameCommand = new RelayCommand(DoRename, () => IsConnected && !IsDialogOpen && Current is not null);
        DeleteCommand = new RelayCommand(DoDelete, () => IsConnected && !IsDialogOpen && Current is not null);
        SaveCommand = new AsyncRelayCommand(DoSave, () => IsConnected && !IsDialogOpen && Current is not null && IsDirty && IsValid);
        AddStepCommand = new RelayCommand(DoAddStep, () => !IsDialogOpen && Current is not null && _groups.Count > 0);
        DeleteStepCommand = new RelayCommand(DoDeleteStep, CanDeleteStep);
        DialogConfirmCommand = new AsyncRelayCommand(DoDialogConfirm, () => IsDialogOpen && (IsConnected || Dialog == SequenceDialogKind.Discard));
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
        _subscription = EventBus.Register<SequenceChangedDto>(SequenceListDto.EventToken, _ => RequestReload());

        // 工艺配方库变了（新建、改名、删除）：选工艺配方的弹窗、"配方在不在库里"的检查都要跟着变，一起重拉
        _recipeSubscription?.Dispose();
        _recipeSubscription = EventBus.Register<ProcessRecipeChangedDto>(ProcessRecipeListDto.EventToken, _ => RequestReload());

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
    /// 拉列表和可选站点分组。打开着的那个没改过就跟着重拉（别处可能改了、删了）；
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
                _isInstalled = listResponse.Code != ErrorCodes.SequenceNotInstalled;
                if (_isInstalled)
                {
                    _needsReload = true;
                    ClientLog.Error(LogModule, L10n.Get("recipe.sequence.load_failed", ReasonOf(listResponse)));
                }

                UpdatePageState();
                return;
            }

            var list = listResponse.DeserializeData<SequenceListDto>();
            var groups = (await _service.GetStationGroupsAsync(new RpcRequest())).DeserializeData<List<SequenceStationGroupDto>>();
            var recipeResponse = await _recipeService.GetListAsync(new RpcRequest());
            if (version != _loadVersion)
            {
                return;
            }

            _isLoaded = true;
            _isInstalled = true;
            _groups = groups.Select(group => new StationGroupModel(group)).ToList();
            ApplyRecipes(recipeResponse);
            ApplyList(list);
            UpdatePageState();

            var slot = _selectedSlot;
            if (slot is not null && !IsDirty)
            {
                await Open(slot);
            }
            else
            {
                // 正在改的不重拉，但工艺配方库可能变了：按新的库再查一遍
                Validate();
            }
        }
        catch (Exception exception)
        {
            if (version == _loadVersion)
            {
                _needsReload = true;
                ClientLog.Error(LogModule, L10n.Get("recipe.sequence.load_failed", exception.Message));
            }
        }
    }

    /// <summary>
    /// 换上工艺配方库的列表：装了库就只能从库里选；没装库（回 process_recipe.not_installed）放开手输、不查在不在库里；
    /// 其他原因没拉到的保留上一次的，记一笔日志。
    /// </summary>
    private void ApplyRecipes(RpcResponse response)
    {
        if (response.Success)
        {
            _hasRecipeLibrary = true;
            ProcessRecipes.Clear();
            foreach (var option in ProcessRecipeOptionModel.From(response.DeserializeData<ProcessRecipeListDto>()))
            {
                ProcessRecipes.Add(option);
            }
        }
        else if (response.Code == ErrorCodes.ProcessRecipeNotInstalled)
        {
            _hasRecipeLibrary = false;
            ProcessRecipes.Clear();
        }
        else
        {
            ClientLog.Error(LogModule, L10n.Get("recipe.sequence.load_failed", ReasonOf(response)));
        }

        CanTypeRecipe = !_hasRecipeLibrary;
    }

    /// <summary>
    /// 摆列表：个数变了才重建（选中的编号找回来，找不到选第一个），名称每次都对一遍。
    /// </summary>
    private void ApplyList(SequenceListDto list)
    {
        int digits = Math.Max(MinIndexDigits, list.Capacity.ToString(CultureInfo.InvariantCulture).Length);
        if (Slots.Count != list.Capacity || (Slots.Count > 0 && Slots[0].IndexText.Length != digits))
        {
            int? keep = _selectedSlot?.Index;
            Slots.Clear();
            for (int index = 1; index <= list.Capacity; index++)
            {
                Slots.Add(new SequenceSlotModel(index, digits));
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
    private async Task Open(SequenceSlotModel slot)
    {
        int version = ++_openVersion;
        if (!slot.IsUsed)
        {
            ShowSequence(null);
            return;
        }

        try
        {
            var response = await _service.GetAsync(new SequenceIndexRequest { Index = slot.Index });
            if (version != _openVersion || !ReferenceEquals(slot, _selectedSlot))
            {
                return;
            }

            if (!response.Success)
            {
                if (response.Code == ErrorCodes.SequenceNotFound)
                {
                    // 刚被别处删了：当空编号
                    slot.Name = null;
                    ShowSequence(null);
                    return;
                }

                ClientLog.Error(LogModule, L10n.Get("recipe.sequence.load_failed", ReasonOf(response)));
                return;
            }

            ShowSequence(response.DeserializeData<SequenceDto>());
        }
        catch (Exception exception)
        {
            ClientLog.Error(LogModule, L10n.Get("recipe.sequence.load_failed", exception.Message));
        }
    }

    /// <summary>
    /// 把一个流程配方摆到右边（null = 空编号）：步骤按可选分组建勾选块，选中第一个中间步骤；摆完不算"有没保存的修改"。
    /// </summary>
    private void ShowSequence(SequenceDto? dto, int selectPosition = 1)
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
                    Steps.Add(CreateStep(step.Group, step.Stations, step.Recipe));
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
        BuildRoute();
    }

    /// <summary>
    /// 建一步：分组在可选分组里就列出这个分组的全部模块（勾上存过的）；分组在 sc.xml 里没了，就只列存过的站点并标红，让人删掉重加。
    /// </summary>
    private SequenceStepModel CreateStep(string groupName, IEnumerable<string> stations, string recipe)
    {
        var picked = stations.ToList();
        var group = _groups.FirstOrDefault(item => string.Equals(item.Name, groupName, StringComparison.OrdinalIgnoreCase));
        if (group is null)
        {
            return new SequenceStepModel(groupName, picked, picked, recipe, recipe.Length > 0, false, false, OnStepChanged);
        }

        return new SequenceStepModel(group.Name, group.Modules, picked, recipe, group.NeedsRecipe, group.IsLoadPort, true, OnStepChanged);
    }

    /// <summary>
    /// 一步改了（勾选、配方）：选中这一行、记"有没保存的修改"、重新检查、重画路线预览。
    /// </summary>
    private void OnStepChanged(SequenceStepModel step)
    {
        if (_applying)
        {
            return;
        }

        SelectedStep = step;
        MarkDirty();
        Validate();
        BuildRoute();
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
    /// 检查步骤（跟后端保存时的检查一样）：分组还在、至少勾一个站点、要工艺配方的填了配方，装了工艺配方库时配方还得在库里。
    /// 有问题的行标红，标题条右边列出来；全过了才能保存。
    /// </summary>
    private void Validate()
    {
        var messages = new List<string>();
        foreach (var step in Steps)
        {
            string stationHint = string.Empty;
            if (!step.IsKnownGroup)
            {
                stationHint = L10n.Get("recipe.sequence.group_missing", step.Group);
                messages.Add(L10n.Get("recipe.sequence.error_group", step.Number, step.Group));
            }
            else if (step.Options.All(option => !option.IsChecked))
            {
                stationHint = L10n.Get("recipe.sequence.need_station", step.Group);
                messages.Add(L10n.Get("recipe.sequence.error_station", step.Number, step.Group));
            }

            string recipe = step.Recipe.Trim();
            bool recipeMissing = step.NeedsRecipe && recipe.Length == 0;
            bool recipeUnknown = step.NeedsRecipe && !recipeMissing && _hasRecipeLibrary
                && !ProcessRecipes.Any(option => string.Equals(option.Name, recipe, StringComparison.OrdinalIgnoreCase));
            if (recipeMissing)
            {
                messages.Add(L10n.Get("recipe.sequence.error_recipe", step.Number));
            }
            else if (recipeUnknown)
            {
                messages.Add(L10n.Get(ErrorCodes.SequenceRecipeNotFound, step.Number, recipe));
            }

            step.StationHint = stationHint;
            step.HasRecipeError = recipeMissing || recipeUnknown;
            step.HasError = stationHint.Length > 0 || recipeMissing || recipeUnknown;
        }

        ValidationText = string.Join(L10n.Get("recipe.sequence.error_separator"), messages);
        IsValid = messages.Count == 0;
        RefreshCommands();
    }

    /// <summary>
    /// 重画路线预览：一步一列，勾了几个站点就叠几个框，没勾的画一个红框；要工艺配方的在下面写配方名。
    /// </summary>
    private void BuildRoute()
    {
        RouteColumns.Clear();
        foreach (var step in Steps)
        {
            var kind = step.IsLoadPort ? RouteBoxKind.LoadPort : step.NeedsRecipe ? RouteBoxKind.Process : RouteBoxKind.Other;
            var boxes = step.CheckedStations.Select(name => new RouteBoxModel(name, kind)).ToList();
            if (boxes.Count == 0)
            {
                boxes.Add(new RouteBoxModel(L10n.Get("recipe.sequence.route_none", step.Group), RouteBoxKind.Missing));
            }

            bool recipeMissing = step.NeedsRecipe && step.Recipe.Trim().Length == 0;
            string recipeText = !step.NeedsRecipe
                ? string.Empty
                : recipeMissing ? L10n.Get("recipe.sequence.route_no_recipe") : step.Recipe.Trim();
            RouteColumns.Add(new RouteColumnModel(step.Number, boxes, recipeText, recipeMissing));
        }
    }

    private void DoCreate()
    {
        OpenDialog(SequenceDialogKind.Create);
    }

    private void DoRename()
    {
        OpenDialog(SequenceDialogKind.Rename);
    }

    private void DoDelete()
    {
        OpenDialog(SequenceDialogKind.Delete);
    }

    /// <summary>
    /// 添加一步：在公共选择弹窗里选站点分组（列的是 sc.xml 的分组），新步骤插在选中那一步后面
    /// （选中最后一步时插在它前面），默认勾上这个分组的全部模块。
    /// </summary>
    private void DoAddStep()
    {
        if (Current is null || _groups.Count == 0)
        {
            return;
        }

        var columns = new[]
        {
            new PickerColumn { HeaderKey = "recipe.sequence.group", Path = nameof(StationGroupModel.Name), Width = GroupColumnWidth },
            new PickerColumn { HeaderKey = "recipe.sequence.modules", Path = nameof(StationGroupModel.ModulesText) },
        };
        var picked = DialogService.ShowPicker(L10n.Get("recipe.sequence.pick_group"), columns, _groups);
        if (picked is StationGroupModel group)
        {
            int selected = SelectedStep is null ? -1 : Steps.IndexOf(SelectedStep);
            int at = selected < 0 || selected >= Steps.Count - 1 ? Math.Max(Steps.Count - 1, 0) : selected + 1;
            var step = CreateStep(group.Name, group.Modules, string.Empty);
            Steps.Insert(at, step);
            Renumber();
            OnStepChanged(step);
        }
    }

    /// <summary>
    /// 第 1 步、最后一步（取片、放片）删不了，中间至少留一步。
    /// </summary>
    private bool CanDeleteStep()
    {
        var step = SelectedStep;
        if (IsDialogOpen || Current is null || step is null)
        {
            return false;
        }

        int position = Steps.IndexOf(step);
        return position > 0 && position < Steps.Count - 1 && Steps.Count > MinSteps;
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
        OnStepChanged(Steps[Math.Min(position, Steps.Count - 2)]);
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

        var request = new SequenceSaveRequest
        {
            Index = current.Index,
            Revision = current.Revision,
            Description = Description.Trim(),
            Steps = Steps.Select(step => new SequenceStepDto
            {
                Group = step.Group,
                Stations = step.CheckedStations,
                Recipe = step.NeedsRecipe ? step.Recipe.Trim() : string.Empty,
            }).ToList(),
            Operator = ClientSession.UserName,
        };

        var response = await Call(() => _service.SaveAsync(request), "recipe.sequence.save_failed");
        if (response is null)
        {
            return;
        }

        var saved = response.DeserializeData<SequenceDto>();
        int position = SelectedStep is null ? 1 : Steps.IndexOf(SelectedStep);
        ShowSequence(saved, position);
        ClientLog.Info(LogModule, L10n.Get("recipe.sequence.saved", IndexText, saved.Name, saved.Revision));
    }

    /// <summary>
    /// 开确认框：标题、主角、说明、按钮字按要做的事摆好。
    /// </summary>
    private void OpenDialog(SequenceDialogKind kind)
    {
        string subject = Current is null ? IndexText : $"{IndexText}  {Current.Name}";
        DialogError = string.Empty;
        DialogMessage = string.Empty;
        ConfirmText = L10n.Get("common.ok");
        CancelText = L10n.Get("common.cancel");
        IsDangerConfirm = false;
        switch (kind)
        {
            case SequenceDialogKind.Create:
                DialogTitle = L10n.Get("recipe.sequence.dialog_create", IndexText);
                DialogSubject = string.Empty;
                DialogName = string.Empty;
                break;
            case SequenceDialogKind.Rename:
                DialogTitle = L10n.Get("recipe.sequence.dialog_rename", IndexText);
                DialogSubject = string.Empty;
                DialogName = Current?.Name ?? string.Empty;
                break;
            case SequenceDialogKind.Delete:
                DialogTitle = L10n.Get("recipe.sequence.dialog_delete");
                DialogSubject = subject;
                DialogMessage = L10n.Get("recipe.sequence.delete_hint");
                ConfirmText = L10n.Get("recipe.sequence.confirm_delete");
                IsDangerConfirm = true;
                break;
            case SequenceDialogKind.Discard:
                DialogTitle = L10n.Get("recipe.sequence.dialog_discard");
                DialogSubject = subject;
                DialogMessage = L10n.Get("recipe.sequence.discard_hint");
                ConfirmText = L10n.Get("recipe.sequence.discard");
                CancelText = L10n.Get("recipe.sequence.keep_editing");
                IsDangerConfirm = true;
                break;
        }

        DialogError = string.Empty;
        Dialog = kind;
    }

    private void DoDialogCancel()
    {
        _pendingSlot = null;
        Dialog = SequenceDialogKind.None;
    }

    private async Task DoDialogConfirm()
    {
        switch (Dialog)
        {
            case SequenceDialogKind.Create:
                await ConfirmCreate();
                break;
            case SequenceDialogKind.Rename:
                await ConfirmRename();
                break;
            case SequenceDialogKind.Delete:
                await ConfirmDelete();
                break;
            case SequenceDialogKind.Discard:
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
            Dialog = SequenceDialogKind.None;
            return;
        }

        var response = await CallInDialog(() => _service.CreateAsync(new SequenceCreateRequest
        {
            Index = slot.Index,
            Name = DialogName.Trim(),
            Operator = ClientSession.UserName,
        }));
        if (response is null)
        {
            return;
        }

        var created = response.DeserializeData<SequenceDto>();
        slot.Name = created.Name;
        Dialog = SequenceDialogKind.None;
        if (ReferenceEquals(slot, _selectedSlot))
        {
            ShowSequence(created);
        }

        ClientLog.Info(LogModule, L10n.Get("recipe.sequence.created", slot.IndexText, created.Name));
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
            Dialog = SequenceDialogKind.None;
            return;
        }

        var response = await CallInDialog(() => _service.RenameAsync(new SequenceRenameRequest
        {
            Index = current.Index,
            Name = DialogName.Trim(),
            Operator = ClientSession.UserName,
        }));
        if (response is null)
        {
            return;
        }

        var renamed = response.DeserializeData<SequenceDto>();
        slot.Name = renamed.Name;
        Dialog = SequenceDialogKind.None;
        if (IsDirty)
        {
            Current = renamed;
        }
        else
        {
            int position = SelectedStep is null ? 1 : Steps.IndexOf(SelectedStep);
            ShowSequence(renamed, position);
        }

        ClientLog.Info(LogModule, L10n.Get("recipe.sequence.renamed", slot.IndexText, renamed.Name));
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
            Dialog = SequenceDialogKind.None;
            return;
        }

        var response = await CallInDialog(() => _service.DeleteAsync(new SequenceDeleteRequest
        {
            Index = current.Index,
            Operator = ClientSession.UserName,
        }));
        if (response is null)
        {
            return;
        }

        slot.Name = null;
        Dialog = SequenceDialogKind.None;
        ShowSequence(null);
        ClientLog.Info(LogModule, L10n.Get("recipe.sequence.deleted", slot.IndexText, current.Name));
    }

    /// <summary>
    /// 放弃修改：丢掉没保存的，切到刚才点的那个编号。
    /// </summary>
    private void ConfirmDiscard()
    {
        var pending = _pendingSlot;
        _pendingSlot = null;
        IsDirty = false;
        Dialog = SequenceDialogKind.None;
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

    private void UpdatePageState()
    {
        if (_isInstalled && Slots.Count > 0)
        {
            PageState = SequencePageState.Ready;
        }
        else if (!IsConnected)
        {
            PageState = SequencePageState.Offline;
        }
        else if (!_isLoaded)
        {
            PageState = SequencePageState.Loading;
        }
        else
        {
            PageState = _isInstalled ? SequencePageState.Loading : SequencePageState.NotInstalled;
        }
    }

    private void NotifyHeader()
    {
        OnPropertyChanged(nameof(HasSequence));
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
