using xyz.Shared.Dtos;

namespace xyz.Components.Interfaces;

/// <summary>
/// 载具 ID 读码器
/// </summary>
public interface ICarrierIdReader
{
    /// <summary>
    /// 读码超时（EC）：载具自动读码一直发起不了，超过它按读码失败
    /// </summary>
    int ReadCarrierIdTimeout { get; }

    /// <summary>
    /// 发起一次读码，只提交就返回，结果之后用 GetCarrierIdResult 取；没连上或上一次还没读完返回 false
    /// </summary>
    bool StartReadCarrierId();

    /// <summary>
    /// 取走最近一次读码结果，取走即清；还没出结果为 null。读到了 Result 是载具号，没读到 ErrorMessage 是原因
    /// </summary>
    HandleResult<string>? GetCarrierIdResult();

    /// <summary>
    /// 关连接，不再重连
    /// </summary>
    void Close();
}
