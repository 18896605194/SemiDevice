using System.Diagnostics;
using System.Globalization;
using xyz.Common.Log;
using xyz.Elements.Enums;
using xyz.Elements.Interfaces;
using xyz.Elements.Models;
using xyz.Drivers.Device;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;

namespace xyz.Elements.Elements;

/// <summary>
/// 模块的一个动作：按顺序走的一串有名字的步骤，由挂着它的模块（或搬运管理）的扫描线程每拍推一下
/// </summary>
public class ModuleRoutine
{
    #region 字段

    private readonly object _terminalGate = new();
    private readonly ManualResetEventSlim _settled = new(false);
    private readonly List<RoutineStep> _steps = [];

    /// <summary>
    /// 做这个动作的模块（日志记在它名下）
    /// </summary>
    private readonly ILogSource _logOwner;

    /// <summary>
    /// 做完的每一步各用了多久（ms），按先后；做成、失败、中止时写进日志
    /// </summary>
    private readonly List<long> _durations = [];

    /// <summary>
    /// 发指令的步骤默认用的通道
    /// </summary>
    private readonly ICommDriver? _driver;

    /// <summary>
    /// 发指令的步骤默认的超时；不发指令的动作为 null
    /// </summary>
    private Func<int>? _commandTimeoutMs;

    /// <summary>
    /// 整个动作的上限；没设为 null
    /// </summary>
    private Func<int>? _limitMs;

    /// <summary>
    /// 整个动作超时报的错误码
    /// </summary>
    private string _limitCode = ErrorCodes.Timeout;

    private volatile RoutineState _state = RoutineState.Running;
    private bool _completionDeferred;

    /// <summary>
    /// 走到第几步（从 0 数）
    /// </summary>
    private volatile int _index;

    /// <summary>
    /// 这一步的 Run 做过了没有
    /// </summary>
    private volatile bool _entered;

    /// <summary>
    /// 第一次推的时刻（Stopwatch 时间戳）；还没推为 -1
    /// </summary>
    private long _startedAt = -1;

    #endregion

    /// <summary>
    /// 建一个动作：owner 是做这个动作的模块（日志记在它名下），name 是动作名（日志、错误码参数用）
    /// </summary>
    public ModuleRoutine(ILogSource owner, string name)
    {
        _logOwner = owner;
        Module = owner.LogModule;
        if (owner is ElementBase element)
        {
            Module = element.Name;
        }

        Name = name;
    }

    /// <summary>
    /// 建一个发指令的动作：后面加的 Command 步骤经这个通道发、按这个超时等（UseTimeout 能换后面几步的超时）
    /// </summary>
    public ModuleRoutine(ILogSource owner, string name, ICommDriver? driver, Func<int> commandTimeoutMs)
        : this(owner, name)
    {
        _driver = driver;
        _commandTimeoutMs = commandTimeoutMs;
    }

    #region 结果（调用方看）

    /// <summary>
    /// 做这个动作的模块名（原因、错误码参数里显示）
    /// </summary>
    public string Module { get; }

    /// <summary>
    /// 日志记在谁名下（做这个动作的模块），子类自己记日志时用
    /// </summary>
    protected ILogSource LogOwner => _logOwner;

    /// <summary>
    /// 动作名，如 Home、Load
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 在走、做成、失败、被中止
    /// </summary>
    public RoutineState State => _state;

    /// <summary>
    /// 停下来了：做成、失败或被中止
    /// </summary>
    public bool IsTerminal => _state != RoutineState.Running;

    /// <summary>
    /// 做成了
    /// </summary>
    public bool IsSuccess => _state == RoutineState.Completed;

    /// <summary>
    /// 收完尾了：挂着它的模块（或搬运管理）落好状态以后才算，等结果的人这时候才醒
    /// </summary>
    public bool IsSettled => _settled.IsSet;

    /// <summary>
    /// 失败、中止的原因（只进日志），开头带停在第几步
    /// </summary>
    public string Reason { get; private set; } = string.Empty;

    /// <summary>
    /// 失败的错误码（界面按它查语言包）
    /// </summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>
    /// 错误码的参数
    /// </summary>
    public IReadOnlyList<string> ErrorArgs { get; private set; } = [];

    #endregion

    #region 进度

    /// <summary>
    /// 一共几步
    /// </summary>
    public int StepCount => _steps.Count;

    /// <summary>
    /// 走到第几步（从 1 数）；停下来以后是停在的那一步，做成了是最后一步；一步都没有为 0
    /// </summary>
    public int StepNumber
    {
        get
        {
            int count = _steps.Count;
            if (count == 0)
            {
                return 0;
            }

            return Math.Min(_index + 1, count);
        }
    }

    /// <summary>
    /// 当前这一步（停下来以后是停在的那一步）；一步都没有为 null
    /// </summary>
    public RoutineStep? CurrentStep
    {
        get
        {
            int number = StepNumber;
            if (number == 0)
            {
                return null;
            }

            return _steps[number - 1];
        }
    }

    /// <summary>
    /// 当前这一步的名字；一步都没有为空
    /// </summary>
    public string StepName
    {
        get
        {
            var step = CurrentStep;
            if (step is null)
            {
                return string.Empty;
            }

            return step.Name;
        }
    }

    /// <summary>
    /// 当前这一步手上的别家动作收完尾了：停下来以后，搬运管理据此放资源、唤醒等结果的人
    /// </summary>
    public bool IsChildSettled
    {
        get
        {
            var step = CurrentStep;
            return step is null || step.IsSettled;
        }
    }

    #endregion

    #region 建动作：一步一步加（开始推之前加完）

    /// <summary>
    /// 整个动作的上限：超了在当前这一步判失败，报 code [模块名, ms]；每拍现读，EC 改了下一拍就按新的算
    /// </summary>
    public ModuleRoutine LimitTo(Func<int> timeoutMs, string code)
    {
        _limitMs = timeoutMs;
        _limitCode = code;
        return this;
    }

    /// <summary>
    /// 后面加的 Command 步骤改用这个超时（比如回零后接着对准，对准那几条按对准的超时算）
    /// </summary>
    public ModuleRoutine UseTimeout(Func<int> timeoutMs)
    {
        _commandTimeoutMs = timeoutMs;
        return this;
    }

    /// <summary>
    /// 加一步：发一条驱动指令（轮到时新建），等设备回；设备回错、超时（先作废这一条）就失败，onSuccess 看回来的结果能不能用
    /// </summary>
    public ModuleRoutine Command<TCommand>(string name, Func<TCommand> create, Func<TCommand, HandleResult>? onSuccess = null)
        where TCommand : DeviceCommand
    {
        var timeout = _commandTimeoutMs;
        if (timeout is null)
        {
            throw new InvalidOperationException($"{Module} {Name}：要发指令的动作得用带通道和超时的构造建");
        }

        Func<DeviceCommand, HandleResult>? check = null;
        if (onSuccess is not null)
        {
            check = command => onSuccess((TCommand)command);
        }

        return Append(new CommandStep(name, _driver, () => create(), timeout, check));
    }

    /// <summary>
    /// 加一步：起一个别家的动作（站点准备、机械手扫片），等它收完尾；被拒、没做成原样带它的错误码
    /// </summary>
    public ModuleRoutine Start(string name, Func<HandleResult<ModuleRoutine>> start, Func<HandleResult>? onSuccess = null)
    {
        return Append(new ChildRoutineStep(name, start, onSuccess, null));
    }

    /// <summary>
    /// 加一步：起一个别家的动作、等它收完尾；没做成交给 onFailed 自己收场（在里面 step.Fail 成自己的错误码），没判就原样带它的错误码
    /// </summary>
    public ModuleRoutine Start(string name, Func<HandleResult<ModuleRoutine>> start, Action<RoutineStep, ModuleRoutine> onFailed)
    {
        return Append(new ChildRoutineStep(name, start, null, onFailed));
    }

    /// <summary>
    /// 加一步：当场做一下（检查、记账），返回不成功就失败
    /// </summary>
    public ModuleRoutine Do(string name, Func<HandleResult> run)
    {
        return Append(new DoStep(name, run));
    }

    /// <summary>
    /// 加一步：等条件成立；等满 timeoutMs 还没成立按 onTimeout 给的错误码失败（0 = 不等，判一次不成立就失败）
    /// </summary>
    public ModuleRoutine Until(string name, Func<bool> condition, Func<int> timeoutMs, Func<HandleResult> onTimeout)
    {
        return Append(new UntilStep(name, condition, timeoutMs, onTimeout));
    }

    /// <summary>
    /// 加一步：这几个设备同时发同一个动作（升降、开关阀、回零、停），都做完算这一步完成；一个设备都没有（machine.xml 里没配）就不加这一步
    /// </summary>
    public ModuleRoutine Devices<TDevice>(string name, IEnumerable<TDevice> devices, ChamberDeviceAction action)
        where TDevice : IDevice
    {
        var orders = new List<DeviceOrder>();
        foreach (var device in devices)
        {
            orders.Add(new DeviceOrder(device, action, () => DeviceActions.Send(device, action)));
        }

        return Devices(name, orders);
    }

    /// <summary>
    /// 加一步：一个设备发一个动作；设备为 null（machine.xml 里没配）就不加这一步
    /// </summary>
    public ModuleRoutine Devices(string name, IDevice? device, ChamberDeviceAction action)
    {
        if (device is null)
        {
            return this;
        }

        return Devices(name, new[] { device }, action);
    }

    /// <summary>
    /// 加一步：几个设备各发各的（位置、速度不一样的移动，先给流量再开阀），都做完算这一步完成；一条都没有就不加这一步
    /// </summary>
    public ModuleRoutine Devices(string name, IEnumerable<DeviceOrder> orders)
    {
        var list = orders.ToList();
        if (list.Count == 0)
        {
            return this;
        }

        return Append(new DeviceStep(name, list));
    }

    /// <summary>
    /// 加一步：只等这几个设备做完，指令别处已经发了（复位、中止时元件自己发的）；手上没有动作的（Idle）算做完。一个设备都没有就不加
    /// </summary>
    public ModuleRoutine Await<TDevice>(string name, IEnumerable<TDevice> devices, ChamberDeviceAction action)
        where TDevice : IDevice
    {
        var list = devices.Cast<IDevice>().ToList();
        if (list.Count == 0)
        {
            return this;
        }

        return Append(new AwaitDeviceStep(name, list, action, false, null));
    }

    /// <summary>
    /// 加一步：只等这个设备做完，指令是入口刚发的（手动页的设备动作）；回到 Idle 算被中止，等了超过 timeoutMs 判没做成
    /// </summary>
    public ModuleRoutine AwaitSent(string name, IDevice device, ChamberDeviceAction action, Func<int> timeoutMs)
    {
        return Append(new AwaitDeviceStep(name, [device], action, true, timeoutMs));
    }

    /// <summary>
    /// 加一步：自己写。run 进这一步做一次（可以为 null），check 之后每拍判一次、true = 这一步做完（为 null 就是做完 run 就算完）；出错在 step 上 Fail
    /// </summary>
    public ModuleRoutine Add(string name, Action<RoutineStep>? run, Func<RoutineStep, bool>? check)
    {
        return Append(new CustomStep(name, run, check));
    }

    /// <summary>
    /// 加一步自己写的步骤对象（有自己的字段要记的，比如搬运抢站点）
    /// </summary>
    public ModuleRoutine Add(RoutineStep step)
    {
        return Append(step);
    }

    private ModuleRoutine Append(RoutineStep step)
    {
        if (_startedAt >= 0)
        {
            throw new InvalidOperationException($"{Module} {Name} 已经开始走了，不能再加步骤");
        }

        step.Attach(this);
        _steps.Add(step);
        return this;
    }

    #endregion

    #region 推进（只在挂着它的模块或搬运管理的扫描线程里调）

    /// <summary>
    /// 推一拍：发当前这一步、判它；做完了当拍接着走下一步，碰到要等的就返回
    /// </summary>
    public void Scan()
    {
        if (IsTerminal)
        {
            return;
        }

        try
        {
            Advance();
        }
        catch (Exception exception)
        {
            Stop(RoutineState.Failed, ErrorCodes.OperationFaulted, $"异常：{exception.Message}", [Name, exception.Message]);
        }
    }

    private void Advance()
    {
        if (_startedAt < 0)
        {
            _startedAt = Stopwatch.GetTimestamp();
            if (_steps.Count > 0)
            {
                LogHelper.Debug(_logOwner, $"{Name} 开始：共 {_steps.Count} 步（{string.Join(" / ", _steps.Select(step => step.Name))}）");
            }
        }

        var limit = _limitMs;
        if (limit is not null)
        {
            int limitMs = limit();
            if (limitMs > 0 && TotalMs() > limitMs)
            {
                Stop(RoutineState.Failed, _limitCode, $"整个动作超过 {limitMs} ms 还没做完",
                    [Module, limitMs.ToString(CultureInfo.InvariantCulture)]);
                return;
            }
        }

        while (_index < _steps.Count)
        {
            var step = _steps[_index];
            if (!_entered)
            {
                _entered = true;
                step.Begin();
                step.Run();
            }

            // Run 里判了失败就不再判；判了没好就等下一拍；判的时候判了失败也停
            if (IsTerminal || !step.Check())
            {
                return;
            }

            if (IsTerminal)
            {
                return;
            }

            long used = step.ElapsedMs;
            _durations.Add(used);
            LogHelper.Debug(_logOwner, $"{Name} {_index + 1}/{_steps.Count} {step.Name}：做完 {used} ms");
            _entered = false;
            _index++;
        }

        Complete();
    }

    #endregion

    #region 结束

    /// <summary>
    /// 做成：全走完了（子类特殊情况也能直接调，比如冒烟的探针）
    /// </summary>
    protected void Complete()
    {
        if (!TrySetTerminal(RoutineState.Completed, string.Empty, string.Empty, []))
        {
            return;
        }

        if (_steps.Count == 0)
        {
            return;
        }

        long total = 0;
        if (_startedAt >= 0)
        {
            total = TotalMs();
        }

        if (_steps.Count == 1)
        {
            LogHelper.Info(_logOwner, $"{Name} 做完 {total} ms");
            return;
        }

        LogHelper.Info(_logOwner, $"{Name} 做完 {total} ms（{DoneSteps()}）");
    }

    /// <summary>
    /// 在步骤外判失败（子类特殊情况用，比如冒烟的探针）；正常在 RoutineStep 上判
    /// </summary>
    protected void Fail(string code, string reason, params string[] args)
    {
        Stop(RoutineState.Failed, code, reason, args);
    }

    /// <summary>
    /// 这一步判失败（RoutineStep.Fail 调）：整个动作停在这一步
    /// </summary>
    internal void FailAt(string code, string reason, IReadOnlyList<string> args)
    {
        Stop(RoutineState.Failed, code, reason, args);
    }

    /// <summary>
    /// 别的线程打断（急停顶替、搬运撤单）：直接停下，先让动作自己收场、再让正在走的这一步收场
    /// </summary>
    public void AbortByHost(string reason)
    {
        string where = Where();
        if (!TrySetTerminal(RoutineState.Aborted, $"停在 {where}：{reason}", ErrorCodes.Aborted, [Name]))
        {
            return;
        }

        if (_steps.Count > 0)
        {
            LogHelper.Warn(_logOwner, $"{Name} 中止在 {where}：{reason}{DoneSoFar()}");
        }

        try
        {
            OnAborted(reason);
            var step = CurrentStep;
            if (step is not null && _entered)
            {
                step.OnAborted(reason);
            }
        }
        catch (Exception exception)
        {
            // 收场出错不打断打断方
            LogHelper.Warn(_logOwner, $"{Name} 中止收场出错：{exception.Message}");
        }
    }

    /// <summary>
    /// 失败以后的收场（搬运：没动过手就撤站点）；子类按需重写
    /// </summary>
    protected virtual void OnFailed()
    {
    }

    /// <summary>
    /// 被中止以后的收场（搬运：机械手在动就发设备急停）；子类按需重写
    /// </summary>
    protected virtual void OnAborted(string reason)
    {
    }

    /// <summary>
    /// 不用做事、建好就算做成的动作（站点不用准备、没有设备的初始化）
    /// </summary>
    public static ModuleRoutine Done(ILogSource owner, string name)
    {
        var routine = new ModuleRoutine(owner, name);
        routine.TrySetTerminal(RoutineState.Completed, string.Empty, string.Empty, []);
        return routine;
    }

    private void Stop(RoutineState state, string code, string reason, IReadOnlyList<string> args)
    {
        string where = Where();
        if (!TrySetTerminal(state, $"{where}：{reason}", code, args))
        {
            return;
        }

        LogHelper.Error(_logOwner, $"{Name} 失败在 {where}：{reason}{StepDetail()}{DoneSoFar()}");
        try
        {
            OnFailed();
        }
        catch (Exception exception)
        {
            LogHelper.Warn(_logOwner, $"{Name} 失败收场出错：{exception.Message}");
        }
    }

    /// <summary>
    /// 从第一次推到现在多少 ms
    /// </summary>
    private long TotalMs()
    {
        return (long)Stopwatch.GetElapsedTime(_startedAt).TotalMilliseconds;
    }

    /// <summary>
    /// 停在哪：第几步 / 共几步 + 步骤名
    /// </summary>
    private string Where()
    {
        int count = _steps.Count;
        if (count == 0)
        {
            return "开始前";
        }

        return $"{StepNumber}/{count} {StepName}";
    }

    /// <summary>
    /// 这一步做了多久、在等什么
    /// </summary>
    private string StepDetail()
    {
        var step = CurrentStep;
        if (step is null || !_entered)
        {
            return string.Empty;
        }

        string waiting = step.Waiting;
        if (waiting.Length == 0)
        {
            return $"（这一步 {step.ElapsedMs} ms）";
        }

        return $"（这一步 {step.ElapsedMs} ms，在等 {waiting}）";
    }

    /// <summary>
    /// 前面做完的几步和各自用时
    /// </summary>
    private string DoneSoFar()
    {
        if (_durations.Count == 0)
        {
            return string.Empty;
        }

        return $"；前面做完：{DoneSteps()}";
    }

    private string DoneSteps()
    {
        var parts = new List<string>();
        for (int i = 0; i < _durations.Count && i < _steps.Count; i++)
        {
            parts.Add($"{_steps[i].Name} {_durations[i]}");
        }

        return string.Join(" / ", parts);
    }

    #endregion

    #region 等结果（RPC 线程用，扫描线程里不能等）

    /// <summary>
    /// 等它收完尾（阻塞，做完了唤醒，不空转）；false 只表示等超时
    /// </summary>
    public bool WaitReply(int timeoutMilliseconds, CancellationToken cancellationToken = default)
    {
        return _settled.Wait(timeoutMilliseconds, cancellationToken);
    }

    /// <summary>
    /// 挂到模块上时调：停下来以后先不唤醒等的人，等模块落好状态再 NotifyCompletion
    /// </summary>
    internal void DeferCompletion()
    {
        lock (_terminalGate)
        {
            if (IsTerminal || _completionDeferred)
            {
                throw new InvalidOperationException("动作只能挂一次，而且必须还在走。");
            }

            _completionDeferred = true;
        }
    }

    internal void NotifyCompletion()
    {
        _settled.Set();
    }

    private bool TrySetTerminal(RoutineState state, string reason, string code, IReadOnlyList<string> args)
    {
        lock (_terminalGate)
        {
            if (IsTerminal)
            {
                return false;
            }

            Reason = reason;
            Code = code;
            ErrorArgs = Array.AsReadOnly(args.ToArray());
            _state = state;
            if (!_completionDeferred)
            {
                NotifyCompletion();
            }

            return true;
        }
    }

    #endregion
}
