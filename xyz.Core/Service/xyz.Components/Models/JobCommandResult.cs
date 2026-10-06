namespace xyz.Components.Models;

/// <summary>
/// 命令的受理结果：收下了带 Job 名；没收带错误码和参数（界面按码查语言包，Host 那边翻成 S16F6 / S16F28 的错误）。
/// 受理只说明命令生效了（状态已经转了，或者 Stop / Abort 已经开始做），整个 Job 什么时候结束看状态推送。
/// </summary>
public sealed record JobCommandResult(bool Accepted, string Code, IReadOnlyList<string> Args, string JobId)
{
    /// <summary>建 CJ 时一起建出来的 PJ 名（按执行顺序）；别的命令为空。</summary>
    public IReadOnlyList<string> ProcessJobs { get; init; } = [];

    public static JobCommandResult Ok(string jobId)
    {
        return new JobCommandResult(true, string.Empty, [], jobId);
    }

    public static JobCommandResult Reject(string code, params string[] args)
    {
        return new JobCommandResult(false, code, args, string.Empty);
    }
}
