using xyz.Shared.Dtos;

namespace xyz.Modules;

/// <summary>
/// 机械手的一个站点（sc.xml 本 Robot 节点 Stations 下的子节点）：
/// 站点号用于取放片下发；转台方位与平移距离随状态推给界面，显示机械手去哪、朝哪。
/// </summary>
/// <param name="Name">模块名，如 LoadPort1。</param>
/// <param name="Number">设备站点号，正整数。</param>
/// <param name="Rotation">机械手服务该站点时的转台方位。</param>
/// <param name="Travel">机械手服务该站点时的平移距离。</param>
public sealed record RobotStation(string Name, int Number, RobotDirection Rotation, double Travel);
