# Job 功能专项对比打分：本平台 vs CTC（FinalClean2）

- 对比日期：2026-10-10
- 范围：**只看功能**（能不能做、做到什么程度、做完是什么效果），不把架构分层、代码质量、并发安全这些算进功能分；架构/工程视角的打分见 `doc/Job-评分-对比CTC.md`。
- 本平台依据：当前 `xyz.Modules.Job`、`Transfer`、E40/E94 组件、JobService、主界面；`tools\JobSmoke` 实测 **514 项检查通过**。
- CTC 依据：`300C\FinalClean2` 的 `AutoTransfer / FAJobController / FAHost / E40FA / E94FA / EquipmentManager_FA / MonitorJob`，以实际代码路径为准。
- 标记：`[有]` = 功能可用；`[部分]` = 有入口但受状态/路径限制，或只做了一半；`[无]` = 没有这个功能。

---

## 1. 功能打分总表

功能分 10 分制，权重两套：一套偏标准功能优先，一套偏现场功能优先。

### 1.1 标准功能优先（偏 SEMI 命令、生命周期、可交付）

| # | 功能维度 | 权重 | 本平台 | CTC |
|---|---|---:|---:|---:|
| 1 | 标准状态与命令（E40/E94） | 18% | **9.0** | 4.5 |
| 2 | 建 Job / 配方 / 任务生成 | 15% | **9.0** | 8.0 |
| 3 | 执行调度 / 物料搬运 | 15% | 8.0 | **9.0** |
| 4 | 生命周期命令（暂停/恢复/停止/中止/取消） | 10% | **9.5** | 4.0 |
| 5 | 料 / 载具 / 晶圆管理 | 10% | **8.5** | 8.0 |
| 6 | 现场生产功能（循环/并行/RCM/回收/回片/分选） | 12% | 2.0 | **9.5** |
| 7 | 异常恢复 / 人工处理 | 8% | **8.5** | 6.0 |
| 8 | 历史 / 追溯 / 统计 | 6% | 5.5 | **7.5** |
| 9 | UI / 操作功能 | 4% | 6.0 | **6.5** |
| 10 | Host 属性 / 选项面 | 2% | **7.5** | 5.0 |
|  | **加权总分** | 100% | **76.1** | **69.9** |

### 1.2 现场功能优先（循环、并行、RCM、回收、整体回片、分选这些权重加大）

| # | 功能维度 | 权重 | 本平台 | CTC |
|---|---|---:|---:|---:|
| 1 | 标准状态与命令（E40/E94） | 12% | **9.0** | 4.5 |
| 2 | 建 Job / 配方 / 任务生成 | 12% | **9.0** | 8.0 |
| 3 | 执行调度 / 物料搬运 | 15% | 8.0 | **9.0** |
| 4 | 生命周期命令 | 10% | **9.5** | 4.0 |
| 5 | 料 / 载具 / 晶圆管理 | 10% | **8.5** | 8.0 |
| 6 | 现场生产功能 | 25% | 2.0 | **9.5** |
| 7 | 异常恢复 / 人工处理 | 8% | **8.5** | 6.0 |
| 8 | 历史 / 追溯 / 统计 | 6% | 5.5 | **7.5** |
| 9 | UI / 操作功能 | 2% | 6.0 | **6.5** |
|  | **加权总分** | 100% | **67.9** | **74.9** |

### 1.3 两张表怎么看

- **标准功能优先：本平台 76.1，CTC 69.9。** 本平台把 SEMI 命令、暂停/恢复/停止/中止语义、建 Job 校验、任务级错误处理做扎实了；CTC 在这几块的命令很多是空壳或半接通。
- **现场功能优先：本平台 67.9，CTC 74.9。** CTC 在车间里真正会用到的东西多：循环模式、并行/重叠投片、RCM/回收片、整体回片、分选/跨载具搬运、Buffer/冷却/对准、历史统计；这些本平台目前基本没有或只保留了接口。
- 两边都丢分的项：HOQ、配方变量、SetStartMethod、PauseEvent、Wafer 级 material type、MtrlOutSpec（本平台明确拒绝，CTC 只在部分路径收下）。

---

## 2. 功能差异总览

### 2.1 双方都有、但效果/语义不同的

| 功能 | 本平台 | CTC | 功能差异 |
|---|---|---|---|
| CJ/PJ 标准状态 | [有] | [有] | 本平台终态稳定、内部 PAUSING/STOPPING/ABORTING 可等设备收尾；CTC E40 完成/停止/中止会立即回 NONE，Host 查不到稳定终态 |
| CJ Start | [有] | [部分] | 本平台 Host/本地同一入口；CTC Host 只改 FA 层状态，要等 EquipmentManager_FA 轮询后同步给调度层 |
| CJ Pause / Resume | [有] | [部分] | 本平台只停启动新 PJ，在跑的 PJ 不受影响，可恢复；CTC FA 层改状态，调度层要 UI 再发一条本地命令，实际是否停住取决于路径 |
| CJ Stop / Abort | [有] | [部分] | 本平台带 SaveJobs/RemoveJobs、等所有 PJ 收尾、中止等设备确认；CTC 直接进 COMPLETED，Action 不解析，调度层同步不完整 |
| CJ Cancel / Deselect | [有] | [部分] | 本平台按 E94 #2/#4 删除或退回队列；CTC Cancel 只在 QUEUED 生效，Deselect 只改 FA 层 |
| PJ Start | [有] | [有] | 都有 |
| PJ Pause / Resume | [有] | [无] | 本平台停投新片、机内走完进 PAUSED，可恢复；CTC Pause 是空方法，Resume 直接返回 false |
| PJ Stop | [有] | [部分] | 本平台不再投片、机内走完、未投记未执行；CTC 只把 FA 状态改到 STOPPING，调度层没有对应处理 |
| PJ Abort | [有] | [部分] | 本平台中止在途、等设备确认、等片位确定；CTC 到 ABORTING 后由 Monitor 调 CancelJob，收尾语义不完整 |
| PJ Cancel | [有] | [部分] | 本平台按 E40 #18 删排队 PJ；CTC PJCancel 实际调 AbortPJ，Dequeue 直接 Remove |
| AutoStart | [有] | [无] | 本平台 SC ProcessJobAutoStart / ControlJobAutoStart 生效；CTC CreateProcessJob / CreateControlJob 的 isAutoStart 参数全方法没引用 |
| 料没到先建 PJ | [有] | [有] | 都有；本平台定不了片会报警并留在排队，CTC 静默等 CarrierID + SlotMap 验证 |
| 料到自动定片/生成任务 | [有] | [有] | 都有；本平台显式生成每片任务，CTC 只登记 SlotWafers，任务没有落点 |
| 建 Job 校验 | [有] | [部分] | 本平台查片归属、同载具槽冲突、回片槽可用、站点是否支持任务；CTC 查到片状态/Idle 为主，PJ 之间的片归属和任务能力检查弱 |
| 配方快照 | [有] | [部分] | 本平台建 PJ 时存流程配方和工艺配方快照；CTC 建时读 SequenceInfo、本地路径写 PM，运行后再读文件 |
| 回片 | [有] | [部分] | 本平台来源口在最后一步就回原槽，否则可回最后一步别的 LoadPort 同号槽；CTC 固定回原载具原槽 |
| 本地 / Host 状态一致性 | [有] | [部分] | 本平台一份 Job 对象给界面、Host、库、EAP；CTC FA 层和调度层各一套，本地/Host 路径不同，远程模式 UI 会双发命令 |
| 存库 | [有] | [部分] | 本平台一 Job 一行，PJ 行里带每片任务 JSON，重启把没做完的记中止；CTC 记 cj_data/pj_data 起止时间和数量，重启丢内存 Job |
| Job 查询 | [有] | [部分] | 本平台 Snapshot 带每片每格任务；CTC FAHost 只能查 FA 层 Job，终态还常已被删 |
| Job 监控 UI | [无] | [有] | CTC 有 MonitorJob 页（CJ/PJ 列表和按钮，部分按钮/FA 数据显示被注释）；本平台目前只有主界面建 Job/启动 Job |
| 报警 | [有] | [部分] | 本平台定不了片报 MaterialUnusableAlarm；CTC 等到条件满足才建调度 Job，中间没有对应报警 |

### 2.2 本平台有、CTC 没有或明显更弱的

- 任务表：一片一行，显式 Pick / Place / Process / ... 任务链和 Waiting / Running / Done / Error / Cancelled 状态，界面和 EAP 都能看到。
- 任务级错误处理：工艺失败只停这一行，别的片照常跑；可重做或人工标记完成。
- 取片/放片分开重试：取片确认后放片失败，可以只重试放片，不用重复取片。
- 片级归属检查：同一片不能被两个没结束的 PJ 抢；同载具槽冲突和回片槽预定都查。
- 料用不了报警：Host 先建的 PJ 在载具到了但定不了片时报 MaterialUnusableAlarm，PJ 留排队。
- Host 命令按状态拒绝并回错误码：不像 CTC 的 PJ 命令一律 ack 成功。
- PJ/CJ 暂停、恢复、停止、中止语义完整：CJ 暂停只停启动新 PJ，PJ 暂停停投片、机内走完，停止不可恢复，中止等设备确认。
- 单片级持久化 + 重启收场：每片任务明细跟着 PJ 行存；重启把没做完的 CJ/PJ 记成中止，不接着跑。
- 本地和 Host 同一份状态：界面看到的就是 Host 操作的，库也是同一份。
- 自动回归：JobSmoke 当前 514 项检查，覆盖建 Job 拒绝、任务顺序、多机械手、暂停/恢复/停止/中止、出错重做、料没到先建、整机停止、存库和重启收场。

### 2.3 CTC 有、本平台没有或明显更弱的（按现场需要补）

- 循环模式：CycleMode / CycleCount / 每个循环 Unload&Load 重复跑一篮片，带 cycle 统计。
- 并行模式 / 重叠投片：IsRunInParallelMode、WaferCountBelowWhichStartNewProcessJob、多 CJ 同时在跑。
- RCM / Dummy / 回收片：WaferInfo 有 RCM、Dummy、Recycle/Chemical 相关处理。
- 整体回片：ReturnAllWafer 一整套（本平台按用户决定先不做）。
- 分选 / 跨载具搬运：MtrlOutSpec、Sorter 分支、BidSortJob，能把片从源载具按槽搬到目标载具。
- Buffer / 冷却 / 对准 / 手臂选择：AutoTransfer 里 Buffer 进出槽、冷却时间、AlignerAngle、ArmSelection、PreRecipe。
- 随机取片顺序：CTC 支持 FromToptoBottom / Random / BySystem；本平台只有 LoadPort 的 TopDown / BottomUp。
- 配方变量和 PM 排除：RecVariableList、EXCLUSION、PM group 书写校验。
- 本地建 Job 时写 PM 配方：CTC 会在建 Job 阶段 WriteRecipeData 并校验 PM group；本平台由站点 StartTask 执行时处理。
- 历史 / 统计：cj_data、pj_data、process_job_data、job_move_history、处理片数/CJ/PJ/cycle time 统计。
- Job 监控页：CTC 有 CJ/PJ 列表和 Pause/Resume/Stop/Abort 按钮（虽然只显示调度层、部分按钮注释）。
- 完成后弹窗、运行中 Unload&Load 循环。
- Host 属性面：DuplicateCreate、SetRecipeVariable、SetStartMethod、PauseEvent、MtrlOutSpec、Wafer 级 MaterialType 等在 CTC 至少有入口（不少是空实现或半实现）。
- 手动/自动/人工恢复统一执行口：这一项反过来是本平台强，列在这里提醒 CTC 的做法不要学。

---

## 3. 功能逐项矩阵

### 3.1 标准命令与生命周期

| 功能 | 本平台 | CTC | 说明 |
|---|---|---|---|
| 建 PJ（E40 S16F11） | [有] | [有] | |
| 批量建 PJ（S16F15） | [有] | [部分] | 本平台逐个建、建不成的报错、建成的留；CTC 有多处索引/返回不完整 |
| 复制建 PJ | [无] | [无] | 双方都没真正实现，CTC 是空方法 |
| PJ Start | [有] | [有] | |
| PJ Pause | [有] | [无] | 本平台进 PAUSING 等机内走完；CTC 空实现 |
| PJ Resume | [有] | [无] | 本平台回暂停前状态；CTC Host 直接返回 false |
| PJ Stop | [有] | [部分] | 本平台机内走完、未投记未执行；CTC 只改 FA 状态 |
| PJ Abort | [有] | [部分] | 本平台等设备确认和片位确定；CTC 由监控回手 CancelJob |
| PJ Cancel | [有] | [部分] | 本平台 E40 #18；CTC 实际调 Abort |
| PJ Dequeue | [有] | [部分] | 本平台 S16F17 删排队 PJ；CTC 直接 Remove，没有 E40 转换 |
| 建 CJ（S14F9） | [有] | [有] | |
| CJ Start | [有] | [部分] | |
| CJ Pause / Resume | [有] | [部分] | |
| CJ Cancel / Deselect | [有] | [部分] | |
| CJ Stop / Abort | [有] | [部分] | 本平台 Action 生效、等 PJ 收尾；CTC 直接 COMPLETED |
| CJ HOQ | [无] | [无] | 本平台明确拒绝；CTC 回成功但不重排 |
| AutoStart（PJ/CJ SC） | [有] | [无] | CTC isAutoStart 参数没引用 |
| SaveJobs / RemoveJobs | [有] | [无] | CTC E94 命令没解析 Action |
| E40/E94 转换号与事件 | [有] | [有] | 本平台终态稳定；CTC 事件多但部分实际发不出 |
| Job 列表 / 属性 | [有] | [部分] | |
| Job space | [有] | [有] | 本平台不限；CTC 100 减当前数 |
| 命令失败错误码 | [有] | [部分] | CTC PJ 侧一律 ack 成功 |
| 配方变量 | [无] | [部分] | 本平台明确拒绝；CTC 只有少数字段被用到 |
| SetStartMethod | [无] | [无] | 双方没有实际实现 |
| PauseEvent | [无] | [部分] | 本平台拒绝；CTC 解析保存但调度没用 |
| Wafer 级 MaterialType | [无] | [部分] | 本平台只支持 Carrier；CTC 枚举有 CARRIER/WAFER，调度实际按载具 |
| MtrlOutSpec | [无] | [部分] | 本平台非空直接不支持；CTC 分选路径用到 |
| CarrierInputSpec / DataCollectionPlan | [无] | [部分] | 双方基本都不看 |

### 3.2 建 Job / 配方 / 任务生成

| 功能 | 本平台 | CTC | 说明 |
|---|---|---|---|
| 本地按槽选 Sequence 建 Job | [有] | [有] | |
| 一个载具多个 Sequence 分多个 PJ | [有] | [有] | 本平台固定按 Sequence 分组；CTC GroupWaferBySequence 可开关 |
| 一个 CJ 收多个 PJ | [有] | [有] | |
| 只收已有 PJ 建 CJ | [有] | [有] | |
| 料没到先建 PJ | [有] | [有] | |
| 料到自动定片 / 生成任务 | [有] | [有] | 本平台生成任务表；CTC 只登记 SlotWafers |
| 槽图 Host 认定后才取放 | [有] | [有] | |
| 片有片 / 正常 / 没做过检查 | [有] | [有] | |
| 片归属检查 | [有] | [部分] | 本平台 OwnerOf；CTC 主要看 ProcessState=Idle |
| 同载具槽冲突检查 | [有] | [部分] | 本平台 CheckSlotsFree |
| 回片槽规则 | [有] | [有] | 规则不同，见 2.1 |
| 回片到别的 LoadPort | [有] | [无] | 本平台支持；CTC 回原载具 |
| 回片槽占用 / 预定检查 | [有] | [部分] | |
| 流程配方快照 | [有] | [部分] | |
| 工艺配方快照 | [有] | [无] | 本平台存 ProcessRecipeData；CTC 运行时再读 |
| PM 配方校验 / 写腔体 | [无] | [有] | CTC 本地建 Job 会 WriteRecipeData 并校验 PM group |
| 站点支持任务校验 | [有] | [部分] | 本平台 SupportedTasks；CTC 靠模块存在和 recipe 解析 |
| 站点组 / 候选站点 | [有] | [有] | |
| TopDown / BottomUp 取片 | [有] | [有] | |
| Random / BySystem 取片 | [无] | [有] | CTC 支持 |
| 配方变量 / EXCLUSION | [无] | [有] | CTC SequenceInfoHelper 使用 |
| Buffer 进出槽 | [无] | [有] | |
| AlignerAngle | [无] | [有] | |
| ArmSelection | [无] | [有] | |
| PreRecipe / 冷却时间 | [无] | [有] | |

### 3.3 执行调度 / 物料搬运

| 功能 | 本平台 | CTC | 说明 |
|---|---|---|---|
| 每片任务链可见 | [有] | [无] | 本平台 TaskRow/WaferTask；CTC 只有全局 wafer 状态 |
| 任务级状态 | [有] | [无] | Waiting / Running / Done / Error / Cancelled |
| 取片确认后进放片格 | [有] | [部分] | 本平台显式；CTC 在模块任务里现算 |
| 放片失败只重试放片 | [有] | [无] | 本平台 JobSmoke 覆盖 |
| 多机械手 / 多手共享 | [有] | [部分] | 本平台 TransferManager 统一占资源；CTC 逻辑分散 |
| 双机械手并行服务不同站点 | [有] | [有] | |
| 同一站点互斥 | [有] | [有] | |
| PM / 腔体选择 | [有] | [有] | |
| Buffer 调度 | [无] | [有] | 本平台现机型没有 Buffer 站点 |
| 冷却等待 | [无] | [有] | |
| 对准角度 | [无] | [有] | |
| 多片并行加工 | [有] | [有] | |
| 回片 | [有] | [有] | |
| 整体回片 | [无] | [有] | ReturnAllWafer；用户已明确先不做 |
| 分选 / 跨载具搬运 | [无] | [有] | Sorter / MtrlOutSpec / BidSortJob |
| 循环模式 | [无] | [有] | CycleMode / CycleCount / Unload&Load |
| 并行模式 / 重叠下一个 Job | [无] | [有] | IsRunInParallelMode / WaferCountBelowWhichStartNewProcessJob |
| RCM / Dummy / 回收片 | [无] | [有] | |
| 手动和 Job 共用一个执行口 | [有] | [无] | 本平台 TransferManager；CTC AutoTransfer/ManualTransfer 分开 |
| 手动动作不跟 Job 挂钩 | [有] | [有] | |

### 3.4 异常恢复 / 人工处理

| 功能 | 本平台 | CTC | 说明 |
|---|---|---|---|
| 出错只停这一行 | [有] | [无] | 本平台任务级 Error；CTC 靠模块报警 |
| 错误任务重做 | [有] | [无] | RetryTask |
| 人工标记完成 | [有] | [无] | CompleteTask |
| 搬运没动手失败自动放锁 | [有] | [部分] | |
| 搬运动过手失败留资源等确认 | [有] | [部分] | |
| 中止等设备确认 / 在途结束 / 片位确定 | [有] | [无] | |
| 停止时未投片记未执行 | [有] | [部分] | |
| 料用不了报警并留队 | [有] | [无] | CTC 静默等待 |
| 设备停止带 Job 中止 | [有] | [有] | |
| 整机报警中止 Job | [有] | [有] | |

### 3.5 数据 / 历史 / UI / Host

| 功能 | 本平台 | CTC | 说明 |
|---|---|---|---|
| 实时存库（当前状态 + 每片任务） | [有] | [部分] | 本平台 process_job 行 JSON；CTC 只有起止/数量 |
| 重启收场（不接跑，记中止） | [有] | [无] | |
| 移动历史（每站到达/离开） | [无] | [有] | CTC job_move_history / wafer_move_history |
| 处理片数 / CJ / PJ / cycle time 统计 | [无] | [有] | CTC stats counters |
| 历史查询接口 | [无] | [部分] | CTC QueryJobMovement，页面另说 |
| Job 全貌查询 API | [有] | [部分] | 本平台 Snapshot/FindByCarrier；CTC 查询面有限 |
| 建 Job UI（逐槽 Sequence） | [有] | [有] | |
| 启动 Job UI | [有] | [有] | |
| Job 监控页（CJ/PJ 列表） | [无] | [有] | CTC MonitorJob 有页面，但只显示调度层，部分按钮注释 |
| Pause/Resume/Stop/Abort 按钮 | [无] | [部分] | 本平台后端有接口、界面没有；CTC 有按钮，部分注释/双发 |
| 任务详情 / 重做 / 标记完成 UI | [无] | [无] | 本平台后端接口已齐，缺页面 |
| 完成后弹窗 | [无] | [部分] | CTC 代码里有开关，但弹窗调用被注释 |
| Host 属性面（E39/E40/E94） | [有] | [有] | 本平台功能可用但选项少；CTC 面宽但很多空实现 |
| Host 命令错误码 | [有] | [部分] | |

---

## 4. 建议补齐顺序（按功能）

### P0（上机/交付前必须有）

1. 上机验证 E40/E94 全命令、断线、超时、重复请求、并发；把 JobSmoke 的场景在真机复跑一遍。
2. Job 监控页：CJ -> PJ -> 每片任务，带 Stop / Pause / Resume / Abort / Retry / Complete 按钮；后端接口已经齐全。
3. 随机取片顺序（Random / BySystem）如果现场需要，可先做 EC 开关。

### P1（上机后按现场需求逐项加）

4. 循环模式（CycleMode / CycleCount / 循环 Unload&Load）。
5. 并行/重叠投片（多 CJ 或 WaferCountBelowWhichStartNewProcessJob）。
6. 整体回片 ReturnAllWafer（用户之前说先不做，等现场要再说）。
7. RCM / Dummy / 回收片。
8. 分选/跨载具搬运（MtrlOutSpec / Sorter）。
9. Buffer / 冷却 / 对准角度 / 手臂选择（大概率要实现成机型 TaskComponent 或站点能力）。
10. 历史/统计：cj_data/pj_data/job_move_history、处理片数、cycle time；先定义现场报表格式。
11. Host 选项：DuplicateCreate、SetRecipeVariable、SetStartMethod、PauseEvent、Wafer 级 MaterialType 按现场实际下发情况逐项确认，明确支持或明确拒绝。

### P2（持续整理）

12. 完成后弹窗、运行中 Unload&Load 循环等便利功能。
13. Job 页操作手册和现场培训材料。

---

## 5. 一句话结论

**纯功能上不是一边倒：标准功能优先本平台 76.1 对 CTC 69.9，本平台略胜；现场功能优先 CTC 74.9 对 67.9，CTC 反超。** 本平台强在标准命令、生命周期语义、建 Job 校验、任务级错误恢复和单一状态源；CTC 强在循环、并行、RCM、回收、整体回片、分选、Buffer/冷却/对准和历史统计这些现场功能。下一步先把 Job 监控页和上机验证补上，再按现场实际需要从 P1 里挑功能加。