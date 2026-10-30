using System.IO;
using System.Windows;
using System.Windows.Media;

namespace CdsAssemblyPreview;

/// <summary>仅生成装机组件的位置，不推断管路数量和连接关系。</summary>
public static class CdsLayoutComposer
{
    public static string Relative(string root, string component) => component.StartsWith(root + ".", StringComparison.Ordinal)
        ? component[(root.Length + 1)..] : component;

    public static CdsLayout Compose(CdsSnapshot snapshot, CdsLayout? saved)
    {
        if (saved is not null && saved.ComponentRoot != snapshot.ComponentRoot)
            throw new InvalidDataException("CDS layout belongs to a different module.");
        if (snapshot.Tanks.Count > 24 || snapshot.Tanks.Select(t => t.Data.Path).Distinct(StringComparer.Ordinal).Count() != snapshot.Tanks.Count)
            throw new InvalidDataException("Invalid CDS tank roster.");
        int columns = Math.Min(3, Math.Max(1, snapshot.Tanks.Count)), rows = Math.Max(1, (snapshot.Tanks.Count + columns - 1) / columns);
        var result = new CdsLayout { ComponentRoot = snapshot.ComponentRoot,
            Width = Math.Max(saved?.Width ?? 0, columns * 650), Height = Math.Max(saved?.Height ?? 0, rows * 560) };
        var usedIds = new HashSet<string>(saved?.Nodes.Select(n => n.Id) ?? [], StringComparer.Ordinal);
        string Id(string seed) { while (!usedIds.Add(seed)) seed += "_"; return seed; }
        CdsNode? Placement(string kind, string path) => saved?.Nodes.SingleOrDefault(n => n.Kind == kind
            && Relative(snapshot.ComponentRoot, n.Component) == Relative(snapshot.ComponentRoot, path));
        for (int i = 0; i < snapshot.Tanks.Count; i++)
        {
            var tank = snapshot.Tanks[i];
            string component = Relative(snapshot.ComponentRoot, tank.Data.Path);
            double x = i % columns * 650, y = i / columns * 560;
            result.Nodes.Add(Placement("Tank", component) ?? new CdsNode("Tank", Id("Tank" + (i + 1)), component, component, x + 220, y + 130, 160, 230));
            var body = result.Nodes[^1]; int sensorIndex = 0;
            foreach (var sensor in CdsSensors.For(tank))
            {
                string path = component + "." + sensor.Name;
                result.Nodes.Add(Placement("Sensor", path) ?? new CdsNode("Sensor", Id("Tank" + (i + 1) + sensor.Name), path, sensor.Name,
                    Math.Clamp(body.X + body.Width + 16, 0, result.Width - 105), Math.Clamp(body.Y - 12 + sensorIndex * 24, 0, result.Height - 22),
                    105, 22, SensorAlarm: sensor.Name is "HH" or "Overfill"));
                sensorIndex++;
            }
            foreach (var route in tank.Routes)
            {
                for (int j = 0; j < route.ValvePaths.Count; j++)
                {
                    string valve = Relative(snapshot.ComponentRoot, route.ValvePaths[j]);
                    (double vx, double vy) = route.Kind switch { "fill" => (110, 200), "supply" => (450, 270), _ => (300, 420) };
                    result.Nodes.Add(Placement("Valve", valve) ?? new CdsNode("Valve", Id("Tank" + (i + 1) + route.Kind + (j + 1)),
                        valve, valve[(valve.LastIndexOf('.') + 1)..], x + vx + j * 55, y + vy, 36, 36));
                }
            }
        }
        // 先保留全部已有位置（包括外部接口），新增组件只占用剩余空位。
        foreach (var terminal in saved?.Nodes.Where(n => n.Kind == "Terminal") ?? []) result.Nodes.Add(terminal);
        var extras = snapshot.Devices.Where(d => !result.Nodes.Any(n => n.Component == Relative(snapshot.ComponentRoot, d.Path))).ToArray();
        foreach (var device in extras)
            if (Placement(device.Kind, device.Path) is { } placed) result.Nodes.Add(placed with { VisualKind = device.VisualKind });
        foreach (var device in extras)
        {
            string component = Relative(snapshot.ComponentRoot, device.Path);
            if (result.Nodes.Any(n => n.Component == component)) continue;
            double width = device.Kind == "Hardware" ? 200 : device.Kind == "Valve" ? 36 : device.Kind == "Sensor" ? 105 : 115;
            double height = device.Kind == "Hardware" && device.VisualKind is FluidHardwareKind.CentrifugalPump or FluidHardwareKind.InlineHeater
                ? width * ProcessHardwareProjection.Bounds(device.VisualKind.Value).Height / ProcessHardwareProjection.Bounds(device.VisualKind.Value).Width
                : device.Kind == "Hardware" ? 120 : device.Kind == "Valve" ? 36 : device.Kind == "Sensor" ? 22 : 28;
            var owner = result.Nodes.FirstOrDefault(n => n.Kind == "Tank" && component.StartsWith(n.Component + ".", StringComparison.Ordinal));
            var preferred = new Point(70, 55);
            if (owner is not null)
            {
                preferred = new Point(owner.X + owner.Width + 16, owner.Y);
                if (device.Kind is "Sensor" or "Analog")
                {
                    var signals = result.Nodes.Where(n => n.Kind is "Sensor" or "Analog" && n.Component.StartsWith(owner.Component + ".", StringComparison.Ordinal)).ToArray();
                    if (signals.Length > 0) preferred = new Point(signals[0].X, signals.Max(n => n.Y + n.Height) + 4);
                }
            }
            Point? position = FindSpace(result, preferred, width, height);
            if (position is null)
            {
                // 仅在现有画布确实放不下时扩展，不能按“额外组件数量”强行增加一整行。
                var expanded = new CdsLayout { ComponentRoot = result.ComponentRoot, Width = result.Width, Height = result.Height + height + 24 };
                expanded.Nodes.AddRange(result.Nodes); result = expanded;
                position = new Point(Math.Clamp(preferred.X, 12, result.Width - width - 12), result.Height - height - 12);
            }
            result.Nodes.Add(new CdsNode(device.Kind, Id("Device" + (result.Nodes.Count + 1)), component, device.Name,
                position.Value.X, position.Value.Y, width, height,
                SensorAlarm: device.Name is "HH" or "Overfill" || device.Name.Contains("Leak", StringComparison.OrdinalIgnoreCase), VisualKind: device.VisualKind));
        }
        foreach (var pipe in saved?.Pipes ?? [])
            if (result.Nodes.Any(n => n.Id == Owner(pipe.From)) && result.Nodes.Any(n => n.Id == Owner(pipe.To))) result.Pipes.Add(pipe);
        return result;
    }

    private static Point? FindSpace(CdsLayout layout, Point preferred, double width, double height)
    {
        var occupied = layout.Nodes.Select(n =>
        {
            var bounds = new RotateTransform(n.Angle, n.X + n.Width / 2, n.Y + n.Height / 2).TransformBounds(new Rect(n.X, n.Y, n.Width, n.Height));
            // 阀门执行器、状态文字及硬件标题也属于可见区域，不能只避开本体。
            if (n.Kind == "Valve") bounds.Union(new Rect(n.X - 16, n.Y - 31, n.Width + 100, n.Height + 60));
            if (n.Kind == "Hardware") bounds.Union(new Rect(n.X, Math.Max(0, n.Y - 28), n.Width, n.Height + 56));
            if (n.Kind == "Terminal") bounds.Union(new Rect(Math.Max(0, n.X - 18), Math.Max(0, n.Y - 8), 115, 42));
            bounds.Inflate(2, 2); return bounds;
        }).ToArray();
        double maxX = layout.Width - width, maxY = layout.Height - height;
        preferred = new Point(Math.Clamp(preferred.X, 0, maxX), Math.Clamp(preferred.Y, 0, maxY));
        bool Fits(Point p) => occupied.All(r => !r.IntersectsWith(new Rect(p.X, p.Y, width, height)));
        if (Fits(preferred)) return preferred;
        Point? best = null; double distance = double.PositiveInfinity;
        for (double y = 0; y <= maxY; y += 12)
            for (double x = 0; x <= maxX; x += 12)
            {
                var point = new Point(x, y); double score = (point - preferred).LengthSquared;
                if (score < distance && Fits(point)) { best = point; distance = score; }
            }
        return best;
    }

    public static string Owner(string port) => port[..port.LastIndexOf('.')];
}
