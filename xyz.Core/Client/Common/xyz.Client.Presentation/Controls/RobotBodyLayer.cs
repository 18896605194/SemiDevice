using System.Windows;
using System.Windows.Media;
using xyz.Client.Presentation.Models;
using static xyz.Client.Presentation.Controls.RobotDrawing;

namespace xyz.Client.Presentation.Controls;

/// <summary>
/// 机械手本体层：底座、状态环、转台。
/// </summary>
internal sealed class RobotBodyLayer : FrameworkElement
{
    #region 画刷与画笔

    private static readonly Brush BaseShadowBrush = Frozen(new RadialGradientBrush(Color.FromArgb(0x90, 0, 0, 0), Color.FromArgb(0x00, 0, 0, 0)));

    private static readonly Brush BaseBrush = Frozen(new RadialGradientBrush(new GradientStopCollection
    {
        new(Color.FromRgb(0x5A, 0x62, 0x70), 0.0),
        new(Color.FromRgb(0x39, 0x3F, 0x49), 0.5),
        new(Color.FromRgb(0x23, 0x27, 0x2E), 1.0),
    })
    {
        GradientOrigin = new Point(0.35, 0.28),
    });

    private static readonly Pen BasePen = Frozen(new Pen(new LinearGradientBrush(Color.FromRgb(0x8A, 0x94, 0xA3), Color.FromRgb(0x16, 0x19, 0x1E), 55), 2));
    private static readonly Pen BaseInnerPen = Frozen(new Pen(Solid(0x16, 0xFF, 0xFF, 0xFF), 1));
    private static readonly Pen BoltPen = Frozen(new Pen(Solid(0xFF, 0x62, 0x6B, 0x78), 0.8));

    private static readonly Brush TurretBrush = Frozen(new LinearGradientBrush(new GradientStopCollection
    {
        new(Color.FromRgb(0x5E, 0x67, 0x75), 0.0),
        new(Color.FromRgb(0x40, 0x47, 0x52), 0.55),
        new(Color.FromRgb(0x2B, 0x30, 0x38), 1.0),
    }, new Point(0, 0), new Point(1, 0.35)));

    private static readonly Pen TurretPen = Frozen(new Pen(Solid(0xFF, 0x76, 0x80, 0x8E), 1.2));
    private static readonly Brush SlotBrush = Frozen(Solid(0xC8, 0x14, 0x17, 0x1C));
    private static readonly Brush CapBrush = Frozen(new RadialGradientBrush(Color.FromRgb(0x8E, 0x98, 0xA7), Color.FromRgb(0x2A, 0x2F, 0x37)) { GradientOrigin = new Point(0.35, 0.3) });
    private static readonly Pen CapPen = Frozen(new Pen(Solid(0xFF, 0x1A, 0x1D, 0x22), 1.2));

    #endregion

    private RobotSceneFrame? _frame;
    private RobotPalette? _palette;

    public RobotBodyLayer()
    {
        IsHitTestVisible = false;
    }

    public void Update(RobotSceneFrame frame)
    {
        _frame = frame;
        InvalidateVisual();
    }

    /// <summary>
    /// 没有给尺寸时（如放在 Canvas 里）按设计尺寸 400×400 占位。
    /// </summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        return new Size(
            double.IsInfinity(availableSize.Width) ? DesignSize : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? DesignSize : availableSize.Height);
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_frame is null || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var frame = _frame;
        var palette = _palette ??= new RobotPalette(this);
        var center = new Point(0, 0);

        dc.PushTransform(new MatrixTransform(BodyMatrix(RenderSize, frame)));
        DrawBase(dc, center);
        DrawStatusRing(dc, center, frame, palette);
        DrawTurret(dc, center, frame, palette);
        dc.DrawEllipse(CapBrush, CapPen, center, 12, 12);
        dc.Pop();
    }

    private static void DrawBase(DrawingContext dc, Point center)
    {
        dc.DrawEllipse(BaseShadowBrush, null, center + new Vector(5, 9), BaseRadius + 18, BaseRadius + 18);
        dc.DrawEllipse(BaseBrush, BasePen, center, BaseRadius, BaseRadius);
        dc.DrawEllipse(null, BaseInnerPen, center, BaseRadius - 6, BaseRadius - 6);
        for (int index = 0; index < 8; index++)
        {
            dc.DrawEllipse(BoltBrush, BoltPen, center + Direction(22.5 + index * 45) * (BaseRadius - 11), 2.4, 2.4);
        }
    }

    /// <summary>
    /// 状态环：空闲/未初始化/离线为常亮色环；动作中为流光环绕；报警为呼吸闪烁。
    /// </summary>
    private static void DrawStatusRing(DrawingContext dc, Point center, RobotSceneFrame frame, RobotPalette palette)
    {
        Color color = palette.StatusColor(frame.Status);
        switch (frame.Status)
        {
            case RobotDisplayStatus.Busy:
            {
                dc.DrawEllipse(null, new Pen(new SolidColorBrush(WithAlpha(color, 0x18)), 12), center, StatusRingRadius, StatusRingRadius);
                dc.DrawEllipse(null, new Pen(new SolidColorBrush(WithAlpha(color, 0x3C)), 3), center, StatusRingRadius, StatusRingRadius);
                double head = frame.TimeMs / 1300.0 * 360 % 360;
                for (int index = 0; index < 12; index++)
                {
                    double end = head - index * 8;
                    var pen = new Pen(new SolidColorBrush(WithAlpha(color, (byte)(235 * (1 - index / 12.0)))), 3.6)
                    {
                        StartLineCap = PenLineCap.Round,
                        EndLineCap = PenLineCap.Round,
                    };
                    DrawArc(dc, center, StatusRingRadius, end - 8.5, end, pen);
                }

                break;
            }

            case RobotDisplayStatus.Alarm:
            {
                double pulse = 0.5 + 0.5 * Math.Sin(frame.TimeMs / 1100.0 * 2 * Math.PI);
                dc.DrawEllipse(null, new Pen(new SolidColorBrush(WithAlpha(color, (byte)(0x24 + pulse * 0x64))), 16), center, StatusRingRadius, StatusRingRadius);
                dc.DrawEllipse(null, new Pen(new SolidColorBrush(WithAlpha(color, 0xE6)), 3), center, StatusRingRadius, StatusRingRadius);
                break;
            }

            default:
            {
                byte alpha = frame.Status == RobotDisplayStatus.Offline ? (byte)0x70 : (byte)0xD9;
                dc.DrawEllipse(null, new Pen(new SolidColorBrush(WithAlpha(color, 0x20)), 10), center, StatusRingRadius, StatusRingRadius);
                dc.DrawEllipse(null, new Pen(new SolidColorBrush(WithAlpha(color, alpha)), 3), center, StatusRingRadius, StatusRingRadius);
                break;
            }
        }
    }

    private static void DrawTurret(DrawingContext dc, Point center, RobotSceneFrame frame, RobotPalette palette)
    {
        dc.PushTransform(new RotateTransform(frame.Rotation, center.X, center.Y));

        bool frogLeg = frame.ArmType == RobotArmType.FrogLeg;
        Rect body = frogLeg
            ? new Rect(center.X - 46, center.Y - 34, 92, 66)
            : new Rect(center.X - 37, center.Y - 50, 74, 96);
        double radius = frogLeg ? 26 : 18;
        dc.DrawRoundedRectangle(ShadowBrush, null, Offset(body, ScreenToLocal(new Vector(4, 7), frame.Rotation)), radius, radius);
        dc.DrawRoundedRectangle(TurretBrush, TurretPen, body, radius, radius);

        if (!frogLeg)
        {
            dc.DrawRoundedRectangle(SlotBrush, null, new Rect(center.X - 4, center.Y - 44, 8, 72), 4, 4);
        }

        // 正前方指示
        var chevron = new Pen(new SolidColorBrush(WithAlpha(palette.Accent, 0xE0)), 2.4)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        double tip = body.Top + 7;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(center.X - 7, tip + 6), false, false);
            context.LineTo(new Point(center.X, tip), true, true);
            context.LineTo(new Point(center.X + 7, tip + 6), true, true);
        }

        geometry.Freeze();
        dc.DrawGeometry(null, chevron, geometry);
        dc.Pop();
    }

    private static void DrawArc(DrawingContext dc, Point center, double radius, double fromDegrees, double toDegrees, Pen pen)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(center + Direction(fromDegrees) * radius, false, false);
            context.ArcTo(center + Direction(toDegrees) * radius, new Size(radius, radius), 0, toDegrees - fromDegrees > 180, SweepDirection.Clockwise, true, false);
        }

        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }
}
