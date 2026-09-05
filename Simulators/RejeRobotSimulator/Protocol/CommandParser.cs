using System.Text.RegularExpressions;

namespace RejeRobotSimulator.Protocol;

/// <summary>
/// 指令解析结果
/// </summary>
public class ParsedCommand
{
    /// <summary>原始指令（不含 @ 和 ;）</summary>
    public string RawCommand { get; set; } = "";

    /// <summary>指令名称（纯字母部分，如 Status / Speed / G）</summary>
    public string CommandName { get; set; } = "";

    /// <summary>参数部分（数字/坐标等，如 Speed50 中的 50）</summary>
    public string Args { get; set; } = "";

    /// <summary>原始收到的完整字符串</summary>
    public string Original { get; set; } = "";
}

/// <summary>
/// 指令解析器：将 @CommandName; 格式的指令拆解为结构化对象
/// </summary>
public static class CommandParser
{
    /// <summary>
    /// 解析指令，返回 null 表示格式不合法
    /// </summary>
    public static ParsedCommand? Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        string trimmed = raw.Trim();

        // 去掉末尾分号
        if (trimmed.EndsWith(";"))
            trimmed = trimmed[..^1];

        // 去掉开头 @
        if (trimmed.StartsWith("@"))
            trimmed = trimmed[1..];

        if (string.IsNullOrWhiteSpace(trimmed))
            return null;

        var result = new ParsedCommand
        {
            Original = raw.Trim(),
            RawCommand = trimmed
        };

        // 协议里有一类指令把"轴名"拼进了指令名（前缀/中缀），例如
        //   @XPos; @Arm1Pos;  @ZWorkHome10;  @SetZPos700;  @ZRange20;  @XHome;
        // 这类先归一化成各 Handler 已支持的规范名 + 轴参数（Arm1/Flip1 等轴名
        // 本身含数字，不能走下面"遇数字即切"的通用拆分）。
        if (TryNormalizeAxisCommand(trimmed, out string axisName, out string axisArgs))
        {
            result.CommandName = axisName;
            result.Args = axisArgs;
            return result;
        }

        // 分离指令名和参数：以第一个数字或空格为界
        // 例如 "Speed50" -> Name="Speed", Args="50"
        //       "G10102" -> Name="G", Args="10102"
        //       "Status" -> Name="Status", Args=""
        //       "OpenEMV 1" -> Name="OpenEMV", Args="1"
        //       "Axis Home" -> Name="Axis", Args="Home"
        //       "SetAxisPos700" -> Name="SetAxisPos", Args="700"

        int splitIndex = -1;
        for (int i = 0; i < trimmed.Length; i++)
        {
            char c = trimmed[i];
            if (char.IsDigit(c) || c == ' ' || c == ',' || c == '.')
            {
                splitIndex = i;
                break;
            }
        }

        if (splitIndex > 0)
        {
            result.CommandName = trimmed[..splitIndex];
            result.Args = trimmed[splitIndex..].Trim();
        }
        else
        {
            result.CommandName = trimmed;
            result.Args = "";
        }

        return result;
    }

    /// <summary>轴名候选（长名在前，保证 "Theta"/"Arm3" 优先于 "X"/"R" 等单字母匹配）</summary>
    private const string AxisAlt = "Theta|Arm1|Arm2|Arm3|Arm4|Flip1|Flip2|Aux|Five|X|Z|R";

    /// <summary>
    /// 把"轴名+数值"的字符串拆开，如 "Z700" -> ("Z","700")、"Arm3500" -> ("Arm3","500")。
    /// 按已知轴名整体匹配，避免在 Arm1~Arm4/Flip1~Flip2 这类带数字的轴名中间切错。
    /// 未识别出轴名返回 (null, 原串)。
    /// </summary>
    public static (string? axis, string value) SplitAxisValue(string token)
    {
        if (string.IsNullOrEmpty(token))
            return (null, "");

        var m = Regex.Match(token, $"^({AxisAlt})(.*)$", RegexOptions.IgnoreCase);
        if (!m.Success)
            return (null, token);

        return (m.Groups[1].Value, m.Groups[2].Value);
    }

    /// <summary>数值参数（可空，可含负号/小数点/逗号）</summary>
    private const string ValuePart = "[-\\d.,]*";

    /// <summary>
    /// 把"轴名拼进指令名"的写法归一化为规范指令名 + 轴参数。
    /// 命中返回 true，并输出 Handler 已支持的 CommandName 与 Args：
    ///   @Set&lt;轴&gt;Pos/Neg/V&lt;值&gt;  -> SetAxisPos/SetAxisNeg/SetAxisV, Args="轴值"
    ///   @&lt;轴&gt;WorkHome&lt;值&gt;       -> AxisWorkHome,                    Args="轴值"
    ///   @&lt;轴&gt;Range&lt;值&gt;          -> AxisRange,                       Args="轴值"
    ///   @&lt;轴&gt;Pos                  -> AxisPos,                         Args="轴"
    ///   @&lt;轴&gt;Home                 -> Home(单轴),                      Args="轴"
    /// </summary>
    public static bool TryNormalizeAxisCommand(string token, out string name, out string args)
    {
        name = "";
        args = "";

        // Set<轴>Pos/Neg/V<值>  -> SetAxisPos / SetAxisNeg / SetAxisV
        var m = Regex.Match(token, $"^Set({AxisAlt})(Pos|Neg|V)({ValuePart})$", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            string suffix = m.Groups[2].Value.ToUpperInvariant();
            name = suffix == "POS" ? "SetAxisPos" : suffix == "NEG" ? "SetAxisNeg" : "SetAxisV";
            args = m.Groups[1].Value + m.Groups[3].Value;
            return true;
        }

        // <轴>WorkHome<值>  -> AxisWorkHome （先于 <轴>Home 判定）
        m = Regex.Match(token, $"^({AxisAlt})WorkHome({ValuePart})$", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            name = "AxisWorkHome";
            args = m.Groups[1].Value + m.Groups[2].Value;
            return true;
        }

        // <轴>Range<值>  -> AxisRange
        m = Regex.Match(token, $"^({AxisAlt})Range({ValuePart})$", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            name = "AxisRange";
            args = m.Groups[1].Value + m.Groups[2].Value;
            return true;
        }

        // <轴>Pos  -> AxisPos（查询，无尾参）
        m = Regex.Match(token, $"^({AxisAlt})Pos$", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            name = "AxisPos";
            args = m.Groups[1].Value;
            return true;
        }

        // <轴>Home  -> Home（单轴回原点；整轴 @Home; 不含轴名，走通用拆分）
        m = Regex.Match(token, $"^({AxisAlt})Home$", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            name = "Home";
            args = m.Groups[1].Value;
            return true;
        }

        return false;
    }

    /// <summary>
    /// 解析动作指令参数 XYYZZ（16进制YY和ZZ）
    /// 例如 "10102" -> X=1, YY=01(十六进制), ZZ=02(十六进制)
    /// </summary>
    public static (int finger, int station, int slot)? ParseMotionArgs(string args)
    {
        if (string.IsNullOrEmpty(args) || args.Length < 5)
            return null;

        try
        {
            int finger = int.Parse(args[..1]);                    // X: 手指选择
            int station = Convert.ToInt32(args[1..3], 16);        // YY: 工位编号（16进制）
            int slot = Convert.ToInt32(args[3..5], 16);           // ZZ: 层数（16进制）
            return (finger, station, slot);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 解析取放一体指令参数 XYYZZUVVWW（10 位）：
    ///   X=取片手指, YY=取片工位(16进制), ZZ=取片层数(16进制),
    ///   U=放片手指, VV=放片工位(16进制), WW=放片层数(16进制)。
    /// </summary>
    public static (int srcFinger, int srcStation, int srcSlot, int dstFinger, int dstStation, int dstSlot)? ParseGapArgs(string args)
    {
        if (string.IsNullOrEmpty(args) || args.Length < 10)
            return null;

        try
        {
            int srcFinger = int.Parse(args[..1]);
            int srcStation = Convert.ToInt32(args[1..3], 16);
            int srcSlot = Convert.ToInt32(args[3..5], 16);
            int dstFinger = int.Parse(args[5..6]);
            int dstStation = Convert.ToInt32(args[6..8], 16);
            int dstSlot = Convert.ToInt32(args[8..10], 16);
            return (srcFinger, srcStation, srcSlot, dstFinger, dstStation, dstSlot);
        }
        catch
        {
            return null;
        }
    }
}
