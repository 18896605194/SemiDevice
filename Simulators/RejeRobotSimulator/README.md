# RejeRobot 机械手仿真器

xyz 自有的设备级仿真器之二：**RejeRobot 机械手**(按真实 TCP 协议复刻,与 XM RobotSimulator 行为一致),
用于在无真机环境下联调 xyz.Drivers 的 Robot 驱动。WPF 深色工业风,复用 xyz.Client.Presentation 样式。

## 运行

```
dotnet run --project Simulators/RejeRobotSimulator [--data <目录>]
```

- 默认监听 `0.0.0.0:9000`,启动即自动监听(可在工具栏改端口后重启监听)。
- `--data <目录>` 指定实例数据目录(config.json / log.txt / crash.log / fault.* 旗标),
  同机跑多实例时各给一个目录互不干扰;持续实例目录:`Simulators/reje-1/`。
- 也可在统一仿真器(SimulatorHub)中作为页签运行,一次开多台机械手;独立 exe 仍可单独使用。

## 协议速览

- 传输: TCP,ASCII,以 `;` 分帧。
- 上行: `@CommandName[参数];`,如 `@Status;` `@Speed50;` `@G10102;` `@XPos;`
- 下行两段式:
  1. 确认 `>;`
  2. 结果 `>00000000#OK@G10102;`(成功) / `>99990011#Robot busy (motion in progress)@G...;`(失败)
- 事件推送: `>00000000#SubWaferEx,<臂>,<0有片|1无片>@Event;`
- 主动报错: 注入错误且 AEO 开启时广播 `>12345678#Simulated error@Error;`
- 心跳: `@OpenStartHeart;` 开,`@HeartTime<ms>;` 调间隔,帧 `>00000000#HeartBeat@HeartBeat;`

### 指令集(56 条)

| 类别 | 指令 |
|---|---|
| 查询 14 | Status / Error / Project / Program / Pressure / Active / QEnable / AxisPos(`<轴>Pos`) / QSpeed / DriveError / QOpMode / QAWC / QAWCD / QSubWafer |
| 设置 30 | Reset / PStop / SStop / Resume / PowerOn / PowerOff / Speed / ArmDistance / Unload / OpenEMV / CloseEMV / OpenStartHeart / CloseStartHeart / QHT / HeartTime / AxisWorkHome / SetAxisPos / SetAxisNeg / SetAxisV / DMO / DMC / AEO / AEC / SWO / SWC / AxisRange / ZLS / ZFS / SAWCD / SubWafer |
| 动作 12 | Home / G / GIN / GOT / GW / GWA / P / PIN / POT / PW / PWA / GAP |

- 动作参数 `XYYZZ`:手指 X(十进制)、工位 YY 与层 ZZ(十六进制),如 `G10102` = 手指1 从工位 0x01 层 0x02 取片。
- `GAP XYYZZUVVWW` 取放一体,耗时翻倍,结果两帧(先 `@G...` 后 `@P...`)。
- 轴名归一化:`@XPos;→AxisPos`、`@SetZPos700;→SetAxisPos(Z700)`、`@ThetaWorkHome45;→AxisWorkHome`、`@Arm3Home;→Home(Arm3)`。

### 错误码

| 码 | 含义 |
|---|---|
| 00000000 | 成功 |
| 99990011 | 忙(动作执行中再收动作) |
| 99990030 | 动作被 SStop/PStop 打断 |
| 99990003 | 参数无效 |
| 99990020 | 未使能 |
| 99990010 | 非自动模式(PowerOn/Off/Resume 限自动) |
| 99990099 | 未知指令 |

## 关键行为语义(与 XM 一致)

- **一次一个运动**:动作判定与置位在锁内原子完成,忙时拒绝 99990011。
- **急停/平稳停走快路**:SStop/PStop 在读循环内联处理,立即 Set AbortSignal 打断在途运动,
  运动指令按 99990030 回错;读循环不排队,运动期间急停帧进得来。
- **取放片更新手指在位**:`G/GIN/GOT` 置 true、`P/PIN/POT` 置 false、`GAP` 取臂 true 放臂 false;
  订阅(SubWafer 1)时变化即推 SubWaferEx,订阅成功立即补推 4 臂在位基线。
- **回包延迟**:ACK 延迟 / 结果延迟 / 动作耗时 / 失败率 四个参数工具栏可实时调(模拟慢设备/随机失败)。

## 故障注入(无人值守冒烟)

数据目录下放旗标文件即生效,删掉即恢复:

| 旗标文件 | 效果 |
|---|---|
| `fault.robot.error` | 注入控制器错误码 12345678(等价[注入错误]按钮,AEO 开启时广播 Error 帧) |
| `fault.robot.noverify` | 取放片不更新手指在位,让上位机后置校验失败 |

## 界面

- 左侧状态拨动区:伺服使能 / 操作模式 / 速度 / 注入·清除错误 / 4 臂手指在位与压力 / 10 轴坐标(改完即写状态,上位机下一拍轮询可查) / 订阅·主动报错·DeadMan·滑片检测开关。
- 右侧:当前状态快照(400ms 刷新)+ 收发日志(接收=绿 发送=蓝 推送=紫 系统=灰 错误=红 自测=黄),支持手动广播任意帧。
- 「协议自测」不依赖 TCP,直接走 ProcessCommand 真实路由,校验解析/回帧/互斥/中止语义。
- 「静默日志」跑批压测用,丢弃日志不再编组 UI 线程。
