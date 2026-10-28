namespace xyz.Client.Presentation.Controls;

/// <summary>原二维机械手外观；绑定入口与运动行为沿用 RobotViewBase。</summary>
public partial class Robot : RobotViewBase
{
    public Robot()
    {
        InitializeComponent();
        InitializeMotion();
    }

    protected override void RenderFrame(double rotation, double travel)
    {
        TurretRotation.Angle = rotation;
        TravelTransform.X = travel;
    }
}
