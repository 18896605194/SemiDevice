using System.Windows;

namespace CdsAssemblyPreview;

public enum HardwareViewMode { ThreeD, Front2D }

/// <summary>同一三维件的正交正视范围与管口，不另画一套二维轮廓。</summary>
public static class ProcessHardwareProjection
{
    public static Rect Bounds(FluidHardwareKind kind) => kind switch
    {
        FluidHardwareKind.CentrifugalPump => new Rect(-2.04, 0, 3.69, 2.04),
        FluidHardwareKind.InlineHeater => new Rect(-2.04, 0, 4.08, 1.81),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    public static Point Port(FluidHardwareKind kind, bool outlet)
    {
        var bounds = Bounds(kind);
        var model = kind == FluidHardwareKind.CentrifugalPump
            ? outlet ? new Point(-1.21, 2.04) : new Point(-2.04, 0.99)
            : outlet ? new Point(2.04, 1.0) : new Point(-2.04, 1.0);
        return new Point((model.X - bounds.X) / bounds.Width, (bounds.Bottom - model.Y) / bounds.Height);
    }
}
