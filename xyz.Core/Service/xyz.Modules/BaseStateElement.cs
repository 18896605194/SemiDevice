using System.Globalization;
using xyz.Common.Log;
using xyz.Elements;
using xyz.Elements.Attributes;
using xyz.Elements.Elements;
using xyz.Elements.Enums;
using xyz.Elements.Interfaces;
using xyz.Modules.Enums;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;

namespace xyz.Modules;

/// <summary>
/// 带状态表、一次挂一个动作的元件：模块和整机都从它来
/// </summary>
public abstract class BaseStateElement : ElementBase, IRoutineHost
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

    /// <summary>
    /// 这个模块的状态迁移规则：平台的默认表在各模块基类的构造里装，机型在 AddTransitions 里加自己的
    /// </summary>
    protected StateTable States { get; }

    /// <summary>
    /// 元件初始化：部件、子元件都认完以后，给机型一次往状态表里加自己那几行的机会
    /// </summary>
    public override bool InitElement()
    {
        bool succeeded = base.InitElement();
        AddTransitions();
        return succeeded;
    }

    /// <summary>
    /// 机型往平台的默认表上加自己的动作（在里面调 States.Add）：这时候名字、machine.xml 参数、认好的部件都在了
    /// </summary>
    protected virtual void AddTransitions()
    {
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
    protected bool Attach(ModuleRoutine routine, bool replace = false)
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
                            // 已有动作在跑 → 挂不上

                    current.AbortByHost("被新动作顶替");
                }

                CompleteRoutine(current);
            }

            routine.DeferCompletion();
            _routine = routine;  //挂上动作
            return true;
        }
    }

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
    protected HandleResult? CheckState(string action)
    {
        lock (RoutineGate)
        {
            #region 能不能动作

            // 急停只看模块能不能用（停用、驱动没装好），不看设备连没连上：没连上也要能把在途的动作顶掉，免得它一直等到超时
            HandleResult? unavailable;
            if (action == ModuleAction.Abort)
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
            if (!TryGetTransition(state, action, out _))
            {
                return HandleResult.Fail(ErrorCodes.ModuleStateNotAllowed, Name, state.ToString(CultureInfo.InvariantCulture));
            }

            #endregion

            #region 有没有正在做的动作（急停可以顶替，不查）

            var current = _routine;
            if (current is not null && !current.IsTerminal && action != ModuleAction.Abort)
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
    protected HandleResult<ModuleRoutine> Begin(string action, ModuleRoutine routine)
    {
        lock (RoutineGate)
        {
            if (!TryGetTransition(State, action, out var transition))  //查询状态表
            {
                return HandleResult<ModuleRoutine>.Fail(ErrorCodes.ActionRejected, Name, State.ToString(CultureInfo.InvariantCulture));
            }

            int from = State;
            if (Begin(routine, transition.ExecutingState, transition.SuccessState, action == ModuleAction.Abort) is null)  //真正挂上动作
            {
                return HandleResult<ModuleRoutine>.Fail(ErrorCodes.ActionRejected, Name, State.ToString(CultureInfo.InvariantCulture));
            }

            CurrentAction = action;
            FromState = from;
            LogHelper.Info(this, $"开始 {action}");
            return HandleResult<ModuleRoutine>.Success(routine);
        }
    }

    /// <summary>
    /// 现在挂着的动作名；没挂为 null。机型自己加的动作名平台的 switch 认不出，收尾由机型自己做
    /// </summary>
    protected string? CurrentAction { get; private set; }

    /// <summary>
    /// 发起当前这个动作时模块在哪个状态（复位、中止的收尾按它判）
    /// </summary>
    protected int FromState { get; private set; }

    /// <summary>
    /// 发起一个动作，直接给执行态和成功态：挂动作 → 落执行态
    /// 交互环里的步骤这种本来就不是动作、准入已在入口查过的用它，不往状态表加行
    /// </summary>
    protected ModuleRoutine? Begin(ModuleRoutine routine, int executingState, int successState, bool replace = false)
    {
        lock (RoutineGate)
        {
            if (!CanBeginAction)
            {
                return null;
            }

            if (!Attach(routine, replace))
            {
                return null;
            }

            _transition = (executingState, successState);
            State = executingState;
            return routine;
        }
    }

    /// <summary>
    /// 把挂着的动作往前推一步，到终态就收尾；框架扫一拍时调（IRoutineHost），排在元件自己的 OnScan 之前
    /// </summary>
    public void StepRoutine()
    {
        lock (RoutineGate)
        {
            var routine = _routine;
            if (routine is null)
            {
                return;
            }

            //动作扫描
            routine.Step();

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

            OnRoutineCompleted(routine);   // 回调里还读得到 CurrentAction、FromState
        }
        finally
        {
            _routine = null;
            CurrentAction = null;
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
