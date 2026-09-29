using System.ServiceModel;
using xyz.Shared.Dtos;

namespace xyz.Shared.Services;

/// <summary>
/// IO 输出点手动写入（IO 界面用）。读不走这里——点位值由后端按周期整包推送（IoDto）。
/// 写进 PLC 即回包，实际输出状态以下一包推送的回读为准。
/// </summary>
[ServiceContract]
public interface IIoService
{
    /// <summary>
    /// 写一个 DO 点。失败回 io.write_failed（PLC 没连上、点表里没有这个 DO、写 PLC 出错）。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> WriteDoAsync(IoWriteRequest request);

    /// <summary>
    /// 下发一个 AO 点（工程值）。超出点表标定的工程量范围回 io.out_of_range；写不进回 io.write_failed。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> WriteAoAsync(IoWriteRequest request);
}
