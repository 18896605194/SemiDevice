using System.Globalization;
using xyz.Components.Components;
using xyz.Components.Interfaces;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Shared.Rpc;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Service;

/// <summary>
/// IO  服务
/// </summary>
public class IoService : BaseService, IIoService
{
    private readonly IIo? _io;

    public IoService()
    {
        _io = IocHelper.GetService<IIo>();
    }

    public Task<RpcResponse> WriteDoAsync(RpcRequest request)
    {
        if (!request.TryGet<int>(RpcParams.Index, out var index)
            || !request.TryGet<double>(RpcParams.Value, out var value))
        {
            return InvalidRequest();
        }

        bool on = value != 0;
        if (_io is null || !_io.WriteDo(index, on))
        {
            return WriteFailed("DO", index);
        }

        return Task.FromResult(RpcResponse.Ok());
    }

    public Task<RpcResponse> WriteAoAsync(RpcRequest request)
    {
        if (!request.TryGet<int>(RpcParams.Index, out var index)
            || !request.TryGet<double>(RpcParams.Value, out var value))
        {
            return InvalidRequest();
        }

        var point = _io?.Ao.Find(index);
        if (_io is null || point is null)
        {
            return WriteFailed("AO", index);
        }

        // 标定过的点按工程量范围卡住：模拟量输出多半是流量、压力、转速设定，给过头了设备会照做。
        if (point.IsScaled)
        {
            double min = Math.Min(point.LogicalMin, point.LogicalMax);
            double max = Math.Max(point.LogicalMin, point.LogicalMax);
            if (value < min || value > max)
            {
                return Fail(ErrorCodes.IoOutOfRange, index.ToString(), Format(min), Format(max), point.Unit);
            }
        }

        if (!_io.WriteAo(index, value))
        {
            return WriteFailed("AO", index);
        }

        return Task.FromResult(RpcResponse.Ok());
    }

    /// <summary>
    /// 写不成回包（io.write_failed）：哪一类、第几个点
    /// </summary>
    private static Task<RpcResponse> WriteFailed(string type, int index)
    {
        return Fail(ErrorCodes.IoWriteFailed, type, index.ToString());
    }

    private static string Format(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
