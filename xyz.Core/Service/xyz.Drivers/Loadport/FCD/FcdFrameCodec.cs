using System.Text;
using xyz.Drivers.Communication;

namespace xyz.Drivers.Loadport.FCD;

/// <summary>
/// FCD B 类帧编解码：帧 = s00 + 体 + ';' + CR。
/// 自持接收缓冲，每通道一个实例；串口/网口通用。
/// </summary>
public class FcdFrameCodec : IFrameCodec
{
    private const string FramePrefix = "s00";  // SOH('s') + ADR("00")
    private const char FrameEnd = ';';
    private const char FrameDel = '\r';

    private readonly StringBuilder _rxBuffer = new();

    public string Wrap(string body)
    {
        return FramePrefix + body + FrameEnd + FrameDel;
    }

    public IEnumerable<string> Extract(string chunk)
    {
        _rxBuffer.Append(chunk);

        string buffered = _rxBuffer.ToString();
        var frames = new List<string>();
        int index;
        while ((index = buffered.IndexOf(FrameDel)) >= 0)
        {
            string frame = buffered[..index];
            buffered = buffered[(index + 1)..];
            if (!string.IsNullOrWhiteSpace(frame))
            {
                frames.Add(StripFrame(frame));
            }
        }

        _rxBuffer.Clear();
        _rxBuffer.Append(buffered);
        return frames;
    }

    /// <summary>
    /// 去掉 B 类外壳（s00 前缀 / 结尾 ';' / 历史 "$1" 前缀）。
    /// </summary>
    private static string StripFrame(string raw)
    {
        string s = raw.Trim();
        if (s.StartsWith("$1"))
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
