using System.Globalization;
using xyz.Common.Log;
using xyz.Elements.Collectors;
using xyz.Elements.Interfaces;
using xyz.Elements.Models;
using xyz.Secs.Eda;
using xyz.Secs.Eda.E164;
using xyz.Secs.Eda.Model;
using xyz.Tools;

namespace xyz.Elements.Elements;

/// <summary>
/// EDA 的设备这一侧：开机照元件树和 GEM 编号表建设备自描述（不另配），参数现在的值读元件的 SV / EC，SEMI 对象问 E39；
/// 元件报的事件翻成 EDA 事件（元件自己节点上的，加上 E164 的标准事件）也在这里
/// </summary>
internal sealed class EdaEquipment : IEdaEquipment
{
    private readonly Dictionary<string, Func<EdaValue>> _readers;
    private readonly Dictionary<string, string> _locators;
    private readonly IE39Element? _e39;

    /// <summary>
    /// 照元件树和 GEM 编号表建自描述；EAP 链路启用时带上 E164 的 GEM300 部分
    /// </summary>
    public EdaEquipment(string equipmentName, ILogSource owner)
    {
        var roots = IocHelper.GetService<IReadOnlyList<ElementBase>>() ?? Array.Empty<ElementBase>();
        var collectors = IocHelper.GetService<GemCollectors>();
        var sources = new EdaE164Sources();
        var link = IocHelper.GetService<IHsms>();
        if (link is not null && link.IsEnable)
        {
            sources = new EdaE164Sources
            {
                E30 = IocHelper.GetService<IE30Element>() as E30Element,
                Recipe = IocHelper.GetService<IE30RecipeElement>() as E30RecipeElement,
                E40 = IocHelper.GetService<IE40Element>() as E40Element,
                E94 = IocHelper.GetService<IE94Element>() as E94Element,
                E87 = IocHelper.GetService<IE87Element>() as E87Element,
                E90 = IocHelper.GetService<IE90Element>() as E90Element,
            };
        }

        _e39 = IocHelper.GetService<IE39Element>();
        var builder = new EdaMetadataBuilder(equipmentName, roots, collectors, sources, owner);
        Metadata = builder.Build();
        _readers = builder.Readers;
        _locators = builder.Locators;
    }

    public EdaMetadata Metadata { get; }

    public EdaValue? Read(string locator, string parameterName)
    {
        if (!_readers.TryGetValue(EdaMetadataBuilder.ReaderKey(locator, parameterName), out var reader))
        {
            return null;
        }

        return reader();
    }

    public bool CanRead(string locator, string parameterName)
    {
        return _readers.ContainsKey(EdaMetadataBuilder.ReaderKey(locator, parameterName));
    }

    public IReadOnlyList<string> InstanceIds(string objType)
    {
        var type = _e39?.Engine?.FindType(objType);
        if (type is null)
        {
            return Array.Empty<string>();
        }

        return type.ObjectIds();
    }

    /// <summary>
    /// 属性名不分大小写对上 E39 登记的名字；E39 没接、对象没了返回 null
    /// </summary>
    public EdaValue? Attribute(string objType, string objectId, string attributeName)
    {
        var type = _e39?.Engine?.FindType(objType);
        if (type is null)
        {
            return null;
        }

        string? attribute = type.AttributeNames.FirstOrDefault(item => string.Equals(item, attributeName, StringComparison.OrdinalIgnoreCase));
        if (attribute is null || !type.TryGetAttribute(objectId, attribute, out var value))
        {
            return null;
        }

        return EdaValue.From(value);
    }

    /// <summary>
    /// 元件全路径对应的节点路径；元件不在树上返回 null
    /// </summary>
    public string? LocatorOf(string elementPath)
    {
        return _locators.GetValueOrDefault(elementPath);
    }

    /// <summary>
    /// 元件报的事件对出的 EDA 事件：元件自己节点上的，加上 E164 的标准事件（E30 控制状态、配方变了、EC 被改了、GEM300 对象转换）
    /// </summary>
    public List<EdaEventContext> Resolve(ElementBase source, string code, GemData[] data)
    {
        var result = new List<EdaEventContext>();
        if (_locators.TryGetValue(source.FullPath, out string? locator))
        {
            result.Add(new EdaEventContext(locator, code, ValuesOf(data), null));
        }

        string equipment = Metadata.Equipment.Locator;
        if (source is E30Element e30)
        {
            foreach (var control in E164Events.ControlEvents)
            {
                if (control.Code == code)
                {
                    result.Add(E164Events.Control(Metadata, control.EventId, control.Target, e30.ControlState, e30.PreviousControlState));
                }
            }

            if (code == E164Events.OperatorEquipmentConstantChange)
            {
                result.Add(new EdaEventContext(equipment, E164Events.OperatorEquipmentConstantChange, new Dictionary<string, EdaValue>(), null));
            }

            return result;
        }

        if (source is E30RecipeElement && code == E164Events.ProcessProgramChange)
        {
            result.Add(new EdaEventContext(equipment, E164Events.ProcessProgramChange, ValuesOf(data), null));
            return result;
        }

        var objectSource = Match(source, code, out int number);
        if (objectSource is null)
        {
            return result;
        }

        var transition = E164Events.Transition(this, objectSource, number, ValuesOf(data));
        if (transition is not null)
        {
            result.Add(transition);
        }

        return result;
    }

    /// <summary>
    /// 元件报事件时带的 DV 按代码排成值表
    /// </summary>
    private static Dictionary<string, EdaValue> ValuesOf(GemData[] data)
    {
        var values = new Dictionary<string, EdaValue>(StringComparer.Ordinal);
        foreach (var item in data)
        {
            values[item.Name] = EdaValue.From(item.Value);
        }

        return values;
    }

    /// <summary>
    /// 这个元件事件是哪种 GEM300 对象的第几号转换（我们 EAP 元件报的转换事件码 = 前缀 + 转换号）；不是转换事件返回 null
    /// </summary>
    private static E164Object? Match(ElementBase source, string code, out int number)
    {
        number = 0;
        var candidates = new List<E164Object>();
        if (source is E40Element)
        {
            candidates.Add(E164Object.ProcessJob);
        }

        if (source is E94Element)
        {
            candidates.Add(E164Object.ControlJob);
        }

        if (source is E87Element)
        {
            candidates.Add(E164Object.Carrier);
        }

        if (source is E90Element)
        {
            candidates.Add(E164Object.Substrate);
            candidates.Add(E164Object.SubstrateLocation);
        }

        foreach (var candidate in candidates)
        {
            if (!code.StartsWith(candidate.CodePrefix, StringComparison.Ordinal))
            {
                continue;
            }

            if (int.TryParse(code[candidate.CodePrefix.Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
            {
                return candidate;
            }
        }

        return null;
    }
}
