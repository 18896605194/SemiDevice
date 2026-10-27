using System.Text.Encodings.Web;
using System.Text.Json;

namespace xyz.Tools;

public static class JsonHelper
{

    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        // 中文原样写，不转成 \uXXXX，日志、库里直接看得懂；Unsafe 指放进网页不安全，项目里没有网页
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

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
