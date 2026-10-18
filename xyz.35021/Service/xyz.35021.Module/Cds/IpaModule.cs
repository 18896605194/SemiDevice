using xyz.Components.Attributes;
using xyz.Components.Interfaces;
using xyz.Modules;
using xyz.Shared.Dtos;
using xyz.Tools;
using xyz._35021.Shared.Dtos;

namespace xyz._35021.Module.Cds;

/// <summary>
/// 35021 IPA 供液模块：默认做法都用平台的（BaseCdsModule），这里只推 IPA 自己的那一份（IpaDto）
/// </summary>
[Component(description: "35021 IPA 供液模块")]
public class IpaModule : BaseCdsModule
{
    private IpaDto? _lastPublished;

    /// <summary>
    /// 映射组件（开机 Bind 时取）：槽转成给界面的样子
    /// </summary>
    private IMapperComponent? _mapper;

    public override void Bind()
    {
        _mapper = IocHelper.GetService<IMapperComponent>();
    }

    /// <summary>
    /// 推 IPA 的状态和下面各槽（液位、状态、信号），变了才推；token 是模块名，留存
    /// </summary>
    protected override void PublishState()
    {
        var mapper = _mapper;
        if (mapper is null)
        {
            return;
        }

        var dto = new IpaDto { Module = Name, State = State };
        foreach (var tank in Tanks)
        {
            dto.Tanks.Add(mapper.Map<TankDto>(tank));
        }

        if (!dto.HasStateChanged(_lastPublished))
        {
            return;
        }

        _lastPublished = dto;
        EventBus.Send(dto, Name);
    }
}
