using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using xyz.Client.Presentation.Controls.ThreeD;
using xyz.Shared.Dtos;

namespace ChamberLayoutEditor;

public sealed partial class ChamberLayoutScene
{
    private sealed class PartVisual : IDisposable
    {
        private const double ArmScale = 0.7;
        private readonly ChamberLayoutScene _scene;
        private readonly ChamberPlacement _part;
        private readonly ChamberPartVisual _visual;
        private Dictionary<string, double> _sizes = [];
        private ChamberVisualFeedback _state = new();
        private ChamberPlacement? _target;
        private bool _hitsCup;
        public ModelVisual3D Root { get; } = new PlacementRoot();
        private PartVisual? Parent => _part.ParentId.Length > 0 ? _scene._parts[_part.ParentId] : null;
        private double ParentScale => Parent is null ? 1 : ArmScale;
        public Point3D Origin => Root.TransformToAncestor(_scene._world).Transform(new Point3D());
        public PartVisual(ChamberLayoutScene scene, ChamberPlacement part)
        {
            _scene = scene; _part = part;
            _visual = scene._visualCatalog.Create(new ChamberVisualContext(part.Kind, scene._model?.Document?.ComponentRoot ?? "",
                part.Component, part.ParentId, scene._model?.Document?.ChamberType ?? ""));
            if (_visual.Visual is HardwareVisual3D native) scene.Theme(native);
            if (_visual.Visual is ChamberBaseVisual3D plate) scene._base ??= plate;
            Root.Children.Add(_visual.Visual); _visual.GeometryChanged += _scene.UpdateAttachments;
        }
        public void Attach()
        {
            if (Parent is not { } parent) _scene._world.Children.Add(Root);
            else if (_part.Kind == "Nozzle") parent._visual.MovingAttachments.Add(Root);
            else parent.Root.Children.Add(Root);
        }
        public Vector3D PlacementDelta(Vector3D delta)
        {
            if (VisualTreeHelper.GetParent(Root) is not ModelVisual3D parent) return delta;
            var inverse = parent.TransformToAncestor(_scene._world).Inverse!;
            return (inverse.Transform(new Point3D(delta.X, delta.Y, delta.Z)) - inverse.Transform(new Point3D())) * ParentScale;
        }
        private void UpdatePlacement()
        {
            double sx = _part.IsArm ? ArmScale : _part.Kind == "Lift" ? _part.Size("WidthScale") : 1;
            double sy = _part.Kind is "Bowl" or "Lift" ? _part.Size("HeightScale") : _part.Kind == "Nozzle" ? _part.Size("DiameterScale") : sx;
            double sz = _part.Kind == "Door" ? _part.Size("DepthScale") : _part.Kind == "Nozzle" ? _part.Size("DiameterScale") : sx;
            double x = _part.X / ParentScale, y = _part.Y / ParentScale, z = _part.Z / ParentScale;
            if (_part.Kind == "HomeCup" && Parent is { } parent)
            {
                double home = parent._part.HomeAngle * Math.PI / 180; var mount = parent.Mount();
                x += mount.X + parent._visual.Length * Math.Cos(home); z += mount.Z - parent._visual.Length * Math.Sin(home);
            }
            var transform = new Transform3DGroup(); transform.Children.Add(new ScaleTransform3D(sx, sy, sz));
            transform.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), _part.Yaw)));
            transform.Children.Add(new TranslateTransform3D(x, y, z)); Root.Transform = transform;
        }
        private Point3D Mount()
        {
            var support = _scene._parts.Values.FirstOrDefault(p => p._part.ParentId == _part.Id && p._part.Kind == "Lift");
            return support is null ? new Point3D(0, 0.85, 0) : support.Root.Transform.Transform(new Point3D(0, support._visual.MountHeight, 0));
        }
        public void Apply(ChamberEditorViewModel model, ChamberPlacement? target)
        {
            _target = target; bool preview = model.IsPreview;
            var motion = model.MotionFor(_part.Id); UpdatePlacement();
            _state = new(preview && (motion.Raised || motion.Open) ? CylinderPosition.Opened : CylinderPosition.Closed,
                IsOn: preview && motion.Spray, IsMoving: _part.Kind == "Disk" && preview && motion.Spin,
                IsSelected: ReferenceEquals(model.Selected, _part), Animate: preview && _scene.IsVisible);
            _sizes = _part.Dimensions.ToDictionary(d => d.Key, d => d.SafeValue);
            if (_part.Kind == "HomeCup") { _sizes["Radius"] /= ParentScale; _sizes["Height"] /= ParentScale; }
            if (_part.Kind == "Nozzle")
            {
                double length = _part.AutoLength && Parent is { } parent ? parent._visual.Length * ArmScale : _part.Size("Length");
                _sizes["Length"] = length * _part.Size("LengthScale") / ParentScale; _sizes["StreamLength"] /= ParentScale;
            }
            if (!_part.IsArm) _visual.Apply(_sizes, _state);
        }
        public void ApplyArm(ChamberEditorViewModel model)
        {
            if (!_part.IsArm) return;
            var pivot = Root.Transform.Transform(Mount());
            double dx = _target is null ? _part.Size("ArmLength") : _target.X - pivot.X, dz = _target is null ? 0 : _target.Z - pivot.Z;
            double length = _part.AutoLength ? Math.Max(0.05, Math.Sqrt(dx * dx + dz * dz)) : _part.Size("ArmLength");
            _sizes = new() { ["Length"] = length / ArmScale, ["Width"] = _part.Size("ArmWidth") / ArmScale,
                ["Thickness"] = _part.Size("ArmThickness") / ArmScale, ["PivotRadius"] = _part.Size("PivotRadius") / ArmScale };
            _visual.Apply(_sizes, _state);
            double yaw = _part.Yaw * Math.PI / 180;
            double center = Math.Atan2(-(Math.Sin(yaw) * dx + Math.Cos(yaw) * dz), Math.Cos(yaw) * dx - Math.Sin(yaw) * dz) * 180 / Math.PI;
            double sweep = ((center - _part.HomeAngle) % 360 + 540) % 360 - 180;
            _visual.SetAngle(_part.HomeAngle + sweep * (model.IsPreview ? model.MotionFor(_part.Id).Reach : 0), model.IsPreview && _scene.IsVisible && !model.IsDragging);
        }
        public void UpdateMount()
        {
            if (_part.IsArm)
            {
                var mount = Mount(); _visual.Visual.Transform = new TranslateTransform3D(mount.X, mount.Y + _part.Size("ArmThickness") / ArmScale * 1.5, mount.Z);
            }
            if (_part.Kind == "HomeCup") UpdatePlacement();
            if (_part.Kind == "Nozzle")
            {
                double length = _part.AutoLength && Parent is { } parent ? parent._visual.Length * ArmScale : _part.Size("Length");
                _sizes["Length"] = length * _part.Size("LengthScale") / ParentScale;
                _visual.Apply(_sizes, _state);
            }
        }
        public void UpdateStreams()
        {
            _hitsCup = false;
            if (_part.Kind != "Nozzle" || Parent is not { } parent) return;
            var transform = _visual.Visual.TransformToAncestor(_scene._world);
            var tip = transform.Transform(new Point3D(_visual.Length, -_visual.OutletDrop, 0));
            var below = transform.Transform(new Point3D(_visual.Length, -_visual.OutletDrop - 1, 0));
            double landing = _scene.BaseSurfaceHeight(tip);
            var cup = _scene._parts.Values.FirstOrDefault(p => p._part.ParentId == parent._part.Id && p._part.Kind == "HomeCup");
            if (cup is not null && Math.Abs(parent._visual.Angle - parent._part.HomeAngle) < 1)
            {
                var center = cup._visual.Visual.TransformToAncestor(_scene._world).Transform(new Point3D(0, cup._sizes["Height"] * (0.41 / 0.655), 0));
                if (Math.Pow(tip.X - center.X, 2) + Math.Pow(tip.Z - center.Z, 2) < Math.Pow(cup._part.Size("Radius"), 2)) { landing = center.Y; _hitsCup = true; }
            }
            if (!_hitsCup && _target is { } disk && Math.Pow(tip.X - disk.X, 2) + Math.Pow(tip.Z - disk.Z, 2) < Math.Pow(disk.Size("Radius"), 2))
                landing = disk.Y + disk.Size("Thickness") + 0.001;
            _sizes["StreamLength"] = Math.Max(0, (tip.Y - landing) / Math.Max(0.001, tip.Y - below.Y)); _visual.Apply(_sizes, _state);
        }
        public void UpdateCup()
        {
            if (_part.Kind == "HomeCup") _visual.Apply(_sizes, _state with
            { IsOn = _scene._parts.Values.Any(p => p._part.ParentId == _part.ParentId && p._part.Kind == "Nozzle" && p._hitsCup && p._state.IsOn) });
        }
        public (double CenterError, double HomeError, double TipHeight) Probe()
        {
            double radians = _visual.Angle * Math.PI / 180;
            var tip = _visual.Visual.TransformToAncestor(_scene._world).Transform(new Point3D(_visual.Length * Math.Cos(radians), 0, -_visual.Length * Math.Sin(radians)));
            var cup = _scene._parts.Values.FirstOrDefault(p => p._part.ParentId == _part.Id && p._part.Kind == "HomeCup"); var home = cup?.Origin ?? tip;
            return (_target is null ? 0 : Math.Sqrt(Math.Pow(tip.X - _target.X, 2) + Math.Pow(tip.Z - _target.Z, 2)),
                Math.Sqrt(Math.Pow(tip.X - home.X, 2) + Math.Pow(tip.Z - home.Z, 2)), tip.Y);
        }
        public (double Angle, bool Raised, bool Spray, bool Spin, bool Open) ProbeMotion() =>
            (_visual.Angle, _state.Position == CylinderPosition.Opened && _part.Kind is "Lift" or "Bowl", _state.IsOn,
                _part.Kind == "Disk" && _state.IsMoving && _state.Animate, _part.Kind == "Door" && _state.Position == CylinderPosition.Opened);
        public IReadOnlyDictionary<string, double> ProbeDimensions()
        {
            var result = _part.Dimensions.ToDictionary(d => d.Key, d => d.SafeValue);
            if (_part.IsArm) { result["ArmLength"] = _visual.Length * ArmScale; result["ArmWidth"] = _sizes["Width"] * ArmScale; }
            if (_part.Kind == "Nozzle") result["Length"] = _visual.Length * ParentScale;
            return result;
        }
        public void Dispose() { _visual.GeometryChanged -= _scene.UpdateAttachments; _visual.Dispose(); }
    }
}
