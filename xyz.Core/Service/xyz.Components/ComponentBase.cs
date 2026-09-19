using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using xyz.Common.Log;
using xyz.Components.Attributes;
using xyz.Components.Components;
using xyz.Configs.Models;

namespace xyz.Components;

/// <summary>
/// 组件基类
/// </summary>
public abstract class ComponentBase
{
    #region Column

    /// <summary>
    /// 组件名，例如DISensorComponent
    /// </summary>
    public string Name { get; internal set; } = string.Empty;
    /// <summary>
    /// 本级名称的别名，等于 Name。
    /// 示例：Name = "Chamber1"，Path = "Chamber1"
    /// </summary>
    public string Path => Name;

    /// <summary>
    /// 父组件给子组件声明的角色，比如 Valve、Sensor。
    /// 示例：Role = "Valve"
    /// </summary>
    public string? Role { get; internal set; }

    /// <summary>
    /// 完整层级路径，比如 Chamber1.Valve1。
    /// </summary>
    public string FullPath { get; internal set; } = string.Empty;

    /// <summary>
    /// 初始化顺序，默认 10000，越小越先（Init 时同一层的子组件按它依次初始化；sc.xml 节点的 InitOrder 属性配）。
    /// </summary>
    public int InitOrder { get; internal set; } = 10000;

    /// <summary>
    /// 是否启用，false 不参与扫描。
    /// </summary>
    public bool IsEnabled { get; internal set; } = true;

    /// <summary>
    /// 子组件的集合
    /// </summary>
    private readonly List<ComponentBase> _children = new();
    public IReadOnlyList<ComponentBase> Children => _children;

    #endregion

    /// <summary>
    /// 添加子组件
    /// </summary>
    /// <param name="child"></param>
    public void AddChild(ComponentBase child)
    {
        _children.Add(child);
    }

    #region 查找子组件

    /// <summary>
    /// 按名字在子组件树（含各级后代，不含自己）中查找，忽略大小写；找不到返回 null。
    /// </summary>
    public ComponentBase? FindChild(string name)
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
    /// 按名字 + 类型在子组件树（含各级后代，不含自己）中查找，忽略大小写；
    /// 名字命中但类型不符（或找不到）返回 null。T 可为具体组件类（含派生）或接口。
    /// </summary>
    public T? FindChild<T>(string name) where T : class
    {
        return FindChild(name) as T;
    }

    /// <summary>
    /// 按类型在子组件树（含各级后代，不含自己）中查找第一个匹配项；找不到返回 null。
    /// T 可以是具体组件类（含其派生类）或接口（如 IRfidReader，匹配任意实现类）。
    /// </summary>
    public T? FindChild<T>() where T : class
    {
        foreach (var child in _children)
        {
            if (child is T matched)
            {
                return matched;
            }

            var found = child.FindChild<T>();
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>
    /// 按类型或接口在子组件树（含各级后代，不含自己）中查找全部匹配项（含派生类/实现类）。
    /// </summary>
    public IReadOnlyList<T> FindChildren<T>() where T : class
    {
        var result = new List<T>();
        CollectChildren(this, result);
        return result;
    }

    private static void CollectChildren<T>(ComponentBase component, List<T> result) where T : class
    {
        foreach (var child in component._children)
        {
            if (child is T matched)
            {
                result.Add(matched);
            }

            CollectChildren(child, result);
        }
    }

    #endregion


    #region 开启轮询 间隔50ms
    /// <summary>
    /// 启动组件树的扫描线程。只有模块根组件调用；
    /// 子组件随父组件在同一线程上被 OnScan 递归扫描，不要对子组件单独调用 Start。
    /// </summary>
    public void Start()
    {
        Task.Factory.StartNew(ScanLoop, TaskCreationOptions.LongRunning);
    }

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
        while (true)
        {
            var watch = Stopwatch.StartNew();
            try
            {
                OnScan();
            }
            catch (Exception exception)
            {
                // 单周期异常不杀扫描线程：记录后继续下一周期。
                LogHelper.Warn("Scan", $"[{FullPath}] OnScan 异常: {exception.Message}");
            }

            var elapsed = watch.ElapsedMilliseconds;
            if (elapsed > SlowScanAlarmMilliseconds)
            {
                LogHelper.Error("Scan", $"[{FullPath}] 慢扫描报警 {elapsed}ms（阈值 {SlowScanAlarmMilliseconds}ms）");
            }
            else if (elapsed > SlowScanWarnMilliseconds)
            {
                LogHelper.Warn("Scan", $"[{FullPath}] 慢扫描警告 {elapsed}ms（阈值 {SlowScanWarnMilliseconds}ms）");
            }

            Thread.Sleep(50);
        }
    }

    /// <summary>
    /// 扫描周期，默认递归扫描子组件；子类重写时调用 base.OnScan() 后追加自己的周期逻辑。
    /// </summary>
    protected virtual void OnScan()
    {
        foreach (var child in _children)
        {
            child.OnScan();
        }
    }

    #endregion

    #region 初始化、中止与复位（都不是必须重写的：组件有自己的处理才重写，记得调 base）

    /// <summary>
    /// 初始化：先按 InitOrder 从小到大初始化子组件，再初始化自己。基类自己没有要做的，
    /// 组件有初始化要做就重写，记得调 base.Init()。由人或调度显式调用，开机不自动做（开机只连驱动）。
    /// 返回值给要等结果的调用方：普通组件当场做完，返回 null；
    /// 模块（LoadPort、Robot）重写时把返回类型收窄成 ModuleOperation?，交出初始化操作让调用方等它做完。
    /// </summary>
    public virtual object? Init()
    {
        foreach (var child in _children.OrderBy(child => child.InitOrder))
        {
            child.Init();
        }

        return null;
    }

    /// <summary>
    /// 中止：先中止子组件，再中止自己。基类自己没有要做的，组件有要停下的（在途动作、运动、握手）就重写，
    /// 记得调 base.Abort()。只停不清报警（报警只能 Reset 清）；要立即返回，不等结果。
    /// 返回值同 Init：普通组件返回 null；模块重写时收窄成 ModuleOperation?，交出设备中止操作。
    /// </summary>
    public virtual object? Abort()
    {
        foreach (var child in _children)
        {
            child.Abort();
        }

        return null;
    }

    /// <summary>
    /// 复位（人工，界面 Reset）：先复位子组件，再清掉自己的报警。条件还在的报警照样清，下个扫描周期会重新报出来。
    /// 组件有自己的复位处理就重写，记得调 base.Reset()；现在不能复位就不调，报警留着。
    /// 返回值给要等结果的调用方：普通组件当场做完，返回 null；
    /// 模块（LoadPort、Robot）重写时把返回类型收窄成 ModuleOperation?，交出设备复位操作让调用方等它做完。
    /// </summary>
    public virtual object? Reset()
    {
        foreach (var child in _children)
        {
            child.Reset();
        }

        AlarmComponent.Current?.Clear(this);
        return null;
    }

    #endregion

    #region 报警（组件只报自己的；报出去以后只能人工 Reset 清）

    private readonly object _alarmGate = new();

    /// <summary>
    /// 条件报警的防抖计时：报警码 → 条件开始成立的时刻（Stopwatch 时间戳）；条件不成立就移除。
    /// </summary>
    private readonly Dictionary<string, long> _alarmConditionSince = new(StringComparer.Ordinal);

    /// <summary>
    /// 报警来源：组件全路径；直接 new 出来、还没装进树的（冒烟、单测）用类型名。
    /// </summary>
    internal string AlarmSource => string.IsNullOrWhiteSpace(FullPath) ? GetType().Name : FullPath;

    /// <summary>
    /// 自己或任意子组件当前有没有报警（按来源路径前缀查报警表）。
    /// DI、AI 这类公共组件装进哪个模块，报警就算在哪个模块头上，组件里不用写任何代码。
    /// </summary>
    public bool HasAlarm => AlarmComponent.Current?.HasAlarmUnder(AlarmSource) ?? false;

    /// <summary>
    /// 报警（动作失败、超时、账实不符这类一次性的事）：立即报，不防抖；已经在报的再报不会重复。
    /// 报警码就是组件上带 [Alarm] 的那个字段值。没装报警组件时什么都不做。
    /// </summary>
    protected void RaiseAlarm(string code)
    {
        AlarmComponent.Current?.Raise(this, code);
    }

    /// <summary>
    /// 条件报警（DI、AI 这类信号），每个扫描周期调一次：条件持续成立满 debounceMs 才算成立、才报；
    /// 没满就断了，计时重来。防抖时间用组件自己的 EC。条件消失不清报警——报警只能人工 Reset 清。
    /// 返回防抖后的条件；raise = false 时只算不报（组件关了直接报警、交给宿主处理时用）。
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
    /// 装配钩子：ComponentLoader 灌完 [SCEditor] 属性后，把本组件在 sc.xml 中的 Setting 节点原样交给组件，
    /// 供读取名字不固定的配置（如机械手站点表）；默认无动作。配置不合法时抛异常，装配即失败。
    /// </summary>
    protected internal virtual void OnSettingLoaded(ModuleConfig setting)
    {
    }

    #endregion

    #region EC live 读写（经 EcComponent）

    /// <summary>
    /// EC live 读：现查 EC 组件（改完即生效）；EC 没装或缺这一项时回退同名属性 [VariableMark] 的 Default。
    /// </summary>
    public string GetEcString(string key)
    {
        var value = EcComponent.Current?.Get(FullPath, key);
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
    /// EC live 写：经 EC 组件改值并落盘 ec.xml，立即对所有 live 读生效。
    /// 返回是否有变化（值相同不落盘；EC 没装时不生效，返回 false）。
    /// </summary>
    protected bool SetEc(string key, string value)
    {
        return EcComponent.Current?.Set(FullPath, key, value) ?? false;
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
