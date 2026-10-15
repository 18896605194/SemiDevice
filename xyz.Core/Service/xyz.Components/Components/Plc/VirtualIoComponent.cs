using System.Globalization;
using System.Text.RegularExpressions;
using xyz.Components.Attributes;
using xyz.Configs.Models;

namespace xyz.Components.Components;

/// <summary>
/// 软件 IO：DO 3000~3999、DI 4000~4999、AO 5000~5999、AI 6000~6999。
/// 输出值存于内存，输入由配置计算；既可作为根组件独立扫描，也可挂在 Io 下共用线程。
/// 通过 IoComponent 统一路由，不向 PLC 下发虚拟索引。
/// </summary>
[Component(description: "虚拟 IO（软件输出、模拟量比较、信号映射）")]
public class VirtualIoComponent : ComponentBase
{
    [SCEditor("", "VirtualIo", "虚拟 DO 初值，例如 DO3000=false;DO3001=true，索引 3000~3999")]
    public string DigitalOutputs { get; set; } = string.Empty;

    [SCEditor("", "VirtualIo", "虚拟 DI，例如 DI4000=AI3>50;DI4001=DO3000;DI4002=DI12==false，索引 4000~4999")]
    public string DigitalInputs { get; set; } = string.Empty;

    [SCEditor("", "VirtualIo", "虚拟 AO 初值，例如 AO5000=12.5，索引 5000~5999；软件工程值，不做 PLC 标定")]
    public string AnalogOutputs { get; set; } = string.Empty;

    [SCEditor("", "VirtualIo", "虚拟 AI 来源，例如 AI6000=AI3;AI6001=AO5000，索引 6000~6999；保持来源工程值")]
    public string AnalogInputs { get; set; } = string.Empty;

    private readonly record struct Point(string Type, int Index);
    private sealed record Input(Point Source, string Operator, double Operand);
    private readonly object _gate = new();
    private Dictionary<int, bool> _do = [];
    private Dictionary<int, double> _ao = [];
    private Dictionary<Point, Input> _inputs = [];
    private readonly Dictionary<int, bool?> _diValues = [];
    private readonly Dictionary<int, double?> _aiValues = [];
    private IoComponent? _owner;

    private static readonly Regex PointPattern = new(@"^(DI|DO|AI|AO)\s*([0-9]+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex InputPattern = new(@"^(DI|DO|AI|AO)\s*([0-9]+)\s*(?:(>=|<=|==|!=|>|<)\s*(.+))?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public IReadOnlyDictionary<int, bool?> DigitalInputValues
    {
        get
        {
            lock (_gate)
            {
                return new Dictionary<int, bool?>(_diValues);
            }
        }
    }

    public IReadOnlyDictionary<int, double?> AnalogInputValues
    {
        get
        {
            lock (_gate)
            {
                return new Dictionary<int, double?>(_aiValues);
            }
        }
    }

    internal void Bind(IoComponent owner)
    {
        if (_owner is not null && !ReferenceEquals(_owner, owner))
        {
            throw new InvalidOperationException("同一个 VirtualIoComponent 不能挂到多个 IO 组件");
        }

        _owner = owner;
    }

    public static bool IsIndexInRange(string type, int index) => type.ToUpperInvariant() switch
    {
        "DO" => index is >= 3000 and < 4000,
        "DI" => index is >= 4000 and < 5000,
        "AO" => index is >= 5000 and < 6000,
        "AI" => index is >= 6000 and < 7000,
        _ => false,
    };

    protected internal override void OnSettingLoaded(ModuleConfig setting)
    {
        base.OnSettingLoaded(setting);
        var digital = new Dictionary<int, bool>();
        var analog = new Dictionary<int, double>();
        var inputs = new Dictionary<Point, Input>();
        foreach (var (point, text) in Definitions(DigitalOutputs, "DO"))
        {
            if (!bool.TryParse(text, out bool value) || !digital.TryAdd(point.Index, value))
            {
                throw Invalid($"DO{point.Index} 初值不是 bool 或索引重复");
            }
        }

        foreach (var (point, text) in Definitions(AnalogOutputs, "AO"))
        {
            if (!TryNumber(text, out double value) || !analog.TryAdd(point.Index, value))
            {
                throw Invalid($"AO{point.Index} 初值不是有限数值或索引重复");
            }
        }

        foreach (var (point, text) in Definitions(DigitalInputs, "DI").Concat(Definitions(AnalogInputs, "AI")))
        {
            var match = InputPattern.Match(text);
            if (!match.Success)
            {
                throw Invalid($"{point.Type}{point.Index} 的来源表达式无效：{text}");
            }

            var source = ParsePoint(match.Groups[1].Value + match.Groups[2].Value);
            string op = match.Groups[3].Value;
            string operandText = match.Groups[4].Value.Trim();
            bool booleanSource = source.Type is "DI" or "DO";
            double operand = 0;
            if (point.Type == "AI")
            {
                if (booleanSource || op.Length > 0)
                {
                    throw Invalid($"AI{point.Index} 必须映射 AI/AO 数值来源：{text}");
                }
            }
            else if (booleanSource)
            {
                if (op.Length > 0)
                {
                    if ((op != "==" && op != "!=") || !bool.TryParse(operandText, out bool level))
                    {
                        throw Invalid($"DI{point.Index} 的布尔来源只支持直接映射或 ==/!= true/false");
                    }

                    operand = level ? 1 : 0;
                }
            }
            else if (op.Length == 0 || !TryNumber(operandText, out operand))
            {
                throw Invalid($"DI{point.Index} 的模拟量比较必须带运算符和有限数值：{text}");
            }

            if (!inputs.TryAdd(point, new Input(source, op, operand)))
            {
                throw Invalid($"{point.Type}{point.Index} 索引重复");
            }
        }

        // 虚拟依赖必须已声明，且不允许循环；物理点在 IO 点表加载后校验。
        var complete = new HashSet<Point>();
        var visiting = new HashSet<Point>();
        void Visit(Point point)
        {
            if (complete.Contains(point))
            {
                return;
            }

            if (!visiting.Add(point))
            {
                throw Invalid($"虚拟输入循环引用：{point.Type}{point.Index}");
            }

            var source = inputs[point].Source;
            if (source.Index >= 3000)
            {
                if (inputs.ContainsKey(source))
                {
                    Visit(source);
                }
                else if (!(source.Type == "DO" && digital.ContainsKey(source.Index))
                    && !(source.Type == "AO" && analog.ContainsKey(source.Index)))
                {
                    throw Invalid($"来源 {source.Type}{source.Index} 未配置");
                }
            }

            visiting.Remove(point);
            complete.Add(point);
        }

        foreach (var point in inputs.Keys)
        {
            Visit(point);
        }

        lock (_gate)
        {
            _do = digital;
            _ao = analog;
            _inputs = inputs;
            _diValues.Clear();
            _aiValues.Clear();
            foreach (var point in inputs.Keys)
            {
                if (point.Type == "DI")
                {
                    _diValues[point.Index] = null;
                }
                else
                {
                    _aiValues[point.Index] = null;
                }
            }
        }
    }

    private IEnumerable<(Point Point, string Text)> Definitions(string text, string type)
    {
        foreach (string entry in text.Replace('；', ';').Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int separator = entry.IndexOf('=');
            if (separator < 0)
            {
                throw Invalid($"缺少赋值符号：{entry}");
            }

            var point = ParsePoint(entry[..separator].Trim());
            if (point.Type != type || !IsIndexInRange(type, point.Index))
            {
                throw Invalid($"{entry} 的目标类型或索引区间错误");
            }

            yield return (point, entry[(separator + 1)..].Trim());
        }
    }

    private Point ParsePoint(string text)
    {
        var match = PointPattern.Match(text);
        if (!match.Success || !int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int index))
        {
            throw Invalid($"点位格式错误：{text}");
        }

        string type = match.Groups[1].Value.ToUpperInvariant();
        if (index >= 3000 && !IsIndexInRange(type, index))
        {
            throw Invalid($"点位区间错误：{text}");
        }

        return new Point(type, index);
    }

    private static bool TryNumber(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);

    private InvalidOperationException Invalid(string message) => new($"{Name}：虚拟 IO 配置错误，{message}");

    internal void ValidateSources()
    {
        foreach (var source in _inputs.Values.Select(input => input.Source).Where(point => point.Index < 3000).Distinct())
        {
            var table = source.Type switch
            {
                "DI" => _owner?.Di,
                "DO" => _owner?.Do,
                "AI" => _owner?.Ai,
                _ => _owner?.Ao,
            };
            if (table?.Find(source.Index) is null)
            {
                throw Invalid($"来源 {source.Type}{source.Index} 不在物理点表中");
            }
        }
    }

    public bool TryReadDi(int index, out bool on)
    {
        lock (_gate)
        {
            on = false;
            if (!IsIndexInRange("DI", index) || !TryEvaluate(new Point("DI", index), out double value))
            {
                return false;
            }

            on = value != 0;
            return true;
        }
    }

    public bool TryReadDo(int index, out bool on)
    {
        lock (_gate)
        {
            return _do.TryGetValue(index, out on);
        }
    }

    public bool TryReadAi(int index, out double value)
    {
        lock (_gate)
        {
            value = 0;
            return IsIndexInRange("AI", index) && TryEvaluate(new Point("AI", index), out value);
        }
    }

    public bool TryReadAo(int index, out double value)
    {
        lock (_gate)
        {
            return _ao.TryGetValue(index, out value);
        }
    }

    public bool WriteDo(int index, bool on)
    {
        lock (_gate)
        {
            if (!_do.ContainsKey(index))
            {
                return false;
            }

            _do[index] = on;
            return true;
        }
    }

    public bool WriteAo(int index, double value)
    {
        lock (_gate)
        {
            if (!_ao.ContainsKey(index) || !double.IsFinite(value))
            {
                return false;
            }

            _ao[index] = value;
            return true;
        }
    }

    // 扫描更新快照；实际读取也重新求值，以免 PLC 断线或源输出变化后仍返回陈旧的有效信号。
    protected override void OnScan()
    {
        base.OnScan();
        lock (_gate)
        {
            foreach (var point in _inputs.Keys)
            {
                TryEvaluate(point, out _);
            }
        }
    }

    private bool TryEvaluate(Point point, out double value)
    {
        value = 0;
        if (!_inputs.TryGetValue(point, out var input))
        {
            return false;
        }

        bool valid = TrySource(input.Source, out double source);
        if (valid)
        {
            value = input.Operator switch
            {
                ">" => source > input.Operand ? 1 : 0,
                ">=" => source >= input.Operand ? 1 : 0,
                "<" => source < input.Operand ? 1 : 0,
                "<=" => source <= input.Operand ? 1 : 0,
                "==" => source == input.Operand ? 1 : 0,
                "!=" => source != input.Operand ? 1 : 0,
                _ => source,
            };
        }

        if (point.Type == "DI")
        {
            _diValues[point.Index] = valid ? value != 0 : null;
        }
        else
        {
            _aiValues[point.Index] = valid ? value : null;
        }

        return valid;
    }

    private bool TrySource(Point source, out double value)
    {
        value = 0;
        if (source.Index >= 3000)
        {
            if (source.Type == "DO")
            {
                bool valid = _do.TryGetValue(source.Index, out bool on);
                value = on ? 1 : 0;
                return valid;
            }

            return source.Type == "AO" ? _ao.TryGetValue(source.Index, out value) : TryEvaluate(source, out value);
        }

        if (_owner is null)
        {
            return false;
        }

        bool level = false;
        bool success = source.Type switch
        {
            "DI" => _owner.TryReadDi(source.Index, out level),
            "DO" => _owner.TryReadDo(source.Index, out level),
            "AI" => _owner.TryReadAi(source.Index, out value),
            _ => _owner.TryReadAo(source.Index, out value),
        };
        if (source.Type is "DI" or "DO")
        {
            value = level ? 1 : 0;
        }

        return success && double.IsFinite(value);
    }
}
