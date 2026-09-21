# 轴 PLC 订阅

普通 IO 仍然整块采集、单点写入。轴通过 `SendPlcDataPath`、`ReceivePlcDataPath` 注册结构体通信，
不加入 `Register(path)` 的 IO 轮询块列表。装配层遍历轴组件调用 `Open(IPlc)`；注册不会回零或使能。

## 分工

- `PlcComponent.Subscriptions.cs` 提供输入、输出订阅接口，不保存轴列表，也不调度轴订阅。
- `AxisComponent.Subscriptions.cs` 负责轴自身的订阅：`Open` 建立订阅，`Close` 释放订阅；`EnsureSubscribed` 检查连接并处理重试，`SubscribePlcData` 连接收发回调。没有通用 `Subscription` 管理类。
- `AxisComponent.Motion.cs` 负责运动指令和状态判断，自身 `OnScan` 调用订阅检查并处理动作超时。
- `BeckhoffPlcComponent` 使用 ADS OnChange 通知接收状态，使用 Cyclic 通知每 50ms 检查待发指令，与 g 的驱动方式一致。
- 退出时关闭轴订阅、停止 IO 采集，然后关闭 PLC；关闭后的 PLC 扫描不会重新连接。

轴应放在已经启动扫描的模块组件树下。独立创建轴做测试时，需要显式执行轴扫描才能处理重连和检测动作超时。

## 调用

```csharp
if (!axis.MoveTo(100, 20))
{
    // 数据未就绪、参数非法、轴不允许运动或已有未完成指令。
    return;
}

// MoveTo 返回 true 表示接受请求，不代表 PLC 已执行或已到位。
// 已在目标容差内时直接完成，不重复下发。
// 在模块状态机后续扫描中查询：
if (axis.OperationState == AxisOperationState.Completed)
{
    // 本次动作完成。
}
```

可用操作：`Home`、`MoveTo`、`MoveBy`、`Jog`、`Spin`、`Stop`、`EmergencyStop`、`ResetDrive`、`SetServo`。
`Init()` 显式请求回零；`Abort()` 请求停止；`Reset()` 请求驱动复位并调用基类清警。
模块仍负责工艺互锁和自身故障处理。通信关闭只释放资源，不等于向设备发送停止命令。

`TryGetStatus(out status)` 同时获取整帧反馈及其有效性。单独读取 `Status`、`CurrentPosition` 等数值时，
必须判断 `HasPlcData`；断线时旧数值仅作保留显示。

## 发送与重连

输出订阅每次连接先读取 PLC 当前指令作为基线，之后只发送轴提供的待发指令（返回 null 表示无待发数据）。
写失败保留请求，下次周期通知重试，写成功才通知轴。普通命令不覆盖在途动作；停止和急停可替换待发命令。
已经进入设备写调用的命令无法撤回，停止在随后周期通知中发送。

连接代次变化立即使轴数据无效。重连重新取基线、重建输入订阅，丢弃旧待发运动和旧连接通知。
装配层在启动轴时检查重复的输出路径，避免两个轴竞争同一个指令区。

## 协议和完成判断

沿用 g 项目的协议：`Pack=8`，命令区 96 字节，状态区 80 字节，命令同步号偏移 88。
命令码和字段定义在 `xyz.Components/Motion`。部署时需与现场 PLC 的类型、布局和变量路径核对。

该协议的状态区没有命令同步号回执，因此 ADS 写成功不能证明 PLC 已执行。
回零和定位必须在发送后观察到运动过程，再收到符合目标的完成状态；不会单凭原有 Homed/InPosition 判完成。
非常短的运动若未在 50ms 通知中观察到过程，会保守等待至超时。
停止/复位也需后续状态通知确认；若 PLC 状态完全没有变化、未产生通知，可能保守超时。
若需要严格确认每条命令（包括无状态变化的命令），需 PLC 回传已接收/已完成同步号，再扩展状态协议。

## 离线验证

`dotnet run --project tools/IoIndexSmoke/IoIndexSmoke.csproj --no-restore`

覆盖协议布局、首次取基线、指令写入、失败重试、停止替换、旧到位状态、重连不重发、旧通知丢弃及资源释放。
测试使用模拟传输，不连接真实 PLC，也不验证现场 ADS 路由和 PLC 实际执行逻辑。
