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
    /// 新建角色，回新建的 RoleDto。参数：Name（string）、Description（string，可不带）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> CreateRoleAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 改角色名和说明。参数：Id（long）、Name（string）、Description（string，可不带）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> UpdateRoleAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 删角色。参数：Id（long）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> DeleteRoleAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 取一个角色配过的权限（RolePermissionsDto）。参数：Id（long，角色编号）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> GetPermissionsAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 存一个角色的权限：整张换掉。参数：RoleId（long）、Items（RolePermissionItem 列表）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> SavePermissionsAsync(RpcRequest request, CallContext context = default);
}
