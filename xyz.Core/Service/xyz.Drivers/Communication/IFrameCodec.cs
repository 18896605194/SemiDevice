using System.Text;

namespace xyz.Drivers.Communication;

/// <summary>
/// 品牌的解码器：发的时候包壳，收的时候拆帧、解成品牌自己的报文对象；自带接收缓冲，一条通道一个实例
/// </summary>
public interface IFrameCodec
{
    /// <summary>
    /// 帧文本用的编码：文本协议用 ASCII，二进制协议用 Latin1（字节过字符串一一对应、不丢）
    /// </summary>
    Encoding Encoding { get; }

    /// <summary>
    /// 包壳：给一段帧体加上协议的头尾（握手用的控制字符这类不用包的原样返回）
    /// </summary>
    string Wrap(string body);

    /// <summary>
    /// 拆帧：接上收到的这一段，拆出已经收齐的帧（去掉壳），没收齐的留着等下一段
    /// </summary>
    IEnumerable<string> Extract(string chunk);

    /// <summary>
    /// 解报文：一帧解成品牌自己的报文对象，认不出来的返回 null（丢掉）；指令拿到的都是这里解好的报文
    /// </summary>
    object? Decode(string frame);
}
