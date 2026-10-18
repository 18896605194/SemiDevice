using xyz.Shared.Dtos;

namespace xyz.Components.Interfaces;

/// <summary>
/// 载具供 Job 使用的准入能力；不负责创建 Job、分配晶圆或占用回片槽。
/// </summary>
public interface IJobAccess : IModule
{
    /// <summary>
    /// 当前载具能否供 Job 定片、选择回片口及开始作业；与工位能否开始搬运准备分别判断。
    /// </summary>
    bool CanUseCarrierForJob { get; }

    /// <summary>
    /// 只查询准入条件，不占用载具或晶圆。null 表示允许，否则返回拒绝原因（错误码和参数）。
    /// </summary>
    HandleResult? CheckCarrierForJob();
}
