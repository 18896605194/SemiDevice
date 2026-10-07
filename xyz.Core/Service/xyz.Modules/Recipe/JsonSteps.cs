using System.Text.Json;

namespace xyz.Modules;

/// <summary>
/// Host 给的配方 JSON 看样子是哪种（流程配方库、工艺配方库各认自己的）：
/// 流程配方每一步带 group（分组），工艺配方每一步带 values（字段名 → 值）。只看样子，内容对不对存的时候再查。
/// </summary>
internal static class JsonSteps
{
    /// <summary>根上有 steps（属性名不分大小写），是不空的数组，每一步都是带 property 这个属性的对象；读不出来的算不是。</summary>
    public static bool AllHave(string json, string property)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var steps = Find(root, "steps");
            if (steps is null || steps.Value.ValueKind != JsonValueKind.Array || steps.Value.GetArrayLength() == 0)
            {
                return false;
            }

            foreach (var step in steps.Value.EnumerateArray())
            {
                if (step.ValueKind != JsonValueKind.Object || Find(step, property) is null)
                {
                    return false;
                }
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>对象里按名字（不分大小写）找一个属性；没有返回 null。</summary>
    private static JsonElement? Find(JsonElement element, string name)
    {
        foreach (var item in element.EnumerateObject())
        {
            if (string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return item.Value;
            }
        }

        return null;
    }
}
