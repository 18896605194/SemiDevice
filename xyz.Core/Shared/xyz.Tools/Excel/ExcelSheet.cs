namespace xyz.Tools;

/// <summary>
/// 一张表：表名 + 一行一行的值（顺序跟列一致，null 留空格）；Rows 写的时候才去枚举，可以边读边给
/// </summary>
public sealed record ExcelSheet(string Name, IEnumerable<object?[]> Rows);
