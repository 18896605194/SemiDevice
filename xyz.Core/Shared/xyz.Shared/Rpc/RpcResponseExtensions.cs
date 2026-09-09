using xyz.Shared.Dtos;
using xyz.Tools;

namespace xyz.Shared.Rpc;

/// <summary>
/// RpcResponse 辅助：成功校验与 Data 反序列化。
/// 原先每个 ViewModel 各写一份 private Deserialize/EnsureSuccess，统一到这里。
/// </summary>
public static class RpcResponseExtensions
{
    /// <summary>
    /// 失败抛 InvalidOperationException(Message)；成功不返回内容。
    /// </summary>
    public static void EnsureSuccess(this RpcResponse response)
    {
        if (!response.Success)
        {
            throw new InvalidOperationException(response.Message);
        }
    }

    /// <summary>
    /// 成功则把 Data 反序列化为 T（统一走 JsonHelper 选项），失败抛 InvalidOperationException。
    /// </summary>
    public static T DeserializeData<T>(this RpcResponse response)
    {
        response.EnsureSuccess();
        return JsonHelper.Deserialize<T>(response.Data)!;
    }
}
