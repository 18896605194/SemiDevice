using System.Text;
using xyz.Drivers.Communication;

namespace xyz.Drivers.Robot.Reje;

/// <summary>
/// 锐洁帧编解码：下发帧 = '@' + 体 + ';'；回复与推送以 ';' 分帧（如 "&gt;;"、"&gt;00000000#OK@Home;"）。
/// 自持接收缓冲，每通道一个实例。
/// </summary>
public class RejeFrameCodec : IFrameCodec
{
    private const char CommandPrefix = '@';
    private const char FrameEnd = ';';

    private readonly StringBuilder _rxBuffer = new();

    public string Wrap(string body)
    {
        return CommandPrefix + body + FrameEnd;
    }

    public IEnumerable<string> Extract(string chunk)
    {
        _rxBuffer.Append(chunk);

        string buffered = _rxBuffer.ToString();
        var frames = new List<string>();
        int index;
        while ((index = buffered.IndexOf(FrameEnd)) >= 0)
        {
            string frame = buffered[..index].Trim();
            buffered = buffered[(index + 1)..];
            if (frame.Length > 0)
            {
                frames.Add(frame);
            }
        }

        _rxBuffer.Clear();
        _rxBuffer.Append(buffered);
        return frames;
    }
}
