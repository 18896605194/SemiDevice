namespace xyz.Shared.Dtos;

/// <summary>
/// 整机状态（主界面系统操作的系统状态、当前模式按它显示）。状态类消息：变化才推，留存（晚连上的客户端立即拿到当前值）。
/// </summary>
public class SystemStateDto
{
    public const string EventToken = "SystemState";

    /// <summary>
    /// 系统状态码：10 未初始化、20 初始化中、30 空闲（Manual）、35 停止中、40 出错、100 自动空闲、110 自动运行、120 全部回片中
    /// （xyz.Modules 的 ModuleState / SystemState）。
    /// </summary>
    public int State { get; set; }
}
