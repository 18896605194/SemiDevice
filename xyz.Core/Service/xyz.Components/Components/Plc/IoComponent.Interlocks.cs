using System.Globalization;
using xyz.Common.Log;

namespace xyz.Components.Components;

public partial class IoComponent
{
    private sealed record Interlock(int DiIndex, bool DiValue, int DoIndex, bool DoValue, string Text);

    // 扫描强制和所有业务下发共用一把锁，避免检查与写入之间被另一条软件命令插入。
    private readonly object _doGate = new();
    private List<Interlock> _interlocks = [];
    private readonly HashSet<int> _forcedDo = [];
    private readonly Dictionary<int, string> _interlockErrors = [];

    private static string BoolText(bool value) => value ? "true" : "false";

    private void LoadSaftyIoInterlock()
    {
        var rules = new List<Interlock>();
        string normalized = SaftyIoInterlock.Replace('；', ';').Replace('：', ':').Replace('，', ',');
        foreach (string item in normalized.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] sides = item.Split(':', StringSplitOptions.TrimEntries);
            if (sides.Length != 2 || !TryParsePoint(sides[0], "DI", out int di, out bool trigger))
            {
                throw InvalidInterlock(item);
            }

            foreach (string output in sides[1].Split(',', StringSplitOptions.TrimEntries))
            {
                if (!TryParsePoint(output, "DO", out int index, out bool forced))
                {
                    throw InvalidInterlock(item);
                }

                // 相同 DI 条件不可能同时要求一个 DO 取相反值，装配时直接报错。
                if (rules.Any(rule => rule.DiIndex == di && rule.DiValue == trigger
                    && rule.DoIndex == index && rule.DoValue != forced))
                {
                    throw new InvalidOperationException($"{Name}：SaftyIoInterlock 规则冲突：DI{di}={BoolText(trigger)} 对 DO{index} 同时要求 true 和 false");
                }

                rules.Add(new Interlock(di, trigger, index, forced, item));
            }
        }

        _interlocks = rules;
        _forcedDo.Clear();
        _interlockErrors.Clear();
    }

    private InvalidOperationException InvalidInterlock(string item) => new(
        $"{Name}：SaftyIoInterlock 里的“{item}”写得不对，应为 DI25=false:DO12=true,DO13=true，多条用分号隔开");

    private static bool TryParsePoint(string text, string prefix, out int index, out bool value)
    {
        index = 0;
        value = false;
        string[] parts = text.Split('=', StringSplitOptions.TrimEntries);
        return parts.Length == 2
            && parts[0].StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && int.TryParse(parts[0][prefix.Length..].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out index)
            && bool.TryParse(parts[1], out value);
    }

    private void ValidateInterlockPoints()
    {
        foreach (var rule in _interlocks)
        {
            if (Di.Find(rule.DiIndex) is null || Do.Find(rule.DoIndex) is null)
            {
                throw new InvalidOperationException($"{Name}：联锁规则 {rule.Text} 引用了点表中不存在的 DI{rule.DiIndex} 或 DO{rule.DoIndex}");
            }
        }
    }

    // 同一轮每个 DI 只取一次，保证多个关联 DO 使用同一电平判断。null 不等于 false。
    private Dictionary<int, bool?> ReadInterlockStates()
    {
        var states = new Dictionary<int, bool?>();
        foreach (var rule in _interlocks)
        {
            if (!states.ContainsKey(rule.DiIndex))
            {
                states[rule.DiIndex] = TryReadDi(rule.DiIndex, out bool value) ? value : null;
            }
        }

        return states;
    }

    private void EnforceInterlocks(Dictionary<int, bool?> states)
    {
        foreach (var group in _interlocks.GroupBy(rule => rule.DoIndex))
        {
            int index = group.Key;
            var active = group.Where(rule => states[rule.DiIndex] == rule.DiValue).ToList();
            if (active.Count == 0)
            {
                _forcedDo.Remove(index);
                _interlockErrors.Remove(index);
                continue; // 解除后保持 DO，不恢复先前值。
            }

            bool forced = active[0].DoValue;
            var uncertain = group.Where(rule => states[rule.DiIndex] is null && rule.DoValue != forced).ToList();
            if (active.Any(rule => rule.DoValue != forced) || uncertain.Count > 0)
            {
                _forcedDo.Remove(index);
                ReportInterlockError(index, $"DO{index} 联锁冲突或关联 DI 读不到，不能确定强制值："
                    + string.Join("；", active.Concat(uncertain).Select(rule => rule.Text).Distinct()));
                continue; // 不按规则顺序交替写相反值；命令入口仍检查全部规则。
            }

            var plc = _plc;
            if (plc is null || !plc.IsConnected || Do.Find(index) is null)
            {
                _forcedDo.Remove(index);
                ReportInterlockError(index, $"DO{index} 联锁强制失败：PLC 未连接或 DO 点不存在");
                continue;
            }

            // 首次触发一定下发；之后回读偏离或读不到就重试，写成功不代表回读已确认。
            if (!_forcedDo.Contains(index) || !TryReadDo(index, out bool actual) || actual != forced)
            {
                // 已检查所有规则，直接走 PLC，避免强制命令递归进入业务拦截入口。
                if (!plc.WriteDo(index, forced))
                {
                    _forcedDo.Remove(index);
                    ReportInterlockError(index, $"DO{index} 联锁强制为 {BoolText(forced)} 失败，将在后续扫描重试");
                    continue;
                }

                if (_forcedDo.Add(index))
                {
                    LogHelper.Info(this, $"联锁强制 DO{index}={BoolText(forced)}：{string.Join("；", active.Select(rule => rule.Text).Distinct())}");
                }
            }

            _interlockErrors.Remove(index);
        }
    }

    private void ReportInterlockError(int index, string message)
    {
        if (!_interlockErrors.TryGetValue(index, out string? previous) || previous != message)
        {
            LogHelper.Warn(this, $"{message}");
            _interlockErrors[index] = message;
        }
    }
}
