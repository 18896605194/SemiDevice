using System.Globalization;

namespace xyz.Components.Models;

/// <summary>
/// 一条安全联锁（SC）规则：DI 条件  受控 DO 的强制值。
/// 条件成立时 DO 必须是配置值；条件不成立时 DO 必须是配置值的相反值，下发不一致就拒绝。
/// </summary>
public sealed class IoInterlockRule
{
    /// <summary>条件 DI 索引。</summary>
    public int DiIndex { get; init; }

    /// <summary>条件成立时 DI 应有的电平。</summary>
    public bool DiValue { get; init; }

    /// <summary>受这条规则约束的 DO 和强制值。</summary>
    public IReadOnlyList<IoInterlockAction> Actions { get; init; } = [];

    /// <summary>规范化后的规则文本，出错时作为原因展示。</summary>
    public string Text { get; init; } = string.Empty;
}

/// <summary>安全联锁规则里的一个 DO 动作。</summary>
public readonly record struct IoInterlockAction(int DoIndex, bool Value);

/// <summary>安全联锁拦下 DO 下发的原因类型。</summary>
public enum IoInterlockBlockKind
{
    /// <summary>下发值和当前 DI 状态下要求的值不一致。</summary>
    Blocked,

    /// <summary>DI 当前值读不到，无法判断（按安全优先拦截）。</summary>
    Unknown,

    /// <summary>多条规则同时要求同一个 DO 为不同值，配置冲突（按安全优先拦截）。</summary>
    Conflict,
}

/// <summary>
/// DO 下发被安全联锁拦截的详情。服务层按 <see cref="Kind"/> 转成对应错误码和原因参数。
/// </summary>
public sealed record IoInterlockBlock(
    IoInterlockBlockKind Kind,
    int DoIndex,
    bool Requested,
    int DiIndex,
    string DiName,
    bool? DiLevel,
    bool? Required,
    string RuleText);

/// <summary>
/// SafetyInterlock 配置串解析：<c>Di25=false:DO12=true,DO13=true;Di26=true:DO14=false</c>。
/// 规则用分号（; 或 ；）隔开，一条规则里 DO 用逗号（, 或 ，）隔开。
/// </summary>
public static class IoInterlockParser
{
    public static IReadOnlyList<IoInterlockRule> Parse(string text)
    {
        var rules = new List<IoInterlockRule>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return rules;
        }

        foreach (string item in text.Split([';', '；'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int colon = item.IndexOfAny([':', '：']);
            if (colon <= 0 || colon >= item.Length - 1)
            {
                throw new InvalidOperationException($"{item}写法不对，要写成 Di25=false:DO12=true,DO13=true");
            }

            string condition = item[..colon].Trim();
            string actionText = item[(colon + 1)..].Trim();

            string[] conditionParts = condition.Split('=', StringSplitOptions.TrimEntries);
            if (conditionParts.Length != 2
                || !conditionParts[0].StartsWith("DI", StringComparison.OrdinalIgnoreCase)
                || !int.TryParse(conditionParts[0][2..].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int diIndex)
                || diIndex < 0
                || !bool.TryParse(conditionParts[1], out bool diValue))
            {
                throw new InvalidOperationException($"{item}的 DI 条件写法不对，要写成 Di25=false");
            }

            var actions = new List<IoInterlockAction>();
            var seen = new HashSet<int>();
            foreach (string entry in actionText.Split([',', '，'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string[] actionParts = entry.Split('=', StringSplitOptions.TrimEntries);
                if (actionParts.Length != 2
                    || !actionParts[0].StartsWith("DO", StringComparison.OrdinalIgnoreCase)
                    || !int.TryParse(actionParts[0][2..].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int doIndex)
                    || doIndex < 0
                    || !bool.TryParse(actionParts[1], out bool doValue))
                {
                    throw new InvalidOperationException($"{item}里的 DO 动作{entry}写法不对，要写成 DO12=true");
                }

                if (!seen.Add(doIndex))
                {
                    throw new InvalidOperationException($"{item}里 DO{doIndex} 配了多次");
                }

                actions.Add(new IoInterlockAction(doIndex, doValue));
            }

            if (actions.Count == 0)
            {
                throw new InvalidOperationException($"{item}后面没配 DO 动作");
            }

            rules.Add(new IoInterlockRule
            {
                DiIndex = diIndex,
                DiValue = diValue,
                Actions = actions,
                Text = $"Di{diIndex}={Bool(diValue)}:" + string.Join(",", actions.Select(a => $"DO{a.DoIndex}={Bool(a.Value)}")),
            });
        }

        return rules;
    }

    private static string Bool(bool value) => value ? "true" : "false";
}