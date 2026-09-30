using ProtoBuf.Grpc;
using xyz.Components.Components;
using xyz.Shared.Dtos;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Service.Systems;

/// <summary>
/// EC gRPC 服务：把 EC 组件里的全部参数（定义 + 当前值）给界面，通用输入框按 EcKey 取范围就靠它。
/// EC 组件在组件层、不引用契约层，所以到 DTO 的转换搭在这儿。
/// </summary>
public class EcService : IEcService
{
    public Task<RpcResponse> GetDefinitionsAsync(RpcRequest request, CallContext context = default)
    {
        var items = EcComponent.Current?.Snapshot() ?? [];
        var dtos = items
            .Select(item => new EcItemDto
            {
                Key = $"{item.Path}.{item.Value.Name}",
                Format = item.Value.Format,
                Min = item.Value.Min,
                Max = item.Value.Max,
                Unit = item.Value.Unit,
                Default = item.Value.Default,
                Value = item.Value.Value,
                Description = item.Value.Description,
                Options = item.Value.Options,
            })
            .ToList();

        return Task.FromResult(RpcResponse.Ok(JsonHelper.Serialize(dtos)));
    }
}
