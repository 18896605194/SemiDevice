namespace xyz.Modules;

/// <summary>
/// 晶圆归属：哪一片正被哪个没结束的 Job 占着（JobManager 实现，装配时挂到搬运管理上）。
/// 搬运管理受理手动单时问它，碰到被 Job 占着的片就拒；Job 下的单只能搬自己的片。
/// </summary>
public interface IWaferOwnership
{
    /// <summary>
    /// 这一片现在归哪个 Job（PJ 名）；不归任何没结束的 Job 返回 null。任意线程可调，不能等。
    /// </summary>
    string? OwnerOf(Guid waferId);
}
