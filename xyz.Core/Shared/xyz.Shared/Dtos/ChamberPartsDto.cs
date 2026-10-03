namespace xyz.Shared.Dtos;

/// <summary>
/// 腔体部件状态（腔体手动页的三维图和部件按钮用）：门、Bowl、旋转电机、各条摆臂（带 Lift 和喷嘴）。
/// 部件按 sc.xml 的结构找，sc 里没配的为 null / 不出现，界面就不画、不给按钮。
/// 跟 ChamberDto 分开推（token 都是模块名，类型不同互不覆盖）：摆臂一动就要推，别让只关心模块状态的界面跟着刷。
/// </summary>
public class ChamberPartsDto
{
    /// <summary>模块实例名，与 EventBus token 一致，如 "Chamber1"。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>腔门（sc 里名叫 Door 的气缸）；没配为 null。</summary>
    public ChamberCylinderDto? Door { get; set; }

    /// <summary>Bowl（腔体下名字以 Bowl 开头的第一个气缸，现在 sc 里叫 Bowl1；升 = 开侧）；没配为 null。</summary>
    public ChamberCylinderDto? Bowl { get; set; }

    /// <summary>旋转电机（腔体下第一个 SpinMotor）；没配为 null。</summary>
    public ChamberSpinDto? Spin { get; set; }

    /// <summary>各条摆臂，按 sc.xml 里的先后。</summary>
    public List<ChamberArmDto> Arms { get; set; } = [];

    /// <summary>
    /// 跟上一次推的比有没有变化；没有上一次视为变化。
    /// </summary>
    public bool HasStateChanged(ChamberPartsDto? previous)
    {
        if (previous is null)
        {
            return true;
        }

        if (Name != previous.Name)
        {
            return true;
        }

        if (!ChamberCylinderDto.Same(Door, previous.Door) || !ChamberCylinderDto.Same(Bowl, previous.Bowl))
        {
            return true;
        }

        if (!ChamberSpinDto.Same(Spin, previous.Spin))
        {
            return true;
        }

        if (Arms.Count != previous.Arms.Count)
        {
            return true;
        }

        for (int i = 0; i < Arms.Count; i++)
        {
            if (!ChamberArmDto.Same(Arms[i], previous.Arms[i]))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// 一个双作用气缸（门、Bowl、Lift）的显示状态。
/// </summary>
public class ChamberCylinderDto
{
    /// <summary>sc.xml 里的节点名，如 "Door"、"Lift"。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>组件全路径，如 "Chamber1.Arm1.Lift"；手动动作按它找部件。</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// 指令在开侧（门开、Bowl 升、Lift 升）：指令一发出去就变，不等到位，界面据此开始画动作；
    /// 刚上电两个线圈都没通时按到位反馈给。
    /// </summary>
    public bool IsOpen { get; set; }

    /// <summary>正在走：指令侧还没到位。</summary>
    public bool IsMoving { get; set; }

    /// <summary>两个快照是否一样（都为 null 也算一样）。</summary>
    public static bool Same(ChamberCylinderDto? left, ChamberCylinderDto? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return left.Name == right.Name
            && left.Path == right.Path
            && left.IsOpen == right.IsOpen
            && left.IsMoving == right.IsMoving;
    }
}

/// <summary>
/// 旋转电机（卡盘）的显示状态。
/// </summary>
public class ChamberSpinDto
{
    /// <summary>sc.xml 里的节点名，如 "SpinMotor"。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>组件全路径，如 "Chamber1.SpinMotor"。</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>在转：实际转速超出速度容差（EC SpeedTolerance）。</summary>
    public bool IsSpinning { get; set; }

    /// <summary>转向：实际转速为正算顺时针（从上往下看）。</summary>
    public bool IsClockwise { get; set; }

    /// <summary>两个快照是否一样（都为 null 也算一样）。</summary>
    public static bool Same(ChamberSpinDto? left, ChamberSpinDto? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return left.Name == right.Name
            && left.Path == right.Path
            && left.IsSpinning == right.IsSpinning
            && left.IsClockwise == right.IsClockwise;
    }
}

/// <summary>
/// 一条摆臂的显示状态：摆到哪、在不在动，以及装在它上面的 Lift 和喷嘴。
/// </summary>
public class ChamberArmDto
{
    /// <summary>sc.xml 里的节点名，如 "Arm1"。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>组件全路径，如 "Chamber1.Arm1"。</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// 摆到哪：0 = Home（回零后的 0 位），1 = 工艺位（EC Center，晶圆中心），中间按轴位置线性换算，可以超出 0~1。
    /// 后端按轴位置和示教位算好，界面只管换成摆角。
    /// </summary>
    public double Reach { get; set; }

    /// <summary>
    /// 第一个边缘（EC Edge）在 Reach 上的位置（= Edge / Center）：界面按 Home → 边缘 → 中心分两段画摆角，轴在 Edge 时喷嘴正好画在盘边上。
    /// 没在 0~1 之间（还没示教，比如默认 Edge = 0 跟 Home 重合）给 0，界面就按 Home → 中心一段画。
    /// </summary>
    public double EdgeReach { get; set; }

    /// <summary>轴在动（PLC 忙）。</summary>
    public bool IsMoving { get; set; }

    /// <summary>装在这条臂上的升降气缸；没配为 null。</summary>
    public ChamberCylinderDto? Lift { get; set; }

    /// <summary>装在这条臂上的喷嘴，按 sc.xml 里的先后。</summary>
    public List<ChamberNozzleDto> Nozzles { get; set; } = [];

    /// <summary>两个快照是否一样。</summary>
    public static bool Same(ChamberArmDto left, ChamberArmDto right)
    {
        if (left.Name != right.Name
            || left.Path != right.Path
            || left.Reach != right.Reach
            || left.EdgeReach != right.EdgeReach
            || left.IsMoving != right.IsMoving
            || !ChamberCylinderDto.Same(left.Lift, right.Lift)
            || left.Nozzles.Count != right.Nozzles.Count)
        {
            return false;
        }

        for (int i = 0; i < left.Nozzles.Count; i++)
        {
            if (!ChamberNozzleDto.Same(left.Nozzles[i], right.Nozzles[i]))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// 一路喷嘴的显示状态。
/// </summary>
public class ChamberNozzleDto
{
    /// <summary>sc.xml 里的节点名，如 "Nozzle_DIW"。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>组件全路径，如 "Chamber1.Arm1.Nozzle_DIW"。</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>药液名（sc.xml 的 Chemical），如 "DIW"。</summary>
    public string Chemical { get; set; } = string.Empty;

    /// <summary>在出液：接了流量开关看流量开关，没接看阀的输出回读。</summary>
    public bool IsOn { get; set; }

    /// <summary>两个快照是否一样。</summary>
    public static bool Same(ChamberNozzleDto left, ChamberNozzleDto right)
    {
        return left.Name == right.Name
            && left.Path == right.Path
            && left.Chemical == right.Chemical
            && left.IsOn == right.IsOn;
    }
}
