# LoadPort 模块对比评分：本平台（xyz.Framework） vs CTC（PncEfem）

- 评分日期：2026-10-10
- 对象 A（本平台）：`D:\Common`（xyz.Framework）
  - `xyz.Core\Service\xyz.Modules\Loadport\**`（BaseLoadPortModule 991 行、CarrierComponent 459 行、Enums / Operations / StateMachines）
  - `xyz.Core\Service\xyz.Modules\E84\**`（E84Component 581 行 + 契约）
  - `xyz.Core\Service\xyz.Drivers\Loadport\**`（驱动基类 243 行、FCD 品牌驱动 + 13 条指令）
  - `xyz.Core\Service\xyz.Components\Components\Drivers\LoadPortDriverComponent / FcdLoadPortComponent`、`Interfaces\ILoadPort / ICarrier / IE87Callback / IE84Callback / IE84Provider`
  - `xyz.Core\Service\xyz.Components\Components\Eap\E87\**`（EAP 载具管理，只作为 LoadPort 的 Host 侧评分）
  - `xyz.Core\Service\xyz.Service\LoadPortService.cs`、客户端 `Controls\LoadPort*` / `xyz.Client.Manual\Views\LoadPortManualControl.xaml`、`xyz.Configs\Config\sc.xml`
  - 测试：`tools\OperationWaitSmoke`（2244 行，LoadPort/E84 主战场）、`JobSmoke`、`WaferLedgerSmoke`、`SequenceSmoke`、`EapSmoke`
- 对象 B（CTC）：`D:\Soft\CTC\CTC\CTC\300C\FinalClean2`（另有一份老的 `FinalClean`，结构相同，不重复计分）
  - `PncEfem\LPs\**`：LPModule 1017 行 + 25 个 Routine + PncLoadPort 410 行
  - `PncEfem\Devices\IoLP.cs`（1165 行，IO 版设备）
  - `EfemCluster\EfemClusterSchedulerLib\Schedulers\SchedulerLoadPort.cs`、`EfemClusterUI\Controls\Parts\LoadPort.xaml(.cs)`
  - 配置：`EfemClusterRT\Config\**\System.sccfg`（每机型一份，约 1.6 万行）、`PncEfem\Config\PncEFEM\**\DeviceModelLP.xml / _ioDefineLP.xml`
  - 说明：CTC 的 `MECF.Framework.RT.EquipmentLibrary`（PNCRorzeLoadPort、RorzeE84）、`FAJobController` 等类库**不在本仓库**，涉及处标注"仓库外"，按现场已用给分并注明不确定性。

评分口径：0–10 分，10 = 该维度达到可交付/领先水平。默认权重是"平台化交付视角"（可复用、可维护、可验证），另附"设备能力视角"做敏感性对照。只评 LoadPort 模块本身；E84、E87 只按"与 LoadPort 的集成"计分。

---

## 1. 规模与结构对照（实测）

| 指标 | 本平台 | CTC（FinalClean2） |
|---|---|---|
| LoadPort 核心代码 | **4846 行 / 44 文件**（Modules\Loadport 1958 + E84 916 + Drivers\Loadport 1125 + 驱动组件/接口 518 + Service/工位基类 329） | **8066 行 / 28 文件**（LPs 6589 + IoLP 1231 + Scheduler 105 + UI 141） |
| E87（Host 侧） | 1813 行 / 10 文件（E87Component、Host、E87Port + 6 个状态机） | LPModule 里的 `IE87CallBack` 方法体基本为空；真实交互靠 `EV.Notify` 事件 + 仓库外 FAJobController |
| 注释率 | 13.9% / 16.3% / 23.6% / 23.2%（分模块） | 3.7% / 1.1% / 0% / 6.4% |
| 动作/状态数 | 动作 7 个（Home/Load/Unload/Clamp/Unclamp/Reset/Abort）+ 状态查询、版本查询、读码、带图卸载（SC 开关），状态码 6 个 | 动作 ≈25 个（含 Dock/Undock/OpenDoor/CloseDoor/Map/Latch/UnLatch/VacuumOn/Off/MappingForward/Backward/ZMapStart/End/ZAxisUp/Down/UnloadAndLoad/AutoCloseDoor），状态 30 个 |
| 机型适配方式 | sc.xml 装组件、Type 换品牌；机型模块类可空 | sccfg 每机型一份（16k 行 × 8 套），设备类用 `E39Top.ToolType != "PXWHKWFM"` 之类硬编码分支 |
| 测试 | 5 个冒烟工具，OperationWaitSmoke 单文件 2244 行、含 200 次完成竞态、E84 全流程、Mapping、自动卸载、断线重连等 | 仓库内 0 个测试工程 |

---

## 2. 逐维度评分

### 2.1 设备动作与硬能力覆盖（权重 18%）
- **CTC 9.0**：`LPModule` 的 FSM 覆盖到 Dock/Undock、门开/关门（独立动作）、Latch/UnLatch、真空开/关、Z 轴升降/Map 起止、Mapping 正/反向、UnloadAndLoad、AutoCloseDoor；`IoLP` 直接接满 DO/DI/AI（20 路状态字、25 槽 Mapping、20 路 FOUP ID）；`PncLoadPort` 有 `IsWaferProtrude` 片突出检查和 `IsEnableTransferWafer` 交叉/叠片/认不出拦截；指示灯（Busy/Complete）会控。
- **本平台 6.0**：一个动作 = 一条驱动指令（`LoadPortCommandOperation`），平台给了 7 个动作 + 状态轮询 + 读码 + Unload 后扫图对账（同一个 Unload 动作里先 CULOD 再 CLDMP）；FCD 驱动只有 `CLOAD`（Load）/`CULOD`（Unload）/`CLDMP`（原地扫图）/`ORGSH`（Home）/`PODCL`（夹紧）/`PODOP`（松开）/`ABORT`/`RESET`/`STATE`/`VERSN` 共 10 条。**没有** Dock/Undock、独立的开门/关门、Latch、真空、Z 轴、Mapping 正反向、UnloadAndLoad、片突出检查、指示灯控制。`LoadPortStatus` 里定义了 `IsLocked / IsTableIn / IsTableOut / IsDeviceAutoMode`，但 `FcdGetStateCommand` 只解析了在位/到位/报警/门开/门关 5 位（注释写明"其余位待协议手册确认"）。
- 结论：硬能力覆盖是当前最大短板，CTC 领先 3 分。

### 2.2 架构分层与可扩展性（权重 12%）
- **本平台 9.5**：驱动（`xyz.Drivers.Loadport`，不引用任何业务工程）→ 驱动组件（`LoadPortDriverComponent` 管通讯配置/生命周期/重连/主动事件）→ 品牌壳（`FcdLoadPortComponent` 只造驱动和指令）→ 模块（`BaseLoadPortModule` 管动作/状态/联锁/E87/E84）→ 载具组件（`CarrierComponent`）；sc.xml 换 Type 即换品牌，模块一次编写多机型复用（35021 的 `LoadPortModule` 是空类，只留给机型扩展）；接口契约 `ILoadPort / ICarrier / IE84Callback / IE87Callback / IE84Provider` 清楚，无全局单例，状态迁移表 `LoadPortStateTable` 单独成表。
- **CTC 5.5**：FSM + Routine 模式可读性尚可，但 `LPModule` 把设备、FSM、OP、FSM 函数、IE87 回调、报警处理全揉在 1194 行里；25 个 Routine 是同一套 `ExecuteAndWait + Stop + throw` 样板复制；设备依赖 `DEVICE / SC / EV / DATA / WaferManager.Instance / CarrierManager.Instance` 等全局单例，单元测试无从下手；机型差异靠 `E39Top.ToolType` 硬编码。

### 2.3 可靠性 / 容错 / 并发（权重 12%）
- **本平台 9.0**：指令按名字占在途槽位（`Submit`），回复丢了/超时可 `Abandon`/`AbandonAll` 让位；发送走 Channel 串行队列，接线故障只关自己那一轮连接；每拍状态查询带超时作废 + 恢复日志；驱动断线按 EC `ReconnectIntervalMs` 先关后开；主动上报（PODON/PODOF）与在途回复按帧路由区分；模块 `Begin/OperationGate` 加锁、状态查询/扫描单一扫描线程模型；每个动作独立超时 + 对应报警。冒烟里专门做 200 次"完成 vs 超时 vs 中止"竞态。
- **CTC 6.0**：`Monitor()` 里用 `Task.Delay(100).ContinueWith` 往 FSM 补 `MSG.Error`（竞态补丁）；`IoLP.IsError` 恒 `false`；`PncLoadPort.Monitor` 注释承认"unload 过程中 `_isPresent` 可能突然 false 再变 true"要靠边沿触发器兜；`LPReadCarrierIdRoutine` 用 `_retryCount` 隐晦地串三次读码；超时只有一套 `MotionTimeout` 通用值（30s），不区分动作；断线只看 `MSG.Disconnected/Connected`，没有指令作废与重连退避机制。

### 2.4 E84 搬运交接（权重 8%）
- **本平台 9.0**：完整 PIO 握手状态机（NotAvailable/Available/Requesting/WaitBusy/Transferring/WaitComplete/Releasing/TimedOut），TP1–TP5 分别计时并各带 EC 超时；光幕输入（含反向 SC）关闸门；Auto 模式没走交接就放上/拿走载具 → `UnexpectedCarrierAlarm`；超时锁存后人工 `Retry` / `Complete(carrierPlaced)` 恢复并把进展经 `IE84Callback` 报 EAP；`IE84Provider` 让 EAP 决定 Auto/搬运状态、HO_AVBL 由端口开关；装的没装（SC）与现场开关（EC）分离。冒烟覆盖送盒/取盒/闸门/中止/超时/恢复。
- **CTC 7.0**：`DeviceModelEfem.xml` 里每口一组 `E84Passiver`（L_REQ/U_REQ/READY/HO_AVBL/ES + 输入），`ToolLoader` 把 `RorzeE84.Provider` 接到 FAJobController；具体握手实现在仓库外的 `MECF.Framework...LoadPorts.PNCRorze` 库，本仓库看不到，只能按现场已用给分（±1）。LPModule 侧只留了空的 `OnE84HandoffStart/Complete` 回调。

### 2.5 EAP / E87 对接（权重 8%）
- **本平台 9.5**：每个端口一个 `E87Port`，6 个小状态机（搬运、存取方式、关联、载具 ID、槽图、取放）；读到号等 Host、槽图一律等 Host、第二次 PWC 比对不一致回 CAACK=3；Cancel/CarrierRelease/CarrierReCreate/S3F25 启停用+改存取方式；`PortPollMs` 每状态轮询（注释指出 CTC 只在两个状态查会卡住）；`IE87Callback` 上报 11 类事件（到达/移除/读码/读码失败/槽图/Load/Unload/Access 起停/干完/端口错）；EapSmoke 对假 Host 全流程验证。
- **CTC 6.0**：`LPModule : IE87CallBack` 的 `CarrierIDReadSuccess / MappingComplete / LoadComplete / UnloadComplete / CarrierComplete / StartAccessingLP / StopAccessingLP` **方法体是空的**，真实语义散在 `EV.Notify(...)`（CARRIER_LOCKED/UNLOCKED、SlotMapAvailable、CarrierUnloaded…）与仓库外的 FAJobController；E39 端口/载具对象、夹紧松开事件是 CTC 有、本平台明确砍掉的（decisions.md 记录，属有意取舍）。

### 2.6 Mapping 与晶圆账一致性（权重 8%）
- **本平台 9.0**：Load 回来的槽图不落账（槽数 ≠ SC SlotCount）或判失败（交叉/叠片/认不出，账照落便于界面显示），两种都报 `SlotMapAlarm`；**自动跑货**的 Unload（Job 干完自动卸、Host 放行）按 SC `AutoRunMapOnUnload` 决定带图与晶圆账逐槽 `Verify`，对不上判失败、端口落 Error，不让人/天车取走；**手动卸载豁免**（`UnloadManually()`，不扫图不对账），账乱了操作员也能把盒子放出去；Mapping 落账时认不出的槽按"有片"记（宁多勿漏）；自动卸载前检查"从这个口取出去的片还有几片在外"。
- **CTC 7.0**：`PncLoadPort.OnSlotMapRead` 把 0/1/2/W/? 落到 `WaferManager` + `CarrierManager`，交叉/叠片/认不出分别报警并置 `MapError`，`IsEnableTransferWafer` 拦住机械手；但"报警后照样落账、端口靠报警进 Error"（decisions.md 已记录新平台改掉了这一点）；卸载对账：CTC 的 `Unload` 是纯卸载，`CheckMap`（设备槽图 vs 账逐槽比）只活在 `LPAutoCloseDoorRoutine` 里，且只有 `AutoTransfer` 的 `IsAutoUnloadWhenJobComplete=false` 分支才走到——比新平台的"自动恒判"弱，也解释了我们原先把手动也带上对账是不对的。

### 2.7 HMI 与手动操作（权重 8%）
- **本平台 8.5**：槽位控件 `Controls\LoadPort.xaml`（25 槽、大号在上、按晶圆账画片）；`LoadPortInfoCard` 六盏灯（通讯/在位/到位/报警/自动/手动）；`LoadPortManualControl.xaml` 有 Online/Offline、Auto/Manual、Read ID、Home/Load/Unload/Reset/Abort 十个按钮 + 载具号/状态；主界面按 sc.xml 自动生成 LoadPort 页签 + 创建/启动 Job；调度图按站点摆卡片。
- **CTC 4.0**：`EfemClusterUI\Controls\Parts\LoadPort.xaml` 只有背景图 + FOUP 图 + 旋转角，右键菜单整段被注释掉；没有槽位图、没有卡片灯、没有独立手动页（手动动作靠 `OP.Subscribe` 口 + 别处界面）。

### 2.8 配置化与机型适配（权重 8%）
- **本平台 8.5**：sc.xml 一个 LoadPort 一段，下挂 `Driver / RFID / Carrier / E84` 子节点；EC 带单位、范围、默认值、中文描述；`[SCEditor]` / `[VariableMark]` 声明即入界面；每台机一份 sc.xml，模块代码共享。
- **CTC 6.5**：参数很全（`LoadPort.MotionTimeout / HomeTimeout / ReadSlotMapDelayTime / IsAutoUnloadWhenJobComplete / IsAutoClampWhen* / EnableAutoCarrierIdRead / BypassLightCurtain / 每口 IsEnable…`），但塞在 1.6 万行的 sccfg 里、每机型复制一份；部分参数 `visible="false"`；行为分支硬编码机型名（`E39Top.ToolType`）。

### 2.9 测试与可验证性（权重 8%）
- **本平台 9.5**：`OperationWaitSmoke`（假通道 + 真 FCD 驱动帧解析、一拍一拍推扫描）覆盖：动作失败/超时/中止、Load 联锁、Mapping 三种异常、带图卸载对账、自动卸载（等机械手、片没回齐）、E84 全流程与意外放取、状态查询超时恢复、在途指令作废、断线重连、复位/中止落态、载具组件全生命周期、读码重试与超时、E87/EAP 假 Host；另 JobSmoke / WaferLedgerSmoke / EapSmoke / SequenceSmoke 交叉覆盖。
- **CTC 2.0**：仓库内没有任何测试工程，验证靠现场与各机型 sccfg 反复试错。

### 2.10 可维护性、文档与交付成熟度（权重 10%）
- **本平台 8.0**：注释率 14–24%、每个公开成员都有中文说明；`decisions.md` 记录了与 CTC 的逐条对照取舍、`backend.md` 有模块契约说明；但**未上机验证**：FCD 指令名与状态串位定义仍标"待协议手册确认"（sc.xml 注释："FCD 的指令名上机前要核对"），EAP 起来时端口上已有盒子补报还没做，Interlock 层只有设计文档未编码。
- **CTC 5.5**：代码重复、注释少、`IoLP` 1150 行代码里堆 20 路 AI 状态字和 25 个 Mapping 访问器，多处注释掉的死分支；但它是在多机型现场长期跑过的版本，硬件语义（含各类异常位）被实际验证过，属于"脏但可靠"。成熟度分给高一些、可维护性分给低一些。

---

## 3. 加权总分

| # | 维度 | 权重 | 本平台 | CTC |
|---|---|---:|---:|---:|
| 1 | 设备动作与硬能力覆盖 | 18% | 6.0 | **9.0** |
| 2 | 架构分层与可扩展性 | 12% | **9.5** | 5.5 |
| 3 | 可靠性/容错/并发 | 12% | **9.0** | 6.0 |
| 4 | E84 搬运交接 | 8% | **9.0** | 7.0 |
| 5 | EAP/E87 对接 | 8% | **9.5** | 6.0 |
| 6 | Mapping/晶圆账一致性 | 8% | **9.0** | 7.0 |
| 7 | HMI 与手动操作 | 8% | **8.5** | 4.0 |
| 8 | 配置化与机型适配 | 8% | **8.5** | 6.5 |
| 9 | 测试与可验证性 | 8% | **9.5** | 2.0 |
| 10 | 可维护性/文档/成熟度 | 10% | **8.0** | 5.5 |
| | **加权总分** | 100% | **84.2** | **61.5** |

敏感性（换成"设备能力/现场优先"视角：覆盖 30%、架构 8%、可靠 12%、E84 10%、E87 6%、账 8%、HMI 4%、配置 5%、测试 5%、成熟 12%）：

| 视角 | 本平台 | CTC | 差距 |
|---|---:|---:|---:|
| 平台化交付（缺省） | **84.2** | 61.5 | +22.7 |
| 设备能力/现场优先 | **80.3** | 67.3 | +13.0 |

结论：**无论哪种口径，本平台都领先；但领先来自工程化（架构、EAP、测试、HMI、文档），"设备动作覆盖 + 上机验证"这两项仍落后 CTC**，也正是接下来要补的。

---

## 4. 相对 CTC 的差距清单（按优先级）

### P0（上机前必须闭环）
1. **FCD 协议上机核对**：指令名（LOAD/UNLOAD/CLDMP/HOM/CLMP/UCLP/ABS/RST/STATE/VERSN）与真实 LP300 手册对齐；`GET:STATE` 64 字符串把 `IsLocked / IsTableIn / IsTableOut / IsDeviceAutoMode` 的位定义补全（现在注释写着"其余位待协议手册确认"，模型有字段、驱动没填）。
2. **动作覆盖面补齐（按现场设备能力）**：Dock/Undock、独立 OpenDoor/CloseDoor、Latch/UnLatch、真空、Z 轴、Mapping 正/反向——驱动加指令、模块加动作与状态表项。若某些 LP300 型号确实没有这些机构，也要在 sc.xml/文档里写清"该机型不支持"，避免与 CTC 对照时被误判为缺失。
3. **片突出（protrude）检查**：CTC `CheckReadyForTransfer` 用 `_lpDevice.IsWaferProtrude` 挡机械手；本平台 `CanPrepare` 只看载具到位/认定，没有该项（FCD 状态串里是否有突出位需确认）。

### P1（上机后一个月内）
4. **指示灯控制**：CTC 有 `SetIndicator(Busy/Complete)`；确认 LP300 是否支持，支持就补进驱动与模块（JobManager/Job 状态驱动）。
5. **EAP 起来时端口上已有盒子补报**（decisions.md 已记为"还没做"）。
6. **CTC 侧几个现场习惯参数**：自动夹紧（`IsAutoClampWhenFoupPlacement`）、卸载后自动夹紧、卸载后自动关门的等效语义，逐条决定"要/不要/做成 SC"。
7. **AutoRunMapOnUnload 语义已收敛（2026-10-10 落地）**：SC 只管自动跑货（Job 干完自动卸、Host 放行），True = 带图+对账、False = 不扫不对账；**手动卸载不吃这个 SC**（`UnloadManually()` 恒纯卸载）——对齐 CTC 的手动行为。FCD 没有带图卸载的指令，CUDMP 那一套已删，True 时是先 CULOD 关门、再主动 CLDMP 扫一遍对账；剩下要上机确认的是卸载后发 CLDMP 的实际行为（扫完门是关的、槽图怎么回），以及按机型把 SC 值打开。

### P2（排期优化）
8. **E84 双载具位/连续交接**：`CS_1`、`CONT` 当前"本流程不看"；双位端口机型要补。
9. **CycleRun 的 EC 声明未实现**：`IsCycle / CycleRunTotal` 只声明没逻辑（decisions.md 说留着），要么实现要么删掉，避免误配。
10. **E39 载具/端口对象与夹紧松开事件**：与 Host 侧确认是否真的不需要（decisions.md 记为有意砍掉）。
11. **Interlock 层落地**：目前只有 `doc\interlock-design.md` 设计，动作联锁都是 `protected virtual` 硬编码；按设计做完后 "机械手 Idle / 光幕 / 压力" 之类的现场规则可配置。
12. **`LoadPortStatus` 与 `LoadPortDto` 的字段利用率**：模型字段多于驱动可解析字段，建议加一条冒烟断言"驱动解析出的字段集合 ⊆ 模型字段"，防止长期空置。

---

## 5. 相对 CTC 的领先项（保持住）

1. **分层与品牌可换**：`xyz.Drivers`（纯协议）↔ 驱动组件（生命周期/重连/事件）↔ 品牌壳 ↔ 模块，sc.xml 换 Type 即换品牌，模块零改动多机型复用（CTC 每机型复制）。
2. **在途指令机制**：按指令名占位、超时作废、断线作废、Channel 串行发送、回复帧路由——这是 CTC 完全没有的（CTC 回复丢了就一直等/卡住）。
3. **E84 全流程可控**：TP1–TP5、光幕、Auto 下意外放取报警、超时锁存 + Retry/Complete 人工恢复、进展上报 EAP、测试覆盖（CTC 在仓库外，且 LPModule 只留空回调）。
4. **Mapping/账一致性**：槽数不符不落账、异常槽判失败、可选的卸载对账、认不出按有片记、自动卸载前检查"片没回齐"——粒度明显细于 CTC（CTC 报警后照样落账并且卸载对账默认禁用）。
5. **E87 状态机**：每口 6 状态机 + PWC 比对 + Cancel/Recreate + AccessMode + PortID + 每状态轮询（对比 LPModule 的空回调 + 事件散报）。
6. **HMI**：槽位图、六灯卡片、手动页、Job 创建入口（CTC 只有一张 FOUP 图，菜单注释掉）。
7. **测试资产**：5 个冒烟工具、假通道 + 真协议解析、竞态/超时/断线/对账全覆盖（CTC 零测试）。
8. **文档与注释**：注释率 14–24% vs 1–4%，`decisions.md` 能把"与 CTC 的差异为什么这么定"逐条讲清，交接成本低。

---

## 6. 一句话结论

> **本平台 LoadPort 在架构、可靠性、EAP、HMI、测试、文档六个维度全面超过 CTC（综合 84.2 : 61.5），但 CTC 在"设备动作覆盖 + 机构级检查（片突出、指示灯、夹紧/真空/Z 轴/Dock）"和"现场长期验证"上仍占优；把 FCD 协议上机核对与动作覆盖补齐后，本平台可以整体替代 CTC 的 LPModule，而不只是换架构。**
