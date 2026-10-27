namespace xyz.Tools;

/// <summary>
/// 表里的一列：列名、显示格式（如 "yyyy-MM-dd HH:mm:ss"）、列宽（字符数），后两个不给按默认
/// </summary>
public sealed record ExcelColumn(string Name, string? Format = null, double? Width = null);
