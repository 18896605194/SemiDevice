using System.Text.RegularExpressions;
using xyz.Common.Log;
using xyz.Components.Attributes;
using xyz.Components.Models;
using xyz.Secs;
using xyz.Secs.Hsms;
using xyz.Secs.SecsII;

namespace xyz.Components.Components;

/// <summary>
/// 对象服务（SEMI E39，sc.xml 的 Eap 下的 E39 节点）：Host 用 S14 按对象类型查属性（S14F1）、改属性（S14F3）、
/// 问有哪些类型（S14F5）、问属性名（S14F7）、建对象（S14F9）、删对象（S14F11）。
/// 自己不管任何对象：E87、E90、E40、E94 开机时把自己的对象类型登记进来，这里只解析报文、过滤、拼回复。
/// 查在 ON-LINE 就行；改、建、删是动作命令，要 ON-LINE REMOTE。
/// </summary>
[Component(description: "对象服务（SEMI E39）：Host 用 S14 查、改、建、删各标准登记的对象")]
public class E39Component : ComponentBase
{
    /// <summary>OBJACK：0 没错、1 有错。</summary>
    private const byte ObjectAccepted = 0;

    private const byte ObjectError = 1;

    /// <summary>ATTRRELN（E39）：0 等于、1 不等于、2 小于、3 小于等于、4 大于、5 大于等于、6 有值、7 没值、8 列表里有、9 列表里没有。</summary>
    private const byte RelationEqual = 0;

    private const byte RelationNotEqual = 1;

    private const byte RelationLess = 2;

    private const byte RelationLessOrEqual = 3;

    private const byte RelationGreater = 4;

    private const byte RelationGreaterOrEqual = 5;

    private const byte RelationPresent = 6;

    private const byte RelationAbsent = 7;

    private const byte RelationContained = 8;

    private const byte RelationNotContained = 9;

    private readonly object _gate = new();
    private readonly List<IE39ObjectType> _types = [];
    private E30Component? _gem;

    #region 登记

    /// <summary>接到链路上：登记 S14 的处理方（EAP 组件在链路打开之前调）。</summary>
    public void Attach(HsmsComponent link, E30Component gem)
    {
        ArgumentNullException.ThrowIfNull(link);
        ArgumentNullException.ThrowIfNull(gem);
        _gem = gem;
        link.Handle(14, 1, GetAttributes);
        link.Handle(14, 3, SetAttributes);
        link.Handle(14, 5, GetTypes);
        link.Handle(14, 7, GetAttributeNames);
        link.Handle(14, 9, CreateObjectAsync);
        link.Handle(14, 11, DeleteObjectAsync);
    }

    /// <summary>登记一种对象（各标准组件接链路时调）；同名类型登记两次是配置错了，开机就抛。</summary>
    internal void Register(IE39ObjectType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        lock (_gate)
        {
            if (_types.Any(existing => string.Equals(existing.TypeName, type.TypeName, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"E39 对象类型 {type.TypeName} 登记了两次");
            }

            _types.Add(type);
        }
    }

    private IE39ObjectType? Find(string typeName)
    {
        lock (_gate)
        {
            return _types.FirstOrDefault(type => string.Equals(type.TypeName, typeName.Trim(), StringComparison.OrdinalIgnoreCase));
        }
    }

    private IReadOnlyList<IE39ObjectType> Types
    {
        get
        {
            lock (_gate)
            {
                return _types.ToList();
            }
        }
    }

    #endregion

    #region S14F1 查属性

    /// <summary>
    /// S14F1 查属性 → S14F2：L[5]{OBJSPEC, OBJTYPE, L{OBJID}, L{L[3]{ATTRID, ATTRDATA, ATTRRELN}}, L{ATTRID}}。
    /// 对象表空 = 这一类全部（再按条件过滤，条件之间是"并且"）；属性表空 = 全部属性。不认识的对象、属性记进错误表，认识的照报。
    /// </summary>
    private SecsReply GetAttributes(HsmsMessage message)
    {
        var body = SecsRead.List(SecsRead.Body(message), "S14F1", 5);
        var errors = new List<E5Error>();
        var type = ResolveType(body[0], body[1], errors);
        if (type is null)
        {
            return SecsReply.Of(SecsItem.L(SecsItem.L(), Ack(errors)));
        }

        var requested = SecsRead.List(body[2], "OBJID 表").Select(item => SecsRead.Text(item, "OBJID").Trim()).ToList();
        var qualifiers = SecsRead.List(body[3], "条件表").Select(item => ReadQualifier(type, item, errors)).ToList();
        var attributes = SecsRead.List(body[4], "ATTRID 表").Select(item => ResolveAttribute(type, item, errors))
            .Where(name => name is not null).Cast<string>().ToList();
        if (attributes.Count == 0 && body[4].Count == 0)
        {
            attributes = type.AttributeNames.ToList();
        }

        var existing = type.ObjectIds();
        var objects = new List<SecsItem>();
        var candidates = requested.Count == 0 ? existing : requested;
        foreach (string id in candidates)
        {
            string? objectId = existing.FirstOrDefault(item => string.Equals(item, id, StringComparison.OrdinalIgnoreCase));
            if (objectId is null)
            {
                errors.Add(E5Error.Of(E5Error.UnknownObject, $"{type.TypeName} {id} not found"));
                continue;
            }

            if (!qualifiers.All(qualifier => qualifier is null || Matches(type, objectId, qualifier)))
            {
                continue;
            }

            var values = new List<SecsItem>();
            foreach (string attribute in attributes)
            {
                if (type.TryGetAttribute(objectId, attribute, out var value))
                {
                    values.Add(SecsItem.L(SecsItem.A(attribute), value));
                }
            }

            objects.Add(SecsItem.L(SecsItem.A(objectId), SecsItem.L(values)));
        }

        return SecsReply.Of(SecsItem.L(SecsItem.L(objects), Ack(errors)));
    }

    /// <summary>一个过滤条件：属性名、拿来比的值、比法。</summary>
    private sealed record Qualifier(string Attribute, SecsItem Data, byte Relation);

    private static Qualifier? ReadQualifier(IE39ObjectType type, SecsItem item, List<E5Error> errors)
    {
        var parts = SecsRead.List(item, "条件");
        if (parts.Count is < 2 or > 3)
        {
            throw new SecsException($"条件应是 L[2] 或 L[3]，收到 {parts.Count} 项");
        }

        string? attribute = ResolveAttribute(type, parts[0], errors);
        byte relation = parts.Count == 3 && parts[2].Count > 0 ? SecsRead.Code(parts[2], "ATTRRELN") : RelationEqual;
        if (relation > RelationNotContained)
        {
            errors.Add(E5Error.Of(E5Error.ParametersImproperlySpecified, $"ATTRRELN {relation} invalid"));
            return null;
        }

        return attribute is null ? null : new Qualifier(attribute, parts[1], relation);
    }

    /// <summary>条件成立：(条件里的值) 比法 (对象的属性值)，照 E39 的写法——"SubstrateCount 大于 3"发的是 3 小于属性值。</summary>
    private static bool Matches(IE39ObjectType type, string objectId, Qualifier qualifier)
    {
        if (!type.TryGetAttribute(objectId, qualifier.Attribute, out var value))
        {
            return false;
        }

        switch (qualifier.Relation)
        {
            case RelationEqual:
                return AreEqual(qualifier.Data, value);

            case RelationNotEqual:
                return !AreEqual(qualifier.Data, value);

            case RelationPresent:
                return value.Count > 0;

            case RelationAbsent:
                return value.Count == 0;

            case RelationContained:
                return value.Format == SecsFormat.List && value.Items.Any(element => AreEqual(qualifier.Data, element));

            case RelationNotContained:
                return value.Format != SecsFormat.List || !value.Items.Any(element => AreEqual(qualifier.Data, element));

            default:
                int? order = Compare(qualifier.Data, value);
                return order is not null && qualifier.Relation switch
                {
                    RelationLess => order.Value < 0,
                    RelationLessOrEqual => order.Value <= 0,
                    RelationGreater => order.Value > 0,
                    _ => order.Value >= 0,
                };
        }
    }

    /// <summary>
    /// 相等：文字不分大小写、支持 * 和 ? 通配；数字按数值比（格式宽度不同也算）；别的按格式和内容逐字节比。
    /// </summary>
    private static bool AreEqual(SecsItem data, SecsItem value)
    {
        if (IsText(data) && IsText(value))
        {
            string pattern = "^" + Regex.Escape(data.GetString()).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            return Regex.IsMatch(value.GetString(), pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        if (IsNumber(data) && IsNumber(value))
        {
            var left = Numbers(data);
            var right = Numbers(value);
            return left.Length == right.Length && left.Zip(right).All(pair => pair.First == pair.Second);
        }

        return data.Equals(value);
    }

    /// <summary>大小：文字按字母（不分大小写）、数字按第一个数；比不了（格式对不上）返回 null。</summary>
    private static int? Compare(SecsItem data, SecsItem value)
    {
        if (IsText(data) && IsText(value))
        {
            return string.Compare(data.GetString(), value.GetString(), StringComparison.OrdinalIgnoreCase);
        }

        if (IsNumber(data) && IsNumber(value))
        {
            var left = Numbers(data);
            var right = Numbers(value);
            if (left.Length == 0)
            {
                return null;
            }

            return right.Length == 0 ? 1 : left[0].CompareTo(right[0]);
        }

        return null;
    }

    private static bool IsText(SecsItem item)
    {
        return item.Format is SecsFormat.Ascii or SecsFormat.Jis8;
    }

    private static bool IsNumber(SecsItem item)
    {
        return item.Format is SecsFormat.I1 or SecsFormat.I2 or SecsFormat.I4 or SecsFormat.I8
            or SecsFormat.U1 or SecsFormat.U2 or SecsFormat.U4 or SecsFormat.U8 or SecsFormat.F4 or SecsFormat.F8;
    }

    private static double[] Numbers(SecsItem item)
    {
        return item.Format switch
        {
            SecsFormat.F4 or SecsFormat.F8 => item.GetDoubleArray(),
            SecsFormat.U1 or SecsFormat.U2 or SecsFormat.U4 or SecsFormat.U8 => item.GetUInt64Array().Select(number => (double)number).ToArray(),
            _ => item.GetInt64Array().Select(number => (double)number).ToArray(),
        };
    }

    #endregion

    #region S14F3 改属性

    /// <summary>
    /// S14F3 改属性 → S14F4：L[4]{OBJSPEC, OBJTYPE, L{OBJID}, L{L[2]{ATTRID, ATTRDATA}}}，对象表空 = 这一类全部。
    /// 回每个对象改完后的值；对象不存在的回空属性表并记错误。要 ON-LINE REMOTE。
    /// </summary>
    private SecsReply SetAttributes(HsmsMessage message)
    {
        var body = SecsRead.List(SecsRead.Body(message), "S14F3", 4);
        var errors = new List<E5Error>();
        if (_gem is null || !_gem.IsRemote)
        {
            errors.Add(E5Error.NotRemote());
            return SecsReply.Of(SecsItem.L(SecsItem.L(), Ack(errors)));
        }

        var type = ResolveType(body[0], body[1], errors);
        if (type is null)
        {
            return SecsReply.Of(SecsItem.L(SecsItem.L(), Ack(errors)));
        }

        var requested = SecsRead.List(body[2], "OBJID 表").Select(item => SecsRead.Text(item, "OBJID").Trim()).ToList();
        var changes = SecsRead.List(body[3], "属性表").Select(item =>
        {
            var pair = SecsRead.List(item, "L{ATTRID, ATTRDATA}", 2);
            return (Name: ResolveAttribute(type, pair[0], errors), Value: pair[1]);
        }).ToList();

        var existing = type.ObjectIds();
        var objects = new List<SecsItem>();
        foreach (string id in requested.Count == 0 ? existing : requested)
        {
            string? objectId = existing.FirstOrDefault(item => string.Equals(item, id, StringComparison.OrdinalIgnoreCase));
            if (objectId is null)
            {
                errors.Add(E5Error.Of(E5Error.UnknownObject, $"{type.TypeName} {id} not found"));
                objects.Add(SecsItem.L(SecsItem.A(id), SecsItem.L()));
                continue;
            }

            var values = new List<SecsItem>();
            foreach (var (name, value) in changes)
            {
                if (name is null)
                {
                    continue;
                }

                var error = type.SetAttribute(objectId, name, value);
                if (error is not null)
                {
                    errors.Add(error);
                }

                if (type.TryGetAttribute(objectId, name, out var current))
                {
                    values.Add(SecsItem.L(SecsItem.A(name), current));
                }
            }

            objects.Add(SecsItem.L(SecsItem.A(objectId), SecsItem.L(values)));
        }

        return SecsReply.Of(SecsItem.L(SecsItem.L(objects), Ack(errors)));
    }

    #endregion

    #region S14F5 / S14F7 类型和属性名

    /// <summary>S14F5 有哪些对象类型 → S14F6 L[2]{L{OBJTYPE}, L[2]{OBJACK, 错误表}}。</summary>
    private SecsReply GetTypes(HsmsMessage message)
    {
        var errors = new List<E5Error>();
        var spec = message.Body;
        if (spec is not null && spec.Count > 0 && !IsEquipmentSpec(SecsRead.Text(spec, "OBJSPEC")))
        {
            errors.Add(E5Error.Of(E5Error.UnknownObjectSpec, $"OBJSPEC {SecsRead.Text(spec, "OBJSPEC")} unknown"));
            return SecsReply.Of(SecsItem.L(SecsItem.L(), Ack(errors)));
        }

        return SecsReply.Of(SecsItem.L(SecsItem.L(Types.Select(type => SecsItem.A(type.TypeName))), Ack(errors)));
    }

    /// <summary>S14F7 某些类型的属性名 → S14F8：L[2]{OBJSPEC, L{OBJTYPE}}，类型表空 = 全部类型。</summary>
    private SecsReply GetAttributeNames(HsmsMessage message)
    {
        var body = SecsRead.List(SecsRead.Body(message), "S14F7", 2);
        var errors = new List<E5Error>();
        string spec = SecsRead.Text(body[0], "OBJSPEC");
        if (!IsEquipmentSpec(spec))
        {
            errors.Add(E5Error.Of(E5Error.UnknownObjectSpec, $"OBJSPEC {spec} unknown"));
            return SecsReply.Of(SecsItem.L(SecsItem.L(), Ack(errors)));
        }

        var names = SecsRead.List(body[1], "OBJTYPE 表").Select(item => SecsRead.Text(item, "OBJTYPE")).ToList();
        var types = new List<IE39ObjectType>();
        if (names.Count == 0)
        {
            types.AddRange(Types);
        }

        foreach (string name in names)
        {
            var type = Find(name);
            if (type is null)
            {
                errors.Add(E5Error.Of(E5Error.UnknownObjectType, $"OBJTYPE {name} unknown"));
                continue;
            }

            types.Add(type);
        }

        return SecsReply.Of(SecsItem.L(
            SecsItem.L(types.Select(type => SecsItem.L(SecsItem.A(type.TypeName), SecsItem.L(type.AttributeNames.Select(SecsItem.A))))),
            Ack(errors)));
    }

    #endregion

    #region S14F9 / S14F11 建、删

    /// <summary>
    /// S14F9 建对象 → S14F10 L[3]{OBJSPEC（建成的对象 ID，没建成为空）, L{L[2]{ATTRID, ATTRDATA}}（建成的属性）, L[2]{OBJACK, 错误表}}。
    /// 交给登记了这一类的标准组件建（CJ 归 E94）。要 ON-LINE REMOTE。
    /// </summary>
    private async Task<SecsReply> CreateObjectAsync(HsmsMessage message)
    {
        var body = SecsRead.List(SecsRead.Body(message), "S14F9", 3);
        string spec = SecsRead.Text(body[0], "OBJSPEC");
        string typeName = SecsRead.Text(body[1], "OBJTYPE");
        var attributes = SecsRead.List(body[2], "属性表").Select(item =>
        {
            var pair = SecsRead.List(item, "L{ATTRID, ATTRDATA}", 2);
            return (Name: SecsRead.Text(pair[0], "ATTRID"), Value: pair[1]);
        }).ToList();

        if (_gem is null || !_gem.IsRemote)
        {
            return SecsReply.Of(CreateReply(string.Empty, [], [E5Error.NotRemote()]));
        }

        var type = Find(typeName);
        if (type is null)
        {
            return SecsReply.Of(CreateReply(string.Empty, [], [E5Error.Of(E5Error.UnknownObjectType, $"OBJTYPE {typeName} unknown")]));
        }

        var created = await type.CreateAsync(spec, attributes).ConfigureAwait(false);
        if (!created.IsSuccess)
        {
            return SecsReply.Of(CreateReply(string.Empty, [], created.Errors));
        }

        var values = new List<SecsItem>();
        foreach (var (name, _) in attributes)
        {
            string? attribute = type.AttributeNames.FirstOrDefault(item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase));
            if (attribute is not null && type.TryGetAttribute(created.ObjectId, attribute, out var value))
            {
                values.Add(SecsItem.L(SecsItem.A(attribute), value));
            }
        }

        LogHelper.Info(Name, $"Host 建了 {type.TypeName} {created.ObjectId}");
        return SecsReply.Of(CreateReply(created.ObjectId, values, []));
    }

    private static SecsItem CreateReply(string objectId, IReadOnlyList<SecsItem> attributes, IReadOnlyList<E5Error> errors)
    {
        return SecsItem.L(SecsItem.A(objectId), SecsItem.L(attributes), Ack(errors));
    }

    /// <summary>
    /// S14F11 删对象 → S14F12 L[2]{L{L[2]{ATTRID, ATTRDATA}}, L[2]{OBJACK, 错误表}}：L[2]{"OBJTYPE:OBJID", L{属性}}。
    /// 交给登记了这一类的组件删。要 ON-LINE REMOTE。
    /// </summary>
    private async Task<SecsReply> DeleteObjectAsync(HsmsMessage message)
    {
        var body = SecsRead.List(SecsRead.Body(message), "S14F11", 2);
        string spec = SecsRead.Text(body[0], "OBJSPEC");
        if (_gem is null || !_gem.IsRemote)
        {
            return SecsReply.Of(SecsItem.L(SecsItem.L(), Ack([E5Error.NotRemote()])));
        }

        int colon = spec.IndexOf(':');
        var type = colon > 0 ? Find(spec[..colon]) : null;
        if (type is null)
        {
            return SecsReply.Of(SecsItem.L(SecsItem.L(), Ack([E5Error.Of(E5Error.UnknownObjectSpec, $"OBJSPEC {spec} unknown")])));
        }

        string objectId = spec[(colon + 1)..].Trim();
        var error = await type.DeleteAsync(objectId).ConfigureAwait(false);
        return SecsReply.Of(SecsItem.L(SecsItem.L(), Ack(error is null ? [] : [error])));
    }

    #endregion

    #region 公用

    /// <summary>OBJSPEC 指的是设备本身：本机的对象都在顶层，OBJSPEC 给空的（也认 "Equipment"）。</summary>
    private static bool IsEquipmentSpec(string spec)
    {
        return spec.Trim().Length == 0 || string.Equals(spec.Trim(), "Equipment", StringComparison.OrdinalIgnoreCase);
    }

    private IE39ObjectType? ResolveType(SecsItem specItem, SecsItem typeItem, List<E5Error> errors)
    {
        string spec = SecsRead.Text(specItem, "OBJSPEC");
        if (!IsEquipmentSpec(spec))
        {
            errors.Add(E5Error.Of(E5Error.UnknownObjectSpec, $"OBJSPEC {spec} unknown"));
            return null;
        }

        string typeName = SecsRead.Text(typeItem, "OBJTYPE");
        var type = Find(typeName);
        if (type is null)
        {
            errors.Add(E5Error.Of(E5Error.UnknownTargetType, $"OBJTYPE {typeName} unknown"));
        }

        return type;
    }

    /// <summary>ATTRID：文字给的按名字（不分大小写）找；整数给的按 S14F8 的先后（1 号起）找。不认识的记错误、返回 null。</summary>
    private static string? ResolveAttribute(IE39ObjectType type, SecsItem item, List<E5Error> errors)
    {
        if (item.Format is SecsFormat.Ascii or SecsFormat.Jis8)
        {
            string name = item.GetString().Trim();
            string? attribute = type.AttributeNames.FirstOrDefault(candidate => string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase));
            if (attribute is null)
            {
                errors.Add(E5Error.Of(E5Error.UnknownAttribute, $"{type.TypeName} has no attribute {name}"));
            }

            return attribute;
        }

        uint number = SecsRead.Id(item, "ATTRID");
        if (number >= 1 && number <= type.AttributeNames.Count)
        {
            return type.AttributeNames[(int)number - 1];
        }

        errors.Add(E5Error.Of(E5Error.UnknownAttribute, $"{type.TypeName} has no attribute #{number}"));
        return null;
    }

    /// <summary>L[2]{OBJACK, 错误表}。</summary>
    private static SecsItem Ack(IReadOnlyList<E5Error> errors)
    {
        return SecsItem.L(SecsItem.U1(errors.Count == 0 ? ObjectAccepted : ObjectError), E5Error.List(errors));
    }

    #endregion
}
