namespace xyz.Modules;

/// <summary>
/// 一个下拉字段按数据源取出来能选的：不跟别的字段走的是一张表；跟着别的字段走的（数据源写了 @字段名）是"那个字段的值 → 一张表"。
/// 名字都按 sc.xml 里的写法，先后也按 sc.xml。
/// </summary>
public sealed class ProcessRecipeChoices
{
    public static readonly ProcessRecipeChoices Empty =
        new([], new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase));

    public ProcessRecipeChoices(IReadOnlyList<string> values, IReadOnlyDictionary<string, IReadOnlyList<string>> byParent)
    {
        Values = values;
        ByParent = byParent;
    }

    /// <summary>
    /// 不跟别的字段走时能选的。
    /// </summary>
    public IReadOnlyList<string> Values { get; }

    /// <summary>
    /// 跟着别的字段走时：那个字段的值（设备名，不分大小写）→ 能选的。
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> ByParent { get; }

    /// <summary>
    /// 这一步能选的：跟着别的字段走（parentKey 不空）时按这一步那个字段的值取，那个字段没选就什么都没有。
    /// </summary>
    public IReadOnlyList<string> For(string parentKey, ProcessRecipeStep step)
    {
        if (parentKey.Length == 0)
        {
            return Values;
        }

        return ByParent.TryGetValue(step.Get(parentKey).Trim(), out var list) ? list : [];
    }

    /// <summary>
    /// 几个腔体的合起来：都能选的并在一起，先后按第一次出现的。
    /// </summary>
    public static ProcessRecipeChoices Merge(IEnumerable<ProcessRecipeChoices> all)
    {
        var values = new List<string>();
        var byParent = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var choices in all)
        {
            AddDistinct(values, choices.Values);
            foreach (var pair in choices.ByParent)
            {
                if (!byParent.TryGetValue(pair.Key, out var list))
                {
                    list = [];
                    byParent[pair.Key] = list;
                }

                AddDistinct(list, pair.Value);
            }
        }

        return new ProcessRecipeChoices(values,
            byParent.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<string>)pair.Value, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 不分大小写去重地追加。
    /// </summary>
    public static void AddDistinct(List<string> target, IEnumerable<string> items)
    {
        foreach (string item in items)
        {
            if (!target.Contains(item, StringComparer.OrdinalIgnoreCase))
            {
                target.Add(item);
            }
        }
    }
}

/// <summary>
/// 配方用到某个腔体上对不上的地方：哪个字段（显示名）、选的什么值——这个腔体没有。
/// </summary>
public sealed record ProcessRecipeMismatch(string Field, string Value);
