using System.Text;
using xyz.Drivers.Communication;

namespace xyz._35021.Module.Drivers.Fcd;

/// <summary>
/// 富创得 LoadPort 解码器：帧 = s00 + 体 + ';' + CR，解成 FcdMessage；串口、网口通用，自带接收缓冲，一条通道一个
/// </summary>
public sealed class FcdCodec : IFrameCodec
{
    private const string FramePrefix = "s00";  // SOH('s') + ADR("00")
    private const char FrameEnd = ';';
    private const char FrameDel = '\r';

    private readonly StringBuilder _rxBuffer = new();

    /// <summary>
    /// 文本协议，ASCII
    /// </summary>
    public Encoding Encoding => Encoding.ASCII;

    /// <summary>
    /// 包壳：s00 + 体 + ';' + CR
    /// </summary>
    public string Wrap(string body)
    {
        return FramePrefix + body + FrameEnd + FrameDel;
    }

    /// <summary>
    /// 拆帧：按 CR 切，去掉 B 类外壳
    /// </summary>
    public IEnumerable<string> Extract(string chunk)
    {
        _rxBuffer.Append(chunk);

        string buffered = _rxBuffer.ToString();
        var frames = new List<string>();
        int index = buffered.IndexOf(FrameDel);
        while (index >= 0)
        {
            string frame = buffered[..index];
            buffered = buffered[(index + 1)..];
            if (!string.IsNullOrWhiteSpace(frame))
            {
                frames.Add(StripFrame(frame));
            }

            index = buffered.IndexOf(FrameDel);
        }

        _rxBuffer.Clear();
        _rxBuffer.Append(buffered);
        return frames;
    }

    /// <summary>
    /// 解报文：类型:名字/数据；没有冒号的认不出，丢掉
    /// </summary>
    public object? Decode(string frame)
    {
        int colon = frame.IndexOf(':');
        if (colon <= 0)
        {
            return null;
        }

        string rest = frame[(colon + 1)..];
        string name = rest.Split('/')[0];
        string data = string.Empty;
        if (rest.Length > name.Length)
        {
            data = rest[(name.Length + 1)..];
        }

        return new FcdMessage
        {
            Type = frame[..colon].ToUpperInvariant(),
            Name = name,
            Data = data,
        };
    }

    /// <summary>
    /// 去掉 B 类外壳（s00 前缀 / 结尾 ';' / 历史 "$1" 前缀）
    /// </summary>
    private static string StripFrame(string raw)
    {
        string s = raw.Trim();
        if (s.StartsWith("$1", StringComparison.Ordinal))
        {
            s = s[2..];
        }

        if (s.Length >= 3 && s[0] == 's')
        {
            s = s[3..];
        }

        if (s.EndsWith(FrameEnd))
        {
            s = s[..^1];
        }

        return s.Trim();
    }
}
