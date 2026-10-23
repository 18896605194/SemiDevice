namespace xyz._35021.Module.Drivers.Reje.Commands;

/// <summary>
/// Pick（@GXYYZZ;）：用手指 X 从工位 YY 的层 ZZ 取片（工位、层为十六进制）
/// </summary>
public sealed class RejePickCommand : RejeCommand
{
    private readonly string _wire;

    public RejePickCommand(int arm, int station, int slot)
    {
        _wire = "G" + RejeProtocol.EncodeMotionArgs(arm, station, slot);
    }

    protected override string Wire => _wire;

    protected override bool Handle(RejeMessage message)
    {
        bool success = message.Code == RejeProtocol.SuccessCode;
        if (string.Equals(message.Name, _wire, StringComparison.OrdinalIgnoreCase))
        {
            if (success)
            {
                Complete();
            }
            else
            {
                Fail($"{message.Code}#{message.Content}");
            }

            return true;
        }

        // 部分拒绝帧只回指令名、不带参数（没上使能时 G10102 回 "G"）：只在失败时认；成功帧必须完整回显，免得把别人的结果当成功
        if (!success && string.Equals(message.Name, "G", StringComparison.OrdinalIgnoreCase))
        {
            Fail($"{message.Code}#{message.Content}");
            return true;
        }

        return false;
    }
}
