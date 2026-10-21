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
    /// 新建角色，回新建的 RoleDto
    /// </summary>
    [OperationContract]
    Task<RpcResponse> CreateRoleAsync(RoleSaveRequest request, CallContext context = default);

    /// <summary>
    /// 改角色名和说明
    /// </summary>
    [OperationContract]
    Task<RpcResponse> UpdateRoleAsync(RoleSaveRequest request, CallContext context = default);

    /// <summary>
    /// 删角色
    /// </summary>
    [OperationContract]
    Task<RpcResponse> DeleteRoleAsync(AccountIdRequest request, CallContext context = default);

    /// <summary>
    /// 取一个角色配过的权限（RolePermissionsDto）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> GetPermissionsAsync(AccountIdRequest request, CallContext context = default);

    /// <summary>
    /// 存一个角色的权限：整张换掉
    /// </summary>
    [OperationContract]
    Task<RpcResponse> SavePermissionsAsync(RolePermissionsSaveRequest request, CallContext context = default);
}
