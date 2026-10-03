using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using xyz.Client.Common.Events;
using xyz.Client.Common.Log;
using xyz.Client.Common.Rpc;
using xyz.Client.Common.Session;
using xyz.Client.DataModels.ViewModels;
using xyz.Client.Presentation.Localization;
using xyz.Client.Setting.Models;
using xyz.Shared.Dtos;
using xyz.Shared.Rpc;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Client.Setting.ViewModels;

/// <summary>
/// 账单调整页 ViewModel（设置 → 账单调整）：实物和系统账对不上时（比如取片报警，片其实已经在手指上、账还在腔体里），
/// 人按实物把这片的账移到它真正在的地方、删掉，或者在空槽上补一片。只改系统账，设备不动作；每次调整后端记一条调整记录（操作人、原因）。
/// 左栏选源（有片的槽，移账、删账用），右栏选目标（空槽，移账、补账用）；位置跟着后端配置走（所有机械手的站点 + 机械手自己），页面不写死。
/// 账一变后端推一条通知，这里攒一下再重拉；页面不在前台时只记一笔，切回来再拉。
/// </summary>
public class WaferLedgerViewModel : BaseViewModel
{
    private const string LogModule = "WaferLedger";

    /// <summary>
    /// 账变通知攒多久再拉：整篮 Mapping、一次传片会一下来一串，合成一次。
    /// </summary>
    private static readonly TimeSpan ReloadDelay = TimeSpan.FromMilliseconds(300);

    #region Column

    /// <summary>
    /// 全部位置（按机械手、LoadPort、腔体、其他排好，同种类保持后端给的先后）；两栏的位置列表都是它的分组视图。
    /// </summary>
    public ObservableCollection<LedgerLocationModel> Locations { get; } = [];

    /// <summary>
    /// 源栏位置列表（按种类分组）。
    /// </summary>
    public ICollectionView SourceLocationsView { get; }

    /// <summary>
    /// 目标栏位置列表（按种类分组）；跟源栏是两个视图，两边各选各的。
    /// </summary>
    public ICollectionView TargetLocationsView { get; }

    /// <summary>
    /// 源栏槽位表：源栏选中位置的每个槽（LoadPort 大号在上，跟实物一样）。
    /// </summary>
    public ObservableCollection<LedgerSlotModel> SourceSlots { get; } = [];

    /// <summary>
    /// 目标栏槽位表。
    /// </summary>
    public ObservableCollection<LedgerSlotModel> TargetSlots { get; } = [];

    /// <summary>
    /// 调整记录（最新的在最上面，最多 50 条）。
    /// </summary>
    public ObservableCollection<LedgerAdjustmentModel> Records { get; } = [];

    private LedgerLocationModel? _sourceLocation;

    /// <summary>
    /// 源栏选中的位置；换了就换槽位表，选好的源在这个位置上就把那一行选上。
    /// </summary>
    public LedgerLocationModel? SourceLocation
    {
        get => _sourceLocation;
        set
        {
            if (SetProperty(ref _sourceLocation, value))
            {
                FillSlots(SourceSlots, value, forSource: true);
                SourceRow = SourceSlots.FirstOrDefault(row => row.IsSameSlot(Source));
            }
        }
    }

    private LedgerLocationModel? _targetLocation;

    /// <summary>
    /// 目标栏选中的位置：选位置就是在选目标。选好的目标在这个位置上就选回它，不在就默认选表里第一个空槽——
    /// 选了位置"新建""移动"马上能点，想换再点别的空槽；这个位置没有空槽时目标清空。
    /// （源栏不这样默认：删、移针对的是具体一片，要人自己点。）
    /// </summary>
    public LedgerLocationModel? TargetLocation
    {
        get => _targetLocation;
        set
        {
            if (SetProperty(ref _targetLocation, value))
            {
                FillSlots(TargetSlots, value, forSource: false);
                var row = TargetSlots.FirstOrDefault(item => item.IsSameSlot(Target))
                    ?? TargetSlots.FirstOrDefault(item => item.IsPickable);
                TargetRow = row;
                if (row is null)
                {
                    Target = null;
                }
            }
        }
    }

    private LedgerSlotModel? _sourceRow;

    /// <summary>
    /// 源栏槽位表选中的那一行（表格的选中项）：点了有片的行就把它定为源。
    /// 换位置时表格把选中清掉，源不跟着丢（工具栏、确认框看的是 <see cref="Source"/>）。
    /// </summary>
    public LedgerSlotModel? SourceRow
    {
        get => _sourceRow;
        set
        {
            if (SetProperty(ref _sourceRow, value) && value is not null && value.IsPickable)
            {
                Source = value;
            }
        }
    }

    private LedgerSlotModel? _targetRow;

    /// <summary>
    /// 目标栏槽位表选中的那一行：点了空槽就把它定为目标。
    /// </summary>
    public LedgerSlotModel? TargetRow
    {
        get => _targetRow;
        set
        {
            if (SetProperty(ref _targetRow, value) && value is not null && value.IsPickable)
            {
                Target = value;
            }
        }
    }

    private LedgerSlotModel? _source;

    /// <summary>
    /// 选好的源：要调整的那一片（位置、槽、片号）。
    /// </summary>
    public LedgerSlotModel? Source
    {
        get => _source;
        private set
        {
            if (SetProperty(ref _source, value))
            {
                RefreshCommands();
            }
        }
    }

    private LedgerSlotModel? _target;

    /// <summary>
    /// 选好的目标：移账要放进去、补账要建片的空槽。
    /// </summary>
    public LedgerSlotModel? Target
    {
        get => _target;
        private set
        {
            if (SetProperty(ref _target, value))
            {
                RefreshCommands();
            }
        }
    }

    private string _newWaferId = string.Empty;

    /// <summary>
    /// 补账时填的片号（确认框里的输入框）。
    /// </summary>
    public string NewWaferId
    {
        get => _newWaferId;
        set
        {
            if (SetProperty(ref _newWaferId, value ?? string.Empty) && _newWaferId.Trim().Length > 0)
            {
                IsWaferIdMissing = false;
            }
        }
    }

    private bool _isWaferIdMissing;

    /// <summary>
    /// 补账没填片号就点了确认：确认框留着，提示先填。
    /// </summary>
    public bool IsWaferIdMissing
    {
        get => _isWaferIdMissing;
        private set => SetProperty(ref _isWaferIdMissing, value);
    }

    private string _reason = string.Empty;

    /// <summary>
    /// 原因（选填），记进调整记录。
    /// </summary>
    public string Reason
    {
        get => _reason;
        set
        {
            if (SetProperty(ref _reason, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(ReasonText));
            }
        }
    }

    /// <summary>
    /// 确认框里显示的原因：没填显示"—"。
    /// </summary>
    public string ReasonText => string.IsNullOrWhiteSpace(Reason) ? L10n.Get("setting.ledger.no_reason") : Reason.Trim();

    /// <summary>
    /// 操作人（当前用户），记进调整记录。
    /// </summary>
    public string OperatorName => ClientSession.UserName;

    private bool _isConnected;

    /// <summary>
    /// 跟后端连着；断开时账还显示着，但不能调。
    /// </summary>
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

    private LedgerPageState _pageState = LedgerPageState.Offline;

    /// <summary>
    /// 页面现在能不能用：有账才显示两栏和记录，其他情况正中给一句提示。
    /// </summary>
    public LedgerPageState PageState
    {
        get => _pageState;
        private set
        {
            if (SetProperty(ref _pageState, value))
            {
                OnPropertyChanged(nameof(IsReady));
            }
        }
    }

    public bool IsReady => PageState == LedgerPageState.Ready;

    private LedgerAction _pendingAction;

    /// <summary>
    /// 确认框正在确认的事；None 时确认框关着。
    /// </summary>
    public LedgerAction PendingAction
    {
        get => _pendingAction;
        private set
        {
            if (SetProperty(ref _pendingAction, value))
            {
                OnPropertyChanged(nameof(IsConfirming));
                OnPropertyChanged(nameof(IsMoving));
                OnPropertyChanged(nameof(IsDeleting));
                OnPropertyChanged(nameof(IsCreating));
                OnPropertyChanged(nameof(IsSourceAction));
                ConfirmCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsConfirming => PendingAction != LedgerAction.None;

    public bool IsMoving => PendingAction == LedgerAction.Move;

    public bool IsDeleting => PendingAction == LedgerAction.Delete;

    public bool IsCreating => PendingAction == LedgerAction.Create;

    /// <summary>
    /// 正在确认的是对源那片做的事（移动、删除）：确认框显示源的片号和位置。补账没有源，显示片号输入框和目标位置。
    /// </summary>
    public bool IsSourceAction => PendingAction == LedgerAction.Move || PendingAction == LedgerAction.Delete;

    #endregion

    #region Command

    /// <summary>
    /// 移动账单：源、目标都选好才能点，弹确认框。
    /// </summary>
    public IRelayCommand MoveCommand { get; }

    /// <summary>
    /// 新建账单（补账）：选好目标空槽就能点，弹确认框填片号。
    /// </summary>
    public IRelayCommand CreateCommand { get; }

    /// <summary>
    /// 删除账单：选好源就能点，弹确认框。
    /// </summary>
    public IRelayCommand DeleteCommand { get; }

    /// <summary>
    /// 确认框里的"确认移动 / 确认删除 / 确认新建"：发给后端改账。
    /// </summary>
    public IAsyncRelayCommand ConfirmCommand { get; }

    /// <summary>
    /// 确认框里的"取消"（Esc 也是）。
    /// </summary>
    public IRelayCommand CancelCommand { get; }

    #endregion

    #region Service

    private readonly IWaferLedgerService _service;

    /// <summary>
    /// 攒账变通知用：第一条来了起表，到点拉一次。
    /// </summary>
    private readonly DispatcherTimer _reloadTimer;

    private IDisposable? _subscription;

    private bool _isPageVisible;

    /// <summary>
    /// 账可能变了还没拉（页面不在前台、没连上时攒着）。
    /// </summary>
    private bool _isDirty = true;

    /// <summary>
    /// 第几轮拉账；拉回来时已经有更新的一轮就扔掉，以新的为准。
    /// </summary>
    private int _loadVersion;

    /// <summary>
    /// 拉到过账没有（没拉到过之前不说"没开""没有位置"）。
    /// </summary>
    private bool _isLoaded;

    /// <summary>
    /// 后端说晶圆账开着。
    /// </summary>
    private bool _isLedgerEnabled;

    /// <summary>
    /// 从第几轮拉账起，把最新一条记录当成本机刚做的那一条标出来（本机改完账后设，用一次）。
    /// </summary>
    private int _freshFromVersion = int.MaxValue;

    /// <summary>
    /// 本机最近做的那一条调整记录；之后重拉还认得出它，一直标着。
    /// </summary>
    private WaferAdjustmentDto? _freshRecord;

    #endregion

    public WaferLedgerViewModel()
    {
        _service = GrpcClientFactory.Create<IWaferLedgerService>();
        SourceLocationsView = GroupByKind(Locations);
        TargetLocationsView = GroupByKind(Locations);

        MoveCommand = new RelayCommand(DoMove, () => IsConnected && Source is not null && Target is not null);
        CreateCommand = new RelayCommand(DoCreate, () => IsConnected && Target is not null);
        DeleteCommand = new RelayCommand(DoDelete, () => IsConnected && Source is not null);
        ConfirmCommand = new AsyncRelayCommand(DoConfirm, () => IsConnected && PendingAction != LedgerAction.None);
        CancelCommand = new RelayCommand(DoCancel);

        _reloadTimer = new DispatcherTimer { Interval = ReloadDelay };
        _reloadTimer.Tick += (_, _) =>
        {
            _reloadTimer.Stop();
            _ = Reload();
        };
    }

    /// <summary>
    /// 订账变通知、跟着连接走：连上就拉账（页面在前台时）。
    /// </summary>
    public override void Init()
    {
        _subscription?.Dispose();
        _subscription = EventBus.Register<WaferLedgerChangedDto>(WaferLedgerDto.EventToken, _ => RequestReload());

        RemoteEventBus.ConnectionChanged -= OnConnectionChanged;
        RemoteEventBus.ConnectionChanged += OnConnectionChanged;
        OnConnectionChanged(RemoteEventBus.IsConnected);
    }

    /// <summary>
    /// 页面显示 / 隐藏（View 的 IsVisibleChanged 调）：不在前台时账变只记一笔不拉，切回来有变化再拉。
    /// </summary>
    public void SetPageVisible(bool visible)
    {
        _isPageVisible = visible;
        if (visible && _isDirty && IsConnected)
        {
            // 切过来时立即拉，不用等攒通知的那一下
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
            // 断开期间账会不会变不知道：连回来整个重拉；确认框开着就关掉（发不出去）
            _isDirty = true;
            PendingAction = LedgerAction.None;
        }

        UpdatePageState();
    }

    /// <summary>
    /// 要重拉：记一笔；页面在前台、连着才起表，一串通知只拉一次。
    /// </summary>
    private void RequestReload()
    {
        _isDirty = true;
        if (!_isPageVisible || !IsConnected || _reloadTimer.IsEnabled)
        {
            return;
        }

        _reloadTimer.Start();
    }

    /// <summary>
    /// 拉账和调整记录，拉回来整个换掉（选好的源、目标、两栏选中的位置都按名字找回来）。
    /// </summary>
    private async Task Reload()
    {
        int version = ++_loadVersion;
        _isDirty = false;
        try
        {
            var ledger = (await _service.GetLedgerAsync(new RpcRequest())).DeserializeData<WaferLedgerDto>();
            var adjustments = (await _service.GetAdjustmentsAsync(new RpcRequest())).DeserializeData<List<WaferAdjustmentDto>>();
            if (version != _loadVersion)
            {
                return;
            }

            if (version >= _freshFromVersion)
            {
                _freshRecord = adjustments.FirstOrDefault();
                _freshFromVersion = int.MaxValue;
            }

            Apply(ledger, adjustments);
        }
        catch (Exception exception)
        {
            if (version == _loadVersion)
            {
                _isDirty = true;
                ClientLog.Error(LogModule, L10n.Get("setting.ledger.load_failed", exception.Message));
            }
        }
    }

    private void Apply(WaferLedgerDto ledger, IReadOnlyList<WaferAdjustmentDto> adjustments)
    {
        _isLoaded = true;
        _isLedgerEnabled = ledger.IsEnabled;

        string? sourceName = SourceLocation?.Name;
        string? targetName = TargetLocation?.Name;
        var source = Source;
        var target = Target;

        Locations.Clear();
        foreach (var dto in ledger.Locations.OrderBy(location => location.Kind))
        {
            Locations.Add(new LedgerLocationModel(dto));
        }

        // 选好的源、目标找回来：位置和槽还在、源那槽上还是同一片、目标那槽还空着才留
        Source = Resolve(source, forSource: true);
        Target = Resolve(target, forSource: false);
        bool stillValid = PendingAction switch
        {
            LedgerAction.Move => Source is not null && Target is not null,
            LedgerAction.Delete => Source is not null,
            LedgerAction.Create => Target is not null,
            _ => true,
        };
        if (!stillValid)
        {
            // 要确认的那片或那个空槽已经变了（比如设备刚动过），确认框作废
            PendingAction = LedgerAction.None;
        }

        SourceLocation = Find(sourceName) ?? Locations.FirstOrDefault(location => location.HasWafer) ?? Locations.FirstOrDefault();
        TargetLocation = Find(targetName) ?? Locations.FirstOrDefault(location => location.HasFree) ?? Locations.FirstOrDefault();

        Records.Clear();
        foreach (var dto in adjustments)
        {
            Records.Add(new LedgerAdjustmentModel(dto, PositionOf, IsFresh(dto)));
        }

        UpdatePageState();
        RefreshCommands();
    }

    /// <summary>
    /// 按位置名 + 槽号在新账里找回选好的槽；找不到或已经不能选（源那槽换了片、没片了，目标那槽有片了）返回 null。
    /// </summary>
    private LedgerSlotModel? Resolve(LedgerSlotModel? chosen, bool forSource)
    {
        if (chosen is null)
        {
            return null;
        }

        var location = Find(chosen.Location.Name);
        if (location is null)
        {
            return null;
        }

        var slot = location.Slots.FirstOrDefault(item => item.Slot == chosen.Slot);
        if (slot is null)
        {
            return null;
        }

        var row = new LedgerSlotModel(location, slot, forSource);
        if (!row.IsPickable)
        {
            return null;
        }

        if (forSource && !string.Equals(row.WaferId, chosen.WaferId, StringComparison.Ordinal))
        {
            return null;
        }

        return row;
    }

    private LedgerLocationModel? Find(string? name)
    {
        if (name is null)
        {
            return null;
        }

        return Locations.FirstOrDefault(location => string.Equals(location.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 调整记录里的"位置 · 槽名"：位置还在就按它的种类叫（机械手是手指），不在了就按槽叫。
    /// </summary>
    private string PositionOf(string module, int slot)
    {
        var location = Find(module);
        if (location is null)
        {
            return $"{module} · {L10n.Get("setting.ledger.slot_name", slot)}";
        }

        return location.PositionText(slot);
    }

    private bool IsFresh(WaferAdjustmentDto dto)
    {
        var fresh = _freshRecord;
        return fresh is not null
            && fresh.Time == dto.Time
            && fresh.Action == dto.Action
            && fresh.WaferId == dto.WaferId;
    }

    private void UpdatePageState()
    {
        if (Locations.Count > 0)
        {
            PageState = LedgerPageState.Ready;
        }
        else if (!IsConnected)
        {
            PageState = LedgerPageState.Offline;
        }
        else if (!_isLoaded)
        {
            PageState = LedgerPageState.Loading;
        }
        else if (!_isLedgerEnabled)
        {
            PageState = LedgerPageState.Disabled;
        }
        else
        {
            PageState = LedgerPageState.NoLocations;
        }
    }

    private void DoMove()
    {
        if (Source is not null && Target is not null)
        {
            PendingAction = LedgerAction.Move;
        }
    }

    private void DoCreate()
    {
        if (Target is not null)
        {
            NewWaferId = string.Empty;
            IsWaferIdMissing = false;
            PendingAction = LedgerAction.Create;
        }
    }

    private void DoDelete()
    {
        if (Source is not null)
        {
            PendingAction = LedgerAction.Delete;
        }
    }

    private void DoCancel()
    {
        PendingAction = LedgerAction.None;
    }

    /// <summary>
    /// 确认后发给后端改账。成了：记一条日志，清掉选择和原因，移账、补账后源栏跳到目标位置（看得到片已经在那了），新记录标出来；
    /// 没成：原因记到日志栏。两种都马上重拉一次（没成多半是账刚被别处改过）。补账没填片号时确认框留着，提示先填。
    /// </summary>
    private async Task DoConfirm()
    {
        var action = PendingAction;
        var source = Source;
        var target = Target;
        string waferId = NewWaferId.Trim();
        if (action == LedgerAction.Create && waferId.Length == 0)
        {
            IsWaferIdMissing = true;
            return;
        }

        bool done;
        if (action == LedgerAction.Move && source is not null && target is not null)
        {
            done = await Submit(
                () => _service.MoveAsync(new WaferMoveRequest
                {
                    FromModule = source.Location.Name,
                    FromSlot = source.Slot,
                    ToModule = target.Location.Name,
                    ToSlot = target.Slot,
                    Reason = Reason.Trim(),
                    Operator = OperatorName,
                }),
                "setting.ledger.move_failed",
                L10n.Get("setting.ledger.moved", source.WaferId, source.PositionText, target.PositionText));
            if (done)
            {
                SourceLocation = target.Location;
            }
        }
        else if (action == LedgerAction.Delete && source is not null)
        {
            done = await Submit(
                () => _service.DeleteAsync(new WaferDeleteRequest
                {
                    Module = source.Location.Name,
                    Slot = source.Slot,
                    Reason = Reason.Trim(),
                    Operator = OperatorName,
                }),
                "setting.ledger.delete_failed",
                L10n.Get("setting.ledger.deleted", source.WaferId, source.PositionText));
        }
        else if (action == LedgerAction.Create && target is not null)
        {
            done = await Submit(
                () => _service.CreateAsync(new WaferCreateRequest
                {
                    Module = target.Location.Name,
                    Slot = target.Slot,
                    WaferId = waferId,
                    Reason = Reason.Trim(),
                    Operator = OperatorName,
                }),
                "setting.ledger.create_failed",
                L10n.Get("setting.ledger.created", waferId, target.PositionText));
            if (done)
            {
                SourceLocation = target.Location;
            }
        }
        else
        {
            // 要确认的那片或那个空槽已经不在了，确认框作废
            PendingAction = LedgerAction.None;
            return;
        }

        if (done)
        {
            ClearSelection();
            Reason = string.Empty;
            _freshFromVersion = _loadVersion + 1;
        }

        await Reload();
    }

    /// <summary>
    /// 改完账清掉选好的源和目标（表格里的选中一起清）。
    /// </summary>
    private void ClearSelection()
    {
        SourceRow = null;
        TargetRow = null;
        Source = null;
        Target = null;
    }

    /// <summary>
    /// 发一次改账请求：成了记一条信息日志返回 true；没成把后端给的原因（按错误码翻成当前语言）记到日志栏返回 false。
    /// 不管成没成确认框都关掉。
    /// </summary>
    private async Task<bool> Submit(Func<Task<RpcResponse>> call, string failedKey, string doneText)
    {
        try
        {
            var response = await call();
            if (!response.Success)
            {
                string reason = string.IsNullOrEmpty(response.Code) ? response.Message : L10n.Get(response.Code, response.Args);
                ClientLog.Error(LogModule, L10n.Get(failedKey, reason));
                return false;
            }

            ClientLog.Info(LogModule, doneText);
            return true;
        }
        catch (Exception exception)
        {
            ClientLog.Error(LogModule, L10n.Get(failedKey, exception.Message));
            return false;
        }
        finally
        {
            PendingAction = LedgerAction.None;
        }
    }

    /// <summary>
    /// 换槽位表：LoadPort 跟实物一样大号在上（25 → 01），其他位置从小到大。
    /// </summary>
    private static void FillSlots(ObservableCollection<LedgerSlotModel> rows, LedgerLocationModel? location, bool forSource)
    {
        rows.Clear();
        if (location is null)
        {
            return;
        }

        var slots = location.Kind == WaferLocationKind.LoadPort ? location.Slots.Reverse() : location.Slots;
        foreach (var slot in slots)
        {
            rows.Add(new LedgerSlotModel(location, slot, forSource));
        }
    }

    private static ICollectionView GroupByKind(ObservableCollection<LedgerLocationModel> locations)
    {
        var view = new ListCollectionView(locations);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(LedgerLocationModel.KindText)));
        return view;
    }

    private void RefreshCommands()
    {
        MoveCommand.NotifyCanExecuteChanged();
        CreateCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        ConfirmCommand.NotifyCanExecuteChanged();
    }
}
