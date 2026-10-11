using System.Globalization;
using xyz.Common.Log;
using xyz.Components.Components;
using xyz.Components;
using xyz.Components.Attributes;
using xyz.Components.Enums;
using xyz.Modules.Enums;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;

namespace xyz.Modules;


public abstract class BaseModule : ComponentBase
{
    public abstract int State { get; protected set; }

    [VariableMark(VariableType.SV, ValueFormat.Enum, description: "模块模式（Online/Offline）")]
    public ModuleMode Mode { get; private set; } = ModuleMode.Offline;

    [VariableMark(VariableType.EC, ValueFormat.Int, unit: "ms", min: "10", max: "5000",
        @default: "200", description: "单周期慢扫描警告阈值")]
    public int SlowScanWarnMs
    {
        get { return GetEcInt(nameof(SlowScanWarnMs)); }
        set { SetEcInt(nameof(SlowScanWarnMs), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, unit: "ms", min: "10", max: "5000",
        @default: "300", description: "单周期慢扫描报警阈值")]
    public int SlowScanAlarmMs
    {
        get { return GetEcInt(nameof(SlowScanAlarmMs)); }
        set { SetEcInt(nameof(SlowScanAlarmMs), value); }
    }

    protected override int SlowScanWarnMilliseconds => SlowScanWarnMs;

    protected override int SlowScanAlarmMilliseconds => SlowScanAlarmMs;

    public virtual ModuleOperation? InitModule()
    {
        return null;
    }

    public void Online()
    {
        SetMode(ModuleMode.Online);
    }

    /// <summary>
    /// 下线：退出自动调度。
    /// </summary>
    public void Offline()
    {
        SetMode(ModuleMode.Offline);
    }

    private void SetMode(ModuleMode mode)
    {
        if (Mode == mode)
        {
            return;
        }

        Mode = mode;
        LogHelper.Info($"[{Name}] {mode}");
    }


    #region 状态迁移表

    private readonly Dictionary<(int? State, string Action), (int ExecutingState, int SuccessState)> _transitions = new();

    protected void RegisterTransitions(
        IEnumerable<KeyValuePair<(int? State, string Action), (int ExecutingState, int SuccessState)>> entries)
    {
        _transitions.Clear();
        foreach (var entry in entries)
        {
            _transitions[entry.Key] = entry.Value;
        }
    }

    /// <summary>
    /// 单条追加/覆盖迁移（机型在注册的默认表上定制用）。
    /// </summary>
    protected void AddTransition(
        (int? State, string Action) key, (int ExecutingState, int SuccessState) transition)
    {
        _transitions[key] = transition;
    }

    /// <summary>
    /// 动作发起前查表：先按当前状态精确匹配，未命中再查 null 通配；都不中返回 false（状态不允许）。
    /// </summary>
    protected bool TryGetTransition(int state, string action, out (int ExecutingState, int SuccessState) transition)
    {
        if (_transitions.TryGetValue((state, action), out transition))
        {
            return true;
        }

        return _transitions.TryGetValue((null, action), out transition);
    }

    #endregion

    #region 操作挂载（单动作，扫描线程统一步进）

    protected object OperationGate { get; } = new();
    private ModuleOperation? _operation;

    /// <summary>
    /// 当前挂载的操作；无则 null。
    /// </summary>
    public ModuleOperation? CurrentOperation
    {
        get
        {
            lock (OperationGate)
            {
                return _operation;
            }
        }
    }

    /// <summary>
    /// 挂载一个操作（单动作规则：已有在途操作时拒绝，replace=true 顶替并把旧的打断）。
    /// 挂载后由本模块扫描线程每周期自动步进，模块无需为此重写 OnScan。
    /// </summary>
    protected bool Run(ModuleOperation operation, bool replace = false)
    {
        lock (OperationGate)
        {
            var current = _operation;
            if (current is not null)
            {
                if (!current.IsTerminal)
                {
                    if (!replace)
                    {
                        return false;
                    }

                    current.AbortByHost("被新操作顶替");
                }

                CompleteOperation(current);
            }

            operation.DeferCompletion();
            _operation = operation;
            return true;
        }
    }

    /// <summary>
    /// 急停动作名：三类模块的动作枚举里急停都叫 Abort，只有它能顶替在途动作。
    /// </summary>
    private const string AbortAction = "Abort";

    private (int ExecutingState, int SuccessState) _transition;

    /// <summary>
    /// 动作能不能发；默认没限制。
    /// 装机停用、驱动没建起来的模块重写成 false——装配里组件初始化没做成就不该还能发指令。
    /// </summary>
    protected virtual bool CanBeginAction => true;

    #region 动作检查：状态 → 资源 → 互锁

    // 每个动作的入口在模块锁里按这个先后查，哪项不过就带原因返回、模块不动，都过了才 Begin：
    //   状态：只看模块自己（能不能动作、状态表允不允许、有没有正在做的动作），这里写好，各动作共用；
    //   资源：这个动作要独占的东西有没有被别人占着（晶圆账、搬运占用、Job 归属），写在模块里；
    //   互锁：周围条件允不允许动（传感器、别的模块、搬运车），平台给默认、机型重写先调 base。
    // 只跟"谁发的命令"有关的检查（EAP 要 ON-LINE REMOTE 这类）不放这里，留在发命令的那一层。

    /// <summary>
    /// 能不能动作：默认看 <see cref="CanBeginAction"/>（停用、驱动没装好）。
    /// 有驱动的模块重写，再加上"设备没连上"——没连上照样发，指令发不出去会落 Error、报警。不通过返回原因，通过返回 null。
    /// </summary>
    protected virtual HandleResult? CheckAvailable()
    {
        if (!CanBeginAction)
        {
            return HandleResult.Fail(ErrorCodes.ModuleDisabled, Name);
        }

        return null;
    }

    /// <summary>
    /// 状态检查（三项检查的第一项）：能不能动作 → 状态表允不允许 → 有没有正在做的动作（急停可以顶替，不查）。
    /// 跟 <see cref="Begin{TAction}"/> 查的是同一套，只是带上原因。不通过返回原因，通过返回 null。
    /// </summary>
    protected HandleResult? CheckState<TAction>(TAction action)
        where TAction : struct, Enum
    {
        lock (OperationGate)
        {
            var unavailable = CheckAvailable();
            if (unavailable is not null)
            {
                return unavailable;
            }

            string name = action.ToString();
            int state = State;
            if (!TryGetTransition(state, name, out _))
            {
                return HandleResult.Fail(ErrorCodes.ModuleStateNotAllowed, Name, state.ToString(CultureInfo.InvariantCulture));
            }

            var current = _operation;
            if (current is not null && !current.IsTerminal && name != AbortAction)
            {
                return HandleResult.Fail(ErrorCodes.ModuleBusy, Name);
            }

            return null;
        }
    }

    #endregion

    /// <summary>
    /// 发起一个动作：查迁移表 → 挂操作 → 落执行态
    /// </summary>
    /// <typeparam name="TAction"></typeparam>
    /// <param name="action"></param>
    /// <param name="operation"></param>
    /// <returns></returns>
    protected ModuleOperation? Begin<TAction>(TAction action, ModuleOperation operation)
        where TAction : struct, Enum
    {
        lock (OperationGate)
        {
            if (!CanBeginAction)
            {
                return null;
            }

            string name = action.ToString();
            if (!TryGetTransition(State, name, out var transition))
            {
                return null;
            }

            if (!Run(operation, replace: name == AbortAction))
            {
                return null;
            }

            _transition = transition;
            State = transition.ExecutingState;
            return operation;
        }
    }

    /// <summary>
    /// 扫描周期：先扫子组件，再步进挂载的操作。
    /// 子类只在有自己的周期逻辑时才重写（记得调 base.OnScan）。
    /// </summary>
    protected override void OnScan()
    {
        base.OnScan();

        lock (OperationGate)
        {
            var operation = _operation;
            if (operation is null)
            {
                return;
            }

            //动作扫描
            operation.Scan();

            if (operation.IsTerminal)
            {
                CompleteOperation(operation);
            }
        }
    }

    private void CompleteOperation(ModuleOperation operation)
    {
        try
        {
            // 先落状态：成功走迁移表的成功态，失败/被打断落 Error。回调里看到的已经是终态。
            State = operation.IsSuccess ? _transition.SuccessState : ModuleState.Error;
            OnOperationCompleted(operation);
        }
        finally
        {
            _operation = null;
            operation.NotifyCompletion();
        }
    }

    /// <summary>
    /// 操作终结回调；状态已落好，模块在这儿做自己的收尾（报警、晶圆账、EAP 回调等）。
    /// </summary>
    protected virtual void OnOperationCompleted(ModuleOperation operation)
    {
    }

    #endregion

    /// <summary>
    /// 发布本模块状态；子类按自己的 DTO 实现，默认不发。
    /// 扫描周期会调，状态环改完状态也会立即调（不等下一拍）。
    /// </summary>
    protected virtual void PublishState()
    {
    }
}
