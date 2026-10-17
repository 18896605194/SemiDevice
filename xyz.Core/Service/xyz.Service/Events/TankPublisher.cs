using xyz.Components;
using xyz.Components.Components;
using xyz.Components.Interfaces;
using xyz.Shared.Dtos;
using xyz.Tools;

namespace xyz.Service.Events;

/// <summary>
/// 供液槽推送：槽在扫描里比出变化发 Changed，这里换成 TankDto 推给界面（token 是槽的全路径，留存）
/// </summary>
public static class TankPublisher
{
    /// <summary>
    /// 装配完调一次，挂上组件树里所有的槽
    /// </summary>
    public static void Bind(IReadOnlyList<ComponentBase> roots)
    {
        foreach (var tank in roots.OfType<TankComponent>()
                     .Concat(roots.SelectMany(root => root.FindChildren<TankComponent>())).Distinct())
        {
            tank.Changed += () => EventBus.Send(ToDto(tank), tank.FullPath, retain: true);
        }
    }

    /// <summary>
    /// 照槽现在的液位、状态和配了的信号取一份
    /// </summary>
    private static TankDto ToDto(TankComponent tank)
    {
        return new TankDto
        {
            Path = tank.FullPath,
            Level = (int)tank.Level,
            State = (int)tank.State,
            LL = SignalOf(tank.LL),
            L = SignalOf(tank.L),
            H = SignalOf(tank.H),
            HH = SignalOf(tank.HH),
            Overfill = SignalOf(tank.Overfill),
        };
    }

    /// <summary>
    /// 没配的信号是 null
    /// </summary>
    private static DiSensorDto? SignalOf(IDiSensor? signal)
    {
        if (signal is null)
        {
            return null;
        }

        return new DiSensorDto { Name = signal.Name, IsActive = signal.IsActive };
    }
}
