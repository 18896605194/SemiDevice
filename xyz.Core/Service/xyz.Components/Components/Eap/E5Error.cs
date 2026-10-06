using xyz.Secs.SecsII;

namespace xyz.Components.Components;

/// <summary>
/// GEM300 报文里的一条错误（SEMI E5 的 ERRCODE + ERRTEXT）：S3F18、S14Fx、S16Fx 的回复都带一张这样的表。
/// ERRTEXT 只能是 ASCII、最长 80 个字符，所以写英文的错误名（中文发不出去）。
/// </summary>
internal sealed record E5Error(ushort Code, string Text)
{
    #region E5 ERRCODE（只列用到的）

    /// <summary>OBJSPEC 里的对象不认识。</summary>
    public const ushort UnknownObjectSpec = 1;

    /// <summary>不认识的目标对象类型。</summary>
    public const ushort UnknownTargetType = 2;

    /// <summary>不认识的对象（实例不存在）。</summary>
    public const ushort UnknownObject = 3;

    /// <summary>不认识的属性名。</summary>
    public const ushort UnknownAttribute = 4;

    /// <summary>属性只读，不能改。</summary>
    public const ushort ReadOnlyAttribute = 5;

    /// <summary>不认识的对象类型。</summary>
    public const ushort UnknownObjectType = 6;

    /// <summary>属性的值不对。</summary>
    public const ushort InvalidAttributeValue = 7;

    /// <summary>对象 ID 已经在用。</summary>
    public const ushort IdentifierInUse = 11;

    /// <summary>参数给得不对。</summary>
    public const ushort ParametersImproperlySpecified = 12;

    /// <summary>参数不够。</summary>
    public const ushort InsufficientParameters = 13;

    /// <summary>不支持这个选项。</summary>
    public const ushort UnsupportedOption = 14;

    /// <summary>忙。</summary>
    public const ushort Busy = 15;

    /// <summary>现在不能处理（设备侧没装、没启用）。</summary>
    public const ushort NotAvailable = 16;

    /// <summary>当前状态下不能执行这个命令。</summary>
    public const ushort InvalidState = 17;

    /// <summary>配方相关的错误。</summary>
    public const ushort RecipeError = 21;

    /// <summary>处理中失败。</summary>
    public const ushort FailedDuringProcessing = 22;

    /// <summary>缺料（载具、片不在）。</summary>
    public const ushort LackOfMaterial = 24;

    #endregion

    /// <summary>ERRTEXT 最长 80 个字符。</summary>
    private const int MaxTextLength = 80;

    public static E5Error Of(ushort code, string text)
    {
        string ascii = GemValue.Ascii(text);
        return new E5Error(code, ascii.Length > MaxTextLength ? ascii[..MaxTextLength] : ascii);
    }

    /// <summary>不是 ONLINE REMOTE：Host 的动作命令不收（E30）。</summary>
    public static E5Error NotRemote()
    {
        return Of(InvalidState, "Equipment is not ONLINE REMOTE");
    }

    /// <summary>一条错误的数据项 L[2]{U2 ERRCODE, A ERRTEXT}。</summary>
    public SecsItem ToItem()
    {
        return SecsItem.L(SecsItem.U2(Code), SecsItem.A(Text));
    }

    /// <summary>错误表 L[n]{L[2]{ERRCODE, ERRTEXT}}，没错就是空表。</summary>
    public static SecsItem List(IEnumerable<E5Error> errors)
    {
        return SecsItem.L(errors.Select(error => error.ToItem()));
    }
}
