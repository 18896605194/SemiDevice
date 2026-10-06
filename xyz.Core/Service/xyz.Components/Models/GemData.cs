namespace xyz.Components.Models;

/// <summary>
/// 报事件时带的一项数据（DV）：名字是这个组件上 [DataVariable] 声明的代码，值是事件发生那一刻的值。
/// 值可以直接给 SecsItem（格式要精确的，比如 E87 的槽图 L[n] U1），别的按值的类型落 SECS 格式。
/// </summary>
public readonly record struct GemData(string Name, object? Value);
