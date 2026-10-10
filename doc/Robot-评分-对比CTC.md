# Robot 模块对比评分：本平台（xyz.Framework） vs CTC（FinalClean2）

- 对比日期：2026-10-10
- 本平台实测：`tools\OperationWaitSmoke` 当前 **929 项检查全部通过**，其中跟机械手、搬运直接相关的约 89 项：取放记账 17 项、报警 30 项、初始化/中止 15 项、搬运出错码 6 项、状态轮询 21 项。`tools\JobSmoke` 另有双机械手共享站点、取放失败留锁等搬运场景。
- 对象 A（本平台）：`D:\Code`
  - 平台模块：`xyz.Modules\Robot\**`（BaseRobotModule 726 行、状态表 39、站点 81、接口 67），约 950 行
  - 机型层：`xyz.35021\Service\xyz.35021.Module\Robot\**`（RobotModule 55 + 7 个操作类各 63~69 行），约 530 行
  - 驱动：`xyz.Drivers\Robot\**`（RobotDriverBase 314、RobotCommand 100、锐洁驱动 + 13 条指令约 620），约 1130 行
  - 组件：RobotDriverComponent 191、RejeRobotComponent 77、RobotArm/RobotAxis 100，约 370 行
  - 搬运：TransferManager 680、TransferRoutine 463、站点环 281，约 1530 行（手动、Job、人工恢复共用）
  - 服务/界面：RobotService 153、TransferService；客户端机械手相关约 3200 行（手动页、Robot 动画控件、调度图）
- 对象 B（CTC）：微信目录里的源码副本 `CTC\CTC\300C\FinalClean2` + `FrameworkLocal`
  - **先说明实际跑的是哪套**：EFEM 机械手（EFR）和 TM 机械手（MTR）都是 `PNCRorzeRobot`，走 Rorze 文本协议，跟 LoadPort 共用一个 `RorzeEfemController`（`PncEfemDeviceManager.cs:55`、`PncMainframeDeviceManager.cs:66`）。目录里的 `PuEfemRobot`、`MYRobot`、`CTCRobot`、`PNCRobot` 都没有实例化，是死代码。
  - EFEM：EfemModule 1046 + EfemModuleDevice 103 + 9 个例程 3009 ≈ **4160 行**
  - TM：TMModule 953 + TMModuleDevice 189 + 7 个例程 3355 ≈ **4500 行**
  - 公共设备层：RobotBase 2232 + PNCRorzeRobot 1574 + RorzeEfemController 446 + Sunway RorzeEfem/Handler/Connection 4020（Handler 跟 LoadPort 共用）
  - 调度：AutoTransfer 4724（机械手相关几百行）+ SchedulerTM 264；界面：TMViewModel 1610 + TMView.xaml 2740；仿真器：CTCRobotSimulator 859
  - 死代码/重复：PuEfem + PuEfemRobot 830、MYRobot 247、`Efems\Rorzes` 目录 3618（跟 Sunway 几乎逐行相同）、CTCRobot/PNCRobot 约 2900
- 范围：只比机械手本身，加上它直接依赖的搬运联锁、晶圆账、手动页。Job 和调度见 `Job-评分-对比CTC.md`。

---

## 1. 结构对照

| 项 | 本平台 | CTC |
|---|---|---|
| 分层 | 平台模块（状态表、超时、报警、记账、状态轮询）→ 机型层（只写动作）→ 驱动组件（选品牌）→ 锐洁驱动 → 指令对象 | 模块（FSM + 例程）→ 设备（RobotBase 29 个状态的第二个 FSM）→ Rorze 控制器 → Handler；**两层 FSM 各管各的状态** |
| 机械手数 | sc.xml 配几台算几台；两台可以共享站点，站点环保证互斥 | EFEM 一台、TM 一台，各管各的区域，写死在 DeviceManager 里 |
| 手臂 | N 只手（sc.xml 配几个 Arm 节点就是几只），按站点限定可用手 | 固定 4 把刀 + 两种组合（下双 1+2、上双 3+4），每把刀一个 SC 使能开关 |
| 动作 | Home / Reset / Abort / Pick / Place / PowerOn / PowerOff | Home / Pick / Place / Swap / Goto / Map / Extend / Retract / CycleTest / Abort / Reset / Online / Offline |
| 换品牌 | sc.xml 改 Driver 的 Type | Rorze 写死；站点编码两份静态表 |
| 测试 | 约 89 项机械手相关冒烟 + JobSmoke 搬运场景 | 0 个自动化测试；有协议仿真器，但手指传感器字段格式对不上（`CTCRobotSimulator.cs:205` 对 `PNCRorzeRobot.cs:1481-1492`） |

CTC 动作列表长，但真正能用的要打折扣（见 §3、§5）。

---

## 2. 一次取片怎么走：逐段对照

| 阶段 | 本平台（Job/手动传片，经 TransferManager） | CTC TM（MTR → PM） | CTC EFEM（EFR → LP） |
|---|---|---|---|
| 接单检查 | 源有片、目标空、片归属、槽位/手指/片锁、站点许用这只手、自动挑空手（`TransferManager.cs:273-475`） | 账上源有片、手上没片；刀使能；手指传感器为空；PM 传感器有片；臂配置 ARM1_3/ARM2_4 匹配（`TMPickRoutine.cs:84-213`） | 账上源有片；刀使能；手指传感器；LP 门开/突片检查**实际不生效**（见 §5-1） |
| 站点准备 | 先抢目标再抢源，两步准备（准备一、准备二），站点状态环加锁互斥（`TransferRoutine.cs:161-225`） | PM 握手：`SetManualPickPlace` + `PickRequest` → 等 `PickAllowed`；挡板到位，写死 500 ms（`TMPickRoutine.cs:351-366,812-822`） | `CheckReadyForTransfer` 被注释（`EfemPickRoutine.cs:302-306`） |
| 发指令 | 落 Transferring 标记 → 发 Pick → 等结果帧；EC 超时（`TransferRoutine.cs:308-337`） | 发 LOAD，等 INF/ABS；三层超时（例程 30 s / 设备 300 s / 通讯 60 s）互不协调 | 同 TM，例程超时默认 10 s |
| 完成后核对 | **不核对手指传感器** | 手指传感器跟账比对，写死 5 s（`TMPickRoutine.cs:468-543`） | 同 TM（`EfemPickRoutine.cs:480-555`） |
| 记账 | 模块操作成功才记（`BaseRobotModule.cs:703-723`） | 设备收到 INF 才记（`PNCRorzeRobot.cs:991-1014`） | 同左 |
| 失败 | 模块落 Error、报警；动过手就留住槽/片/手的锁，等人工确认片位（`TransferManager.cs:594-628`） | TM 进 Error、下线；账不动，也不留锁；ABS 只发报警，设备一直停在 Picking，直到 300 s 超时 | 同左 |

---

## 3. 逐维度评分

### 3.1 取放核心与状态机（权重 12%）
- **本平台 8.5**：一张状态表（`RobotStateTable.cs:10-33`）。Abort 后落 NotInit，必须重新 Home；Reset 只清错，Error 经 Reset 回 NotInit。模块状态跟设备状态只有一份。缺 Goto、Swap、Map 这类附加动作。
- **CTC 5.0**：动作多，但模块和设备两层 FSM 对不上：
  - Abort 后模块回 Idle，设备却进 Init，下一个动作报 not Ready（`EfemModule.cs:478` 对 `RobotBase.cs:1252`）。
  - FsmOnError 只中止 Pick/Place/Goto 三种例程，Swap/CycleTest 出错时例程继续跑（`TMModule.cs:724-740`）。
  - 对外的 Pick/Place 接口一律 `return true`（`EfemModule.cs:913-925`）。
  - EFEM 的 Goto/Map/Extend/Retract 发命令那行被注释，机械手不动就报成功；Extend/Retract 的 `Init` 重载自己调自己，一调就栈溢出（`EfemExtendRoutine.cs:51-54`）。
  - EFEM Swap 走的是被注释掉的 CHANGE 分支，只能等超时（`PNCRorzeRobot.cs:1158-1165`）。
  - TM 的 Swap、Goto 能用。

### 3.2 联锁与安全（权重 14%）
- **本平台 5.0**：
  - 自动路径：站点两步准备 + 状态环 + 账面检查；LoadPort 要 Loaded、载具到位、Host 认可槽图才让进（`BaseLoadPortModule.cs:242-256`）。
  - 腔体的准备一、准备二是空操作（`BaseTransferStationModule.cs:35-51`，腔体和 35021 都没重写），**没接门/挡板/允许取放信号**。
  - 手指在位只用来显示，取放前后都不拿它核对。
  - **手动页的 Pick/Place 直接调模块**（`RobotService.cs:53-96` → `BaseRobotModule.cs:602-646`），只查站点配置和许用手，不查账、不做站点准备。也就是说，LoadPort 门关着也能发取片，有没有撞的风险全看控制器自己有没有联锁。
- **CTC 6.0**：
  - TM 到 PM 有真正的硬件握手（PickRequest/PickAllowed + 挡板到位）；EFEM 和 TM 在取放前后都核对手指传感器；手动 Pick/Place 也走这套例程，有账面和传感器检查。
  - 但 EFEM 到 LP 的门开、突片检查因为类型转换得到 null 而被跳过：`as PncLoadPort`，实际对象是 `PNCRorzeLoadPort`（`EfemPickRoutine.cs:167-182`）。
  - Buffer、LPT 真空和 EFR 手部报警信号所在的 xml 没加载，取到的都是 null（`PncEfemDeviceManager.cs:74` 被注释）。
  - 手动 TM 动作跳过 `CheckReadyForTransfer`；仿真模式跳过所有传感器检查。

### 3.3 晶圆账与失败恢复（权重 12%）
- **本平台 8.5**：
  - 成功才记账；失败分两种：没动过手就把站点环还回去，动过手就留住槽、片、手的锁，卡住后续自动动作，等人工确认（`TransferRoutine.cs:68,430-462`）。有改账页（移账、删账、补账）。
  - 扣分：放锁的 `ReleaseAsync` 只有 gRPC 接口（`TransferService.cs:71`），**界面上没入口**。Job 中止要等这一单的 Held 清空才落 Aborted（`JobManager.cs:2096-2101`），没人放锁就卡在 Aborting。
- **CTC 4.0**：
  - 也是成功才记账，但失败后什么都不锁。Swap 取成功、放失败时账两边都不动，账实不符。
  - Home 时比对传感器和账并弹窗，但建片、删片的代码被注释（`PNCRorzeRobot.cs:454-535`）。
  - `WaferMovedStart` 在动作前就写，失败时只留下开始记录。
  - 修账靠界面的 Create/MoveWaferInformation。

### 3.4 通讯驱动与断线恢复（权重 11%）
- **本平台 7.0**：
  - 在途表按回显名认领；超时用 `Abandon` 作废，让出槽位（`RobotDriverBase.cs:84-138`）。急停确认后打断在途运动（`RejeRobotDriver.cs:96-103`）。发送失败关连接、作废全部在途指令。收、发各用一条队列串行处理，在途表有锁。
  - **扣分：机械手断线不重连**。LoadPort、RFID 用了 `DriverReconnector`（`LoadPortDriverComponent.cs:57`、`RfidDriverComponent.cs:70`），机械手没用；Reset 也不重开连接。网线松一下或控制器重启，只能重启服务。
  - 报错显示原码（如 `40010006#Arm2 No Wafer When Put`），没有翻译表。
- **CTC 4.0**：
  - 掉线后连 3 次、间隔 500 ms，再连不上进 Error，要 Reset 才再连。断线时只清命令队列，动作中途断线要等 300 s 超时（`PNCRorzeRobot.cs:283-307`）。心跳整段被注释。
  - 报警码表因为截取时带着 `|` 前缀，永远查不中（`RorzeEfemConnection.cs:357,362`）。
  - 未示教时 `IsBusy` 留在 true，机械手卡死（`PNCRorzeRobot.cs:949-958`）。
  - 在途表读不加锁，200 ms 和 10 ms 两个线程轮询同一条连接。

### 3.5 手臂、站点与多机械手（权重 8%）
- **本平台 8.0**：
  - 手指数、站点号、伸出方向、许用手都在 sc.xml 配，配错开机就报（`RobotStation.cs`）。自动挑空手；两台机械手共享站点有测试。
  - 缺"某只手坏了临时停用"的开关（要改站点 Arms），也不分片子尺寸。
- **CTC 6.5**：
  - 4 把刀 + 两种组合，每把刀有 SC 使能；每个 PM 用哪组刀可配（ARM1_3/ARM2_4）。
  - 站点编码两份静态表重复；片子尺寸参数算出来了但没拼进命令。

### 3.6 吞吐类功能（权重 9%）
- **本平台 2.0**：没有 Swap、双取双放、空闲预定位。两只手靠调度排两趟独立搬运。
- **CTC 7.5**：
  - TM 在 PM 换片：LOAD 后 UNLOAD 连发两条（`PNCRorzeRobot.cs:1148-1155`）。
  - Buffer 偶数槽成对双取（`AutoTransfer.cs:2423-2459`）。
  - 空闲时预先 Goto 到片停留最久的 PM（`AutoTransfer.cs:3031-3064`）。
  - 扣分：EFEM Swap 坏的；选刀逻辑里有几处取错刀、下标差一（`AutoTransfer.cs:2293-2294,2500-2501`）。

### 3.7 状态监视与报警（权重 8%）
- **本平台 8.0**：
  - 轮询报错、伺服、速度和每根轴的坐标；手指在位由控制器主动推送。
  - 报警用属性声明，带说明和处理办法，只能人工 Reset 清（`BaseRobotModule.cs:114-124`）。
  - 控制器在空闲时推报错只报警、不落 Error，要等下一个动作被拒才进 Error。
- **CTC 4.5**：
  - 手指传感器 200 ms 刷新。
  - 轴坐标数据点从来不更新，一直是 0。
  - SIGSTAT 里的真空、气压、急停、门都解析了，随后直接丢掉（`RorzeEfemController.cs:296-351`）。
  - 报警全靠散落的 `EV.PostAlarmLog`；AlarmTrigs 列表是空的。

### 3.8 手动界面与维护工具（权重 8%）
- **本平台 5.0**：
  - 手动页有 Pick/Place/Home/Abort/Reset/PowerOn/PowerOff；调度图动画；轴位表；下拉跟着站点走。
  - **缺**：设速度（`RejeSetSpeedCommand` 写了没接）、带联锁的手动传片（`TransferAsync` 只有后端）、放锁、循环测试、动作计数、示教数据查看。
- **CTC 6.0**：
  - 有 Home/Abort/Reset/Online/Offline/Pick/Place/Swap/CycleTest，能读示教表，有每把刀的动作计数，能手动建片和移片。
  - 扣分：速度设置只写 SC，键名还对不上（`PNCRorzeRobot.cs:240-251`）；Goto 按钮隐藏；循环测试第二轮从原槽取，必然失败（`TMPickAndPlaceCycleTestRoutine.cs:112-118`）。

### 3.9 配置化与换品牌（权重 6%）
- **本平台 9.0**：品牌只在驱动组件和指令里，机型层不碰协议；超时全在 EC，开关在 SC。
- **CTC 3.5**：只支持 Rorze；超时一部分写死（500 ms、5 s、2 s）；SC 键不一致（`EFEM.EfemRobot.*` 在 sccfg 里不存在）；有 `GTXPreFurnaceClean` 这类机型分支。

### 3.10 测试与可验证性（权重 6%）
- **本平台 8.5**：约 89 项机械手相关冒烟（记账、报警、轮询顺序、超时作废、迟到回复、关连接作废）+ JobSmoke 搬运场景；用真锐洁驱动接假通道。扣分：仓库里没有协议级仿真器，也没有真机回归。
- **CTC 2.0**：0 个自动化测试；协议仿真器只回 ACK，2 s 后回 INF，手指传感器格式对不上。

### 3.11 代码质量与可维护性（权重 6%）
- **本平台 8.0**：注释写的是"为什么"。扣分：35021 的 7 个操作类几乎逐行相同，只差指令和超时；SetSpeed 指令是死代码。
- **CTC 2.5**：确认的 bug 有一串（`TMPickAndPlaceRoutine.cs:264` 把 Blade2 写了两遍、Extend 栈溢出、Goto 参数越界、IsBusy 卡死、速度范围判断恒为假）；三套机械手类和两份 Rorze 目录是死代码；吞异常、日志刷屏。

---

## 4. 加权总分

### 4.1 默认口径：平台化交付（可复用、可维护、可验证）

| # | 维度 | 权重 | 本平台 | CTC |
|---|---|---:|---:|---:|
| 1 | 取放核心与状态机 | 12% | **8.5** | 5.0 |
| 2 | 联锁与安全 | 14% | 5.0 | **6.0** |
| 3 | 晶圆账与失败恢复 | 12% | **8.5** | 4.0 |
| 4 | 通讯驱动与断线恢复 | 11% | **7.0** | 4.0 |
| 5 | 手臂、站点与多机械手 | 8% | **8.0** | 6.5 |
| 6 | 吞吐类功能 | 9% | 2.0 | **7.5** |
| 7 | 状态监视与报警 | 8% | **8.0** | 4.5 |
| 8 | 手动界面与维护工具 | 8% | 5.0 | **6.0** |
| 9 | 配置化与换品牌 | 6% | **9.0** | 3.5 |
| 10 | 测试与可验证性 | 6% | **8.5** | 2.0 |
| 11 | 代码质量与可维护性 | 6% | **8.0** | 2.5 |
|  | **加权总分** | 100% | **69.0** | **48.8** |

### 4.2 敏感性口径：现场设备能力优先

权重重排：核心 12%、联锁 18%、账与恢复 12%、通讯 12%、手臂 8%、吞吐 14%、监视 6%、手动维护 10%、配置 3%、测试 2%、质量 3%。

| 视角 | 本平台 | CTC | 差距 |
|---|---:|---:|---:|
| 平台化交付（默认） | **69.0** | 48.8 | +20.2 |
| 现场设备能力优先 | **63.6** | 53.0 | +10.6 |

结论：
- 两种口径都是本平台领先，但比 Job 那次（84.9 对 44.7）**差距小得多**。
- 本平台的机械手底子好：状态只有一份，失败时会留锁，驱动对在途指令处理严谨，有测试。短板集中在**联锁和现场功能**：腔体握手、手指核对、手动页走后门、断线不重连、没有换片和双取。
- CTC 正好反过来：功能多，跑过现场，但一半功能是坏的或注释掉的。

---

## 5. CTC 侧查到的代码级问题（按严重度）

1. **EFEM 到 LP 的门开、突片联锁形同虚设**：`DEVICE.GetDevice<LoadPortBaseDevice>(...) as PncLoadPort`，实际对象是 `PNCRorzeLoadPort : LoadPortBaseDevice`，转换结果为 null，被 `lpDevice != null` 跳过（`EfemPickRoutine.cs:167-182`，Place、Swap 同样）。只剩"LPModule 不在 Error"一项。
2. **两层 FSM 不同步**：Abort 后模块 Idle、设备 Init；FsmOnError 不中止 Swap/CycleTest；服务接口一律回 true；`SchedulerTM` 下发失败也设 `_task`，任务永远等不到完成，只能 Reset 解开（`SchedulerTM.cs:70-82`）。
3. **断线、异常结束等 300 s**：断线只清队列；ABS 只发报警，设备停在 Picking 直到设备层 300 s 超时。
4. **账实不符没有保护**：失败不留锁；Swap 半成功时账两边都不动；Home 时的建片、删片被注释。
5. **注释掉的功能报成功**：EFEM 的 Goto/Map/Extend/Retract 不发命令就报成功；速度设置不发命令；Reset 不发清错（`PNCRorzeRobot.cs:575-586`）。
6. **确认的 bug**：Extend/Retract 栈溢出、Goto 参数越界、`Blade2 && Blade2` 笔误、未示教时 `IsBusy` 卡死、报警码表查不中、速度范围判断恒为假。
7. **死代码和重复**：PuEfemRobot、MYRobot、CTCRobot、PNCRobot 都没实例化；`Rorzes` 和 `Sunway` 两个目录几乎逐行相同；`ConverBladeToParameter` 抄了 7 份。
8. **线程安全**：在途表读不加锁；两个线程用不同的锁轮询同一条连接；Monitor 每拍开一个 `Task.Delay(100)`。

---

## 6. 本平台相对 CTC 的差距清单

### P0（上机前）

1. **机械手断线重连**：照 LoadPort 接 `DriverReconnector`。重连后要把 `_waferEventSubscribed` 清掉，重新订阅在位推送。
   - 不改的后果：网线松一下或控制器重启，机械手一直断着，只能重启服务。
2. **手动页 Pick/Place 加最基本的联锁**：至少过账面检查（源有片、手指空、目标空），并确认站点允许进入（LoadPort 要 Loaded，腔体要空闲）。也可以把手动页改成走 `TransferAsync`。
   - 不改的后果：门关着、槽里有片也能发取放，撞不撞全看锐洁控制器自己的联锁；取完账移不动，只记一条 Error 日志（`BaseRobotModule.cs:718-722`），账实不符。
3. **放锁入口**：动过手的失败要在界面上能"确认片位 → 放锁"（后端 `ReleaseAsync` 已有）。
   - 不改的后果：搬运失败后这片、这两个槽、这只手一直锁着，Job 中止卡在 Aborting。可以并进 Job 监控页一起做。
4. **腔体取放握手**：腔体准备一、准备二现在是空操作。35021 腔体如果有门、挡板，或者有"允许取放"信号，要接进准备二和 `OnTransferFinished`。CTC TM 的 PickRequest/PickAllowed + 挡板到位就是这个。
   - 要先确认 35021 腔体硬件有没有门。

### P1（上机后按现场需求）

5. **取放后核对手指在位**：取完应有片、放完应无片。只在 `WaferEventEnabled` 开着、`HasWafer` 不为 null 时核对，不符就报警、落 Error。
   - 不改的后果：控制器回成功但片没取上或掉了，账会错，到下一步才暴露。
6. **设速度**：`RejeSetSpeedCommand` 已经有了，接到驱动组件、模块动作、手动页即可。现场调试、示教都要降速。
7. **换片（Swap）、预定位**：看节拍需要再加。锐洁要是没有一条指令换片的功能，就像 CTC TM 那样拆成两条连发。

### P2（有需要再做）

8. 单只手停用开关（现在要改站点 Arms）、动作计数、循环测试、示教数据查看。
9. 报错码中文翻译：原码自带英文说明，目前够用。

### 不值得改的

- 35021 的 7 个操作类合成一个通用的单指令操作：只是少几百行重复，不改也不会出问题。

---

## 7. 一句话结论

本平台机械手的骨架比 CTC 干净得多：状态只有一份，失败会留锁等人工，驱动对在途指令处理严谨，有约 89 项回归；CTC 也没有能学的架构。但这次差距只有 10～20 分，不像 Job 那样悬殊。原因是 CTC 在联锁（PM 握手、手指核对）和现场功能（换片、双取、预定位）上确实有东西，而我们还缺断线重连、手动页联锁、放锁入口、腔体握手这四样上机前必须补的。
