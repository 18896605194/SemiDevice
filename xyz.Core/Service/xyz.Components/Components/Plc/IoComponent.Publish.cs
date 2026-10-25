using xyz.Common.Log;
using xyz.Components.Models;
using xyz.Shared.Dtos;
using xyz.Tools;

namespace xyz.Components.Components;

/// <summary>
/// IO 点位推送：按 EC 周期把采到的点整包推给 IO 界面，界面只订阅不拉
/// </summary>
public partial class IoComponent
{
    /// <summary>
    /// 推给界面的那一包：骨架只建一次（点表装机时定死），每次只刷值
    /// </summary>
    private IoDto? _snapshot;

    private long _lastPublishAt;

    private string? _lastPublishFault;

    /// <summary>
    /// 扫描里调：到了推送周期就整包推一次
    /// </summary>
    private void PublishSnapshot()
    {
        long now = Environment.TickCount64;
        if (_lastPublishAt != 0 && now - _lastPublishAt < PublishIntervalMs)
        {
            return;
        }

        _lastPublishAt = now;
        try
        {
            _snapshot ??= BuildSkeleton();
            _snapshot.IsCollecting = IsCollected;
            RefreshValues(_snapshot);

            // 状态类消息：留存着，客户端连上立刻拿到当前这一包，不用等下一次
            EventBus.Send(_snapshot, IoDto.EventToken);
            _lastPublishFault = null;
        }
        catch (Exception exception)
        {
            if (_lastPublishFault != exception.Message)
            {
                _lastPublishFault = exception.Message;
                LogHelper.Warn(this, $"IO 点位推送失败: {exception.Message}");
            }
        }
    }

    /// <summary>
    /// 按 类型 → 模块 分组建骨架
    /// </summary>
    private IoDto BuildSkeleton()
    {
        var dto = new IoDto();
        AddType(dto, Di, false);
        AddType(dto, Do, true);
        AddType(dto, Ai, false);
        AddType(dto, Ao, true);
        LogHelper.Info(this, $"IO 界面骨架建好：{string.Join("、", dto.Types.Select(
            type => $"{type.Type} {type.Modules.Count} 个模块 {type.Modules.Sum(module => module.Points.Count)} 点"))}");
        return dto;
    }

    private static void AddType(IoDto dto, IoPointTable table, bool isOutput)
    {
        if (table.Points.Count == 0)
        {
            return;
        }

        var typeDto = new IoTypeDto { Type = table.Name };
        foreach (var group in table.Points.GroupBy(ModuleOf).OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            var moduleDto = new IoModuleDto { Module = group.Key };
            foreach (var point in group.OrderBy(point => point.Index))
            {
                var pointDto = new IoPointDto
                {
                    Index = point.Index,
                    Name = point.Name,
                    Component = point.Component,
                    Description = point.Description,
                    Unit = point.Unit,
                    IsOutput = isOutput,
                };
                if (point.IsScaled)
                {
                    pointDto.Min = Math.Min(point.LogicalMin, point.LogicalMax);
                    pointDto.Max = Math.Max(point.LogicalMin, point.LogicalMax);
                }

                moduleDto.Points.Add(pointDto);
            }

            typeDto.Modules.Add(moduleDto);
        }

        dto.Types.Add(typeDto);
    }

    /// <summary>
    /// 点表的 Module 列，没填的归到"未分组"
    /// </summary>
    private static string ModuleOf(IoPoint point)
    {
        if (point.Module.Length == 0)
        {
            return "未分组";
        }

        return point.Module;
    }

    /// <summary>
    /// 只刷值：每个点读 PLC 组件这一拍的缓存
    /// </summary>
    private void RefreshValues(IoDto snapshot)
    {
        foreach (var typeDto in snapshot.Types)
        {
            foreach (var moduleDto in typeDto.Modules)
            {
                foreach (var pointDto in moduleDto.Points)
                {
                    RefreshPoint(typeDto.Type, pointDto);
                }
            }
        }
    }

    /// <summary>
    /// 读不到的点 IsValid = false，界面显示成"—"
    /// </summary>
    private void RefreshPoint(string type, IoPointDto point)
    {
        bool on = false;
        double value = 0;
        bool valid = false;
        bool isAnalog = false;
        switch (type)
        {
            case "DI":
                valid = TryReadDi(point.Index, out on);
                break;
            case "DO":
                valid = TryReadDo(point.Index, out on);
                break;
            case "AI":
                valid = TryReadAi(point.Index, out value);
                isAnalog = true;
                break;
            case "AO":
                valid = TryReadAo(point.Index, out value);
                isAnalog = true;
                break;
        }

        point.IsValid = valid;
        point.IsOn = on;
        if (isAnalog)
        {
            point.Value = value.ToString("0.###");
        }
        else if (on)
        {
            point.Value = bool.TrueString;
        }
        else
        {
            point.Value = bool.FalseString;
        }
    }
}
