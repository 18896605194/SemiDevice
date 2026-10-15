using xyz.Components.Attributes;
using xyz.Components.Models;
using xyz.Configs.Models;

namespace xyz.Components.Components;

public abstract class VirtualInputComponent : ComponentBase
{
    [SCEditor("-1", "VirtualInput", "全局唯一索引：虚拟 DI 4000~4999，虚拟 AI 6000~6999", Required = true)]
    public int Index { get; set; } = -1;

    [SCEditor("", "VirtualInput", "计算表达式：支持 IO 点、常量、括号、+ - * /、比较、&& || !", Required = true)]
    public string Expression { get; set; } = string.Empty;

    private sealed record State(double? Value);
    private State _state = new(null);
    private VirtualInputRegistry? _registry;
    protected double? CurrentValue => Volatile.Read(ref _state).Value;
    public bool IsValid => CurrentValue.HasValue;
    internal abstract string PointType { get; }
    internal IoExpression Compiled { get; private set; } = null!;

    protected internal override void OnSettingLoaded(ModuleConfig setting)
    {
        base.OnSettingLoaded(setting);
        int start = PointType == "DI" ? 4000 : 6000;
        if (Index < start || Index >= start + 1000)
        {
            throw new InvalidOperationException($"{Name}：{PointType} Index 必须在 {start}~{start + 999}");
        }

        try
        {
            Compiled = IoExpression.Parse(Expression, PointType == "DI");
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidOperationException($"{Name}（{PointType}{Index}）：{exception.Message}", exception);
        }

        SetValue(null);
    }

    internal void Bind(VirtualInputRegistry registry) => _registry = registry;
    internal void SetValue(double? value) => Volatile.Write(ref _state, new State(value));

    protected bool TryEvaluate(out double value)
    {
        value = 0;
        return _registry?.TryRead(new IoReference(PointType, Index), out value) ?? false;
    }

    protected override void OnScan()
    {
        base.OnScan();
        TryEvaluate(out _);
    }
}
