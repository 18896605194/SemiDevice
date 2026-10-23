using System.Globalization;

namespace xyz._35021.Module.Drivers.Reje.Commands;

/// <summary>
/// &lt;轴&gt;Pos（如 @XPos;、@Arm1Pos;）：查一根轴的当前坐标，轴名照 sc 的轴节点
/// </summary>
public sealed class RejeQueryAxisPosCommand : RejeCommand
{
    private readonly string _wire;

    public RejeQueryAxisPosCommand(string axis)
    {
        _wire = axis + "Pos";
    }

    protected override string Wire => _wire;

    /// <summary>
    /// 查到的坐标
    /// </summary>
    public double? Position { get; private set; }

    protected override bool Handle(RejeMessage message)
    {
        if (!string.Equals(message.Name, _wire, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (message.Code != RejeProtocol.SuccessCode)
        {
            Fail($"{message.Code}#{message.Content}");
            return true;
        }

        if (!double.TryParse(message.Content.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double position))
        {
            Fail($"{_wire} 内容无法识别: {message.Content}");
            return true;
        }

        Position = position;
        Complete();
        return true;
    }
}
