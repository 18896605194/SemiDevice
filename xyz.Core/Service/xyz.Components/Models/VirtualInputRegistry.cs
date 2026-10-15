using xyz.Components.Components;

namespace xyz.Components.Models;

/// <summary>一棵装配树的虚拟输入索引和依赖关系；不使用跨设备/跨装配的静态点表。</summary>
internal sealed class VirtualInputRegistry
{
    private readonly Dictionary<IoReference, VirtualInputComponent> _points;
    private readonly IoComponent? _io;
    private readonly object _gate = new();

    private VirtualInputRegistry(IEnumerable<VirtualInputComponent> points, IoComponent? io)
    {
        _io = io;
        _points = [];
        foreach (var component in points)
        {
            var point = new IoReference(component.PointType, component.Index);
            if (!_points.TryAdd(point, component))
            {
                throw new InvalidOperationException($"虚拟点 {point} 重复：{_points[point].FullPath}、{component.FullPath}");
            }
        }

        var complete = new HashSet<IoReference>();
        var visiting = new HashSet<IoReference>();
        void Visit(IoReference point)
        {
            if (complete.Contains(point))
            {
                return;
            }

            if (!visiting.Add(point))
            {
                throw new InvalidOperationException($"虚拟输入循环引用：{point}");
            }

            foreach (var source in _points[point].Compiled.References)
            {
                if (source.Index >= 3000)
                {
                    if (!_points.ContainsKey(source))
                    {
                        throw new InvalidOperationException($"{_points[point].FullPath} 引用了未配置的虚拟点 {source}");
                    }

                    Visit(source);
                }
                else if (_io is null)
                {
                    throw new InvalidOperationException($"{source} 是物理点，需要装配 IoComponent");
                }
            }

            visiting.Remove(point);
            complete.Add(point);
        }

        foreach (var point in _points.Keys)
        {
            Visit(point);
        }
    }

    public static void Attach(IReadOnlyList<ComponentBase> roots)
    {
        var all = Flatten(roots).ToList();
        var points = all.OfType<VirtualInputComponent>().ToList();
        if (points.Count == 0)
        {
            return;
        }

        var io = all.OfType<IoComponent>().ToList();
        if (io.Count > 1)
        {
            throw new InvalidOperationException("全局虚拟输入索引只能绑定一个 IoComponent 读写入口");
        }

        var registry = new VirtualInputRegistry(points, io.SingleOrDefault());
        foreach (var point in points)
        {
            point.Bind(registry);
        }

        io.SingleOrDefault()?.AttachVirtualInputs(registry);
    }

    private static IEnumerable<ComponentBase> Flatten(IEnumerable<ComponentBase> roots)
    {
        foreach (var component in roots)
        {
            yield return component;
            foreach (var child in Flatten(component.Children))
            {
                yield return child;
            }
        }
    }

    public void ValidateSources()
    {
        foreach (var source in _points.Values.SelectMany(point => point.Compiled.References).Distinct())
        {
            if (source.Index >= 3000)
            {
                continue;
            }

            var table = source.Type switch
            {
                "DI" => _io?.Di,
                "DO" => _io?.Do,
                "AI" => _io?.Ai,
                _ => _io?.Ao,
            };
            if (table?.Find(source.Index) is null)
            {
                throw new InvalidOperationException($"虚拟输入的来源 {source} 不在物理 IO 点表中");
            }
        }
    }

    public bool TryRead(IoReference point, out double value)
    {
        lock (_gate)
        {
            // 一次求值中同一来源只读一次；跨模块依赖实时求值，不依赖各模块的扫描先后。
            var cache = new Dictionary<IoReference, double?>();
            double? Read(IoReference source)
            {
                if (cache.TryGetValue(source, out double? cached))
                {
                    return cached;
                }

                double? result;
                if (_points.TryGetValue(source, out var component))
                {
                    result = component.Compiled.Evaluate(Read);
                    component.SetValue(result);
                }
                else
                {
                    result = ReadPhysical(source);
                }

                cache[source] = result;
                return result;
            }

            double? evaluated = _points.ContainsKey(point) ? Read(point) : null;
            value = evaluated ?? 0;
            return evaluated.HasValue;
        }
    }

    private double? ReadPhysical(IoReference point)
    {
        if (_io is null || point.Index >= 3000)
        {
            return null;
        }

        bool level = false;
        double value = 0;
        bool valid = point.Type switch
        {
            "DI" => _io.TryReadDi(point.Index, out level),
            "DO" => _io.TryReadDo(point.Index, out level),
            "AI" => _io.TryReadAi(point.Index, out value),
            _ => _io.TryReadAo(point.Index, out value),
        };
        if (point.IsBoolean)
        {
            value = level ? 1 : 0;
        }

        return valid && double.IsFinite(value) ? value : null;
    }
}
