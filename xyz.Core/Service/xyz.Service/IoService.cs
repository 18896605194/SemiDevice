using xyz.Elements.Interfaces;
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

        if (_io is null)
        {
            return NotInstalled(ErrorCodes.IoNotInstalled);
        }

        return Reply(_io.WriteDo(index, value != 0));
    }

    public Task<RpcResponse> WriteAoAsync(RpcRequest request)
    {
        if (!request.TryGet<int>(RpcParams.Index, out var index)
            || !request.TryGet<double>(RpcParams.Value, out var value))
        {
            return InvalidRequest();
        }

        if (_io is null)
        {
            return NotInstalled(ErrorCodes.IoNotInstalled);
        }

        return Reply(_io.WriteAo(index, value));
    }
}
