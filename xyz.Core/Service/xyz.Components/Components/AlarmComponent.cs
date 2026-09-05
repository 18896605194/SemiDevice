using System.Diagnostics;
using System.Reflection;
using xyz.Components.Alarm;
using xyz.Components.Attributes;

namespace xyz.Components.Components;

/// <summary>
/// 设备级报警管理组件。同一台设备共用一个实例，管理触发、恢复及人工确认。
/// 不直接控制模块动作、四色灯或蜂鸣器，不自动保存历史记录。
/// </summary>
[Component(description: "报警管理组件")]
public class AlarmComponent : ComponentBase, IAlarmComponent
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<(string SourcePath, string AlarmCode), AlarmAttribute> _definitions = new();
    private readonly Dictionary<(string SourcePath, string AlarmCode), AlarmItem> _activeAlarms = new();
    private readonly Queue<AlarmItem> _notifications = new();
    private bool _publishing;

    /// <summary>
    /// 报警发生变化后的快照；重复触发、重复确认不发送通知。
    /// 按变化顺序分发，不持有状态锁，也不保证在 UI 线程执行。
    /// 订阅者应及时返回，异常写入 Trace，不影响其他订阅者和已经完成的状态变更。
    /// </summary>
    public event Action<AlarmItem>? AlarmChanged;

    /// <summary>当前活动报警的快照，按触发时间排序；人工确认后的报警仍在此列表中。</summary>
    public IReadOnlyList<AlarmItem> ActiveAlarms
    {
        get
        {
            lock (_syncRoot)
            {
                return _activeAlarms.Values
                    .OrderBy(alarm => alarm.RaisedAt)
                    .Select(alarm => alarm.Snapshot())
                    .ToArray();
            }
        }
    }

    /// <summary>
    /// 初始化时注册指定组件的报警定义。路径由上层提供，需在同一设备内唯一。
    /// 读取公开实例字段或属性上的 AlarmAttribute，字符串成员值作为报警代码。
    /// 包含继承的报警定义，不自动递归注册子组件；重复注册同一路径及代码会报错。
    /// </summary>
    public void Register(string sourcePath, ComponentBase source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(source);

        var definitions = new Dictionary<string, AlarmAttribute>(StringComparer.Ordinal);
        foreach (var member in source.GetType().GetMembers(BindingFlags.Public | BindingFlags.Instance))
        {
            var attribute = member.GetCustomAttribute<AlarmAttribute>(inherit: true);
            if (attribute is null)
            {
                continue;
            }

            string? alarmCode = member switch
            {
                FieldInfo field when field.FieldType == typeof(string) => field.GetValue(source) as string,
                PropertyInfo property when property.PropertyType == typeof(string)
                    && property.GetMethod?.IsPublic == true
                    && property.GetIndexParameters().Length == 0 => property.GetValue(source) as string,
                _ => null
            };

            if (string.IsNullOrWhiteSpace(alarmCode))
            {
                throw new InvalidOperationException($"{sourcePath}.{member.Name} 的报警代码必须是可读取的非空字符串。");
            }

            if (!definitions.TryAdd(alarmCode, attribute))
            {
                throw new InvalidOperationException($"{sourcePath} 包含重复的报警代码：{alarmCode}。");
            }
        }

        lock (_syncRoot)
        {
            // 先检查整批定义，避免注册失败后留下部分已注册的报警。
            foreach (var alarmCode in definitions.Keys)
            {
                if (_definitions.ContainsKey((sourcePath, alarmCode)))
                {
                    throw new InvalidOperationException($"报警 {sourcePath}.{alarmCode} 已注册。");
                }
            }

            foreach (var definition in definitions)
            {
                _definitions.Add((sourcePath, definition.Key), definition.Value);
            }
        }
    }

    /// <summary>触发已注册的报警。已活动时返回 false，保留首次触发时间和确认状态。</summary>
    public bool Raise(string sourcePath, string alarmCode)
    {
        ValidateKey(sourcePath, alarmCode);
        lock (_syncRoot)
        {
            var key = (sourcePath, alarmCode);
            if (_activeAlarms.ContainsKey(key))
            {
                return false;
            }

            if (!_definitions.TryGetValue(key, out var definition))
            {
                throw new InvalidOperationException($"报警 {sourcePath}.{alarmCode} 尚未注册。");
            }

            var alarm = new AlarmItem
            {
                SourcePath = sourcePath,
                AlarmCode = alarmCode,
                AlarmText = definition.AlarmText,
                Category = definition.Category,
                Level = definition.AlarmLevel,
                Description = definition.Description,
                Solution = definition.Solution,
                RaisedAt = DateTimeOffset.UtcNow
            };

            _activeAlarms.Add(key, alarm);
            _notifications.Enqueue(alarm.Snapshot());
        }

        PublishChanges();
        return true;
    }

    /// <summary>
    /// 由故障检测方在故障恢复后调用。移出活动列表并通知恢复时间，不自动确认报警。
    /// 不存在活动报警时返回 false；恢复快照仅通过 AlarmChanged 通知，不在本组件内保留历史。
    /// </summary>
    public bool Clear(string sourcePath, string alarmCode)
    {
        ValidateKey(sourcePath, alarmCode);
        lock (_syncRoot)
        {
            if (!_activeAlarms.Remove((sourcePath, alarmCode), out var alarm))
            {
                return false;
            }

            alarm.ClearedAt = DateTimeOffset.UtcNow;
            _notifications.Enqueue(alarm.Snapshot());
        }

        PublishChanges();
        return true;
    }

    /// <summary>人工确认活动报警，不清除故障，也不控制蜂鸣器。不存在或已确认时返回 false。</summary>
    public bool Acknowledge(string sourcePath, string alarmCode)
    {
        ValidateKey(sourcePath, alarmCode);
        lock (_syncRoot)
        {
            if (!_activeAlarms.TryGetValue((sourcePath, alarmCode), out var alarm) || alarm.IsAcknowledged)
            {
                return false;
            }

            alarm.AcknowledgedAt = DateTimeOffset.UtcNow;
            _notifications.Enqueue(alarm.Snapshot());
        }

        PublishChanges();
        return true;
    }

    private static void ValidateKey(string sourcePath, string alarmCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(alarmCode);
    }

    private void PublishChanges()
    {
        lock (_syncRoot)
        {
            if (_publishing)
            {
                return;
            }

            _publishing = true;
        }

        // 状态变更和入队使用同一把锁；仅一个调用方分发，保证并发和回调重入时的通知顺序。
        while (true)
        {
            AlarmItem alarm;
            lock (_syncRoot)
            {
                if (_notifications.Count == 0)
                {
                    _publishing = false;
                    return;
                }

                alarm = _notifications.Dequeue();
            }

            var handlers = AlarmChanged;
            if (handlers is null)
            {
                continue;
            }

            foreach (Action<AlarmItem> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(alarm);
                }
                catch (Exception exception)
                {
                    try
                    {
                        Trace.TraceError($"AlarmChanged 订阅者处理 {alarm.SourcePath}.{alarm.AlarmCode} 失败：{exception}");
                    }
                    catch
                    {
                        // 诊断监听器自身失败时，也不能阻断后续报警通知。
                    }
                }
            }
        }
    }
}
