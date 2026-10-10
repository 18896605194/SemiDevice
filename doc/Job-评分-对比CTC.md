# Job 模块对比评分：本平台（xyz.Framework） vs CTC（FinalClean2）

- 对比日期：2026-10-10
- 本平台实测：`tools\JobSmoke` 当前 **514 项检查全部通过**（比 10-07 那版 306 项多了双机械手共享、重试放片、取放分开重试等场景）
- 对象 A（本平台）：`D:\Code`
  - Job 核心：`xyz.Core\Service\xyz.Modules\Job\**`（19 个文件、4714 行；JobManager 2531、Cj/Pj 管理器 577、任务表 633、调度组件 355、模型/DTO 约 380）
  - 搬运执行：`xyz.Core\Service\xyz.Modules\Transfer\**` + `TransferStation\**`（约 1760 行；Job、手动、人工恢复共用）
  - EAP：`xyz.Components\Components\Eap\E40\E40Component.cs` 617 行、`E94\E94Component.cs` 580 行、`IJobManager.cs` 52 行、`JobErrors.cs`
  - 服务/UI：`xyz.Service\Jobs\JobService.cs` 362 行、主界面 LoadPort 页签（建 Job / 启动 Job / 逐槽 Sequence）
  - 测试：`tools\JobSmoke` 单文件 1905 行，假 LoadPort / 腔体 / 机械手 / 搬运管理，一拍一拍推扫描
- 对象 B（CTC）：微信接收的 CTC 源码副本，扫的是 `CTC\CTC\300C\FinalClean2` + `CTC\CTC\FrameworkLocal`
  - FA 层：`FAHost.cs` 3622 行、`FAJobController.cs` 1604 行、`E40FA\**` 1101 行、`E94FA\**` 754 行、`Common\Jobs\**` 485 行
  - 调度层：`EfemClusterSchedulerLib\AutoTransfer.cs` 4724 行、`ManualTransfer.cs` 492 行、`ReturnAllWafer.cs` 727 行、`HomeAll.cs` 124 行、`Schedulers\**` 2263 行
  - 适配：`EquipmentManager_FA.cs` 514 行；历史：`JobDataRecorder / FAJobDataRecorder / JobMoveHistoryRecorder` 302 行；UI：`MonitorJobView(.xaml/.cs)/MonitorJobViewModel` 584 行
  - 副本实况：`AutoTransfer.cs` 有 4 份（FinalClean / FinalClean2 / C1 / C2，4 个哈希都不同）、`FAJobController / FAHost / E40_ProcessJobStateMachine / E94_StateMachine` 各 2 份（GTX / PXW，也都不同）
- 范围说明：只对比 Job 体系（含接 Job 的调度、EAP 上报、存库、UI）。CTC 里 `MECF.Framework.RT.EquipmentLibrary` 等仓库外类按现场已知处理；`D:\Soft\CTC` 当前不在本机，本轮以微信目录里的副本为准。

---

## 1. 规模与结构对照

| 项 | 本平台 | CTC（FinalClean2） |
|---|---|---|
| Job 核心状态/管理 | JobManager 2531 + Cj/Pj 管理器 577 + 状态机 169 + 模型 266 ≈ **3543 行** | FAJobController 1604 + E40FA 1101 + E94FA 754 + Common Jobs 485 ≈ **3944 行**（FA 层；调度层还各有一套 Job，见下） |
| 调度/执行 | SchedulerComponent 355 + 任务表 633 + TransferManager/TransferStation 约 1760，合计约 **2748 行** | AutoTransfer 4724 + ManualTransfer 492 + ReturnAllWafer 727 + HomeAll 124 + Schedulers 2263 ≈ **8330 行** |
| EAP / 适配 | E40 617 + E94 580 + IJobManager 52 + JobService 362 ≈ **1611 行** | FAHost 3622（Job/设备部分）+ EquipmentManager_FA 514 ≈ 4000 行以上 |
| 持久化 | control_job / process_job 两表，一 Job 一行，每片任务进 PJ 行 JSON；后台 Channel 写 | cj_data / pj_data / process_job_data / job_move_history，起止时间与数量，字符串拼 SQL |
| UI | 主界面 LoadPort 页签：建 Job、逐槽 Sequence、启动；JobService 查全貌；**暂无 Job 监控页** | MonitorJob 页：CJ/PJ 两张表 + 命令按钮；**只显示 Scheduler.\* 的 Job，多项按钮注释** |
| 测试 | JobSmoke 1905 行、**514 checks PASS** | Job/调度 **0 个自动化测试** |
| 机型适配 | Job/Scheduler 平台件；机型差异在 sc.xml 的 Task 子类与站点 SupportedTasks | `_toolType` 硬编码 SPMHK/CU2/GTX；AutoTransfer 4 份、FAJobController 2 份 |
| 合计（不含机型副本） | 约 **10.2k 行**（含测试/搬运/EAP） | 约 **15k 行**（不含 FAHost 的 Job 片段；不含 4 份 AutoTransfer 副本） |

结构结论：两边 Job 核心状态代码量接近，但 CTC 的体量主要在 **第二套 Job + 调度单体**；本平台把状态、任务、调度、搬运、EAP 分层，单文件更大的是 JobManager 2531 行。

---

## 2. 两边怎么跑：流程逐段对照

| 阶段 | 本平台 | CTC |
|---|---|---|
| 建 PJ | Host S16F15 / 本地服务都调 `IJobManager.CreateProcessJobAsync` → JobManager 一把锁里查重、查配方快照、定片/生成任务行 | Host S16F15 → FAHost → `FAJobController.PJEnhCreat`（FA 层 PJ） → EquipmentManager_FA → 轮询 → `MSG.FAJobCommand` → `AutoTransfer.CreateProcessJob`（调度层再建一个 PJ）；本地界面建 Job 只走 `AutoTransfer.CreateJob` |
| 建 CJ | 同一个 `CreateControlJobAsync`，按 PJ 名关联；本地与 Host 同检查 | Host S14F9 → `FAJobController.CJCreate`（FA 层 CJ） → 轮询 → `AutoTransfer.CreateControlJob`（调度层 CJ）；本地 `CreateJob` 只建调度层 |
| 启动 | 同一个 `StartControlJobAsync` / E94 `ExecuteControlJobCommandAsync`，受 SC `ProcessJobAutoStart / ControlJobAutoStart` 控制 | Host CJStart → FA 层 E94 → 设 EXECUTING → EquipmentManager_FA 看到后调 `_auto.StartJob`；本地 Start 走 `System.StartJob` 只启调度层；远程模式 UI 会把一条命令同时发给 FA 和调度两套 |
| 执行 | JobManager 扫描固定 6 步：收结果 → 核对片位 → 料到了定片 → 推 CJ/PJ 状态和许可 → Scheduler.Dispatch → 发布；调度只认 TaskRow/WaferTask 与 TaskPermission | AutoTransfer 自己维护 `_lstControlJobs/_lstProcessJobs`，在各 `Monitor*Task` 里根据全局 `WaferInfo.ProcessJob / NextSequenceStep / ProcessState / SubstE90Status` 现算下一步，直接操作机器人/腔体/Buffer |
| 完成 | 任务行全回片 → PJ Finish → CJ Complete → EAP 回调和 DB 都从同一份对象出 | 调度层 `UpdateProcessJobStatus / UpdateControlJobStatus` 自己判完 → 回手调 `FAJobController.CheckPJComplete / CheckCJComplete` → FA 层再转状态、发事件；`Task.Delay(2000)` 异步补 |
| 暂停 | CJ 暂停=不再启动新 PJ，在跑 PJ 不受影响；PJ 暂停=不投新片、机内走完进 PAUSED，可恢复 | FA 层 E94 能改状态；PJ Pause 是空实现、Resume 返回 false；实际要停调度层得另发本地命令 |
| 停止/中止 | 同一个 `AbortAllAsync / Stop / Abort`；中止等设备确认、在途结束、片位确定才 #16；不归 CJ 的 PJ 也能收 | FA 层 CJStop/CJAbort 改状态；PJ STOP 没有对应的调度层同步；PJ ABORT 走 `fMonitorFAJob` 看到 ABORTING 才 `_auto.CancelJob`；中止路径两套状态可能对不齐 |
| 真源 | **JobManager 一份 CJ/PJ 对象**，界面、Host、存库、EAP 全从它出 | FA 层一份 E40/E94 对象、调度层一份 ControlJobInfo/ProcessJobInfo，靠轮询、事件（PJ 状态事件体是空的）和 Task.Delay 同步 |

---

## 3. 逐维度评分

> 10 分制，分数是工程视角的主观分，权重可调；默认权重偏平台化交付。

### 3.1 SEMI E40/E94 标准与命令（权重 14%）

- **本平台 9.0**：CJ 内部 9 态、PJ 12 态（含 PAUSING/STOPPING/ABORTING/STOPPED/ABORTED），转换号 #1~#18 明确并在上报与库里保留；命令按状态表拒绝并回错误码；E40/E94 组件完整解析 SECS、E39 对象/属性、E5 错误，Host 与本地同接口。
  - 扣分：HOQ 明确不支持；PJ 的 SetRecipeVariable / SetStartMethod 等未实现；部分选项（PauseEvent、非空 MtrlOutSpec）直接拒绝，需上机按 Host 实际用法确认。
- **CTC 4.0**：E40/E94 枚举和转换表都有，CEID 事件也全；但：
  - E40 `FsmEnterProcessComplete` 里直接 `PostMessage(JobTerminate)`（`E40_ProcessJobStateMachine.cs:193`），PROCESS COMPLETE / STOPPED / ABORTED 都是瞬时态，MaterialRemoved 分支实际上走不到，Host 查不到稳定终态；
  - PJ `Pause` 是空实现、`Resume` 直接 `ret=false`（`FAJobController.cs:893`、`FAHost.cs` PJ 命令段）；PJ 命令封装返回 `void`，FAJobController 一律回成功；
  - HOQ 返回 true 但什么都不做；SetRecipeVariable / SetStartMethod / DuplicateCreate 是空方法；
  - `E94_StateMachine.Check` 抛 `NotImplementedException`；E40 `Check` 恒 true；两边的 `CheckToPostMessage` 在 `return false` 后面写日志，日志永远执行不到。

### 3.2 本地/Host 同一入口与状态一致性（权重 11%）

- **本平台 10.0**：`IJobManager` 同时给 JobService（界面）和 E40/E94 组件用；本地和 Host 走同一对象、同一套检查、同一把锁、同一个事件源；没有第二份 Job 状态。
- **CTC 2.5**：FAJobController 和 AutoTransfer 各一套 CJ/PJ；本地界面走设备管理 → 调度，Host 走 FAHost → FAJobController → EquipmentManager_FA → 调度；同步靠 `fMonitorFAJob()` 每拍轮询 + `Task.Delay(1500).ContinueWith(...)` / `Task.Delay(500)`（`EquipmentManager_FA.cs:186、191`）；`Instance_OnProcessJobStateChangeEvent` 是空方法（`AutoTransfer.cs:236`）；MonitorJob 订阅了 FA 和调度两套数据，代码里只显示 Scheduler.\*（FA 分支被注释掉）。状态分叉时 Host 和界面看到的不一定是同一份。

### 3.3 Job/任务模型与调度解耦（权重 11%）

- **本平台 9.5**：一片一行 TaskRow + WaferTask 链（Pick / Place / Process / ）显式记录 Waiting / Running / Done / Error / Cancelled、实际站点槽位、机械手/手臂、错误码；站点用 `ITransferStation.SupportedTasks` 声明能力；Scheduler 只认任务和 `TaskPermission`，取放交 TransferManager（Job、手动、人工恢复公用一个执行口）；暂停/停止不写进调度，只改许可。
- **CTC 5.0**：没有显式任务表；靠全局 `WaferInfo.ProcessJob / NextSequenceStep / ProcessState / SubstE90Status` 加模块状态，在各 `Monitor*Task` 里现算下一步；任务推进、机械手取放、PM 选择、Buffer 逻辑全在一个 4724 行类里，任务级的这一步是谁、做到哪、错在哪没有落点，错误只能看报警和模块状态。

### 3.4 可靠性与并发（权重 12%）

- **本平台 9.0**：Job 一把 `_gate` 锁串行化命令与扫描；命令 `Monitor.TryEnter` + `CommandTimeoutMs`；扫描固定 6 步；搬运失败区分没动手/动过手；有慢扫描 warn/alarm；存库单独 Channel，不占扫描线程。扣分：上机实测还是空白。
- **CTC 4.5**：目标机型现场长期跑过，流程细节是实战出来的；但 Job/调度三个关键文件里 **0 个锁、AutoTransfer 0 个 try/catch**，`Task.Delay(...).ContinueWith` 在线程池上直接改共享 Job 列表；大量全局单例；命令失败也可能 ack 成功。现场能跑不等于并发和异常路径安全。

### 3.5 异常恢复与人工处理（权重 8%）

- **本平台 8.5**：任务级 Error 只停自己那一行，别的片照常；`RetryTask / CompleteTask` 支持重做或人工标记完成；动过手的搬运失败保持资源与站点交互，等人工核对后释放；中止等设备确认、在途结束、片位确定；整机停止走 Job 中止，不直接拍模块。缺整体回片（用户明确先不做）。
- **CTC 6.0**：有 ReturnAllWafer（727 行）、报警处理、Hold/Release、E90 跟踪；但没有任务级错误态/重做入口；`AbortProcessJob` 直接从列表删 PJ（`AutoTransfer.cs:4550`），不清 WaferInfo 的 ProcessJob/NextSequenceStep 引用，异常收场更依赖现场经验。

### 3.6 持久化 / 重启 / 历史（权重 7%）

- **本平台 8.0**：control_job、process_job 两表，一 Job 一行，PJ 行里带每片任务明细 JSON；后台 Channel 合并写；重启把上次没做完的 CJ/PJ 记成中止（E94 #12/#13、E40 #16），不接着跑；工程上更完整。缺 Job 历史查询页/移动历史报表。
- **CTC 6.5**：cj_data / pj_data / process_job_data / job_move_history 加统计计数，历史报表更成熟；但字符串拼 SQL、只记起止时间和数量、没有每片任务明细，也没有重启收场逻辑（重启后内存 Job 直接丢）。

### 3.7 HMI / 可观测（权重 7%）

- **本平台 7.0**：主界面 LoadPort 页签能逐槽选 Sequence、按配方分组建多个 PJ、一个 CJ、LotID、启动 Job；JobService 的全貌 DTO 带每片每格任务状态和错误码。扣分：Job 列表/详情/重做/标记完成界面还没做。
- **CTC 5.0**：MonitorJob 页有 CJ/PJ 两张表和各操作按钮，但只显示调度层 Job，FA 订阅那段注释，很多按钮也注释，没有片/任务明细，建 Job 在别的页；现场主要靠它看有没有 Job 在跑。

### 3.8 配置化 / 机型适配（权重 7%）

- **本平台 8.5**：Job/Scheduler 是平台件；机型差异在 sc.xml 的 Task 子类（35021 TaskComponent）和站点的 SupportedTasks / StartTask；Job 代码里没有 ToolType 分支。
- **CTC 3.5**：AutoTransfer 里 121 处 SC，配置不少但散在每机型约 1.6MB 的 sccfg；`_toolType` 在代码里硬编码 SPMHK / CU2 / GTX 分支；AutoTransfer 4 份副本哈希都不同，FAJobController/FAHost/E40/E94 各 2 份，改一处要同步多处。

### 3.9 测试与可验证性（权重 8%）

- **本平台 9.5**：JobSmoke 1905 行，一个进程推扫描；本次实测 `PASS: 514 job checks`，覆盖建 Job 的各种拒绝、任务顺序、取放确认、多机械手共享、暂停/恢复、停止/中止、任务出错重做/标记完成、料没到先建 PJ、整机停止、存库与重启收场。唯一缺口：上机/真驱动验证。
- **CTC 1.0**：Job/调度没有自动化测试工程（只有拾放 cycle test routine 之类），改动靠现场回归。这也是双 Job 系统能长期带病跑的根本原因之一。

### 3.10 可维护性 / 文档（权重 7%）

- **本平台 8.5**：19 个文件按 CJ/PJ/状态机/任务/调度/发布拆开；公开成员有 XML 注释；decisions.md/backend.md 有取舍记录。扣分：JobManager 2531 行偏大、命令方法有重复。
- **CTC 3.0**：AutoTransfer 4724 行一个类、FAJobController 1604 行；注释率约 4.8% / 2.6%，状态机几乎 0；注释掉的分支和死代码多；E40/E94 模型在 Common 和 FACore 各有一份；按机型复制源码。维护成本高。

### 3.11 现场生产功能覆盖（权重 8%）

- **本平台 4.0**：按用户要求先简化：一个 LoadPort 同时一个 CJ、不做循环 / 并行 / RCM / 回收片 / 整体回片 / 分选；现场要多机并行、循环跑、返工片时还要补。
- **CTC 9.0**：CycleMode、RunInParallelMode、RCM wafer、Recycle / Chemical 统计、ReturnAllWafer、Sorter 分支、统计计数、历史报表、运行中 Unload&Load 循环等都有；虽然代码不好读，但现场覆盖广。

---

## 4. 加权总分

### 4.1 默认口径：平台化交付（可复用、可维护、可验证）

| # | 维度 | 权重 | 本平台 | CTC |
|---|---|---:|---:|---:|
| 1 | SEMI E40/E94 标准与命令 | 14% | **9.0** | 4.0 |
| 2 | 本地/Host 同一入口、状态一致性 | 11% | **10.0** | 2.5 |
| 3 | Job/任务模型与调度解耦 | 11% | **9.5** | 5.0 |
| 4 | 可靠性与并发 | 12% | **9.0** | 4.5 |
| 5 | 异常恢复与人工处理 | 8% | **8.5** | 6.0 |
| 6 | 持久化/重启/历史 | 7% | **8.0** | 6.5 |
| 7 | HMI/可观测 | 7% | **7.0** | 5.0 |
| 8 | 配置化/机型适配 | 7% | **8.5** | 3.5 |
| 9 | 测试与可验证性 | 8% | **9.5** | 1.0 |
| 10 | 可维护性/文档 | 7% | **8.5** | 3.0 |
| 11 | 现场生产功能覆盖 | 8% | 4.0 | **9.0** |
|  | **加权总分** | 100% | **84.9** | **44.7** |

### 4.2 敏感性口径：现场设备能力优先

权重重排：标准 8%、单一真源 6%、任务模型 8%、可靠性 10%、恢复 11%、持久化 8%、HMI 8%、配置 5%、测试 4%、维护 8%、现场功能 24%。

| 视角 | 本平台 | CTC | 差距 |
|---|---:|---:|---:|
| 平台化交付（默认） | **84.9** | 44.7 | +40.2 |
| 现场设备能力优先 | **75.6** | 55.2 | +20.4 |

结论：两种口径本平台都领先；CTC 的分数集中在现场功能多、跑得久，本平台的分数集中在单一真源、标准可查、可测试、可维护。本平台最大的风险不是设计，而是**还没上机**；CTC 最大的教训是**不要学双 Job 系统**；这一条已经在 decisions.md 里明确避开。

---

> 功能专项打分另见 doc/Job-功能对比-打分.md：标准功能优先 76.1 vs 69.9，现场功能优先 67.9 vs 74.9。

## 5. CTC 侧查到的代码级问题（按严重度）

1. **双 Job 系统**：FA 层 `FAJobController._processJobs/_controlJobs`（E40/E94 对象）与调度层 `AutoTransfer._lstControlJobs/_lstProcessJobs`（ControlJobInfo/ProcessJobInfo）各一套；本地路径只建调度层，Host 路径两边都建，靠轮询 + Task.Delay 对状态。
2. **Host PJ 命令假成功/空实现**：`PJPauseing/PJResume` 是空壳（`FAJobController.cs:893`）；E40 命令封装返回 `void`，FAJobController 一律回成功；`RESUME` 在 FAHost 直接返回 false；`HOQ` 返回 true 但不做任何事；SetRecipeVariable / SetStartMethod / DuplicateCreate 空。
3. **E40 终态不落地**：`FsmEnterProcessComplete` 立即 `JobTerminate`（`E40_ProcessJobStateMachine.cs:193`）；`FsmEnterAborted/Stopped` 也立即 `JobTerminate`；MaterialRemoved 分支走不到；PJ 完成即从字典删除，Host 查不到历史终态。
4. **状态机检查不完整**：E94 `Check` 抛 `NotImplementedException`（`E94_StateMachine.cs:225`），E40 `Check` 恒 true；两边的 `CheckToPostMessage` 在 `return false` 后写日志，永远不执行。
5. **AutoStart/StartMethod 不生效**：`AutoTransfer.CreateProcessJob/CreateControlJob` 的 `isAutoStart` 参数全方法未引用；本地 `CreateJob` 的 autoStart 也没落到调度状态上。
6. **时间同步脆弱**：Host 建 Job 后靠 `Task.Delay(1500)` 等调度层建好，再 `Task.Delay(500)` 推进 FA 状态；调度完成靠 `Task.Delay(2000)` 回 FA 层（`EquipmentManager_FA.cs:186、191`，`AutoTransfer.cs:1483`）。机器一忙或线程池一堵，同步就错。
7. **无锁、无异常保护**：Job/调度三个关键文件 0 个 lock，AutoTransfer 0 个 try/catch；Timer 回调 + Task.Delay 线程池回调与扫描线程并发改 Job 列表。
8. **AbortProcessJob 清不干净**：只 `_lstProcessJobs.Remove(pj)`（`AutoTransfer.cs:4550`），不清 WaferInfo.ProcessJob / NextSequenceStep / 工艺状态，留下悬空引用。
9. **MonitorJob 死代码/双份**：`FAJobController.MonitorJob()` 被 EquipmentManager_FA 注释掉后重写一份（`EquipmentManager_FA.cs:24`），两份逻辑还会漂移。
10. **UI 只显示调度层 Job**：MonitorJobViewModel 里 `LocalProcessJobs/LocalControlJobs` 生效，FA 订阅分支被注释；Host 建的 Job 在 FA 层，页面看到的是调度层副本。
11. **两台/多机型源码复制**：AutoTransfer 4 份、FAJobController 2 份、FAHost 2 份、E40/E94 状态机各 2 份；四份 AutoTransfer 哈希都不同，属于已经分叉的副本。
12. **历史/持久化**：只用 `INSERT/UPDATE` 字符串拼 SQL 记起止时间和数量；没有每片任务明细；没有重启收场，重启后内存 Job 直接丢。

---

## 6. 本平台相对 CTC 的差距清单

### P0（上机 / 交付前）

1. **上机验证**：FCD / 机器人 / EAP 真机联调；重点跑 Host E40/E94 全命令、断线重连、命令并发、超时与重复请求。
2. **Job 监控/详情页**：后端 Snapshot/RetryTask/CompleteTask 接口已经齐全，界面上还缺 CJ → PJ → 每片任务（取/放/工艺/错误/重做/标记完成）的页面；建议照 decisions.md 的样稿 v2 做。
3. **异常场景上机演练**：工艺失败停一行、搬运动过手失败留锁、整机停止走 Job 中止、EAP 起/停时载具补报、重启收场。

### P1（上机后按现场需求）

4. **历史与统计**：现在一 Job 一行、保留终态，够追溯这个 Job 最后怎么样；如果要 CTC 那种 cj_data/pj_data/job_move_history 报表、处理片数统计、操作审计，再按现场报表格式加。
5. **现场便利功能**：循环模式（CycleMode）、多 CJ/并行调度、RCM / 回收片、整体回片（用户已明确先不做）、运行中 Unload&Load 循环、分选/回片流程；建议按机型 SC 开关逐项加，不要一次全上。
6. **Host 选项补齐**：按 Host 实际下发的 E40/E94 属性逐项确认，例如 PJ 暂停事件、MtrlOutSpec 非空、SetRecipeVariable/SetStartMethod、HOQ；要么支持，要么像现在这样明确回不支持。
7. **SECS 仿真回归**：把 JobSmoke 扩到经 E40/E94 组件的报文级，形成 Host 侧自动回归。

### P2（持续整理）

8. **JobManager 拆文件/减重复**：命令方法模式重复度高（用户已选择显式写法，不急着动）；等 Job 监控页落地后再考虑按 PJ 命令/CJ 命令/任务恢复拆 partial。
9. **Job 页操作手册/培训材料**：代码侧有 decisions/backend，界面侧的操作说明（怎么建 Job、怎么处理错误任务、怎么收场）还缺一页现场文档。

---

## 7. 一句话结论

本平台的 Job 比 CTC 的 FA Job + 调度 Job 双系统干净得多：**一份真源、显式任务表、状态机可查、命令不假成功、有 514 项自动回归**；CTC 值得学的只有现场功能覆盖（循环、并行、RCM、回片、历史统计）和跑得多。接下来优先把 Job 监控页和上机验证补上，再按现场需求加功能；不要回头学 CTC 的双 Job 同步。
