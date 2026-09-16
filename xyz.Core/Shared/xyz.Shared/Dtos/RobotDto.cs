namespace xyz.Shared.Dtos;


public class RobotDto
{
    /// <summary>模块实例名，与 EventBus token 一致，如 "Robot1"。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>模块状态码，取值见 ModuleState/RobotState。</summary>
    public int State { get; set; }

    /// <summary>驱动连接是否可用。</summary>
    public bool IsConnected { get; set; }

    /// <summary>查询反馈：伺服是否上使能；null 表示反馈不可用。</summary>
    public bool? IsServoOn { get; set; }

    /// <summary>查询反馈：设备当前报错（错误码#内容）；null 表示无报错或反馈不可用。</summary>
    public string? DeviceError { get; set; }

    /// <summary>当前站点：最近一次发起成功的取放片站点名（如 LoadPort1）；还没取放过为 null。</summary>
    public string? Station { get; set; }

    /// <summary>当前站点在 sc.xml 站点表里配置的转台方位，界面显示机械手朝哪。</summary>
    public RobotDirection Rotation { get; set; }

    /// <summary>当前站点在 sc.xml 站点表里配置的平移距离，界面显示机械手去哪。</summary>
    public double Travel { get; set; }

    /// <summary>各手指在位（设备推送），按手指号升序；尚未收到推送的手指不在列表中。</summary>
    public List<RobotArmDto> Arms { get; set; } = [];

    /// <summary>
    /// 比较当前发布的模块状态、连接状态、设备反馈、当前站点和手指在位；没有上一次状态时视为变化。
    /// </summary>
    public bool HasStateChanged(RobotDto? previous)
    {
        if (previous is null)
        {
            return true;
        }

        if (Name != previous.Name)
        {
            return true;
        }

        if (State != previous.State)
        {
            return true;
        }

        if (IsConnected != previous.IsConnected)
        {
            return true;
        }

        if (IsServoOn != previous.IsServoOn)
        {
            return true;
        }

        if (DeviceError != previous.DeviceError)
        {
            return true;
        }

        if (Station != previous.Station || Rotation != previous.Rotation || Travel != previous.Travel)
        {
            return true;
        }

        if (Arms.Count != previous.Arms.Count)
        {
            return true;
        }

        for (int i = 0; i < Arms.Count; i++)
        {
            if (Arms[i].Arm != previous.Arms[i].Arm || Arms[i].HasWafer != previous.Arms[i].HasWafer)
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// Robot 单个手指的在位契约对象。
/// </summary>
public class RobotArmDto
{
    /// <summary>手指号，从 1 开始。</summary>
    public int Arm { get; set; }

    /// <summary>该手指上是否有片。</summary>
    public bool HasWafer { get; set; }
}
