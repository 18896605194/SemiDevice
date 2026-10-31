using System.Globalization;
using xyz.Common.Log;

namespace xyz.Modules;

/// <summary>
/// 状态迁移表：(当前状态, 动作) → (执行态, 成功态)。当场做完、没有执行过程的迁移两个状态写一样。
/// 只管规则，不存当前状态——当前状态谁的就存在谁身上
/// </summary>
public sealed class StateTable
{
    private readonly Dictionary<(int? State, string Action), (int Executing, int Success)> _rows = new();

    /// <summary>
    /// 日志记在谁名下（持有这张表的模块或 Job）
    /// </summary>
    private readonly ILogSource _owner;

    public StateTable(ILogSource owner)
    {
        _owner = owner;
    }

    /// <summary>
    /// 整张装上（平台的默认表），原来的清掉
    /// </summary>
    public void Register(IEnumerable<KeyValuePair<(int? State, string Action), (int Executing, int Success)>> rows)
    {
        _rows.Clear();
        foreach (var row in rows)
        {
            _rows[row.Key] = row.Value;
        }
    }

    /// <summary>
    /// 单条追加 / 覆盖，机型在 AddTransitions 里调。
    /// 盖掉已有的那一行时记一条日志——盖是允许的，但得让人在开机日志里看得见
    /// </summary>
    public void Add((int? State, string Action) key, (int Executing, int Success) row)
    {
        if (_rows.ContainsKey(key))
        {
            string state = "任意状态";
            if (key.State is not null)
            {
                state = key.State.Value.ToString(CultureInfo.InvariantCulture);
            }

            LogHelper.Info(_owner, $"盖掉已有的状态迁移：{state} + {key.Action}");
        }

        _rows[key] = row;
    }

    /// <summary>
    /// 查表：先按当前状态精确匹配，没中再查 null 通配；都不中返回 false（这个状态不允许这个动作）
    /// </summary>
    public bool TryGet(int state, string action, out (int Executing, int Success) row)
    {
        if (_rows.TryGetValue((state, action), out row))
        {
            return true;
        }

        return _rows.TryGetValue((null, action), out row);
    }
}
