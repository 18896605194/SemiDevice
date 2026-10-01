namespace xyz.Components.Enums;

/// <summary>
/// 界面改一项 EC 的结果（EcComponent.TrySet）。
/// </summary>
public enum EcSetResult
{
    /// <summary>
    /// 改了，已写回 ec.xml。
    /// </summary>
    Ok,

    /// <summary>
    /// 跟现在的值一样，没动。
    /// </summary>
    Unchanged,

    /// <summary>
    /// 没有这一项（组件树上没声明）。
    /// </summary>
    NotFound,

    /// <summary>
    /// 写法不对（整数里有字母、布尔不是 True/False……）。
    /// </summary>
    InvalidFormat,

    /// <summary>
    /// 超出声明的上下限。
    /// </summary>
    OutOfRange,

    /// <summary>
    /// 不在枚举的可选值里。
    /// </summary>
    InvalidOption,

    /// <summary>
    /// ec.xml 写不进去（文件被占着、没权限），值没改。
    /// </summary>
    SaveFailed
}
