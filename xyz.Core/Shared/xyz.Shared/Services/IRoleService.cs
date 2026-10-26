using ProtoBuf.Grpc;
using System.ServiceModel;
using xyz.Shared.Dtos;

namespace xyz.Shared.Services;

/// <summary>
/// 角色管理 gRPC 服务契约
/// </summary>
[ServiceContract]
public interface IRoleService
{
    /// <summary>
    /// 全部角色（List&lt;RoleDto&gt;）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> GetRolesAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 新建角色（RoleDto）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> CreateRoleAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 修改角色
    /// </summary>
    [OperationContract]
    Task<RpcResponse> UpdateRoleAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 删除角色
    /// </summary>
    [OperationContract]
    Task<RpcResponse> DeleteRoleAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 获取角色权限（RolePermissionsDto）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> GetPermissionsAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 保存角色权限
    /// </summary>
    [OperationContract]
    Task<RpcResponse> SavePermissionsAsync(RpcRequest request, CallContext context = default);
}
