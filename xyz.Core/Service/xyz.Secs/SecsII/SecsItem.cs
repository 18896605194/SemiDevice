using xyz.Secs.Diagnostics;

namespace xyz.Secs.SecsII;

/// <summary>
/// SECS-II 数据项：一棵不可变的树。List 节点持有子项列表；叶子按格式持有托管数组
/// （I 系列统一 long[]、U 系列统一 ulong[]、F 系列统一 double[]，宽度信息由 Format 携带），
/// 编解码时再按格式落宽度，避免各宽度各建一套类型。
/// 构造走静态工厂：SecsItem.L(SecsItem.U4(10000), SecsItem.A("xyz"))。
/// </summary>
public sealed class SecsItem
{
    private readonly object _data;

    private SecsItem(SecsFormat format, object data)
    {
        Format = format;
        _data = data;
    }

    public SecsFormat Format { get; }

    /// <summary>
    /// 元素个数：List 是子项个数，数组是元素个数，A/J 是字符数；空串、空数组、空列表都是 0。
    /// </summary>
    public int Count => _data switch
    {
        IReadOnlyList<SecsItem> items => items.Count,
        string text => text.Length,
        Array array => array.Length,
        _ => 0,
    };

    /// <summary>
    /// List 的子项；不是 List 时抛异常——拿之前先看 Format。
    /// </summary>
    public IReadOnlyList<SecsItem> Items =>
        _data as IReadOnlyList<SecsItem> ?? throw new SecsException($"Format={Format} 不是 List，没有子项");

    /// <summary>
    /// 第一个子项，GEM 报文最常用的取值方式；List 为空时抛异常。
    /// </summary>
    public SecsItem First()
    {
        var items = Items;
        return items.Count > 0 ? items[0] : throw new SecsException("List 为空，没有第一个子项");
    }

    #region 工厂

    /// <summary>列表；null 子项会被跳过，方便按条件拼报文。</summary>
    public static SecsItem L(params SecsItem?[] items)
    {
        return L((IEnumerable<SecsItem?>)items);
    }

    public static SecsItem L(IEnumerable<SecsItem?> items)
    {
        return new SecsItem(SecsFormat.List, items.Where(item => item is not null).Cast<SecsItem>().ToArray());
    }

    public static SecsItem A(string text) => new(SecsFormat.Ascii, text);

    public static SecsItem J(string text) => new(SecsFormat.Jis8, text);

    public static SecsItem B(params byte[] data) => new(SecsFormat.Binary, data);

    public static SecsItem Boolean(params bool[] data) => new(SecsFormat.Boolean, data);

    public static SecsItem I1(params sbyte[] data) => new(SecsFormat.I1, Array.ConvertAll(data, value => (long)value));

    public static SecsItem I2(params short[] data) => new(SecsFormat.I2, Array.ConvertAll(data, value => (long)value));

    public static SecsItem I4(params int[] data) => new(SecsFormat.I4, Array.ConvertAll(data, value => (long)value));

    public static SecsItem I8(params long[] data) => new(SecsFormat.I8, data);

    public static SecsItem U1(params byte[] data) => new(SecsFormat.U1, Array.ConvertAll(data, value => (ulong)value));

    public static SecsItem U2(params ushort[] data) => new(SecsFormat.U2, Array.ConvertAll(data, value => (ulong)value));

    public static SecsItem U4(params uint[] data) => new(SecsFormat.U4, Array.ConvertAll(data, value => (ulong)value));

    public static SecsItem U8(params ulong[] data) => new(SecsFormat.U8, data);

    public static SecsItem F4(params float[] data) => new(SecsFormat.F4, Array.ConvertAll(data, value => (double)value));

    public static SecsItem F8(params double[] data) => new(SecsFormat.F8, data);

    #endregion

    #region 取值

    public string GetString() => _data as string ?? throw new SecsException($"Format={Format} 不是字符串");

    public byte[] GetBinary() => _data as byte[] ?? throw new SecsException($"Format={Format} 不是 Binary");

    public bool[] GetBooleanArray() => _data as bool[] ?? throw new SecsException($"Format={Format} 不是 Boolean");

    /// <summary>I/U 系列通吃：取第一个元素的 64 位值（U8 高于 long.MaxValue 时抛溢出异常，用 GetUInt64）。</summary>
    public long GetInt64()
    {
        return GetInt64Array().First();
    }

    public long[] GetInt64Array()
    {
        return _data switch
        {
            long[] values => values,
            ulong[] values => Array.ConvertAll(values, value => checked((long)value)),
            _ => throw new SecsException($"Format={Format} 不是整数"),
        };
    }

    public ulong GetUInt64()
    {
        return GetUInt64Array().First();
    }

    public ulong[] GetUInt64Array()
    {
        return _data switch
        {
            ulong[] values => values,
            long[] values => Array.ConvertAll(values, value => checked((ulong)value)),
            _ => throw new SecsException($"Format={Format} 不是整数"),
        };
    }

    public double GetDouble()
    {
        return GetDoubleArray().First();
    }

    public double[] GetDoubleArray() =>
        _data as double[] ?? throw new SecsException($"Format={Format} 不是浮点");

    #endregion

    #region 相等与文本

    /// <summary>深比较：格式一致、数据逐元素一致。round-trip 测试靠它验证编解码无损。</summary>
    public override bool Equals(object? obj)
    {
        if (obj is not SecsItem other || Format != other.Format)
        {
            return false;
        }

        switch (_data)
        {
            case IReadOnlyList<SecsItem> items:
                var others = other.Items;
                if (items.Count != others.Count)
                {
                    return false;
                }
                for (int i = 0; i < items.Count; i++)
                {
                    if (!items[i].Equals(others[i]))
                    {
                        return false;
                    }
                }
                return true;
            case string text:
                return text == other.GetString();
            case byte[] binary:
                return binary.SequenceEqual(other.GetBinary());
            case bool[] flags:
                return flags.SequenceEqual(other.GetBooleanArray());
            case long[] values:
                return values.SequenceEqual(other.GetInt64Array());
            case ulong[] values:
                return values.SequenceEqual(other.GetUInt64Array());
            case double[] values:
                return values.SequenceEqual(other.GetDoubleArray());
            default:
                return false;
        }
    }

    public override int GetHashCode() => HashCode.Combine(Format, Count);

    public override string ToString() => SecsMessageText.Format(this);

    #endregion
}
