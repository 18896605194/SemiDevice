using xyz.Elements.Elements;
using xyz.Elements.Interfaces;

namespace xyz.Elements.Models;

/// <summary>一棵装配树的虚拟输入索引和依赖关系；不使用跨设备/跨装配的静态点表。</summary>
internal sealed class VirtualInputRegistry
{
    private readonly Dictionary<IoReference, IVirtualInputElement> _points;
    private readonly IIo? _io;
    private readonly object _gate = new();

    private VirtualInputRegistry(IEnumerable<IVirtualInputElement> points, IIo? io)
    {
        _io = io;
        _points = [];
        foreach (var element in points)
        {
            var point = new IoReference(element.PointType, element.Index);
            if (!_points.TryAdd(point, element))
            {
                throw new InvalidOperationException($"虚拟点 {point} 重复：{_points[point].FullPath}、{element.FullPath}");
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
                    throw new InvalidOperationException($"{source} 是物理点，需要装配 IoElement");
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

    public static void Attach(IReadOnlyList<ElementBase> roots)
    {
        var all = Flatten(roots).ToList();
        var points = all.OfType<IVirtualInputElement>().ToList();
        if (points.Count == 0)
        {
            return;
        }

        var io = all.OfType<IIo>().ToList();
        if (io.Count > 1)
        {
            throw new InvalidOperationException("全局虚拟输入索引只能绑定一个 IoElement 读写入口");
        }

        var registry = new VirtualInputRegistry(points, io.SingleOrDefault());
        foreach (var point in points)
        {
            point.Bind(registry);
        }

        io.SingleOrDefault()?.AttachVirtualInputs(registry);
    }

    private static IEnumerable<ElementBase> Flatten(IEnumerable<ElementBase> roots)
    {
        foreach (var element in roots)
        {
            yield return element;
            foreach (var child in Flatten(element.Children))
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

            IoPointTable? table;
            switch (source.Type)
            {
                case "DI":
                    table = _io?.Di;
                    break;
                case "DO":
                    table = _io?.Do;
                    break;
                case "AI":
                    table = _io?.Ai;
                    break;
                default:
                    table = _io?.Ao;
                    break;
            }

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
                if (_points.TryGetValue(source, out var element))
                {
                    result = element.Compiled.Evaluate(Read);
                    element.SetValue(result);
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
        bool valid;
        switch (point.Type)
        {
            case "DI":
                valid = _io.TryReadDi(point.Index, out level);
                break;
            case "DO":
                valid = _io.TryReadDo(point.Index, out level);
                break;
            case "AI":
                valid = _io.TryReadAi(point.Index, out value);
                break;
            default:
                valid = _io.TryReadAo(point.Index, out value);
                break;
        }

        if (point.IsBoolean)
        {
            value = level ? 1 : 0;
        }

        return valid && double.IsFinite(value) ? value : null;
    }
}
