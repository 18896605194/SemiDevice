using xyz.Common.Log;
using xyz.Components;
using xyz.Components.Components;
using xyz.Components.Enums;
using xyz.Components.Interfaces;
using xyz.Modules;
using xyz.Shared.Dtos;
using xyz.Tools;

namespace xyz.Service.Events;

/// <summary>
/// 设备总状态：定时看报警和各模块有没有在执行动作，得出 红 = 报警、黄 = 警告、绿 = 运行，再带上整机模式（Auto = 自动派单开着），
/// 有变化时点亮组件树里的四色灯（只认 ILightComponent，不依赖具体灯组件），并经事件流推给客户端（顶栏四色灯、主界面系统操作）。
/// 设备没配四色灯时照样推给客户端。蓝灯（通讯）和蜂鸣器不在这里管。
/// </summary>
public static class EquipmentStatusPublisher
{
    private const int IntervalMs = 200;

    private static readonly object Gate = new();
    private static Timer? _timer;
    private static (bool HasAlarm, bool HasWarning, bool IsRunning, bool IsAuto)? _last;

    /// <summary>
    /// 灯输出失败时记过的原因，同一个灯同样的错只记一次，免得每次变化都刷日志。
    /// </summary>
    private static readonly Dictionary<ILightComponent, string> LightErrors = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// 上一次刷新失败的原因，同样的错只记一次。
    /// </summary>
    private static string? _lastFault;

    /// <summary>
    /// 模块启动后调一次。
    /// </summary>
    public static void Start(IReadOnlyList<ComponentBase> roots, IReadOnlyList<BaseModule> modules)
    {
        var lights = roots.OfType<ILightComponent>()
            .Concat(roots.SelectMany(root => root.FindChildren<ILightComponent>()))
            .Distinct()
            .ToList();

        _timer?.Dispose();
        _timer = new Timer(_ => Publish(lights, modules), null, 0, IntervalMs);
    }

    /// <summary>
    /// 定时器回调里的异常会把宿主进程带崩，所以整个一拍兜住；同样的错只记一次。
    /// </summary>
    private static void Publish(IReadOnlyList<ILightComponent> lights, IReadOnlyList<BaseModule> modules)
    {
        try
        {
            PublishCore(lights, modules);
            _lastFault = null;
        }
        catch (Exception exception)
        {
            if (_lastFault != exception.Message)
            {
                _lastFault = exception.Message;
                LogHelper.Warn("EquipmentStatus", $"设备总状态刷新失败: {exception.Message}");
            }
        }
    }

    private static void PublishCore(IReadOnlyList<ILightComponent> lights, IReadOnlyList<BaseModule> modules)
    {
        lock (Gate)
        {
            var dto = Snapshot(modules);
            var status = (dto.HasAlarm, dto.HasWarning, dto.IsRunning, dto.IsAuto);
            if (_last == status)
            {
                return;
            }

            // 只是模式变了也重刷一遍灯：写的是同样的值，模式又是人点的、很少变，不值得为它单分一种情况。
            _last = status;
            foreach (var light in lights)
            {
                Drive(light, dto.HasAlarm, dto.HasWarning, dto.IsRunning);
            }

            EventBus.Send(dto, EquipmentStatusDto.EventToken);
        }
    }

    /// <summary>
    /// 这一刻的设备总状态：报警看报警组件（Warn 级算警告，其余算报警），运行看有没有模块在执行动作，模式看搬运管理的自动派单。
    /// 没装报警组件、搬运管理的照样能算（没报警、Manual）。
    /// </summary>
    public static EquipmentStatusDto Snapshot(IReadOnlyList<BaseModule> modules)
    {
        var alarms = AlarmComponent.Current?.ActiveAlarms ?? [];
        return new EquipmentStatusDto
        {
            HasAlarm = alarms.Any(alarm => alarm.Level != AlarmLevel.Warn),
            HasWarning = alarms.Any(alarm => alarm.Level == AlarmLevel.Warn),
            IsRunning = modules.Any(module => module.CurrentOperation is not null),
            IsAuto = TransferManager.Current?.IsAutoDispatch == true,
        };
    }

    private static void Drive(ILightComponent light, bool red, bool yellow, bool green)
    {
        try
        {
            light.SetRed(red);
            light.SetYellow(yellow);
            light.SetGreen(green);
            LightErrors.Remove(light);
        }
        catch (Exception exception)
        {
            if (LightErrors.TryGetValue(light, out var last) && last == exception.Message)
            {
                return;
            }

            LightErrors[light] = exception.Message;
            var name = (light as ComponentBase)?.FullPath ?? light.GetType().Name;
            LogHelper.Warn("EquipmentStatus", $"{name} 四色灯输出失败: {exception.Message}");
        }
    }
}
