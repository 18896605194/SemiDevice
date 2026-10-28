using xyz.Elements.Attributes;

namespace xyz.Elements.Interfaces;

/// <summary>
/// PLC 的数据块读写（轴的收发块）
/// </summary>
[Ioc]
public interface IPlcBlock
{
    /// <summary>
    /// 登记一个每拍读回来的数据块
    /// </summary>
    void Register(string path);

    /// <summary>
    /// 取一个数据块最近一拍的内容，读不到返回 false
    /// </summary>
    bool TryReadBlock(string path, out byte[] block);

    /// <summary>
    /// 往一个数据块写
    /// </summary>
    bool WriteBlock(string path, byte[] data);
}
