# 轴与 PLC 数据块

普通 IO 整块采集、单点写入。轴按 `SendPlcDataPath`（命令块）、`ReceivePlcDataPath`（状态块）两个数据块名绑 PLC，
两块都登记进 PLC 组件的 50ms 整块缓存：发令是同步写命令块，状态每拍从缓存取。
装配层遍历轴组件调用 `Open(IPlc)`；登记不会回零或使能。

## 分工

- `PlcComponent`：登记的块每拍整块读回缓存（`Register` / `TryReadBlock`），写直接下发（`WriteBlock`）；
  连接换代时清缓存，断线期间 `TryReadBlock` 一律 false。
- `AxisComponent.cs`：SC/EC/SV/报警声明和状态属性。
- `AxisComponent.Motion.cs`：`Open` 登记两块；`Send` 校验 → 组包 → 同步写；`OnScan` 读状态、判完成、判超时。

轴放在已经启动扫描的模块组件树下。独立创建轴做测试时，要显式调用轴扫描才会读状态和判完成。

## 调用

```csharp
if (!axis.MoveTo(100, 20))
{
    // 数据未就绪、参数非法、轴不允许运动、上一条还没完成，或写 PLC 失败。
    return;
}

// 返回 true = 指令已写进 PLC，不代表已到位。已在目标容差内时直接完成，不重复下发。
// 在模块状态机后续扫描里查询：
if (axis.ActionState == ActionState.Completed)
{
    // 本次动作完成。
}
```

可用操作：`Home`、`MoveTo`、`MoveBy`、`Jog`、`Spin`、`Stop`、`EmergencyStop`、`ResetDrive`、`SetServo`。
`Init()` 请求回零；`Abort()` 请求停止；`Reset()` 请求驱动复位并调用基类清警。
停止/急停/下使能可以打断在途动作，其他指令要等上一条完成。模块仍负责工艺互锁和自身故障处理。

读 `Status`、`CurrentPosition` 等数值时先看 `HasPlcData`；断线时旧数值只作显示。

## 同步号与重连

连上后的第一拍先把 PLC 现有的命令块读回来当基线（同步号、伺服电平接着用），之后每条指令同步号 +1。
断线时 `TryReadBlock` 读不到：在途动作置 Failed，基线作废；重连后重新取基线，旧动作不重发。
装配层启动轴时检查重复的命令块路径，避免两个轴写同一个指令区。

## 协议和完成判断

沿用 g 项目的协议：`Pack=8`，命令块 96 字节，状态块 80 字节，命令同步号偏移 88。
命令码和字段定义在 `xyz.Components/Motion`。部署时需与现场 PLC 的类型、布局和变量路径核对。

状态块没有命令同步号回执，ADS 写成功不能证明 PLC 已执行。PLC 扫描刷缓存和轴扫描读缓存是两个没对齐的 50ms 循环，
发令后头两拍读到的可能还是发令前的旧帧，所以：看到过"运动中"（Busy=1 或 Stopped=0），或者已过 3 拍（`SettleScans`），
这一帧的停稳/到位才作数。50ms 内跑完的短行程看不到"运动中"，过了 3 拍按到位算，不会等到超时。
回零、定位、停止、复位、到速各自的完成条件见 `CheckCompletion`；超时按 `TimeoutMs` / `StopTimeoutMs` 报对应报警。
若需严格确认每条命令，需 PLC 回传已接收/已完成同步号，再扩展状态协议。

## 离线验证

`dotnet run --project tools/IoIndexSmoke/IoIndexSmoke.csproj --no-restore`

覆盖协议布局、首次取基线、立即写入、旧帧窗口、短行程完成、停止/急停打断、写失败、断线作废、重连不重发、
驱动器报错与复位、伺服电平。测试用假 PLC，不连真 PLC，不验证现场 ADS 路由和 PLC 实际执行逻辑。
