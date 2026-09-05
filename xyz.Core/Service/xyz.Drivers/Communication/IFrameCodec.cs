namespace xyz.Drivers.Communication;

/// <summary>
/// 协议内容的包装，每一款的协议可能不一样，这边入股内容一样头文件不一样，也可以适配
/// </summary>
public interface IFrameCodec
{
    string Wrap(string body);

    IEnumerable<string> Extract(string chunk);
}
