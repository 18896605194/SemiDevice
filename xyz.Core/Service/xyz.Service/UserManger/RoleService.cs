using Mapster;
using ProtoBuf.Grpc;
using System.Text.Json;
using xyz.Database.Auth;
using xyz.Database.DbProvider;
using xyz.Shared.Dtos;
using xyz.Shared.Services;

namespace xyz.Service.UserManger;

/// <summary>
/// 角色管理 gRPC 服务实现，继承数据库操作基类，直接对接 SqlSugar/SQLite。
/// </summary>
public class RoleService : RepositoryBase<RoleEntity>, IRoleService
{
    static RoleService()
    {
        XyzDb.InitTables<RoleEntity>();
    }

    public async Task<RpcResponse> GetRolesAsync(RpcRequest request, CallContext context = default)
    {
        var entities = await GetAllEnabledAsync();
        var roles = entities.Select(role => role.Adapt<RoleDto>()).ToList();

        return RpcResponse.Ok(JsonSerializer.Serialize(roles));
    }

    public async Task<RpcResponse> ExistsAsync(RpcRequest request, CallContext context = default)
    {
        var name = GetParameter(request, "Name");
        var exists = await AnyAsync(role =>
            role.Name == name && role.IsEnabled);

        return RpcResponse.Ok(JsonSerializer.Serialize(exists));
    }

    public async Task<RpcResponse> CreateRoleAsync(RpcRequest request, CallContext context = default)
    {
        var name = GetParameter(request, "Name");
        var description = GetParameter(request, "Description");

        var entity = new RoleEntity
        {
            Name = name,
            Code = name,
            Description = description
        };

        entity.Id = await InsertAsync(entity);

        return RpcResponse.Ok(JsonSerializer.Serialize(entity.Adapt<RoleDto>()));
    }

    public async Task<RpcResponse> DeleteRoleAsync(RpcRequest request, CallContext context = default)
    {
        var idText = GetParameter(request, "Id");
        if (!long.TryParse(idText, out var id))
        {
            return RpcResponse.Fail("无效的角色 Id");
        }

        await DeleteAsync(role => role.Id == id);

        return RpcResponse.Ok("{}");
    }

    private static string GetParameter(RpcRequest request, string key)
    {
        return request.Parameters.TryGetValue(key, out var value) ? value : string.Empty;
    }
}
