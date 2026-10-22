using xyz.Drivers.Aligner;

namespace xyz.Components.Interfaces;

/// <summary>
/// 对准器驱动组件（挂在对准器模块下，品牌指令由具体驱动出）
/// </summary>
public interface IAlignerDriverComponent
{
    /// <summary>
    /// 连上了没有
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// 回原点，被拒是 null
    /// </summary>
    AlignerCommand? Home();

    /// <summary>
    /// 对准：找缺口、对中心，把缺口转到 angle 度，做完松开片等机械手取；被拒是 null
    /// </summary>
    AlignerCommand? Align(double angle);

    /// <summary>
    /// 停止，被拒是 null
    /// </summary>
    AlignerCommand? Stop();

    /// <summary>
    /// 清设备报错，被拒是 null
    /// </summary>
    AlignerCommand? ResetDrive();

    /// <summary>
    /// 作废全部在途指令（动作没做成时调，免得回复丢了一直占着、同一条再也发不出去）
    /// </summary>
    void AbandonAll(string reason);
}
