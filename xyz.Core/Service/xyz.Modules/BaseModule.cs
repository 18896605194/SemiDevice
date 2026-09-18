using xyz.Common.Log;
using xyz.Components;
using xyz.Components.Attributes;
using xyz.Components.Enums;
using xyz.Shared.Dtos;

namespace xyz.Modules;


public abstract class BaseModule : ComponentBase
{
    public virtual bool Open()
    {
        return true;
    }

    #region 模块模式

    /// <summary>
    /// 模块模式（SV）：是否参与自动调度。Online()/Offline() 只改它，不动设备；默认 Offline，掉电不保持。
    /// </summary>
    [VariableMark(VariableType.SV, ValueFormat.Enum, description: "模块模式（Online/Offline）")]
    public ModuleMode Mode { get; private set; } = ModuleMode.Offline;

    /// <summary>
    /// 上线：参与自动调度。
    /// </summary>
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

    #endregion

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

    #region EC 在线参数（扫描）

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

    /// <summary>
    /// 慢扫描警告阈值（组件基类的口子，转发到 EC 属性，在线改完即生效）。
    /// </summary>
    protected override int SlowScanWarnMilliseconds => SlowScanWarnMs;

    /// <summary>
    /// 慢扫描报警阈值（组件基类的口子，转发到 EC 属性，在线改完即生效）。
    /// </summary>
    protected override int SlowScanAlarmMilliseconds => SlowScanAlarmMs;

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
            if (_operation is { } current)
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
            OnOperationCompleted(operation);
        }
        finally
        {
            _operation = null;
            operation.NotifyCompletion();
        }
    }

    /// <summary>
    /// 操作终结回调；需要处理结果的模块重写（如按状态迁移表落状态）。
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
