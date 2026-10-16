using xyz.Shared.Dtos;

namespace xyz.Components.Interfaces;

/// <summary>
/// 载具 ID 读码器
/// </summary>
public interface ICarrierIdReader
{
    /// <summary>
    /// 读码超时（EC）
    /// </summary>
    int ReadCarrierIdTimeout { get; }

    /// <summary>
    /// 读码
    /// </summary>
    bool StartReadCarrierId();

    /// <summary>
    /// 读码结果
    /// </summary>
    HandleResult<string>? GetCarrierIdResult();

    /// <summary>
    /// 关连接，不再重连
    /// </summary>
    void Close();
}
