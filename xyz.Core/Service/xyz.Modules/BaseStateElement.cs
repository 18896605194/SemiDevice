using System.Globalization;
using xyz.Elements;
using xyz.Elements.Attributes;
using xyz.Elements.Elements;
using xyz.Elements.Enums;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;

namespace xyz.Modules;

/// <summary>
/// 带状态表、一次挂一个动作的元件：模块和整机都从它来
/// </summary>
public abstract class BaseStateElement : ElementBase
{
    public abstract int State { get; protected set; }

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

    #region 挂动作（一次一个，扫描线程统一推）

    protected object RoutineGate { get; } = new();
    private ModuleRoutine? _routine;

    /// <summary>
    /// 当前挂着的动作；没有为 null
    /// </summary>
    public ModuleRoutine? CurrentRoutine
    {
        get
        {
            lock (RoutineGate)
            {
                return _routine;
            }
        }
    }

    /// <summary>
    /// 挂上一个动作（一次一个：已有在途的就拒，replace=true 顶替并把旧的打断）
    /// 挂载后由本模块扫描线程每周期自动步进，模块无需为此重写 OnScan。
    /// </summary>
    protected bool Run(ModuleRoutine routine, bool replace = false)
    {
        lock (RoutineGate)
        {
            var current = _routine;
            if (current is not null)
            {
                if (!current.IsTerminal)
                {
                    if (!replace)
                    {
                        return false;
                    }

                    current.AbortByHost("被新动作顶替");
                }

                CompleteRoutine(current);
            }

            routine.DeferCompletion();
            _routine = routine;
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
        lock (RoutineGate)
        {
            string name = action.ToString();

            #region 能不能动作

            // 急停只看模块能不能用（停用、驱动没装好），不看设备连没连上：没连上也要能把在途的动作顶掉，免得它一直等到超时
            HandleResult? unavailable;
            if (name == AbortAction)
            {
                unavailable = CheckEnabled();
            }
            else
            {
                unavailable = CheckAvailable();
            }

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

            var current = _routine;
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
    protected static HandleResult<ModuleRoutine> Rejected(HandleResult rejected)
    {
        return HandleResult<ModuleRoutine>.Fail(rejected.ErrorMessage, [.. rejected.Args]);
    }

    #endregion

    /// <summary>
    /// 发起一个动作：查迁移表 → 挂动作 → 落执行态
    /// </summary>
    /// <typeparam name="TAction"></typeparam>
    /// <param name="action"></param>
    /// <param name="routine"></param>
    /// <returns></returns>
    protected ModuleRoutine? Begin<TAction>(TAction action, ModuleRoutine routine)
        where TAction : struct, Enum
    {
        lock (RoutineGate)
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

            if (!Run(routine, replace: name == AbortAction))
            {
                return null;
            }

            _transition = transition;
            State = transition.ExecutingState;
            return routine;
        }
    }

    /// <summary>
    /// 扫描周期：先扫子元件，再推一下挂着的动作
    /// 子类只在有自己的周期逻辑时才重写（记得调 base.OnScan）。
    /// </summary>
    protected override void OnScan()
    {
        base.OnScan();

        lock (RoutineGate)
        {
            var routine = _routine;
            if (routine is null)
            {
                return;
            }

            //动作扫描
            routine.Scan();

            if (routine.IsTerminal)
            {
                CompleteRoutine(routine);
            }
        }
    }

    private void CompleteRoutine(ModuleRoutine routine)
    {
        try
        {
            // 先落状态：成功走迁移表的成功态，失败/被打断落 Error。回调里看到的已经是终态。
            if (routine.IsSuccess)
            {
                State = _transition.SuccessState;
            }
            else
            {
                State = ModuleState.Error;
            }

            OnRoutineCompleted(routine);
        }
        finally
        {
            _routine = null;
            routine.NotifyCompletion();   // 唤醒等结果的
        }
    }

    /// <summary>
    /// 动作停下以后的回调：状态已落好，模块在这儿做自己的收尾（报警、晶圆账、EAP 回调等）。
    /// </summary>
    protected virtual void OnRoutineCompleted(ModuleRoutine routine)
    {
    }

    #endregion

    protected virtual void PublishState()
    {
    }
}
