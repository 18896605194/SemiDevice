using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using ProtoBuf;
using xyz.Tools;

namespace xyz.Shared.Dtos;

/// <summary>
/// 通用 RPC 请求
/// </summary>
[ProtoContract]
public class RpcRequest
{
    [ProtoMember(1)]
    public string Parameters { get; set; } = string.Empty;

    /// <summary>
    /// 客户端用：按名字装参数，名字用 RpcParams 里的常量，如 RpcRequest.Of((RpcParams.Index, 1), (RpcParams.Name, "A"))
    /// </summary>
    public static RpcRequest Of(params (string Name, object? Value)[] parameters)
    {
        // 同一个名字给了两次的，以后给的为准
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (name, value) in parameters)
        {
            values[name] = value;
        }

        return new RpcRequest { Parameters = JsonHelper.Serialize(values) };
    }

    /// <summary>
    /// 服务端用：按名字取一个参数（不分大小写），没带、是 null 或类型对不上回 false
    /// </summary>
    public bool TryGet<T>(string name, [MaybeNullWhen(false)] out T value)
    {
        value = default;
        if (!TryFind(name, out var element))
        {
            return false;
        }

        try
        {
            value = element.Deserialize<T>(JsonHelper.Options);
        }
        catch (JsonException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }

        return value is not null;
    }

    /// <summary>
    /// 服务端用：取可以不带的参数，没带、是 null 或类型对不上用 fallback
    /// </summary>
    public T Get<T>(string name, T fallback)
    {
        if (!TryGet(name, out T? value) || value is null)
        {
            return fallback;
        }

        return value;
    }

    /// <summary>
    /// 在 Parameters 里按名字找一项（不分大小写）；Parameters 不是 JSON 对象也当没找到
    /// </summary>
    private bool TryFind(string name, out JsonElement element)
    {
        element = default;
        if (string.IsNullOrWhiteSpace(Parameters))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(Parameters);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    // 出了 using 文档就释放了，取出来的这一项要单独复制一份
                    element = property.Value.Clone();
                    return true;
                }
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }
}
