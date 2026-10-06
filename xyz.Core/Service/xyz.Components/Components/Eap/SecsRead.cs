using System.Globalization;
using xyz.Secs;
using xyz.Secs.Diagnostics;
using xyz.Secs.Hsms;
using xyz.Secs.SecsII;

namespace xyz.Components.Components;

/// <summary>
/// 读 Host 报文里的数据：结构、类型不对一律抛 SecsException（链路照 E5 回 S9F7 数据不对），处理方不用自己一个个判。
/// what 是这一项叫什么（DATAID、CEID……），只用来写日志。
/// </summary>
internal static class SecsRead
{
    /// <summary>报文体；没带体抛。</summary>
    public static SecsItem Body(HsmsMessage message)
    {
        return message.Body ?? throw new SecsException($"{message.Name} 没带数据");
    }

    /// <summary>一个列表的子项；count 给了就要正好这么多项。</summary>
    public static IReadOnlyList<SecsItem> List(SecsItem? item, string what, int? count = null)
    {
        if (item is null || item.Format != SecsFormat.List)
        {
            throw new SecsException($"{what} 应是列表，收到 {Describe(item)}");
        }

        if (count is not null && item.Count != count.Value)
        {
            throw new SecsException($"{what} 应有 {count.Value} 项，收到 {item.Count} 项");
        }

        return item.Items;
    }

    /// <summary>
    /// 一个编号（ID）：任何整数格式的单个非负值都收；也收全是数字的 ASCII（有的 Host 编号用文字发）。
    /// </summary>
    public static uint Id(SecsItem item, string what)
    {
        if (item.Format is SecsFormat.Ascii or SecsFormat.Jis8)
        {
            if (uint.TryParse(item.GetString().Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out uint parsed))
            {
                return parsed;
            }

            throw new SecsException($"{what} 应是编号，收到 {Describe(item)}");
        }

        var values = Integers(item, what);
        if (values.Length != 1 || values[0] < 0 || values[0] > uint.MaxValue)
        {
            throw new SecsException($"{what} 应是单个非负整数，收到 {Describe(item)}");
        }

        return (uint)values[0];
    }

    /// <summary>
    /// 一串编号：可以是列表（每项一个编号），也可以是一个整数数组（S5F5 的 ALID 向量就是这么发的）；空的就是空表。
    /// </summary>
    public static IReadOnlyList<uint> Ids(SecsItem item, string what)
    {
        if (item.Format == SecsFormat.List)
        {
            return item.Items.Select(child => Id(child, what)).ToList();
        }

        var values = Integers(item, what);
        if (values.Any(value => value < 0 || value > uint.MaxValue))
        {
            throw new SecsException($"{what} 应是非负整数，收到 {Describe(item)}");
        }

        return values.Select(value => (uint)value).ToList();
    }

    /// <summary>一段文字（A 或 J）。</summary>
    public static string Text(SecsItem item, string what)
    {
        if (item.Format is not (SecsFormat.Ascii or SecsFormat.Jis8))
        {
            throw new SecsException($"{what} 应是文字，收到 {Describe(item)}");
        }

        return item.GetString();
    }

    /// <summary>一个字节码（B 或 U1，单个值），比如 ALED、RSDC。</summary>
    public static byte Code(SecsItem item, string what)
    {
        if (item.Format == SecsFormat.Binary)
        {
            var bytes = item.GetBinary();
            if (bytes.Length == 1)
            {
                return bytes[0];
            }
        }
        else if (item.Format is SecsFormat.U1 or SecsFormat.U2 or SecsFormat.U4 or SecsFormat.U8
                 or SecsFormat.I1 or SecsFormat.I2 or SecsFormat.I4 or SecsFormat.I8)
        {
            var values = Integers(item, what);
            if (values.Length == 1 && values[0] >= 0 && values[0] <= byte.MaxValue)
            {
                return (byte)values[0];
            }
        }

        throw new SecsException($"{what} 应是单个字节码，收到 {Describe(item)}");
    }

    /// <summary>一个布尔（Boolean 单值；也收 B / U1 的 0、非 0）。</summary>
    public static bool Flag(SecsItem item, string what)
    {
        if (item.Format == SecsFormat.Boolean)
        {
            var flags = item.GetBooleanArray();
            if (flags.Length == 1)
            {
                return flags[0];
            }

            throw new SecsException($"{what} 应是单个布尔，收到 {Describe(item)}");
        }

        return Code(item, what) != 0;
    }

    private static long[] Integers(SecsItem item, string what)
    {
        try
        {
            return item.GetInt64Array();
        }
        catch (OverflowException)
        {
            throw new SecsException($"{what} 超出范围，收到 {Describe(item)}");
        }
        catch (SecsException)
        {
            throw new SecsException($"{what} 应是整数，收到 {Describe(item)}");
        }
    }

    private static string Describe(SecsItem? item)
    {
        return item is null ? "空" : SecsMessageText.Format(item).Replace("\r", " ").Replace("\n", " ");
    }
}
