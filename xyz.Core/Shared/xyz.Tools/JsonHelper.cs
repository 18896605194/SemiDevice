using System.Text.Json;

namespace xyz.Tools;

/// <summary>
/// 统一 JSON 入口，前后端共用：事件信封 Payload 与 RpcResponse.Data 都走这里，
/// 内部固定一套选项（写入 camelCase，读取属性名大小写不敏感，服务端 PascalCase/camelCase 都能解析）。
/// 不要在业务代码里各自 new JsonSerializerOptions。
/// </summary>
public static class JsonHelper
{
    /// <summary>
    /// 统一序列化选项。
    /// </summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// 序列化为 JSON 字符串。
    /// </summary>
    public static string Serialize<T>(T value)
    {
        return JsonSerializer.Serialize(value, Options);
    }

    /// <summary>
    /// 反序列化；空串/空白返回 default。
    /// </summary>
    public static T? Deserialize<T>(string json)
    {
        return string.IsNullOrWhiteSpace(json) ? default : JsonSerializer.Deserialize<T>(json, Options);
    }

    /// <summary>
    /// 按指定类型反序列化。
    /// </summary>
    public static object? Deserialize(string json, Type type)
    {
        return string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize(json, type, Options);
    }
}
