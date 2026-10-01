using System.Text;
using xyz.Secs.Hsms;
using xyz.Secs.SecsII;

namespace xyz.Secs.Diagnostics;

/// <summary>
/// 报文明文格式化：SxFy + SystemBytes + 缩进的 Item 树，fab 现场排障的通用格式。
/// 样例：
/// S1F14 W  Sys=7
/// L[2]
///   B[00]
///   L[2]
///     A"xyz"
///     A"1.0.0"
/// </summary>
public static class SecsMessageText
{
    public static string Format(HsmsMessage message)
    {
        var builder = new StringBuilder();
        builder.Append(message.Name).Append("  Sys=").Append(message.Header.SystemBytes);
        if (message.Body is not null)
        {
            builder.AppendLine();
            Format(message.Body, builder, 0);
        }
        return builder.ToString();
    }

    public static string Format(SecsItem item)
    {
        var builder = new StringBuilder();
        Format(item, builder, 0);
        return builder.ToString().TrimEnd();
    }

    private static void Format(SecsItem item, StringBuilder builder, int depth)
    {
        builder.Append(' ', depth * 2);
        switch (item.Format)
        {
            case SecsFormat.List:
                builder.AppendLine($"L[{item.Count}]");
                foreach (var child in item.Items)
                {
                    Format(child, builder, depth + 1);
                }
                break;

            case SecsFormat.Ascii:
            case SecsFormat.Jis8:
                builder.AppendLine($"{item.Format}\"{item.GetString().Replace("\r", "\\r").Replace("\n", "\\n")}\"");
                break;

            case SecsFormat.Binary:
                builder.AppendLine($"B[{string.Join(' ', item.GetBinary().Select(value => value.ToString("X2")))}]");
                break;

            case SecsFormat.Boolean:
                builder.AppendLine($"Boolean[{string.Join(' ', item.GetBooleanArray().Select(value => value ? "True" : "False"))}]");
                break;

            case SecsFormat.I1:
            case SecsFormat.I2:
            case SecsFormat.I4:
            case SecsFormat.I8:
                builder.AppendLine($"{item.Format}[{string.Join(' ', item.GetInt64Array())}]");
                break;

            case SecsFormat.U1:
            case SecsFormat.U2:
            case SecsFormat.U4:
            case SecsFormat.U8:
                builder.AppendLine($"{item.Format}[{string.Join(' ', item.GetUInt64Array())}]");
                break;

            case SecsFormat.F4:
            case SecsFormat.F8:
                builder.AppendLine($"{item.Format}[{string.Join(' ', item.GetDoubleArray())}]");
                break;

            default:
                builder.AppendLine(item.Format.ToString());
                break;
        }
    }
}
