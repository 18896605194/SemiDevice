using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using xyz.Client.Common.Ec;
using xyz.Client.Common.Events;
using xyz.Client.Common.Log;
using xyz.Client.Common.Rpc;
using xyz.Client.DataModels.ViewModels;
using xyz.Client.Manual.Models;
using xyz.Client.Presentation.Localization;
using xyz.Client.Presentation.Models;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Shared.Rpc;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Client.Manual.ViewModels;

/// <summary>
/// 腔体手动操作面板 ViewModel：按钮发指令，状态靠订阅刷新；启用、模式、片位、当前配方、部件状态都是后端推的，这里不写死。
/// 整腔：Start 按配方框里的配方名起工艺（Process，只在空闲时允许）；Abort = 急停（可顶替在途动作）、Reset = 清报警 + 设备复位清错。
/// 部件：后端按 sc.xml 推来的部件里，轴一根一个页签（Axes），双作用气缸一行一个（Cylinders），三维图也用同一份推送（Parts）。
/// 部件动作都走 PartActionAsync（部件路径 + 动作名 + 参数）：点动按住期间每 200 ms 续一次，松手发停止。
/// </summary>
public class ChamberManualViewModel : BaseViewModel, IDisposable
{
    #region Column

    /// <summary>
    /// 模块实例名，与 EventBus token / gRPC 参数一致，如 "Chamber1"。
    /// </summary>
    public string ModuleName { get; }

    /// <summary>
    /// 显示模型：状态推送来就地刷新，所以是 get-only。
    /// </summary>
    public ChamberModel Model { get; } = new();

    /// <summary>
    /// 三维图的部件显示模型：部件推送来就地刷新。
    /// </summary>
    public ChamberPartsModel Parts { get; } = new();

    /// <summary>
    /// 轴页签：sc 里这个腔体下的轴（摆臂、旋转电机……），按 sc 的先后；加了轴自动多一个页签。
    /// </summary>
    public ObservableCollection<AxisPartModel> Axes { get; } = [];

    private AxisPartModel? _selectedAxis;

    /// <summary>
    /// 选中的轴：下面的状态、参数、按钮都对它。
    /// </summary>
    public AxisPartModel? SelectedAxis
    {
        get => _selectedAxis;
        set => SetProperty(ref _selectedAxis, value);
    }

    /// <summary>
    /// 气缸表：sc 里这个腔体下的双作用气缸（Door、Bowl1、Arm1.Lift……），按 sc 的先后。
    /// </summary>
    public ObservableCollection<CylinderPartModel> Cylinders { get; } = [];

    private string _recipe = string.Empty;

    /// <summary>
    /// 要起的工艺配方名（Start 按它发 Process），在选择框里从工艺配方库挑（后端没装库时手填）。
    /// Start 不跟着它变灰，空着点 Start 在日志里提示。
    /// </summary>
    public string Recipe
    {
        get => _recipe;
        set => SetProperty(ref _recipe, value);
    }

    /// <summary>
    /// 选工艺配方弹窗里列的工艺配方（工艺配方库，连上后端时拉、库变了重拉）。
    /// </summary>
    public ObservableCollection<ProcessRecipeOptionModel> ProcessRecipes { get; } = [];

    private bool _canTypeRecipe;

    /// <summary>
    /// 配方框能不能手输：后端没装工艺配方库时才让手输（后端那边也不查）；装了就只能从库里选。
    /// </summary>
    public bool CanTypeRecipe
    {
        get => _canTypeRecipe;
        private set => SetProperty(ref _canTypeRecipe, value);
    }

    #endregion

    #region Command

    public IAsyncRelayCommand ProcessCommand { get; }

    public IAsyncRelayCommand HomeCommand { get; }

    public IAsyncRelayCommand AbortCommand { get; }

    public IAsyncRelayCommand ResetCommand { get; }

    public IAsyncRelayCommand OnlineCommand { get; }

    public IAsyncRelayCommand OfflineCommand { get; }

    /// <summary>选中的轴回零。</summary>
    public IAsyncRelayCommand AxisHomeCommand { get; }

    /// <summary>选中的轴按移动速度走到目标位置。</summary>
    public IAsyncRelayCommand AxisMoveCommand { get; }

    /// <summary>选中的轴停止：别的动作在途也照发，所以可以同时点。</summary>
    public IAsyncRelayCommand AxisStopCommand { get; }

    /// <summary>选中的轴驱动器复位清错。</summary>
    public IAsyncRelayCommand AxisResetCommand { get; }

    /// <summary>选中的轴步进一个步距（参数 "1" 正向、"-1" 反向），按点动速度走。</summary>
    public IAsyncRelayCommand<string> AxisStepCommand { get; }

    /// <summary>点动按下（参数 "1" 正向、"-1" 反向）：按点动速度一直走。</summary>
    public IRelayCommand<string> JogPressCommand { get; }

    /// <summary>点动按住期间续（HoldButton 每 200 ms 发一次）。</summary>
    public IRelayCommand<string> JogRenewCommand { get; }

    /// <summary>点动松手：发停止。</summary>
    public IRelayCommand<string> JogReleaseCommand { get; }

    /// <summary>页签右边的"&gt;"：选下一根轴（最后一根再按回到第一根）。</summary>
    public IRelayCommand NextAxisCommand { get; }

    /// <summary>气缸升（开侧），参数是那一行。</summary>
    public IAsyncRelayCommand<CylinderPartModel> CylinderOpenCommand { get; }

    /// <summary>气缸降（关侧），参数是那一行。</summary>
    public IAsyncRelayCommand<CylinderPartModel> CylinderCloseCommand { get; }

    #endregion

    #region Service

    /// <summary>第二个参数是动作名的部件错误码（chamber.part_not_found 的参数是模块和路径，不在里面）。</summary>
    private static readonly HashSet<string> PartActionCodes =
    [
        ErrorCodes.ChamberPartActionUnsupported,
        ErrorCodes.ChamberPartCommandRejected,
        ErrorCodes.ChamberPartActionFailed,
        ErrorCodes.ChamberPartActionArgsInvalid,
        ErrorCodes.ChamberPartNotHeld,
    ];

    private readonly IChamberService _service;

    private readonly IProcessRecipeService _recipeService;

    private IDisposable? _stateSubscription;

    private IDisposable? _partsSubscription;

    private IDisposable? _recipeSubscription;

    /// <summary>
    /// 第几轮拉工艺配方列表；拉回来时已经有更新的一轮就扔掉。
    /// </summary>
    private int _recipeVersion;

    /// <summary>正在点动的轴（按下时选中的那根）；松手、点动没发出去、续不上了置空。</summary>
    private AxisPartModel? _jogAxis;

    /// <summary>点动请求本身：松手先等它回来再发停止，免得停止比点动先到后台。</summary>
    private Task? _jogStart;

    #endregion

    public ChamberManualViewModel(string moduleName)
    {
        ModuleName = moduleName;
        _service = GrpcClientFactory.Create<IChamberService>();
        _recipeService = GrpcClientFactory.Create<IProcessRecipeService>();

        ProcessCommand = new AsyncRelayCommand(DoProcess);
        HomeCommand = new AsyncRelayCommand(DoHome);
        AbortCommand = new AsyncRelayCommand(DoAbort);
        ResetCommand = new AsyncRelayCommand(DoReset);
        OnlineCommand = new AsyncRelayCommand(DoOnline);
        OfflineCommand = new AsyncRelayCommand(DoOffline);
        AxisHomeCommand = new AsyncRelayCommand(DoAxisHome);
        AxisMoveCommand = new AsyncRelayCommand(DoAxisMove);
        AxisStopCommand = new AsyncRelayCommand(DoAxisStop, AsyncRelayCommandOptions.AllowConcurrentExecutions);
        AxisResetCommand = new AsyncRelayCommand(DoAxisReset);
        AxisStepCommand = new AsyncRelayCommand<string>(DoAxisStep);
        JogPressCommand = new RelayCommand<string>(DoJogPress);
        JogRenewCommand = new RelayCommand<string>(DoJogRenew);
        JogReleaseCommand = new RelayCommand<string>(DoJogRelease);
        NextAxisCommand = new RelayCommand(DoNextAxis);
        CylinderOpenCommand = new AsyncRelayCommand<CylinderPartModel>(row => DoCylinder(row, true));
        CylinderCloseCommand = new AsyncRelayCommand<CylinderPartModel>(row => DoCylinder(row, false));
    }

    public override void Init()
    {
        // 先拉一次当前状态，再订阅推送；只等推送的话页面刚打开会一直是空的。
        try
        {
            var response = _service.GetStateAsync(ModuleName).GetAwaiter().GetResult();
            response.EnsureSuccess();

            var json = response.Data?.TrimStart() ?? string.Empty;
            var chamber = json.StartsWith('[')
                ? JsonHelper.Deserialize<List<ChamberDto>>(json)?.FirstOrDefault()
                : JsonHelper.Deserialize<ChamberDto>(json);
            if (chamber is not null)
            {
                OnStateReceived(chamber);
            }
        }
        catch (Exception exception)
        {
            ClientLog.Error(ModuleName, L10n.Get("chambermanual.state_failed", exception.Message));
        }

        _stateSubscription?.Dispose();
        _stateSubscription = EventBus.Register<ChamberDto>(ModuleName, OnStateReceived);

        // 部件推送是留存消息：订上就补发最后一条，不用另外拉。
        _partsSubscription?.Dispose();
        _partsSubscription = EventBus.Register<ModulePartsDto>(ModuleName, OnPartsReceived);

        // 轴参数的默认值取 EC：EC 拉到（或改了）时把还空着的补上
        ClientEc.Changed -= OnEcChanged;
        ClientEc.Changed += OnEcChanged;

        // 工艺配方列表：连上后端时拉，工艺配方库变了（新建、改名、删除）再拉
        _recipeSubscription?.Dispose();
        _recipeSubscription = EventBus.Register<ProcessRecipeChangedDto>(ProcessRecipeListDto.EventToken, changed => _ = LoadRecipes());
        RemoteEventBus.ConnectionChanged -= OnConnectionChanged;
        RemoteEventBus.ConnectionChanged += OnConnectionChanged;
        OnConnectionChanged(RemoteEventBus.IsConnected);
    }

    public void Dispose()
    {
        _stateSubscription?.Dispose();
        _stateSubscription = null;
        _partsSubscription?.Dispose();
        _partsSubscription = null;
        _recipeSubscription?.Dispose();
        _recipeSubscription = null;
        ClientEc.Changed -= OnEcChanged;
        RemoteEventBus.ConnectionChanged -= OnConnectionChanged;
    }

    private void OnConnectionChanged(bool connected)
    {
        if (connected)
        {
            _ = LoadRecipes();
        }
    }

    private void OnEcChanged()
    {
        foreach (var axis in Axes)
        {
            axis.FillDefaults();
        }
    }

    /// <summary>
    /// 拉工艺配方列表：装了库就只能从库里选；没装库（回 process_recipe.not_installed）放开手输；
    /// 其他原因没拉到的保留上一次的，记一笔日志。
    /// </summary>
    private async Task LoadRecipes()
    {
        int version = ++_recipeVersion;
        try
        {
            var response = await _recipeService.GetListAsync(new RpcRequest());
            if (version != _recipeVersion)
            {
                return;
            }

            if (response.Success)
            {
                ProcessRecipes.Clear();
                foreach (var option in ProcessRecipeOptionModel.From(response.DeserializeData<ProcessRecipeListDto>()))
                {
                    ProcessRecipes.Add(option);
                }

                CanTypeRecipe = false;
            }
            else if (response.Code == ErrorCodes.ProcessRecipeNotInstalled)
            {
                ProcessRecipes.Clear();
                CanTypeRecipe = true;
            }
            else
            {
                ClientLog.Error(ModuleName, L10n.Get("chambermanual.recipes_failed", ReasonOf(response)));
            }
        }
        catch (Exception exception)
        {
            ClientLog.Error(ModuleName, L10n.Get("chambermanual.recipes_failed", exception.Message));
        }
    }

    private void OnStateReceived(ChamberDto dto)
    {
        Model.Update(dto);
    }

    /// <summary>部件推送：三维图、轴页签、气缸表各取各的。</summary>
    private void OnPartsReceived(ModulePartsDto dto)
    {
        Parts.Update(dto);
        SyncAxes(dto);
        SyncCylinders(dto);
    }

    /// <summary>
    /// 轴页签跟推送对齐：组成没变只刷新；变了按新的先后重排，已有的轴保留（页面上填的参数不丢），选中的轴还在就接着选它。
    /// </summary>
    private void SyncAxes(ModulePartsDto dto)
    {
        var parts = dto.Parts.Where(part => part.Kind == PartKinds.Axis).ToList();
        if (parts.Select(part => part.Path).SequenceEqual(Axes.Select(axis => axis.Path)))
        {
            for (int i = 0; i < parts.Count; i++)
            {
                Axes[i].Update(parts[i]);
            }

            return;
        }

        var existing = Axes.ToDictionary(axis => axis.Path, StringComparer.OrdinalIgnoreCase);
        string? selected = SelectedAxis?.Path;
        Axes.Clear();
        foreach (var part in parts)
        {
            if (existing.TryGetValue(part.Path, out var axis))
            {
                axis.Update(part);
            }
            else
            {
                axis = new AxisPartModel(ModuleName, part);
            }

            Axes.Add(axis);
        }

        SelectedAxis = Axes.FirstOrDefault(axis => string.Equals(axis.Path, selected, StringComparison.OrdinalIgnoreCase))
            ?? Axes.FirstOrDefault();
    }

    /// <summary>气缸表跟推送对齐：组成没变只刷新，变了重建。</summary>
    private void SyncCylinders(ModulePartsDto dto)
    {
        var parts = dto.Parts.Where(part => part.Kind == PartKinds.TwoState).ToList();
        if (parts.Select(part => part.Path).SequenceEqual(Cylinders.Select(row => row.Path)))
        {
            for (int i = 0; i < parts.Count; i++)
            {
                Cylinders[i].Update(parts[i]);
            }

            return;
        }

        Cylinders.Clear();
        foreach (var part in parts)
        {
            Cylinders.Add(new CylinderPartModel(ModuleName, part));
        }
    }

    private async Task DoProcess()
    {
        if (string.IsNullOrWhiteSpace(Recipe))
        {
            ClientLog.Error(ModuleName, L10n.Get("chamber.recipe_required", ModuleName));
            return;
        }

        // 同步等工艺做完才回包（上限是腔体的 EC ProcessTimeout），期间按钮保持不可用；要停就按 Abort。
        var response = await _service.ProcessAsync(new ChamberProcessRequest
        {
            Module = ModuleName,
            Recipe = Recipe.Trim(),
        });
        LogFailure(response, "chambermanual.process");
    }

    private async Task DoHome()
    {
        LogFailure(await _service.HomeAsync(ModuleName), "action.home");
    }

    private async Task DoAbort()
    {
        // 急停：可顶替在途动作（被顶的调用方收到 module.action_aborted）。
        LogFailure(await _service.AbortAsync(ModuleName), "action.abort");
    }

    private async Task DoReset()
    {
        // 复位：组件基类先清报警，模块再发设备复位清错。
        LogFailure(await _service.ResetAsync(ModuleName), "action.reset");
    }

    private async Task DoOnline()
    {
        LogFailure(await _service.OnlineAsync(ModuleName), "action.online");
    }

    private async Task DoOffline()
    {
        LogFailure(await _service.OfflineAsync(ModuleName), "action.offline");
    }

    #region 轴

    private async Task DoAxisHome()
    {
        var axis = SelectedAxis;
        if (axis is not null)
        {
            await RunPart(axis.Path, axis.Name, PartActionNames.Home, [], "action.home");
        }
    }

    /// <summary>移动：目标位置必填；移动速度空着就按后端的 EC MoveSpeed。同步等走到才回包。</summary>
    private async Task DoAxisMove()
    {
        var axis = SelectedAxis;
        if (axis is null)
        {
            return;
        }

        if (!TryNumber(axis.TargetPosition, out double target))
        {
            LogInputRequired(axis, "chambermanual.axis.target");
            return;
        }

        var args = new List<string> { Text(target) };
        if (TryNumber(axis.MoveSpeed, out double speed))
        {
            args.Add(Text(Math.Abs(speed)));
        }

        await RunPart(axis.Path, axis.Name, PartActionNames.MoveTo, args, "chambermanual.axis.move");
    }

    private async Task DoAxisStop()
    {
        var axis = SelectedAxis;
        if (axis is not null)
        {
            await RunPart(axis.Path, axis.Name, PartActionNames.Stop, [], "action.abort");
        }
    }

    private async Task DoAxisReset()
    {
        var axis = SelectedAxis;
        if (axis is not null)
        {
            await RunPart(axis.Path, axis.Name, PartActionNames.ResetDrive, [], "action.reset");
        }
    }

    /// <summary>步进：步距必填（取绝对值，方向看按钮）；点动速度空着就按后端的 EC MoveSpeed。</summary>
    private async Task DoAxisStep(string? direction)
    {
        var axis = SelectedAxis;
        if (axis is null)
        {
            return;
        }

        if (!TryNumber(axis.JogStep, out double step) || step == 0)
        {
            LogInputRequired(axis, "chambermanual.axis.step");
            return;
        }

        int sign = SignOf(direction);
        var args = new List<string> { Text(sign * Math.Abs(step)) };
        if (TryNumber(axis.JogSpeed, out double speed) && speed != 0)
        {
            args.Add(Text(Math.Abs(speed)));
        }

        await RunPart(axis.Path, axis.Name, PartActionNames.MoveBy, args,
            sign > 0 ? "chambermanual.axis.step_plus" : "chambermanual.axis.step_minus");
    }

    /// <summary>点动按下：点动速度必填（取绝对值，方向看按钮），发出去就回，之后续、松手停。</summary>
    private void DoJogPress(string? direction)
    {
        var axis = SelectedAxis;
        if (axis is null)
        {
            return;
        }

        if (!TryNumber(axis.JogSpeed, out double speed) || speed == 0)
        {
            LogInputRequired(axis, "chambermanual.axis.jog_speed");
            return;
        }

        int sign = SignOf(direction);
        _jogAxis = axis;
        _jogStart = StartJog(axis, sign * Math.Abs(speed), sign > 0 ? "chambermanual.axis.jog_plus" : "chambermanual.axis.jog_minus");
    }

    private async Task StartJog(AxisPartModel axis, double speed, string actionKey)
    {
        bool started = await RunPart(axis.Path, axis.Name, PartActionNames.Jog, [Text(speed)], actionKey);
        if (!started && ReferenceEquals(_jogAxis, axis))
        {
            _jogAxis = null;
        }
    }

    /// <summary>点动续：后台回"没在按住"（已经被停止、中止顶掉）就不再续。续不上后台过一会儿自己停。</summary>
    private void DoJogRenew(string? direction)
    {
        var axis = _jogAxis;
        if (axis is not null)
        {
            _ = RenewJog(axis);
        }
    }

    private async Task RenewJog(AxisPartModel axis)
    {
        try
        {
            var response = await _service.RenewPartActionAsync(new PartActionRequest
            {
                Module = ModuleName,
                Part = axis.Path,
                Action = PartActionNames.Jog,
            });
            if (!response.Success && ReferenceEquals(_jogAxis, axis))
            {
                _jogAxis = null;
            }
        }
        catch
        {
            // 续不上就不续了：后台在 EC HoldTimeoutMs 之后自己停。
        }
    }

    /// <summary>点动松手：先等点动请求回来，再对按下时那根轴发停止（点动没发出去也照发，停一下没坏处）。</summary>
    private void DoJogRelease(string? direction)
    {
        var axis = _jogAxis ?? SelectedAxis;
        var start = _jogStart;
        _jogAxis = null;
        _jogStart = null;
        if (axis is not null)
        {
            _ = StopJog(axis, start);
        }
    }

    private async Task StopJog(AxisPartModel axis, Task? start)
    {
        try
        {
            if (start is not null)
            {
                await start;
            }

            await RunPart(axis.Path, axis.Name, PartActionNames.Stop, [], "action.abort");
        }
        catch (Exception exception)
        {
            ClientLog.Error(ModuleName, L10n.Get("chambermanual.part_failed", axis.Name, L10n.Get("action.abort"), exception.Message));
        }
    }

    private void DoNextAxis()
    {
        if (Axes.Count == 0)
        {
            return;
        }

        int index = SelectedAxis is null ? -1 : Axes.IndexOf(SelectedAxis);
        SelectedAxis = Axes[(index + 1) % Axes.Count];
    }

    #endregion

    #region 气缸

    private async Task DoCylinder(CylinderPartModel? row, bool open)
    {
        if (row is null)
        {
            return;
        }

        await RunPart(row.Path, row.Name, open ? PartActionNames.Open : PartActionNames.Close, [],
            open ? "chambermanual.cylinder.up" : "chambermanual.cylinder.down");
    }

    #endregion

    /// <summary>
    /// 发部件动作：普通动作同步等部件做完才回包（上限是腔体的 EC PartActionTimeout），结果随部件推送刷到页面和三维图上；
    /// 停止、点动发出去就回。失败在顶栏日志里写"部件 动作 失败：原因"。返回成没成。
    /// </summary>
    private async Task<bool> RunPart(string path, string name, string action, IReadOnlyList<string> args, string actionKey)
    {
        var response = await _service.PartActionAsync(new PartActionRequest
        {
            Module = ModuleName,
            Part = path,
            Action = action,
            Args = [.. args],
        });
        if (!response.Success)
        {
            ClientLog.Error(ModuleName, L10n.Get("chambermanual.part_failed", name, L10n.Get(actionKey), ReasonOf(response)));
        }

        return response.Success;
    }

    private void LogInputRequired(AxisPartModel axis, string fieldKey)
    {
        ClientLog.Error(ModuleName, L10n.Get("chambermanual.input_required", axis.Name, L10n.Get(fieldKey)));
    }

    /// <summary>输入框里的数（InputTextBox 提交过的值，不变区域性）；空着或不是数返回 false。</summary>
    private static bool TryNumber(string text, out double value)
    {
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
    }

    /// <summary>参数按不变区域性写给后端（小数点是点号）。</summary>
    private static string Text(double value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>按钮参数 "-1" 是反向，其余正向。</summary>
    private static int SignOf(string? direction)
    {
        return direction == "-1" ? -1 : 1;
    }

    /// <summary>动作失败时在顶栏日志里写"××失败：原因"；动作名取语言包。</summary>
    private void LogFailure(RpcResponse response, string actionKey)
    {
        if (!response.Success)
        {
            ClientLog.Error(ModuleName, L10n.Get("chambermanual.action_failed", L10n.Get(actionKey), ReasonOf(response)));
        }
    }

    /// <summary>
    /// 失败原因：有错误码按语言包翻，老接口没码才用 Message。
    /// 部件动作的错误码第二个参数是动作名（组件上的方法名，如 MoveTo），先换成语言包里的叫法，中文界面不露英文方法名；
    /// 部件路径照 sc 原样显示，不翻。
    /// </summary>
    private static string ReasonOf(RpcResponse response)
    {
        if (string.IsNullOrEmpty(response.Code))
        {
            return response.Message;
        }

        var args = response.Args.ToList();
        if (PartActionCodes.Contains(response.Code) && args.Count > 1)
        {
            args[1] = PartActionNames.LabelOf(args[1]);
        }

        return L10n.Get(response.Code, args);
    }
}
