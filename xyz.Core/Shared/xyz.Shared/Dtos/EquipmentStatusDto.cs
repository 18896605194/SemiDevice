namespace xyz.Shared.Dtos;

/// <summary>
/// 设备总状态：四色灯的红、黄、绿按它亮（蓝灯是客户端与后端的通讯，客户端自己判断）。
/// 状态类消息：变化才推，留存（晚连上的客户端立即拿到当前值）。
/// </summary>
public class EquipmentStatusDto
{
    public const string EventToken = "EquipmentStatus";

    /// <summary>有报警（Alarm1 / Alarm2 / Fatal 级，还没被人工清除）→ 红灯。</summary>
    public bool HasAlarm { get; set; }

    /// <summary>有警告（Warn 级报警还没被人工清除）→ 黄灯。</summary>
    public bool HasWarning { get; set; }

    /// <summary>有模块正在执行动作 → 绿灯。</summary>
    public bool IsRunning { get; set; }
}
