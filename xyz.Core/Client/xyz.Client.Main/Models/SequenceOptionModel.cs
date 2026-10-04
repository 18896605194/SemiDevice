using System.Globalization;
using xyz.Shared.Dtos;

namespace xyz.Client.Main.Models;

/// <summary>
/// 选 Sequence（流程配方）弹窗里的一行：编号、名称。选中后带回的是名称——流程配方按名字引用。
/// </summary>
public sealed class SequenceOptionModel
{
    /// <summary>
    /// 编号至少显示两位（01）；个数上百、上千时跟着变宽。
    /// </summary>
    private const int MinIndexDigits = 2;

    private SequenceOptionModel(string no, string name)
    {
        No = no;
        Name = name;
    }

    public string No { get; }

    public string Name { get; }

    /// <summary>
    /// 按后端的流程配方列表建一组，编号位数跟着个数走。
    /// </summary>
    public static List<SequenceOptionModel> From(SequenceListDto list)
    {
        int digits = Math.Max(MinIndexDigits, list.Capacity.ToString(CultureInfo.InvariantCulture).Length);
        string indexFormat = "D" + digits.ToString(CultureInfo.InvariantCulture);
        return list.Items
            .Select(item => new SequenceOptionModel(item.Index.ToString(indexFormat, CultureInfo.InvariantCulture), item.Name))
            .ToList();
    }
}
