using xyz.Elements.Interfaces;
using xyz.Shared.Dtos;

namespace xyz.Elements.Models;

/// <summary>
/// 设备步骤里的一条：给哪个设备、什么动作（日志、错误码用）、怎么发（带参数的移动、转速、流量自己给）
/// </summary>
public sealed record DeviceOrder(IDevice Device, ChamberDeviceAction Action, Func<bool> Send);
