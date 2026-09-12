using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using xyz.Common.Log;
using xyz.Components.Attributes;
using xyz.Configs;

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
    /// 初始化顺序，默认 10000，越小越先。
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

    #region 中止与复位钩子

    /// <summary>
    /// 组件中止钩子，默认空实现，由子类重写本组件的中止处理。
    /// 后续由控制流程显式调用，处理应及时返回。
    /// </summary>
    protected virtual void OnAbort()
    {
    }

    /// <summary>
    /// 组件复位钩子，默认空实现，由子类重写本组件的复位处理。
    /// 后续由控制流程显式调用，处理应及时返回。
    /// </summary>
    protected virtual void OnReset()
    {
    }

    #endregion

    #region EC live 读

    /// <summary>
    /// EC live 读：现查 EC 内存树（改完即生效），缺失回退同名属性 [VariableMark] 的 Default。
    /// </summary>
    public string GetEcString(string key)
    {
        var value = EC.GetByPath(FullPath, key);
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
    /// EC live 写：改内存树并落盘 ec.xml，立即对所有 live 读生效。
    /// 返回是否有变化（值相同不落盘）。
    /// </summary>
    protected bool SetEc(string key, string value)
    {
        return EC.SetValueByPath(FullPath, key, value);
    }

    /// <summary>
    /// EC live 写整型。
    /// </summary>
    protected bool SetEcInt(string key, int value)
    {
        return SetEc(key, value.ToString());
    }

    #endregion
}
