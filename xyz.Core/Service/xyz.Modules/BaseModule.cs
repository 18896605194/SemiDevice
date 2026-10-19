using System.Globalization;
using xyz.Common.Log;
using xyz.Components.Components;
using xyz.Components;
using xyz.Components.Attributes;
using xyz.Components.Enums;
using xyz.Components.Interfaces;
using xyz.Modules.Enums;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;

namespace xyz.Modules;


public abstract class BaseModule : ComponentBase, IModule
{
    public abstract int State { get; protected set; }

    [VariableMark(VariableType.SV, ValueFormat.Enum, description: "模块模式（Online/Offline）")]
    public ModuleMode Mode { get; private set; } = ModuleMode.Offline;

    /// <summary>
    /// 模块是哪一类：LoadPort、腔体、buffer、机械手的基类各自重写，别的模块默认其他
    /// </summary>
    public virtual StationKind Kind => StationKind.Other;

    /// <summary>
    /// 站点的类型（sc.xml 里写成 <c>&lt;Value Name="Type" Value="A" /&gt;</c>，不是节点上写类名的那个 Type 属性）：
    /// 几台一样的站点写同一个值（比如 8 个腔体 1~4 写 A、5~8 写 B），工艺配方按它定给哪种站点用。空 = 不分类型。
    /// </summary>
    [SCEditor("", "Module", "站点类型：几台一样的写同一个值（比如腔体 1~4 写 A、5~8 写 B），工艺配方按它挑能跑的站点；空 = 不分类型")]
    public string Type { get; set; } = string.Empty;

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

    /// <summary>
    /// 复位：子组件跟着复位、清自己的报警；模块有自己的复位动作就重写
    /// </summary>
    public override ModuleOperation? Reset()
    {
        base.Reset();
        return null;
    }

    /// <summary>
    /// 中止：子组件跟着中止；模块有自己的中止动作就重写
    /// </summary>
    public override ModuleOperation? Abort()
    {
        base.Abort();
        return null;
    }

    /// <summary>
    /// 上线：参与自动调度。只改模块模式 Mode、不经设备；三类模块的手动页上线按钮都走这里。
    /// 机型要在上线时多做事（比如先查设备能不能用）就重写，记得调 base。
    /// </summary>
    public virtual void Online()
    {
        SetMode(ModuleMode.Online);
    }

    /// <summary>
    /// 下线：退出自动调度。只改模块模式 Mode、不经设备；重写同 <see cref="Online"/>。
    /// </summary>
    public virtual void Offline()
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
    /// 动作能不能发
    /// </summary>
    protected virtual bool CanBeginAction => true;

    #region 动作检查：状态 → 资源 → 互锁

    
    protected virtual HandleResult? CheckAvailable()
    {
        return CheckEnabled();
    }

    /// <summary>
    /// 模块停用
    /// </summary>
    /// <returns></returns>
    private HandleResult? CheckEnabled()
    {
        if (!CanBeginAction)
        {
            return HandleResult.Fail(ErrorCodes.ModuleDisabled, Name);
        }

        return null;
    }

    /// <summary>
    /// 状态检查
    /// </summary>
    /// <typeparam name="TAction"></typeparam>
    /// <param name="action"></param>
    /// <returns></returns>
    protected HandleResult? CheckState<TAction>(TAction action)
        where TAction : struct, Enum
    {
        lock (OperationGate)
        {
            string name = action.ToString();

            #region 能不能动作

            // 急停只看模块能不能用（停用、驱动没装好），不看设备连没连上：没连上也要能把在途的动作顶掉，免得它一直等到超时
            var unavailable = name == AbortAction ? CheckEnabled() : CheckAvailable();
            if (unavailable is not null)
            {
                return unavailable;
            }

            #endregion

            #region 状态表允不允许

            int state = State;
            if (!TryGetTransition(state, name, out _))
            {
                return HandleResult.Fail(ErrorCodes.ModuleStateNotAllowed, Name, state.ToString(CultureInfo.InvariantCulture));
            }

            #endregion

            #region 有没有正在做的动作（急停可以顶替，不查）

            var current = _operation;
            if (current is not null && !current.IsTerminal && name != AbortAction)
            {
                return HandleResult.Fail(ErrorCodes.ModuleBusy, Name);
            }

            #endregion

            return null;
        }
    }

    /// <summary>
    /// 检查没过：把原因（错误码 + 参数）原样转成动作入口的结果
    /// </summary>
    /// <param name="rejected"></param>
    /// <returns></returns>
    protected static HandleResult<ModuleOperation> Rejected(HandleResult rejected)
    {
        return HandleResult<ModuleOperation>.Fail(rejected.ErrorMessage, [.. rejected.Args]);
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
            operation.NotifyCompletion();   //通知操作完成
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
