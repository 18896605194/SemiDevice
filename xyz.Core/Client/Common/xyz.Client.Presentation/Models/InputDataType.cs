namespace xyz.Client.Presentation.Models;

/// <summary>
/// 通用输入框（InputTextBox）的数据类型：决定什么样的输入算合法。
/// </summary>
public enum InputDataType
{
    /// <summary>文本：不校验，也没有上下限的概念。</summary>
    Text,

    /// <summary>整数：只认正负号和数字，查上下限。</summary>
    Integer,

    /// <summary>小数：只认正负号、数字和一个小数点（按点号，不认千分位和科学计数），查上下限。</summary>
    Decimal,
}
