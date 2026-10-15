using System.Globalization;
using xyz.Common.Log;
using xyz.Components.Components;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Shared.Services;

namespace xyz.Service;

/// <summary>
/// IO 输出点手动写入 gRPC 服务（IO 界面开关 DO、下发 AO）：写进 PLC 即回包，实际输出以下一包推送的回读为准。
/// 每次手动写都记一条日志——强制输出是现场最容易出事的操作，事后要查得到什么时候动了哪个点。
/// </summary>
public class IoService : IIoService
{
    public Task<RpcResponse> WriteDoAsync(IoWriteRequest request)
    {
        bool on = request.Value != 0;
        var io = IoComponent.Current;
        if (io is null || !io.WriteDo(request.Index, on))
        {
            return WriteFailed("DO", request.Index);
        }

        LogHelper.Info("Io", $"手动写 DO {request.Index} = {(on ? 1 : 0)}");
        return Task.FromResult(RpcResponse.Ok());
    }

    public Task<RpcResponse> WriteAoAsync(IoWriteRequest request)
    {
        var io = IoComponent.Current;
        var point = io?.Ao.Find(request.Index);
        if (io is null || point is null)
        {
            return WriteFailed("AO", request.Index);
        }

        // 标定过的点按工程量范围卡住：模拟量输出多半是流量、压力、转速设定，给过头了设备会照做。
        if (point.IsScaled)
        {
            double min = Math.Min(point.LogicalMin, point.LogicalMax);
            double max = Math.Max(point.LogicalMin, point.LogicalMax);
            if (request.Value < min || request.Value > max)
            {
                return Task.FromResult(RpcResponse.Fail(ErrorCodes.IoOutOfRange,
                    [request.Index.ToString(), Format(min), Format(max), point.Unit]));
            }
        }

        if (!io.WriteAo(request.Index, request.Value))
        {
            return WriteFailed("AO", request.Index);
        }

        LogHelper.Info("Io", $"手动下发 AO {request.Index} = {Format(request.Value)} {point.Unit}");
        return Task.FromResult(RpcResponse.Ok());
    }

    private static Task<RpcResponse> WriteFailed(string type, int index)
    {
        return Task.FromResult(RpcResponse.Fail(ErrorCodes.IoWriteFailed, [type, index.ToString()]));
    }

    private static string Format(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
