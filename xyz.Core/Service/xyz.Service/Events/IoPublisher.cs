using xyz.Common.Log;
using xyz.Components.Components;
using xyz.Components.Io;
using xyz.Shared.Dtos;
using xyz.Tools;

namespace xyz.Service.Events;

/// <summary>
/// IO 点位推送：按周期把 IO 组件采到的点整包推给客户端顶起的 IO 界面。
/// 后端主动推、前端只订阅——界面不拉、不轮询，也就不会因为界面开着而拖慢采集。
///
/// 骨架（有哪些类型、哪些模块、每个模块哪些点）只建一次：点表装机时定死，运行期不会变；
/// 每一拍只改点上的值。几百个点每 500ms 重建一遍树纯属浪费。
///
/// 这座桥搭在装配层：IO 组件在组件层，不引用契约层——跟报警、日志那两条一个路子。
/// </summary>
public static class IoPublisher
{
    private static readonly object Gate = new();
    private static Timer? _timer;
    private static IoDto? _snapshot;
    private static string? _lastFault;

    private static int _interval;

    /// <summary>
    /// IO 组件起来之后调一次；没装 IO 组件就不推。
    /// </summary>
    public static void Start()
    {
        var io = IoComponent.Current;
        if (io is null)
        {
            LogHelper.Warn("Io", "sc.xml 没配 Io 节点：IO 界面拿不到点位数据");
            return;
        }

        _timer?.Dispose();
        _interval = io.PublishIntervalMs;
        _timer = new Timer(_ => Publish(io), null, 0, _interval);
    }

    /// <summary>
    /// 推送周期是 EC，现场改了要当场生效，所以每拍对一下。
    /// </summary>
    private static void FollowInterval(IoComponent io)
    {
        int interval = io.PublishIntervalMs;
        if (interval == _interval || interval <= 0)
        {
            return;
        }

        _interval = interval;
        _timer?.Change(interval, interval);
        LogHelper.Info("Io", $"IO 推送周期改为 {interval}ms");
    }

    /// <summary>
    /// 定时器回调里的异常会把宿主带崩，整拍兜住；同样的错只记一次。
    /// </summary>
    private static void Publish(IoComponent io)
    {
        try
        {
            FollowInterval(io);
            PublishCore(io);
            _lastFault = null;
        }
        catch (Exception exception)
        {
            if (_lastFault != exception.Message)
            {
                _lastFault = exception.Message;
                LogHelper.Warn("Io", $"IO 点位推送失败: {exception.Message}");
            }
        }
    }

    private static void PublishCore(IoComponent io)
    {
        IoDto snapshot;
        lock (Gate)
        {
            _snapshot ??= BuildSkeleton(io);
            snapshot = _snapshot;
            snapshot.IsCollecting = io.IsCollecting;
            RefreshValues(io, snapshot);
        }

        // 状态类消息：retain 着，客户端连上立刻能拿到当前这一包，不用等下一拍。
        EventBus.Send(snapshot, IoDto.EventToken);
    }

    /// <summary>
    /// 按 类型 → 模块 分组建骨架。模块名来自点表的 Module 列，没填的归到"未分组"。
    /// </summary>
    private static IoDto BuildSkeleton(IoComponent io)
    {
        var dto = new IoDto();
        foreach (var (table, isOutput) in new[]
                 {
                     (io.Di, false), (io.Do, true), (io.Ai, false), (io.Ao, true),
                 })
        {
            if (table.Points.Count == 0)
            {
                continue;
            }

            var typeDto = new IoTypeDto { Type = table.Name };
            foreach (var group in table.Points
                         .GroupBy(point => point.Module.Length == 0 ? "未分组" : point.Module)
                         .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
            {
                var moduleDto = new IoModuleDto { Module = group.Key };
                foreach (var point in group.OrderBy(point => point.Index))
                {
                    moduleDto.Points.Add(new IoPointDto
                    {
                        Index = point.Index,
                        Name = point.Name,
                        Component = point.Component,
                        Description = point.Description,
                        Unit = point.Unit,
                        IsOutput = isOutput,
                    });
                }

                typeDto.Modules.Add(moduleDto);
            }

            dto.Types.Add(typeDto);
        }

        LogHelper.Info("Io", $"IO 界面骨架建好：{string.Join("、", dto.Types.Select(
            type => $"{type.Type} {type.Modules.Count} 个模块 {type.Modules.Sum(module => module.Points.Count)} 点"))}");
        return dto;
    }

    /// <summary>
    /// 只刷值：骨架的顺序跟点表一一对应，按类型名拿回对应的点表再逐点取。
    /// </summary>
    private static void RefreshValues(IoComponent io, IoDto snapshot)
    {
        foreach (var typeDto in snapshot.Types)
        {
            var table = Resolve(io, typeDto.Type);
            if (table is null)
            {
                continue;
            }

            bool isAnalog = typeDto.Type is "AI" or "AO";
            foreach (var moduleDto in typeDto.Modules)
            {
                foreach (var pointDto in moduleDto.Points)
                {
                    var point = table.Find(pointDto.Index);
                    if (point is null)
                    {
                        pointDto.IsValid = false;
                        continue;
                    }

                    pointDto.IsValid = point.IsValid;
                    pointDto.IsOn = point.IsOn;
                    pointDto.Value = isAnalog
                        ? point.Value.ToString("0.###")
                        : point.IsOn ? "1" : "0";
                }
            }
        }
    }

    private static IoPointTable? Resolve(IoComponent io, string type)
    {
        return type switch
        {
            "DI" => io.Di,
            "DO" => io.Do,
            "AI" => io.Ai,
            "AO" => io.Ao,
            _ => null,
        };
    }
}
