namespace xyz.Modules;

/// <summary>
/// 配方用到某个腔体上对不上的地方：哪个字段（按界面语言的列名）、选的什么值——这个腔体没有。
/// </summary>
public sealed record ProcessRecipeMismatch(string Field, string Value);
