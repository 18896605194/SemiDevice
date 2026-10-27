using Mapster;
using xyz.Elements.Attributes;
using xyz.Elements.Elements;
using xyz._35021.Module.Cds;
using xyz._35021.Shared.Dtos;

namespace xyz._35021.Module.Mapper;

/// <summary>
/// 35021 的映射元件：平台的规则照用，再加本机型 DTO 的规则（machine.xml 的 Mapper 节点配它）
/// </summary>
[Element(description: "35021 映射元件：平台的规则 + 本机型 DTO 的规则")]
public class MapperElement35021 : MapperElement
{
    protected override void AddRules(TypeAdapterConfig config)
    {
        base.AddRules(config);

        // IPA → 推给界面的 IPA：模块名在界面叫 Module；下面的槽照平台的槽规则转
        config.NewConfig<IpaModule, IpaDto>()
            .Map(dto => dto.Module, ipa => ipa.Name);
    }
}
