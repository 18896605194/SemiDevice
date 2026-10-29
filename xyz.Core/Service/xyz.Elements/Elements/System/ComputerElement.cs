using xyz.Elements.Attributes;

namespace xyz.Elements.Elements;

/// <summary>
/// 电脑：下面挂 Program（本程序）、Total（整台电脑）两个子节点，各记 CPU、内存、线程数
/// 根元件，开机由装配层 Start；多久取一次由两个子节点自己管（各有各的 SampleIntervalMs）
/// </summary>
[Element(description: "电脑（下挂本程序、整台电脑的 CPU、内存、线程数）")]
public class ComputerElement : ElementBase
{
}
