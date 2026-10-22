namespace xyz.Components.Enums;

/// <summary>
/// 报警分类，就是 SEMI E5 ALCD（报警码字节）的低 7 位，报 Host 时原样带上；轻重看 AlarmLevel，不看它。
/// </summary>
public enum AlarmCategory
{
    /// <summary>
    /// 不用（标准留的，报警声明里不选它）
    /// </summary>
    NotUsed = 0,

    /// <summary>
    /// 人身安全：可能伤到人（急停、门、漏液、烟感）
    /// </summary>
    PersonalSafety = 1,

    /// <summary>
    /// 设备安全：可能损坏设备（溢流、过温、撞机）
    /// </summary>
    EquipmentSafety = 2,

    /// <summary>
    /// 参数控制警告：工艺量进了预警区
    /// </summary>
    ParameterControlWarning = 3,

    /// <summary>
    /// 参数控制错误：工艺量超出允许范围
    /// </summary>
    ParameterControlError = 4,

    /// <summary>
    /// 不可恢复错误：设备或动作出错，要人处理（动作超时、设备报错、通讯断开）
    /// </summary>
    IrrecoverableError = 5,

    /// <summary>
    /// 设备状态警告：设备状况要留意，本身不算故障（寿命、监视信号）
    /// </summary>
    EquipmentStatusWarning = 6,

    /// <summary>
    /// 提示标志：要人来看一下（定不了片、有人手动放拿载具）
    /// </summary>
    AttentionFlags = 7,

    /// <summary>
    /// 数据完整性：账、Mapping 跟实际对不上
    /// </summary>
    DataIntegrity = 8,

    /// <summary>
    /// 其他（标准里 8 以上都算其他）
    /// </summary>
    OtherCategories = 9
}
