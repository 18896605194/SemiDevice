using xyz.Elements.Interfaces;
using xyz.Elements.Attributes;

namespace xyz.Modules;

/// <summary>
/// buffer 一段自己的能力（在位传感器、门这些以后加在这里）；跟机械手交互走站点基类的 ITransferStation
/// </summary>
[Ioc]
public interface IBuffer : IModule
{
}
