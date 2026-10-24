namespace xyz.Shared.Dtos;

/// <summary>
/// 各模块的状态和生产 / 维护模式（顶栏的模块状态行按它显示），整机模块推。
/// 模块照 machine.xml 装配出来的、先后同 machine.xml，不含整机自己。状态类消息：有一个变了才推整张表，留存。
/// </summary>
public class ModuleStatusListDto
{
    public const string EventToken = "ModuleStatus";

    public List<ModuleStatusDto> Modules { get; set; } = [];
}

/// <summary>
/// 一个模块：名字、哪一类、状态码、生产 / 维护
/// </summary>
public class ModuleStatusDto
{
    /// <summary>
    /// 模块名（machine.xml 原样，如 LoadPort1）
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 哪一类：界面按它把状态码翻成字、定色调（同一个码在 LoadPort、腔体意思不同）
    /// </summary>
    public StationKind Kind { get; set; }

    /// <summary>
    /// 状态码
    /// </summary>
    public int State { get; set; }

    /// <summary>
    /// 生产 / 维护；没有这个模式的模块（供液）是 null
    /// </summary>
    public ModuleMode? Mode { get; set; }
}
