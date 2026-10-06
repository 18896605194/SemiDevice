using xyz.Secs.SecsII;

namespace xyz.Components.Components;

/// <summary>
/// E39 的一种对象（OBJTYPE）：各标准组件把自己管的对象登记给 E39——E87 的载具（Carrier）和端口（Port）、
/// E90 的片（Substrate）和片位（SubstLoc）、E40 的 PJ（ProcessJob）、E94 的 CJ（ControlJob）。
/// Host 用 S14 按类型查属性、改属性、建对象、删对象，E39 统一解析报文、拼回复，具体的值由这里给。
/// 都在链路的派发线程上调。
/// </summary>
internal interface IE39ObjectType
{
    /// <summary>对象类型名（OBJTYPE），比较时不分大小写。</summary>
    string TypeName { get; }

    /// <summary>
    /// 属性名，按 S14F8 报的先后：第一个是 1 号，Host 也可以用号指属性（S14F1 / S14F3 的 ATTRID 发整数）。
    /// </summary>
    IReadOnlyList<string> AttributeNames { get; }

    /// <summary>现有的对象 ID。</summary>
    IReadOnlyList<string> ObjectIds();

    /// <summary>取一个对象的一个属性（属性名已经按 AttributeNames 规整过）；对象不存在返回 false。</summary>
    bool TryGetAttribute(string objectId, string attribute, out SecsItem value);

    /// <summary>S14F3 改一个属性；默认都只读。返回 null 是改成了。</summary>
    E5Error? SetAttribute(string objectId, string attribute, SecsItem value)
    {
        return E5Error.Of(E5Error.ReadOnlyAttribute, $"{attribute} is read-only");
    }

    /// <summary>S14F9 建对象；默认不支持。</summary>
    Task<E39Created> CreateAsync(string objectSpec, IReadOnlyList<(string Name, SecsItem Value)> attributes)
    {
        return Task.FromResult(E39Created.Fail(E5Error.Of(E5Error.UnsupportedOption, $"Create {TypeName} not supported")));
    }

    /// <summary>S14F11 删对象；默认不支持。返回 null 是删成了。</summary>
    Task<E5Error?> DeleteAsync(string objectId)
    {
        return Task.FromResult<E5Error?>(E5Error.Of(E5Error.UnsupportedOption, $"Delete {TypeName} not supported"));
    }
}

/// <summary>
/// S14F9 建对象的结果：建成了带对象 ID；没建成带错误。
/// </summary>
internal sealed record E39Created(string ObjectId, IReadOnlyList<E5Error> Errors)
{
    public bool IsSuccess => Errors.Count == 0;

    public static E39Created Ok(string objectId)
    {
        return new E39Created(objectId, []);
    }

    public static E39Created Fail(params E5Error[] errors)
    {
        return new E39Created(string.Empty, errors);
    }
}
