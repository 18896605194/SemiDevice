using Mapster;
using ProtoBuf.Grpc;
using xyz.Tools;
using xyz.Database.Auth;
using xyz.Database.DbProvider;
using xyz.Shared.Dtos;
using xyz.Shared.Services;

namespace xyz.Service.UserManger;

/// <summary>
/// 用户管理 gRPC 服务实现，继承数据库操作基类，直接对接 SqlSugar/SQLite。
/// </summary>
public class UserService : RepositoryBase<UserEntity>, IUserService
{
    static UserService()
    {
        XyzDb.InitTables<UserEntity>();
    }

    public async Task<RpcResponse> GetUsersAsync(RpcRequest request, CallContext context = default)
    {
        var entities = await GetAllEnabledAsync();
        var users = entities.Select(user => user.Adapt<UserDto>()).ToList();

        return RpcResponse.Ok(JsonHelper.Serialize(users));
    }

    public async Task<RpcResponse> ExistsAsync(RpcRequest request, CallContext context = default)
    {
        var userName = GetParameter(request, "UserName");
        var exists = await AnyAsync(user =>
            user.UserName == userName && user.IsEnabled);

        return RpcResponse.Ok(JsonHelper.Serialize(exists));
    }

    public async Task<RpcResponse> CreateUserAsync(RpcRequest request, CallContext context = default)
    {
        var userName = GetParameter(request, "UserName");
        var displayName = GetParameter(request, "DisplayName");
        var roleName = GetParameter(request, "RoleName");

        var entity = new UserEntity
        {
            UserName = userName,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? userName : displayName,
            RoleName = roleName,
            PasswordHash = string.Empty,
            IsEnabled = true
        };

        entity.Id = await InsertAsync(entity);

        return RpcResponse.Ok(JsonHelper.Serialize(entity.Adapt<UserDto>()));
    }

    public async Task<RpcResponse> DeleteUserAsync(RpcRequest request, CallContext context = default)
    {
        var idText = GetParameter(request, "Id");
        if (!long.TryParse(idText, out var id))
        {
            return RpcResponse.Fail("无效的用户 Id");
        }

        await DeleteAsync(user => user.Id == id);

        return RpcResponse.Ok("{}");
    }

    private static string GetParameter(RpcRequest request, string key)
    {
        return request.Parameters.TryGetValue(key, out var value) ? value : string.Empty;
    }
}
