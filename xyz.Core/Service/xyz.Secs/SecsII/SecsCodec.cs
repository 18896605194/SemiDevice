using System.Buffers.Binary;
using System.Text;

namespace xyz.Secs.SecsII;

/// <summary>
/// SECS-II 编解码（SEMI E5）。只管 Item 树 ↔ 字节，不知道网络。
/// 头部规则：1 字节格式（高 6 位格式 + 低 2 位长度字节数）+ 1~3 字节大端长度
/// （List 的长度是子项个数，其余格式是字节数）+ 数据体；多字节整数/浮点一律大端。
/// </summary>
public static class SecsCodec
{
    public const int MaxDepth = 64;
    private static readonly Encoding StrictAscii = Encoding.GetEncoding("us-ascii",
        EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    private static readonly Encoding Jis8 = Encoding.GetEncoding("iso-8859-1",
        EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    /// <summary>
    /// 编码整棵树（含头）。SECS 报文都是小报文（几 KB 级），直接拼数组最简单可靠，不做流式。
    /// </summary>
    public static byte[] Encode(SecsItem item)
    {
        using var buffer = new MemoryStream();
        EncodeItem(item, buffer, 0);
        return buffer.ToArray();
    }

    /// <summary>
    /// 解码整段字节：必须恰好一棵树，有多余字节说明帧切分出了问题，直接抛异常断线重连。
    /// </summary>
    public static SecsItem Decode(ReadOnlySpan<byte> bytes)
    {
        var (item, consumed) = DecodeItem(bytes, 0);
        if (consumed != bytes.Length)
        {
            throw new SecsException($"解码后剩余 {bytes.Length - consumed} 字节，报文非法");
        }
        return item;
    }

    #region 编码

    private static void EncodeItem(SecsItem item, MemoryStream buffer, int depth)
    {
        if (depth > MaxDepth) throw new SecsException("SECS-II 嵌套超过限制");
        switch (item.Format)
        {
            case SecsFormat.List:
                var items = item.Items;
                WriteHead(buffer, SecsFormat.List, items.Count);
                foreach (var child in items)
                {
                    EncodeItem(child, buffer, depth + 1);
                }
                break;

            case SecsFormat.Ascii:
            case SecsFormat.Jis8:
                byte[] text;
                try { text = (item.Format == SecsFormat.Ascii ? StrictAscii : Jis8).GetBytes(item.GetString()); }
                catch (EncoderFallbackException ex) { throw new SecsException($"字符串无法编码为 {item.Format}: {ex.Message}"); }
                WriteHead(buffer, item.Format, text.Length);
                buffer.Write(text, 0, text.Length);
                break;

            case SecsFormat.Binary:
                var binary = item.GetBinary();
                WriteHead(buffer, item.Format, binary.Length);
                buffer.Write(binary, 0, binary.Length);
                break;

            case SecsFormat.Boolean:
                var flags = item.GetBooleanArray();
                WriteHead(buffer, item.Format, flags.Length);
                foreach (var flag in flags)
                {
                    buffer.WriteByte(flag ? (byte)1 : (byte)0);
                }
                break;

            case SecsFormat.I1:
            case SecsFormat.I2:
            case SecsFormat.I4:
            case SecsFormat.I8:
                WriteIntegerArray(buffer, item, item.GetInt64Array(), signed: true);
                break;

            case SecsFormat.U1:
            case SecsFormat.U2:
            case SecsFormat.U4:
            case SecsFormat.U8:
                WriteIntegerArray(buffer, item, item.GetUInt64Array(), signed: false);
                break;

            case SecsFormat.F4:
                var f4 = item.GetDoubleArray();
                WriteHead(buffer, item.Format, f4.Length * 4);
                Span<byte> cell4 = stackalloc byte[8];
                foreach (var value in f4)
                {
                    BinaryPrimitives.WriteInt32BigEndian(cell4, BitConverter.SingleToInt32Bits((float)value));
                    buffer.Write(cell4[..4]);
                }
                break;

            case SecsFormat.F8:
                var f8 = item.GetDoubleArray();
                WriteHead(buffer, item.Format, f8.Length * 8);
                Span<byte> cell8 = stackalloc byte[8];
                foreach (var value in f8)
                {
                    BinaryPrimitives.WriteInt64BigEndian(cell8, BitConverter.DoubleToInt64Bits(value));
                    buffer.Write(cell8);
                }
                break;

            default:
                throw new SecsException($"不支持的格式 {item.Format}");
        }
    }

    /// <summary>整数数组编码：宽度由格式定，值域在工厂构造时已对齐宽度，这里直接取 64 位值的低 N 字节落大端。</summary>
    private static void WriteIntegerArray(MemoryStream buffer, SecsItem item, Array values, bool signed)
    {
        int width = item.Format switch
        {
            SecsFormat.I1 or SecsFormat.U1 => 1,
            SecsFormat.I2 or SecsFormat.U2 => 2,
            SecsFormat.I4 or SecsFormat.U4 => 4,
            _ => 8,
        };
        WriteHead(buffer, item.Format, values.Length * width);
        Span<byte> cell = stackalloc byte[8];
        foreach (var value in values)
        {
            ulong bits = signed ? (ulong)(long)value : (ulong)value;
            for (int i = 0; i < width; i++)
            {
                cell[i] = (byte)(bits >> (8 * (width - 1 - i)));
            }
            buffer.Write(cell[..width]);
        }
    }

    /// <summary>写格式字节 + 长度：按长度自动选 1~3 字节长度头（SECS 报文不可能超过 3 字节长度）。</summary>
    private static void WriteHead(MemoryStream buffer, SecsFormat format, int length)
    {
        int lengthBytes = length <= byte.MaxValue ? 1 : length <= ushort.MaxValue ? 2 : 3;
        if (length < 0 || length > 0xFFFFFF) throw new SecsException("数据项长度超过 3 字节可表示范围");
        buffer.WriteByte((byte)((int)format | lengthBytes));
        Span<byte> span = stackalloc byte[3];
        for (int i = 0; i < lengthBytes; i++)
        {
            span[i] = (byte)(length >> (8 * (lengthBytes - 1 - i)));
        }
        buffer.Write(span[..lengthBytes]);
    }

    #endregion

    #region 解码

    /// <summary>解出一棵子树，返回子树和消费的字节数（List 需要逐子项递归累计）。</summary>
    private static (SecsItem Item, int Consumed) DecodeItem(ReadOnlySpan<byte> bytes, int depth)
    {
        if (depth > MaxDepth) throw new SecsException("SECS-II 嵌套超过限制");
        if (bytes.Length < 2)
        {
            throw new SecsException("剩余字节不足一个项头");
        }

        byte head = bytes[0];
        var format = (SecsFormat)(head & 0xFC);
        int lengthBytes = head & 0x03;
        if (lengthBytes == 0) throw new SecsException("数据项长度字节数不能为 0");
        if (bytes.Length < 1 + lengthBytes)
        {
            throw new SecsException($"格式 {format} 的长度头不完整");
        }

        int length = 0;
        for (int i = 0; i < lengthBytes; i++)
        {
            length = (length << 8) | bytes[1 + i];
        }

        switch (format)
        {
            case SecsFormat.List:
                if (length > (bytes.Length - 1 - lengthBytes) / 2)
                    throw new SecsException("List 声明的子项个数超过剩余数据");
                var children = new List<SecsItem>(length);
                int offset = 1 + lengthBytes;
                for (int i = 0; i < length; i++)
                {
                    var (child, consumed) = DecodeItem(bytes[offset..], depth + 1);
                    children.Add(child);
                    offset += consumed;
                }
                return (SecsItem.L(children), offset);

            case SecsFormat.Ascii:
            case SecsFormat.Jis8:
                RequireLength(format, bytes, lengthBytes, length);
                return (format == SecsFormat.Ascii
                    ? SecsItem.A(DecodeAscii(bytes.Slice(1 + lengthBytes, length)))
                    : SecsItem.J(Jis8.GetString(bytes.Slice(1 + lengthBytes, length))), 1 + lengthBytes + length);

            case SecsFormat.Binary:
                RequireLength(format, bytes, lengthBytes, length);
                var binary = new byte[length];
                bytes.Slice(1 + lengthBytes, length).CopyTo(binary);
                return (SecsItem.B(binary), 1 + lengthBytes + length);

            case SecsFormat.Boolean:
                RequireLength(format, bytes, lengthBytes, length);
                var flags = new bool[length];
                for (int i = 0; i < length; i++)
                {
                    flags[i] = bytes[1 + lengthBytes + i] != 0;
                }
                return (SecsItem.Boolean(flags), 1 + lengthBytes + length);

            case SecsFormat.I1:
            case SecsFormat.I2:
            case SecsFormat.I4:
            case SecsFormat.I8:
            case SecsFormat.U1:
            case SecsFormat.U2:
            case SecsFormat.U4:
            case SecsFormat.U8:
                return DecodeIntegerArray(bytes, format, lengthBytes, length);

            case SecsFormat.F4:
                return DecodeFloating(bytes, format, lengthBytes, length, 4);

            case SecsFormat.F8:
                return DecodeFloating(bytes, format, lengthBytes, length, 8);

            default:
                throw new SecsException($"不支持的格式码 0x{head:X2}");
        }
    }

    private static (SecsItem Item, int Consumed) DecodeIntegerArray(ReadOnlySpan<byte> bytes, SecsFormat format, int lengthBytes, int length)
    {
        RequireLength(format, bytes, lengthBytes, length);
        int width = format switch
        {
            SecsFormat.I1 or SecsFormat.U1 => 1,
            SecsFormat.I2 or SecsFormat.U2 => 2,
            SecsFormat.I4 or SecsFormat.U4 => 4,
            _ => 8,
        };
        if (length % width != 0)
        {
            throw new SecsException($"格式 {format} 数据长度 {length} 不是 {width} 的整数倍");
        }

        int count = length / width;
        int offset = 1 + lengthBytes;
        if (format is SecsFormat.I1 or SecsFormat.I2 or SecsFormat.I4 or SecsFormat.I8)
        {
            var values = new long[count];
            for (int i = 0; i < count; i++)
            {
                values[i] = ReadBigEndianSigned(bytes.Slice(offset + i * width, width));
            }
            return (CreateSigned(format, values), offset + length);
        }
        else
        {
            var values = new ulong[count];
            for (int i = 0; i < count; i++)
            {
                values[i] = ReadBigEndianUnsigned(bytes.Slice(offset + i * width, width));
            }
            return (CreateUnsigned(format, values), offset + length);
        }
    }

    private static SecsItem CreateSigned(SecsFormat format, long[] values) => format switch
    {
        SecsFormat.I1 => SecsItem.I1(Array.ConvertAll(values, value => (sbyte)value)),
        SecsFormat.I2 => SecsItem.I2(Array.ConvertAll(values, value => (short)value)),
        SecsFormat.I4 => SecsItem.I4(Array.ConvertAll(values, value => (int)value)),
        _ => SecsItem.I8(values),
    };

    private static SecsItem CreateUnsigned(SecsFormat format, ulong[] values) => format switch
    {
        SecsFormat.U1 => SecsItem.U1(Array.ConvertAll(values, value => (byte)value)),
        SecsFormat.U2 => SecsItem.U2(Array.ConvertAll(values, value => (ushort)value)),
        SecsFormat.U4 => SecsItem.U4(Array.ConvertAll(values, value => (uint)value)),
        _ => SecsItem.U8(values),
    };

    private static (SecsItem Item, int Consumed) DecodeFloating(ReadOnlySpan<byte> bytes, SecsFormat format, int lengthBytes, int length, int width)
    {
        RequireLength(format, bytes, lengthBytes, length);
        if (length % width != 0)
        {
            throw new SecsException($"格式 {format} 数据长度 {length} 不是 {width} 的整数倍");
        }

        int count = length / width;
        int offset = 1 + lengthBytes;
        var values = new double[count];
        for (int i = 0; i < count; i++)
        {
            values[i] = width == 4
                ? BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(offset + i * width, width)))
                : BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64BigEndian(bytes.Slice(offset + i * width, width)));
        }
        return (width == 4 ? SecsItem.F4(Array.ConvertAll(values, value => (float)value)) : SecsItem.F8(values), offset + length);
    }

    private static long ReadBigEndianSigned(ReadOnlySpan<byte> source)
    {
        long value = source[0] >= 0x80 ? -1 : 0;  // 符号扩展：负数前导补 1
        foreach (var b in source)
        {
            value = (value << 8) | b;
        }
        return value;
    }

    private static ulong ReadBigEndianUnsigned(ReadOnlySpan<byte> source)
    {
        ulong value = 0;
        foreach (var b in source)
        {
            value = (value << 8) | b;
        }
        return value;
    }

    private static void RequireLength(SecsFormat format, ReadOnlySpan<byte> bytes, int lengthBytes, int length)
    {
        if (bytes.Length < 1 + lengthBytes + length)
        {
            throw new SecsException($"格式 {format} 声明 {length} 字节但剩余不足");
        }
    }

    private static string DecodeAscii(ReadOnlySpan<byte> bytes)
    {
        try { return StrictAscii.GetString(bytes); }
        catch (DecoderFallbackException ex) { throw new SecsException($"非法 ASCII 字节: {ex.Message}"); }
    }

    #endregion
}
