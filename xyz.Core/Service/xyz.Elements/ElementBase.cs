using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using xyz.Common.Log;
using xyz.Elements.Attributes;
using xyz.Elements.Elements;
using xyz.Elements.Enums;
using xyz.Elements.Interfaces;
using xyz.Configs.Models;
using xyz.Tools;

namespace xyz.Elements;

/// <summary>
/// 元件基类
/// </summary>
public abstract class ElementBase : ILogSource
{
    #region Column

    /// <summary>
    /// 元件名，例如DISensorElement
    /// </summary>
    public string Name { get; internal set; } = string.Empty;
    /// <summary>
    /// 本级名称的别名，等于 Name。
    /// 示例：Name = "Chamber1"，Path = "Chamber1"
    /// </summary>
    public string Path => Name;

    /// <summary>
    /// 父元件给子元件声明的角色，比如 Valve、Sensor。
    /// 示例：Role = "Valve"
    /// </summary>
    public string? Role { get; internal set; }

    /// <summary>
    /// 完整层级路径，machine.xml 的分组节点（不写 Type 的 LoadPort、Chamber）也算上，比如 Chamber.Chamber1.Door。
    /// </summary>
    public string FullPath { get; internal set; } = string.Empty;

    private string _logModule = string.Empty;

    /// <summary>
    /// 日志第一层：树上最顶上那个元件的全路径（装配时算好；不是装配出来的元件用自己的名字）
    /// </summary>
    public string LogModule
    {
        get
        {
            if (_logModule.Length > 0)
            {
                return _logModule;
            }

            return Name;
        }
    }

    /// <summary>
    /// 日志第二层：从第一层往下剩的路径；最顶上那个元件自己为空
    /// </summary>
    public string LogPart { get; private set; } = string.Empty;

    /// <summary>
    /// 装配时定日志的两层名字：modulePath 是这棵树最顶上那个元件的全路径
    /// </summary>
    internal void AssignLogNames(string modulePath)
    {
        _logModule = modulePath;
        LogPart = string.Empty;
        if (FullPath.Length > modulePath.Length && FullPath.StartsWith(modulePath + ".", StringComparison.Ordinal))
        {
            LogPart = FullPath.Substring(modulePath.Length + 1);
        }
    }

    /// <summary>
    /// 初始化顺序，默认 10000，越小越先（InitElement 时同一层的子元件按它依次初始化；machine.xml 节点的 InitOrder 属性配）。
    /// </summary>
    public int InitOrder { get; internal set; } = 10000;

    /// <summary>
    /// 是否启用，false 不参与扫描。
    /// </summary>
    public bool IsEnabled { get; internal set; } = true;

    /// <summary>
    /// 根元件是否由装配入口启动独立扫描线程；子元件随父元件扫描，不单独启动。
    /// </summary>
    [Machine("false", "Element", "根元件是否启动独立扫描线程；子元件跟随父元件扫描，无需开启")]
    public bool IsRun { get; set; }

    /// <summary>
    /// 子元件的集合
    /// </summary>
    private readonly List<ElementBase> _children = new();
    public IReadOnlyList<ElementBase> Children => _children;

    #endregion

    /// <summary>
    /// 添加子元件
    /// </summary>
    /// <param name="child"></param>
    public void AddChild(ElementBase child)
    {
        _children.Add(child);
    }

    #region 查找子元件

    /// <summary>
    /// 按名字在子元件树（含各级后代，不含自己）中查找，忽略大小写；找不到返回 null。
    /// </summary>
    public ElementBase? FindChild(string name)
    {
        foreach (var child in _children)
        {
            if (string.Equals(child.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return child;
            }

            var found = child.FindChild(name);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>
    /// 按名字 + 类型在子元件树（含各级后代，不含自己）中查找，忽略大小写；
    /// 名字命中但类型不符（或找不到）返回 null。T 可为具体元件类（含派生）或接口。
    /// </summary>
    public T? FindChild<T>(string name) where T : class
    {
        return FindChild(name) as T;
    }

    /// <summary>
    /// 报过"配了好几个"的：哪个元件找哪一类
    /// </summary>
    private static readonly ConcurrentDictionary<(ElementBase Owner, Type Type), bool> ReportedDuplicates = new();

    /// <summary>
    /// 按类型在子元件树（含各级后代，不含自己）里找唯一的那一个：没有返回 null；配了好几个用排在最前的，记一条错误日志
    /// </summary>
    public T? FindChild<T>() where T : class
    {
        var found = FindChildren<T>();
        if (found.Count == 0)
        {
            return null;
        }

        // 只该有一个（载具、读头、E84 这类），对上好几个是 machine.xml 配多了；有的每拍都找（LoadPort 的 E84），同一处只报一次，免得刷满日志
        if (found.Count > 1 && ReportedDuplicates.TryAdd((this, typeof(T)), true))
        {
            string paths = string.Join("、", found.OfType<ElementBase>().Select(element => element.FullPath));
            LogHelper.Error(this, $"下面配了 {found.Count} 个 {typeof(T).Name}，只该有一个，用排在最前的：{paths}");
        }

        return found[0];
    }

    /// <summary>
    /// 按类型或接口在子元件树（含各级后代，不含自己）中查找全部匹配项（含派生类/实现类）。
    /// </summary>
    public IReadOnlyList<T> FindChildren<T>() where T : class
    {
        var result = new List<T>();
        CollectChildren(this, result);
        return result;
    }

    private static void CollectChildren<T>(ElementBase element, List<T> result) where T : class
    {
        foreach (var child in element._children)
        {
            if (child is T matched)
            {
                result.Add(matched);
            }

            CollectChildren(child, result);
        }
    }

    /// <summary>
    /// 按名字找 DI 子节点（到位反馈、液位信号这类）；没挂、或者 DiIndex 配成 -1 都当没有，返回 null
    /// </summary>
    protected IDiSensor? FindSignal(string name)
    {
        var signal = FindChild<IDiSensor>(name);
        if (signal is null || !signal.IsConfigured)
        {
            return null;
        }

        return signal;
    }

    #endregion

    #region 开启、停止轮询 间隔10ms
    /// <summary>
    /// 启动元件树的扫描线程。只有模块根元件调用；
    /// 子元件随父元件在同一线程上被 OnScan 递归扫描，不要对子元件单独调用 Start。
    /// </summary>
    public void Start()
    {
        if (Interlocked.CompareExchange(ref _scanStarted, 1, 0) != 0)
        {
            return;
        }

        _scanTask = Task.Factory.StartNew(ScanLoop, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    /// <summary>
    /// 停扫描：等手上这一拍扫完再回来（最多等 StopScanWaitMs）；宿主退出时先停扫描、再让各元件收尾
    /// </summary>
    public void StopScan()
    {
        _scanStopping = true;

        // 没起过扫描，或者是在自己的扫描里停自己：不等，这一拍扫完循环自己出来
        if (_scanTask is null || Environment.CurrentManagedThreadId == _scanThreadId)
        {
            return;
        }

        if (!_scanTask.Wait(StopScanWaitMs))
        {
            LogHelper.Warn(this, $"扫描 {StopScanWaitMs}ms 内没停下来，不等了");
        }
    }

    private int _scanStarted;

    /// <summary>
    /// 扫描线程（Start 里起），停扫描时等它扫完手上这一拍
    /// </summary>
    private Task? _scanTask;

    /// <summary>
    /// 停扫描的标记：扫描循环每拍看一眼
    /// </summary>
    private volatile bool _scanStopping;

    /// <summary>
    /// 扫描线程的编号：在自己的扫描里停自己时不能等自己
    /// </summary>
    private int _scanThreadId;

    /// <summary>
    /// 停扫描最多等多久（毫秒）：一拍正常几毫秒、慢扫描报警线 300ms，等 2 秒够了；卡死的不等，免得宿主退不掉
    /// </summary>
    private const int StopScanWaitMs = 2000;

    /// <summary>
    /// 扫描周期（毫秒）：扫完一拍歇这么久再扫下一拍
    /// </summary>
    private const int ScanIntervalMs = 10;

    /// <summary>
    /// 单周期慢扫描警告阈值（毫秒），默认 200；超过记警告日志。
    /// </summary>
    protected virtual int SlowScanWarnMilliseconds => 200;

    /// <summary>
    /// 单周期慢扫描报警阈值（毫秒），默认 300；超过按报警级别处理（Error 日志，
    /// 接入报警系统后在此 Raise）。
    /// </summary>
    protected virtual int SlowScanAlarmMilliseconds => 300;

    private void ScanLoop()
    {
        _scanThreadId = Environment.CurrentManagedThreadId;
        while (!_scanStopping)
        {
            ScanOnce();
            Thread.Sleep(ScanIntervalMs);
        }
    }

    /// <summary>
    /// 扫一拍：这一拍抛的异常记日志、不往外抛（单周期异常不杀扫描线程），超过慢扫描阈值记日志。
    /// </summary>
    private void ScanOnce()
    {
        var watch = Stopwatch.StartNew();
        try
        {
            OnScan();
        }
        catch (Exception exception)
        {
            // 单周期异常不杀扫描线程：记录后继续下一周期。
            LogHelper.Warn(this, $"OnScan 异常: {exception.Message}");
        }

        var elapsed = watch.ElapsedMilliseconds;
        if (elapsed > SlowScanAlarmMilliseconds)
        {
            LogHelper.Error(this, $"慢扫描报警 {elapsed}ms（阈值 {SlowScanAlarmMilliseconds}ms）");
        }
        else if (elapsed > SlowScanWarnMilliseconds)
        {
            LogHelper.Warn(this, $"慢扫描警告 {elapsed}ms（阈值 {SlowScanWarnMilliseconds}ms）");
        }
    }

    /// <summary>
    /// 扫描周期，默认递归扫描子元件；子类重写时调用 base.OnScan() 后追加自己的周期逻辑。
    /// </summary>
    protected virtual void OnScan()
    {
        foreach (var child in _children)
        {
            child.OnScan();
        }
    }

    /// <summary>
    /// 单独扫一个子元件：它这一拍抛的异常、扫得慢都记在它自己名下，不往上抛，后面的子元件照扫。
    /// 默认的 OnScan 是直接递归，一个子元件抛异常，这一拍排在它后面的就都不扫了；
    /// 子元件之间要互不耽误的父元件（如 Dispatch 下的 Job 和搬运管理）重写 OnScan、逐个调它。
    /// </summary>
    protected void ScanChild(ElementBase child)
    {
        child.ScanOnce();
    }

    #endregion

    #region 初始化、中止与复位（都不是必须重写的：元件有自己的处理才重写，记得调 base）

    public virtual bool InitElement()
    {
        bool succeeded = true;
        foreach (var child in _children.OrderBy(child => child.InitOrder))
        {
            if (!child.InitElement())
            {
                succeeded = false;
            }
        }

        return succeeded;
    }

    /// <summary>
    /// 开机准备都做完、开始扫描之前调：要用别的元件的在这里找
    /// </summary>
    public virtual void Bind()
    {
    }

    /// <summary>
    /// 开始扫描以后调：要等设备侧都跑起来才做的事
    /// </summary>
    public virtual void OnStarted()
    {
    }

    /// <summary>
    /// 开机推送：都起来以后调一次，界面一连上（含重连）就要拿到的在这里推（留存）
    /// </summary>
    public virtual void PublishOnStartup()
    {
    }

    /// <summary>
    /// 宿主退出时调（按开机的倒序）：断链路、落库、存盘这类收尾
    /// </summary>
    public virtual void OnStopping()
    {
    }

    public virtual object? Abort()
    {
        foreach (var child in _children)
        {
            child.Abort();
        }

        return null;
    }

    public virtual object? Reset()
    {
        foreach (var child in _children)
        {
            child.Reset();
        }

        IocHelper.GetService<IAlarm>()?.Clear(this);
        return null;
    }

    #endregion

    #region 动作状态

    /// <summary>
    /// 当前动作的状态，元件自己说做完没有：指令写进 PLC 即 Running，做完 Completed，超时或设备报错 Failed，被中止回到 Idle。
    /// 基类没有自己管的动作，就是已经做完；自己管动作到完成的元件（轴、气缸、阀）重写。调用方发完指令看它等结果，手动页的部件动作也靠它判做完没有。
    /// </summary>
    public virtual ActionState ActionState => ActionState.Completed;

    #endregion

    #region 报警（元件只报自己的；报出去以后只能人工 Reset 清）

    private readonly object _alarmGate = new();

    /// <summary>
    /// 条件报警的防抖计时：报警码 → 条件开始成立的时刻（Stopwatch 时间戳）；条件不成立就移除。
    /// </summary>
    private readonly Dictionary<string, long> _alarmConditionSince = new(StringComparer.Ordinal);

    /// <summary>
    /// 报警来源：元件全路径；直接 new 出来、还没装进树的（冒烟、单测）用类型名。
    /// </summary>
    internal string AlarmSource => string.IsNullOrWhiteSpace(FullPath) ? GetType().Name : FullPath;

    /// <summary>
    /// 自己或任意子元件当前有没有报警（按来源路径前缀查报警表）。
    /// DI、AI 这类公共元件装进哪个模块，报警就算在哪个模块头上，元件里不用写任何代码。
    /// </summary>
    public bool HasAlarm => IocHelper.GetService<IAlarm>()?.HasAlarmUnder(AlarmSource) ?? false;

    /// <summary>
    /// 报警（动作失败、超时、账实不符这类一次性的事）：立即报，不防抖；已经在报的再报不会重复。
    /// 报警码就是元件上带 [Alarm] 的那个字段值。没装报警元件时什么都不做。
    /// </summary>
    protected void RaiseAlarm(string code)
    {
        IocHelper.GetService<IAlarm>()?.Raise(this, code);
    }

    /// <summary>
    /// 报事件（GEM 的 Collection Event）：事件码就是元件上带 [EventAttribut] 的那个字段值，data 是事件带的 DV
    /// （[DataVariable] 的代码 → 这一刻的值）。发给全部收事件的元件（IEventReceiver）：E30 发 S6F11，EDA 按采集计划采；
    /// 没接 EAP、离线、Host 关了这个事件时 E30 什么都不做，EDA 没有计划要它时也不做。任意线程可调。
    /// </summary>
    protected void RaiseEvent(string code, params Models.GemData[] data)
    {
        foreach (var receiver in IocHelper.GetServices<IEventReceiver>())
        {
            receiver.Report(this, code, data);
        }
    }

    /// <summary>
    /// 条件报警（DI、AI 这类信号），每个扫描周期调一次：条件持续成立满 debounceMs 才算成立、才报；
    /// 没满就断了，计时重来。防抖时间用元件自己的 EC。条件消失不清报警——报警只能人工 Reset 清。
    /// 返回防抖后的条件；raise = false 时只算不报（元件关了直接报警、交给宿主处理时用）。
    /// </summary>
    protected bool CheckAlarm(string code, bool condition, int debounceMs, bool raise = true)
    {
        lock (_alarmGate)
        {
            if (!condition)
            {
                _alarmConditionSince.Remove(code);
                return false;
            }

            if (!_alarmConditionSince.TryGetValue(code, out long since))
            {
                since = Stopwatch.GetTimestamp();
                _alarmConditionSince[code] = since;
            }

            if (Stopwatch.GetElapsedTime(since).TotalMilliseconds < debounceMs)
            {
                return false;
            }
        }

        if (raise)
        {
            RaiseAlarm(code);
        }

        return true;
    }

    #endregion

    #region 装配钩子

    /// <summary>
    /// 装配钩子：ElementLoader 灌完 [Machine] 属性后，把本元件在 machine.xml 中的 Setting 节点原样交给元件，
    /// 供读取名字不固定的配置（如工艺配方字段表）；默认无动作。配置不合法时抛异常，装配即失败。
    /// </summary>
    protected internal virtual void OnSettingLoaded(ModuleConfig setting)
    {
    }

    #endregion

    #region EC live 读写（经 EcElement）

    /// <summary>
    /// EC live 读：现查 EC 元件（改完即生效）；EC 没装或缺这一项时回退同名属性 [VariableMark] 的 Default。
    /// </summary>
    public string GetEcString(string key)
    {
        var value = IocHelper.GetService<IEc>()?.Get(FullPath, key);
        if (!string.IsNullOrEmpty(value))
        {
            return value;
        }

        var mark = GetType().GetProperty(key, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?.GetCustomAttribute<VariableMarkAttribute>();
        return mark?.Default ?? string.Empty;
    }

    /// <summary>
    /// EC live 读整型。
    /// </summary>
    public int GetEcInt(string key)
    {
        int.TryParse(GetEcString(key), out var value);
        return value;
    }

    /// <summary>
    /// EC live 写：经 EC 元件改值并落盘 ec.xml，立即对所有 live 读生效。
    /// 返回是否有变化（值相同不落盘；EC 没装时不生效，返回 false）。
    /// </summary>
    protected bool SetEc(string key, string value)
    {
        return IocHelper.GetService<IEc>()?.Set(FullPath, key, value) ?? false;
    }

    /// <summary>
    /// EC live 写整型。
    /// </summary>
    protected bool SetEcInt(string key, int value)
    {
        return SetEc(key, value.ToString());
    }

    /// <summary>
    /// EC live 读浮点（按不变区域性解析，读不出为 0）。
    /// </summary>
    public double GetEcDouble(string key)
    {
        double.TryParse(GetEcString(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var value);
        return value;
    }

    /// <summary>
    /// EC live 写浮点。
    /// </summary>
    protected bool SetEcDouble(string key, double value)
    {
        return SetEc(key, value.ToString(CultureInfo.InvariantCulture));
    }

    #endregion
}
