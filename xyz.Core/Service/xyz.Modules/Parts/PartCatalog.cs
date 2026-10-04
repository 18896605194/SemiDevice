using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using xyz.Common.Log;
using xyz.Components;
using xyz.Components.Attributes;
using xyz.Shared.Dtos;

namespace xyz.Modules;

/// <summary>
/// 一个模块下的手动部件：按 sc.xml 的组件树（先父后子）找标了 [PartKind] 的组件，读它们标了 [LiveValue] 的属性、
/// 按名字调标了 [ManualAction] 的方法。部件、数据、动作全由组件自己声明，这里不认具体是什么硬件——
/// sc 里加了组件（Arm2……）、写了新组件（温控器……），这里和推送、动作接口都不用改。
/// 组件树装好以后不会再变，部件表建一次；反射结果按组件类缓存，扫描周期里只读属性。
/// </summary>
public sealed class PartCatalog
{
    private const string LogModule = "Parts";

    private static readonly ConcurrentDictionary<Type, PartShape> Shapes = new();

    private readonly string _module;
    private readonly List<ManualPart> _parts = [];

    /// <summary>按模块现在的组件树建部件表（模块自己不算部件）。组件树装完再建。</summary>
    public PartCatalog(ComponentBase module)
    {
        _module = module.Name;
        foreach (var child in module.Children)
        {
            Collect(child);
        }
    }

    /// <summary>部件表，按 sc.xml 里的先后（先父后子）。</summary>
    public IReadOnlyList<ManualPart> Parts => _parts;

    /// <summary>
    /// 部件快照：每个部件把实时数据读一遍。读不出来的给空串、不记日志——每个扫描周期都要读，记了就是每拍一条。
    /// </summary>
    public ModulePartsDto CreateDto()
    {
        var dto = new ModulePartsDto { Module = _module };
        foreach (var part in _parts)
        {
            var values = new Dictionary<string, string>(part.Shape.Values.Count);
            foreach (var value in part.Shape.Values)
            {
                values[value.Name] = Read(value, part.Component);
            }

            dto.Parts.Add(new PartDto
            {
                Path = part.Component.FullPath,
                Kind = part.Kind,
                Type = part.Component.GetType().Name,
                Values = values,
            });
        }

        return dto;
    }

    /// <summary>按全路径（sc.xml 的组件路径，如 "Chamber1.Arm1"）找部件，忽略大小写；不是部件返回 null。</summary>
    public ManualPart? Find(string path)
    {
        return _parts.FirstOrDefault(part => string.Equals(part.Component.FullPath, path, StringComparison.OrdinalIgnoreCase));
    }

    private void Collect(ComponentBase component)
    {
        var shape = Shapes.GetOrAdd(component.GetType(), PartShape.Of);
        if (shape.Kind is not null)
        {
            _parts.Add(new ManualPart(component, shape));
        }

        foreach (var child in component.Children)
        {
            Collect(child);
        }
    }

    /// <summary>读一项实时数据转成字符串：浮点按声明的位数取整（-0 记成 0），布尔 True / False，枚举写名字，其余按不变区域性。</summary>
    private static string Read(LiveValueMember member, ComponentBase component)
    {
        object? value;
        try
        {
            value = member.Property.GetValue(component);
        }
        catch
        {
            return string.Empty;
        }

        switch (value)
        {
            case null:
                return string.Empty;

            case double number:
                return Round(number, member.Decimals);

            case float single:
                return Round(single, member.Decimals);

            case bool flag:
                return flag ? bool.TrueString : bool.FalseString;

            case Enum item:
                return item.ToString();

            case IFormattable formattable:
                return formattable.ToString(null, CultureInfo.InvariantCulture);

            default:
                return value.ToString() ?? string.Empty;
        }
    }

    private static string Round(double number, int decimals)
    {
        if (!double.IsFinite(number))
        {
            return string.Empty;
        }

        double rounded = Math.Round(number, decimals, MidpointRounding.AwayFromZero);
        if (rounded == 0)
        {
            rounded = 0;
        }

        return rounded.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 一个组件类在手动页上的样子：种类（[PartKind]，没标就不是部件）、实时数据（[LiveValue]）、动作（[ManualAction]，按方法名，忽略大小写）。
    /// 标错的（属性读不了、方法不返回 bool、同名方法标了两个）记一条警告后跳过。
    /// </summary>
    internal sealed class PartShape
    {
        private PartShape(string? kind, IReadOnlyList<LiveValueMember> values, IReadOnlyDictionary<string, ManualPartAction> actions)
        {
            Kind = kind;
            Values = values;
            Actions = actions;
        }

        public string? Kind { get; }

        public IReadOnlyList<LiveValueMember> Values { get; }

        public IReadOnlyDictionary<string, ManualPartAction> Actions { get; }

        public static PartShape Of(Type type)
        {
            string? kind = type.GetCustomAttribute<PartKindAttribute>(inherit: true)?.Kind;
            if (kind is null)
            {
                return new PartShape(null, [], new Dictionary<string, ManualPartAction>());
            }

            var values = new List<LiveValueMember>();
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var mark = property.GetCustomAttribute<LiveValueAttribute>(inherit: true);
                if (mark is null)
                {
                    continue;
                }

                var getter = property.GetMethod;
                if (getter is null || !getter.IsPublic || property.GetIndexParameters().Length != 0)
                {
                    LogHelper.Warn(LogModule, $"{type.Name}.{property.Name} 标了 [LiveValue] 但没有可读的公开 get，不推");
                    continue;
                }

                values.Add(new LiveValueMember(property.Name, property, mark.Decimals));
            }

            var actions = new Dictionary<string, ManualPartAction>(StringComparer.OrdinalIgnoreCase);
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                var mark = method.GetCustomAttribute<ManualActionAttribute>(inherit: true);
                if (mark is null)
                {
                    continue;
                }

                if (method.ReturnType != typeof(bool))
                {
                    LogHelper.Warn(LogModule, $"{type.Name}.{method.Name} 标了 [ManualAction] 但不返回 bool（指令发没发出去），不能调");
                    continue;
                }

                if (!actions.TryAdd(method.Name, new ManualPartAction(method, mark)))
                {
                    LogHelper.Warn(LogModule, $"{type.Name}.{method.Name} 有两个标了 [ManualAction] 的同名方法，只认第一个");
                }
            }

            return new PartShape(kind, values, actions);
        }
    }

    /// <summary>一项实时数据：属性名、属性、浮点保留几位。</summary>
    internal sealed record LiveValueMember(string Name, PropertyInfo Property, int Decimals);
}

/// <summary>
/// 一个手动部件：sc.xml 里的组件和它的种类、动作。
/// </summary>
public sealed class ManualPart
{
    internal ManualPart(ComponentBase component, PartCatalog.PartShape shape)
    {
        Component = component;
        Shape = shape;
        Kind = shape.Kind ?? string.Empty;
    }

    /// <summary>sc.xml 里的组件。</summary>
    public ComponentBase Component { get; }

    /// <summary>种类（组件类上的 [PartKind]）。</summary>
    public string Kind { get; }

    internal PartCatalog.PartShape Shape { get; }

    /// <summary>按名字（方法名，忽略大小写）找动作；组件上没有标 [ManualAction] 的同名方法返回 false。</summary>
    public bool TryGetAction(string name, [MaybeNullWhen(false)] out ManualPartAction action)
    {
        return Shape.Actions.TryGetValue(name, out action);
    }
}

/// <summary>
/// 部件的一个手动动作：组件上标了 [ManualAction] 的方法。参数按方法签名从字符串转，调用返回指令发没发出去。
/// </summary>
public sealed class ManualPartAction
{
    private const string LogModule = "Parts";

    private readonly MethodInfo _method;
    private readonly ParameterInfo[] _parameters;

    internal ManualPartAction(MethodInfo method, ManualActionAttribute mark)
    {
        _method = method;
        _parameters = method.GetParameters();
        Name = method.Name;
        IsPriority = mark.Priority;
        Release = string.IsNullOrWhiteSpace(mark.Release) ? null : mark.Release;
    }

    /// <summary>动作名（方法名）。</summary>
    public string Name { get; }

    /// <summary>停止类：模块正忙也照发、不等结果。</summary>
    public bool IsPriority { get; }

    /// <summary>按住类动作松手时发的动作名；不是按住类为 null。</summary>
    public string? Release { get; }

    /// <summary>
    /// 把字符串参数按方法签名转好：个数不能多；少给的用默认值（没默认值算错）；可空参数给空串算 null。
    /// 数字按不变区域性，而且得是有限数。
    /// </summary>
    public bool TryBind(IReadOnlyList<string> args, out object?[] values)
    {
        values = new object?[_parameters.Length];
        if (args.Count > _parameters.Length)
        {
            return false;
        }

        for (int i = 0; i < _parameters.Length; i++)
        {
            var parameter = _parameters[i];
            if (i >= args.Count)
            {
                if (!parameter.HasDefaultValue)
                {
                    return false;
                }

                values[i] = parameter.DefaultValue;
                continue;
            }

            if (!TryConvert(args[i], parameter.ParameterType, out values[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>调动作；返回 true 只表示指令发出去了。组件抛异常记警告、算没发出去。</summary>
    public bool Invoke(ComponentBase component, object?[] values)
    {
        try
        {
            return _method.Invoke(component, values) is true;
        }
        catch (Exception exception)
        {
            LogHelper.Warn(LogModule, $"{component.FullPath}.{Name} 调用异常: {(exception.InnerException ?? exception).Message}");
            return false;
        }
    }

    private static bool TryConvert(string? text, Type type, out object? value)
    {
        value = null;
        text ??= string.Empty;
        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying is not null && text.Length == 0)
        {
            return true;
        }

        var target = underlying ?? type;
        if (target == typeof(string))
        {
            value = text;
            return true;
        }

        if (target == typeof(double))
        {
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) && double.IsFinite(number))
            {
                value = number;
                return true;
            }

            return false;
        }

        if (target == typeof(int))
        {
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int integer))
            {
                value = integer;
                return true;
            }

            return false;
        }

        if (target == typeof(bool))
        {
            if (bool.TryParse(text, out bool flag))
            {
                value = flag;
                return true;
            }

            return false;
        }

        if (target.IsEnum && Enum.TryParse(target, text, true, out object? item) && Enum.IsDefined(target, item!))
        {
            value = item;
            return true;
        }

        return false;
    }
}
