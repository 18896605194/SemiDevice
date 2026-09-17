namespace xyz.Drivers.Robot.Reje.Commands;

public abstract class RejeCommand : RobotCommand
{
    protected RejeCommand(IRobotDriver driver) : base(driver)
    {
    }

    /// <summary>
    /// 在途槽位键 = 下发体：回复只能靠回显名对号，同名指令同一时间只能有一条在途。
    /// </summary>
    public override string Key => Wire;

    /// <summary>
    /// 下发体（不含 '@' 与 ';'），如 G10102。
    /// </summary>
    protected abstract string Wire { get; }

    /// <summary>
    /// 本指令认领的回显名：默认同下发体；厂商回显写法与下发不一致的指令重写（如 OpenEMV1 回 "OpenEMV 1"）。
    /// </summary>
    protected virtual IReadOnlyList<string> Echoes => [Wire];

    /// <summary>
    /// 只在失败帧上额外认领的回显名：部分拒绝帧只回指令名、不带参数（如未上使能时 G10102 回 "@G"）。
    /// 成功帧仍须完整回显，避免把不属于本指令的结果当成功。
    /// </summary>
    protected virtual IReadOnlyList<string> FailureEchoes => [];

    public override string BuildMsg()
    {
        return Wire;
    }

    public override bool ParseMsg(string body)
    {
        // 确认帧（">"）拆不出回显名，不认领；以带回显名的结果帧终结。
        if (!RejeProtocol.TrySplit(body, out string code, out string content, out string name))
        {
            return false;
        }

        if (!Matches(Echoes, name)
            && (code == RejeProtocol.SuccessCode || !Matches(FailureEchoes, name)))
        {
            return false;
        }

        Complete(code == RejeProtocol.SuccessCode
            ? BuildResponse(content)
            : BuildFailure(code, content));
        return true;
    }

    /// <summary>
    /// 成功帧（错误码 00000000）的内容段转换成厂商无关结果；默认成功且只带原文。
    /// 查询类指令重写，解析出结构化结果；内容不合法时返回失败结果。
    /// </summary>
    protected virtual RobotResponse BuildResponse(string content)
    {
        return new RobotResponse
        {
            IsSuccess = true,
            Content = content,
        };
    }

    /// <summary>
    /// 失败帧（错误码非 00000000）转换成结果；默认失败，Error 为"错误码#内容"原文。
    /// </summary>
    protected virtual RobotResponse BuildFailure(string code, string content)
    {
        return new RobotResponse
        {
            IsSuccess = false,
            Error = $"{code}#{content}",
            Content = content,
        };
    }

    private static bool Matches(IReadOnlyList<string> echoes, string name)
    {
        return echoes.Any(echo => string.Equals(echo, name, StringComparison.OrdinalIgnoreCase));
    }
}
