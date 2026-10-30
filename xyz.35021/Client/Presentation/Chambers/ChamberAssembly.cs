using System.Windows.Media;
using System.Windows.Media.Media3D;
using xyz.Client.Presentation.Controls.ThreeD;
using xyz.Client.Presentation.Models;
using xyz.Shared.Dtos;

namespace xyz._35021.Client.Presentation.Chambers;

/// <summary>项目的装配与反馈关联。项目提供的控件优先，平台控件补齐剩余种类。</summary>
internal sealed class ChamberAssembly : IDisposable
{
    private const double ArmScale = 0.7;
    private readonly ChamberLayoutModel _layout;
    private readonly Dictionary<string, Part> _parts = [];
    private bool _applying;
    public ModelVisual3D World { get; } = new();
    public IReadOnlyDictionary<string, ChamberPartVisual> Visuals { get; }
    public ChamberAssembly(ChamberLayoutModel layout, ChamberVisualCatalog catalog)
    {
        _layout = layout;
        var lights = new Model3DGroup(); lights.Children.Add(new AmbientLight(Colors.Gray));
        lights.Children.Add(new DirectionalLight(Colors.White, new Vector3D(-1, -3, -2)));
        World.Children.Add(new ModelVisual3D { Content = lights });
        foreach (var data in layout.Parts)
        {
            var visual = catalog.Create(new ChamberVisualContext(data.Kind, layout.ComponentRoot, data.Component, data.ParentId, layout.ChamberType));
            var part = new Part(data, visual); _parts.Add(data.Id, part);
            visual.GeometryChanged += UpdateMounts;
        }
        foreach (var part in _parts.Values)
        {
            part.Root.Children.Add(part.Visual.Visual);
            if (part.Data.ParentId.Length == 0) World.Children.Add(part.Root);
            else if (part.Data.Kind == "Nozzle") _parts[part.Data.ParentId].Visual.MovingAttachments.Add(part.Root);
            else _parts[part.Data.ParentId].Root.Children.Add(part.Root);
        }
        Visuals = _parts.ToDictionary(p => p.Key, p => p.Value.Visual);
    }
    public void Apply(ChamberDeviceDataDto snapshot, bool animate, object? wafer)
    {
        var feedback = new Dictionary<string, ChamberVisualFeedback>(StringComparer.OrdinalIgnoreCase);
        void Cylinder(ChamberCylinderDto? value) { if (value is not null) feedback[Key(value.Path)] = new(value.Position, Animate: animate); }
        Cylinder(snapshot.Door); foreach (var bowl in snapshot.Bowls) Cylinder(bowl);
        if (snapshot.Spin is { } spin) feedback[Key(spin.Path)] = new(IsMoving: spin.HasPlcData && spin.IsBusy,
            IsError: spin.IsError || !spin.HasPlcData, Animate: animate, Clockwise: spin.CurrentSpeed >= 0, Wafer: wafer);
        foreach (var arm in snapshot.Arms)
        {
            feedback[Key(arm.Path)] = new(IsMoving: arm.HasPlcData && arm.IsBusy, IsError: arm.IsError || !arm.HasPlcData, Animate: animate);
            Cylinder(arm.Lift);
            foreach (var nozzle in arm.Nozzles) feedback[Key(nozzle.Path)] = new(IsOn: nozzle.IsOn, Animate: animate);
        }
        _applying = true;
        try
        {
            foreach (var part in _parts.Values)
            {
                var data = part.Data;
                part.State = feedback.GetValueOrDefault(data.Component) ?? new ChamberVisualFeedback(Animate: animate);
                Placement(part); part.Sizes = new(data.Sizes);
                double scale = data.ParentId.Length > 0 ? ArmScale : 1;
                if (data.Kind == "HomeCup") { part.Sizes["Radius"] /= scale; part.Sizes["Height"] /= scale; }
                if (data.Kind is not ("Arm" or "Nozzle")) part.Visual.Apply(part.Sizes, part.State);
            }
            var disk = _layout.Parts.FirstOrDefault(p => p.Kind == "Disk");
            foreach (var part in _parts.Values.Where(p => p.Data.Kind == "Arm"))
            {
                var data = part.Data; var mount = Mount(part); var pivot = part.Root.Transform.Transform(mount);
                double dx = disk is null ? data.Size("ArmLength") : disk.X - pivot.X, dz = disk is null ? 0 : disk.Z - pivot.Z;
                double length = data.AutoLength ? Math.Max(0.05, Math.Sqrt(dx * dx + dz * dz)) : data.Size("ArmLength");
                part.Sizes = new() { ["Length"] = length / ArmScale, ["Width"] = data.Size("ArmWidth") / ArmScale,
                    ["Thickness"] = data.Size("ArmThickness") / ArmScale, ["PivotRadius"] = data.Size("PivotRadius") / ArmScale };
                part.Visual.Apply(part.Sizes, part.State);
                var axis = snapshot.Arms.First(a => Key(a.Path) == data.Component);
                double yaw = data.Yaw * Math.PI / 180;
                double center = Math.Atan2(-(Math.Sin(yaw) * dx + Math.Cos(yaw) * dz), Math.Cos(yaw) * dx - Math.Sin(yaw) * dz) * 180 / Math.PI;
                double sweep = ((center - data.HomeAngle) % 360 + 540) % 360 - 180;
                part.Visual.SetAngle(data.HomeAngle + sweep * axis.Reach, animate && axis.HasPlcData);
            }
            foreach (var part in _parts.Values.Where(p => p.Data.Kind == "Nozzle"))
            {
                double scale = part.Data.ParentId.Length > 0 ? ArmScale : 1;
                double length = part.Data.AutoLength && part.Data.ParentId.Length > 0 ? _parts[part.Data.ParentId].Visual.Length * ArmScale : part.Data.Size("Length");
                part.Sizes["Length"] = length * part.Data.Size("LengthScale") / scale;
                part.Sizes["StreamLength"] /= scale; part.Visual.Apply(part.Sizes, part.State);
            }
        }
        finally { _applying = false; }
        UpdateMounts();
    }
    private string Key(string path) => ChamberLayoutComposer.Relative(path, _layout.ComponentRoot, _layout.ComponentRoot.Split('.')[^1]);
    private Point3D Mount(Part arm)
    {
        var support = _parts.Values.FirstOrDefault(p => p.Data.ParentId == arm.Data.Id && p.Data.Kind == "Lift");
        return support is null ? new Point3D(0, 0.85, 0) : support.Root.Transform.Transform(new Point3D(0, support.Visual.MountHeight, 0));
    }
    private void Placement(Part part)
    {
        var data = part.Data; double parentScale = data.ParentId.Length > 0 ? ArmScale : 1;
        double sx = data.Kind == "Arm" ? ArmScale : data.Kind == "Lift" ? data.Size("WidthScale") : 1;
        double sy = data.Kind is "Bowl" or "Lift" ? data.Size("HeightScale") : data.Kind == "Nozzle" ? data.Size("DiameterScale") : sx;
        double sz = data.Kind == "Door" ? data.Size("DepthScale") : data.Kind == "Nozzle" ? data.Size("DiameterScale") : sx;
        double x = data.X / parentScale, y = data.Y / parentScale, z = data.Z / parentScale;
        if (data.Kind == "HomeCup")
        {
            var arm = _parts[data.ParentId]; var mount = Mount(arm); double home = arm.Data.HomeAngle * Math.PI / 180;
            x += mount.X + arm.Visual.Length * Math.Cos(home); z += mount.Z - arm.Visual.Length * Math.Sin(home);
        }
        var transform = new Transform3DGroup(); transform.Children.Add(new ScaleTransform3D(sx, sy, sz));
        transform.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), data.Yaw)));
        transform.Children.Add(new TranslateTransform3D(x, y, z)); part.Root.Transform = transform;
    }
    private void UpdateMounts()
    {
        if (_applying) return;
        _applying = true;
        try
        {
            foreach (var part in _parts.Values.Where(p => p.Data.Kind == "Arm"))
            {
                var mount = Mount(part); part.Visual.Visual.Transform = new TranslateTransform3D(mount.X, mount.Y + part.Data.Size("ArmThickness") / ArmScale * 1.5, mount.Z);
            }
            foreach (var part in _parts.Values.Where(p => p.Data.Kind == "HomeCup")) Placement(part);
            foreach (var part in _parts.Values.Where(p => p.Data.Kind == "Nozzle" && p.Data.ParentId.Length > 0))
            {
                var arm = _parts[part.Data.ParentId];
                var transform = part.Visual.Visual.TransformToAncestor(World);
                var tip = transform.Transform(new Point3D(part.Visual.Length, -part.Visual.OutletDrop, 0));
                var below = transform.Transform(new Point3D(part.Visual.Length, -part.Visual.OutletDrop - 1, 0));
                double landing = _layout.Parts.Single(p => p.Kind == "Base").Y;
                var disk = _layout.Parts.FirstOrDefault(p => p.Kind == "Disk");
                if (disk is not null && Math.Pow(tip.X - disk.X, 2) + Math.Pow(tip.Z - disk.Z, 2) < Math.Pow(disk.Size("Radius"), 2)) landing = disk.Y + disk.Size("Thickness") + 0.001;
                var cup = _parts.Values.FirstOrDefault(p => p.Data.ParentId == arm.Data.Id && p.Data.Kind == "HomeCup");
                part.HitsCup = false;
                if (cup is not null && Math.Abs(arm.Visual.Angle - arm.Data.HomeAngle) < 1)
                {
                    var center = cup.Visual.Visual.TransformToAncestor(World).Transform(new Point3D(0, cup.Sizes["Height"] * (0.41 / 0.655), 0));
                    if (Math.Pow(tip.X - center.X, 2) + Math.Pow(tip.Z - center.Z, 2) < Math.Pow(cup.Data.Size("Radius"), 2)) { landing = center.Y; part.HitsCup = true; }
                }
                part.Sizes["StreamLength"] = Math.Max(0, (tip.Y - landing) / Math.Max(0.001, tip.Y - below.Y));
                part.Visual.Apply(part.Sizes, part.State);
            }
            foreach (var cup in _parts.Values.Where(p => p.Data.Kind == "HomeCup"))
                cup.Visual.Apply(cup.Sizes, cup.State with { IsOn = _parts.Values.Any(p => p.Data.Kind == "Nozzle" && p.Data.ParentId == cup.Data.ParentId && p.State.IsOn && p.HitsCup) });
        }
        finally { _applying = false; }
    }
    public void Dispose() { foreach (var part in _parts.Values) { part.Visual.GeometryChanged -= UpdateMounts; part.Visual.Dispose(); } }
    private sealed class Part(ChamberPartLayout data, ChamberPartVisual visual)
    {
        public ChamberPartLayout Data { get; } = data;
        public ChamberPartVisual Visual { get; } = visual;
        public ModelVisual3D Root { get; } = new();
        public Dictionary<string, double> Sizes { get; set; } = [];
        public ChamberVisualFeedback State { get; set; } = new();
        public bool HitsCup { get; set; }
    }
}
