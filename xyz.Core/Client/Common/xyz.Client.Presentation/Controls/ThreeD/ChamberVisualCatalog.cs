using System.ComponentModel;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;
using xyz.Shared.Dtos;

namespace xyz.Client.Presentation.Controls.ThreeD;

/// <summary>机型可提供自己的控件适配器；返回 null 的种类交给平台补齐。</summary>
public interface IChamberVisualProvider
{
    ChamberPartVisual? Create(ChamberVisualContext context);
}

public sealed record ChamberVisualContext(string Kind, string ComponentRoot = "", string Component = "", string ParentId = "", string ChamberType = "");

public sealed record ChamberVisualFeedback(CylinderPosition Position = CylinderPosition.Closed, bool IsOn = false,
    bool IsMoving = false, bool IsError = false, bool IsSelected = false, bool Animate = false,
    bool Clockwise = true, object? Wafer = null);

/// <summary>项目控件的最小绘图契约，不要求继承某个具体平台控件。</summary>
public abstract class ChamberPartVisual : IDisposable
{
    public abstract ModelVisual3D Visual { get; }
    public virtual Visual3DCollection MovingAttachments => Visual.Children;
    public virtual double MountHeight => 0;
    public virtual double Length => 0;
    public virtual double Angle => 0;
    public virtual double OutletDrop => 0;
    public event Action? GeometryChanged;
    protected void OnGeometryChanged() => GeometryChanged?.Invoke();
    public abstract void Apply(IReadOnlyDictionary<string, double> sizes, ChamberVisualFeedback feedback);
    public virtual void SetAngle(double angle, bool animate) { }
    public virtual void Dispose() { }
}

public sealed class ChamberVisualCatalog(IEnumerable<IChamberVisualProvider>? providers = null)
{
    private readonly IChamberVisualProvider[] _providers = providers?.ToArray() ?? [];
    public ChamberPartVisual Create(string kind) => Create(new ChamberVisualContext(kind));
    public ChamberPartVisual Create(ChamberVisualContext context) => CreateProjectVisual(context) ?? new PlatformVisual(context.Kind);
    /// <summary>工具在导出 XAML 时选取项目控件；平台默认控件无需创建实例。</summary>
    public ChamberPartVisual? CreateProjectVisual(ChamberVisualContext context)
    {
        foreach (var provider in _providers)
            if (provider.Create(context) is { } visual) return visual;
        return null;
    }

    private sealed class PlatformVisual : ChamberPartVisual
    {
        private readonly HardwareVisual3D _visual;
        private readonly List<(DependencyPropertyDescriptor Descriptor, EventHandler Handler)> _listeners = [];
        public override ModelVisual3D Visual => _visual;
        public override Visual3DCollection MovingAttachments => _visual is ArmVisual3D arm ? arm.Attachments : _visual.Children;
        public override double MountHeight => (_visual as LiftVisual3D)?.MountHeight ?? 0;
        public override double Length => _visual is ArmVisual3D arm ? arm.Length : (_visual as FluidPipeVisual3D)?.Length ?? 0;
        public override double Angle => (_visual as ArmVisual3D)?.Angle ?? 0;
        public override double OutletDrop => _visual is FluidPipeVisual3D ? FluidPipeVisual3D.OutletDrop : 0;
        public PlatformVisual(string kind)
        {
            _visual = kind switch
            {
                "Base" => new ChamberBaseVisual3D(), "Arm" => new ArmVisual3D(), "Lift" => new LiftVisual3D(),
                "Bowl" => new BowlVisual3D(), "Door" => new DoorVisual3D(), "Disk" => new DiskVisual3D { IsDiskVisible = true },
                "HomeCup" => new HomeCupVisual3D(), "Nozzle" => new FluidPipeVisual3D(),
                _ => throw new ArgumentException("Unknown chamber visual kind.", nameof(kind))
            };
            if (_visual is ArmVisual3D) Listen(ArmVisual3D.AngleProperty);
            if (_visual is LiftVisual3D) Listen(LiftVisual3D.MountHeightProperty);
        }
        private void Listen(DependencyProperty property)
        {
            var descriptor = DependencyPropertyDescriptor.FromProperty(property, _visual.GetType());
            EventHandler handler = (_, _) => OnGeometryChanged(); descriptor.AddValueChanged(_visual, handler);
            _listeners.Add((descriptor, handler));
        }
        public override void Apply(IReadOnlyDictionary<string, double> sizes, ChamberVisualFeedback state)
        {
            double Size(string key) => sizes[key];
            bool raised = state.Position == CylinderPosition.Opened, unknown = state.Position == CylinderPosition.Unknown;
            _visual.IsSelected = state.IsSelected; _visual.IsMoving = state.IsMoving; _visual.IsError = state.IsError;
            switch (_visual)
            {
                case ChamberBaseVisual3D part: part.Length = Size("Length"); part.Width = Size("Width"); part.Thickness = Size("Thickness"); break;
                case ArmVisual3D part: part.Length = Size("Length"); part.Width = Size("Width"); part.Thickness = Size("Thickness"); part.PivotRadius = Size("PivotRadius"); break;
                case BowlVisual3D part: part.Radius = Size("Radius"); part.IsRaised = raised; part.IsUnknown = unknown; break;
                case LiftVisual3D part: part.IsRaised = raised; part.IsUnknown = unknown; break;
                case DoorVisual3D part: part.Width = Size("Width"); part.Height = Size("Height"); part.IsOpen = raised; part.IsUnknown = unknown; break;
                case DiskVisual3D part:
                    part.Radius = Size("Radius"); part.Thickness = Size("Thickness"); part.SpindleHeight = Size("SpindleHeight");
                    part.Data = state.Wafer; part.RotationSpeed = state.Animate && state.IsMoving ? 100 : 0; part.RotateClockwise = state.Clockwise; break;
                case HomeCupVisual3D part: part.Radius = Size("Radius"); part.Height = Size("Height"); part.IsDraining = state.IsOn; break;
                case FluidPipeVisual3D part:
                    part.Length = Size("Length"); part.IsFlowing = state.IsOn; part.IsAnimationEnabled = state.Animate;
                    if (Math.Abs(part.StreamLength - Size("StreamLength")) > 0.0001) part.StreamLength = Size("StreamLength"); break;
            }
        }
        public override void SetAngle(double angle, bool animate)
        {
            if (_visual is not ArmVisual3D arm || Math.Abs(arm.Angle - angle) < 1e-6) return;
            double from = arm.Angle; arm.BeginAnimation(ArmVisual3D.AngleProperty, null); arm.Angle = angle;
            if (animate) arm.BeginAnimation(ArmVisual3D.AngleProperty,
                new DoubleAnimation(from, angle, TimeSpan.FromMilliseconds(200)) { FillBehavior = FillBehavior.Stop });
        }
        public override void Dispose()
        {
            foreach (var (descriptor, handler) in _listeners) descriptor.RemoveValueChanged(_visual, handler);
            if (_visual is FluidPipeVisual3D pipe) pipe.IsAnimationEnabled = false;
            if (_visual is DiskVisual3D disk) disk.RotationSpeed = 0;
            if (_visual is ArmVisual3D arm) arm.BeginAnimation(ArmVisual3D.AngleProperty, null);
        }
    }
}
