using System.Text;
using xyz.Drivers.Communication;

namespace xyz._35021.Module.Drivers.Reje;

/// <summary>
/// 锐洁解码器：下发帧 = '@' + 体 + ';'；回复和推送以 ';' 分帧（如 "&gt;;"、"&gt;00000000#OK@Home;"），解成 RejeMessage；自带接收缓冲，一条通道一个
/// </summary>
public sealed class RejeCodec : IFrameCodec
{
    private const char CommandPrefix = '@';
    private const char FrameEnd = ';';

    private readonly StringBuilder _rxBuffer = new();

    /// <summary>
    /// 文本协议，ASCII
    /// </summary>
    public Encoding Encoding => Encoding.ASCII;

    /// <summary>
    /// 包壳：'@' + 体 + ';'
    /// </summary>
    public string Wrap(string body)
    {
        return CommandPrefix + body + FrameEnd;
    }

    /// <summary>
    /// 拆帧：按 ';' 切，去掉两头空白，空帧丢掉
    /// </summary>
    public IEnumerable<string> Extract(string chunk)
    {
        _rxBuffer.Append(chunk);

        string buffered = _rxBuffer.ToString();
        var frames = new List<string>();
        int index = buffered.IndexOf(FrameEnd);
        while (index >= 0)
        {
            string frame = buffered[..index].Trim();
            buffered = buffered[(index + 1)..];
            if (frame.Length > 0)
            {
                frames.Add(frame);
            }

            index = buffered.IndexOf(FrameEnd);
        }

        _rxBuffer.Clear();
        _rxBuffer.Append(buffered);
        return frames;
    }

    /// <summary>
    /// 解报文：拆成错误码、内容、回显名；确认帧（单独的 '>'，不带回显名，只表示受理）和格式不对的丢掉——指令等的是带回显名的结果帧
    /// </summary>
    public object? Decode(string frame)
    {
        if (!frame.StartsWith(RejeProtocol.ResponsePrefix, StringComparison.Ordinal))
        {
            return null;
        }

        string rest = frame[RejeProtocol.ResponsePrefix.Length..];
        int hash = rest.IndexOf('#');
        int at = rest.LastIndexOf('@');
        if (hash < 0 || at <= hash || at >= rest.Length - 1)
        {
            return null;
        }

        return new RejeMessage
        {
            Code = rest[..hash],
            Content = rest[(hash + 1)..at],
            Name = rest[(at + 1)..],
        };
    }
}
