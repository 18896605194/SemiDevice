# 机型层、测试、运行和验证

## 1. 仓库顶层（`D:\Code`）

- `xyz.Core\`（平台：Shared / Service / Client）、`xyz.35021\`（机型 35021）、`tools\`（冒烟测试、部署和编码脚本、AdsRouter）、
  `doc\`（`AxisPlc.md` 轴和 PLC 数据块、`interlock-design.md` 联锁设计草案未实现、`eventbus-guide.html` 事件总线说明）、`Libs\`（TwinCAT.Ads DLL）。
  `website\`、`HotShot\` 跟框架无关。
- `xyz.Framework.sln`（41 个工程；`tools\IoIndexSmoke` 不在解决方案里，要单独编译）、`xyz.Framework.slnLaunch`（"Client + Service" 同时起宿主和客户端）。
- `Directory.Build.props`：win-x64、x64、输出路径不带 RID（机型部署、ScEdit 依赖这个路径）。包版本写在各 csproj 里。
- **编码**：源文件 UTF-8 **带 BOM**（.cs、.xaml、.csproj、.xml；skill 的 SKILL.md 例外，不加 BOM）。`.editorconfig` 本意如此，但它第 4 行的乱码注释把节头吞了，
  现在实际不生效；`tools\check-bom.ps1` / `add-bom.ps1` / `verify-encoding.ps1` 可以查和补。
  PowerShell 5.1 跑含中文的 .ps1 也要存成带 BOM 的 UTF-8，否则按 GBK 读会解析出错。
- Git：master；提交说明多数只写了 `1`，提交由用户自己做，**不要自作主张提交或推送**。

## 2. 机型层（以 `xyz.35021` 为例）

- 命名空间 `xyz._35021.*`（数字开头前面加下划线）。
- 后端 `Service\xyz.35021.Module`（→ xyz.Modules、xyz.Shared、xyz.35021.Shared）：
  - `Cds\IpaModule : BaseCdsModule`（IPA 供液，2026-10-18 只搭了架子：只推 `IpaDto` = IPA 的状态 + 下面各槽的 `TankDto`）；sc.xml 里 CDS → IPA → Tank1 / Tank2，
    点还没接（`DiIndex` / `DoIndex` 写 -1）。
  - `Loadport\LoadPortModule : BaseLoadPortModule`、`Robot\RobotModule : BaseRobotModule`、`Clean\ChamberModule : BaseChamberModule`、
    `Buffer\BufferModule : BaseBufferModule`（35021 设备上没有 buffer，sc 里没配，类留着放以后独有的东西）（接口基类已经实现，机型类不再写一遍），
    类上 `[Component(description: "...")]`；机械手的动作在 `Operation\` 目录（`XxxOperation : ModuleOperation<ActionStep>`，步骤 SendCommand / WaitCommand）。
  - **腔体的回零、复位、中止、工艺也在平台**（2026-10-10，照 sc 里挂的部件发轴和 IO，见 backend.md「腔体整腔动作」）：35021 的 `ChamberModule` 是空类，
    新机型照样建一个空类继承 `BaseChamberModule`，腔体里装什么只改 sc；设备做法不一样才重写 `CreateHomeOperation` / `CreateResetOperation` / `CreateAbortOperation` /
    `CreateProcessOperation` / `CreatePrepareTransferOperation`，要加条件重写 `HomeInterlock` / `ProcessInterlock` / `DeviceActionInterlock`（2026-10-20 跟 LoadPort、机械手统一）。
  - **LoadPort 的动作、状态查询、在位、重连、推送都在平台**（2026-10-06，用户："以后很多设备都要用"）：`BaseLoadPortModule` 的 7 个动作有默认实现
    （`LoadPortCommandOperation`：一条驱动指令一个动作），35021 的 `LoadPortModule` 是空的，**留着给机型扩展**（用户定的）——
    哪个动作不一样就重写那一个，平台没有的设备在这儿加。新机型照样建一个空类继承 `BaseLoadPortModule`。
  - **站点要中途交接**（顶针这类，分段取放）：站点类重写 `HandoffPickCore` / `HandoffPlaceCore` 起交接动作，sc 里给这个站开 `HandoffOnPick` / `HandoffOnPlace`；
    到这个站的机械手类要在类上声明 `ITransferRobotStep`、重写 `CreateExtend/RetractPick/PlaceOperation` 给伸臂缩臂的设备动作（没声明开机就报错），
    见 decisions.md「分段取放」。
  - **Job 不归机型**：CJ / PJ 和任务组件沿用平台标准件；不同设备需要不同派单策略时，在平台提供相应的 `ISchedulerComponent` 调度组件，机型在 sc.xml 的 `Scheduler` 选择 Type，
    对外契约统一。35021 当前仍用 `xyz.Modules.TaskComponent`、`xyz.Modules.SchedulerComponent`；`BaseSchedulerComponent` 是后续公共逻辑整理项，见 decisions.md「调度可以有多个」。
  - **后续项目再做（2026-10-18，本轮确认）**：浸泡模块留到对应新项目，继承 `BaseTransferStationModule` 实现按槽工艺；真实设备的分段取放指令和站点机构接入也暂缓。
  - `Config\IO\{DI,DO,AI,AO}.csv` 点表（表头 `Index,Module,Component,Name,Tag,Description`，AI/AO 加量程列）；跟 PLC 仿真器的
    `Machines\35021\IO` 是同一份，改了两边一起改。
  - `Config\sc.xml`：这台机的 sc.xml（2026-10-13 从平台 `xyz.Configs` 挪过来，见 backend.md §8）。新机型在自己工程的 `Config\` 放一份。
  - `DeployToHost`（AfterBuild）：把 DLL 拷到 `GrpcHost\bin\<配置>\net10.0\Modules\35021`，点表拷到宿主 `Config\IO`；
    `DeployScToHost` 把 sc.xml 拷到宿主 `Config\`（只在源文件比宿主那份新时拷，现场改过的不冲掉）。`tools\deploy-module.ps1` 也一样。
- `Shared\xyz.35021.Shared`（→ xyz.Shared）：机型错误码（`xyz._35021.Shared.Errors.ErrorCodes`，现在是空的）、机型推给界面的 DTO（`Dtos\IpaDto`）。
- 客户端 `Client\xyz.35021.Client`：`[ClientModule("35021", "...")] Module35021 : IClientModule`，`Register` 里注册机型页面
  （`Manual.LoadPorts`、`Manual.Robot` 两个 keyed 页面），`PresentationAssembly` 指向机型语言包；`DeployToShell` 拷到客户端 `Modules\35021`。
  `Client\Manual`（机型手动页，复用平台 xyz.Client.Manual 的控件）、`Client\Presentation\Localization`（机型独有文字）。
  主界面中间的"整机调度"35021 用平台默认的（照机械手站点表自动摆）；别的机型要不同摆法，在 `Register` 里
  `services.AddKeyedSingleton<UserControl, 自己的View>(ClientViewKeys.MainDispatch)` 换掉这一块（见 client.md §3 主界面）。
  `Client\Manual\Views\TransferView`（"Transfer 调度"）是早先的半成品，没注册、没菜单。
- 手动部署：`tools\deploy-module.ps1 -Module 35021 [-Target Client|Service|All]`。
- 规则：平台不引用机型；机型能做的就按平台的抽象做（继承 Base*Module、实现 I*），平台缺抽象就补到平台，别在机型里另起一套。

## 3. 冒烟测试（`tools\*Smoke`）

| 工程 | 管什么 |
|---|---|
| OperationWaitSmoke | 模块操作等待/超时/中止、LoadPort 动作和模式、E87/E84 交接、机械手取放改账（挂了在位传感器的手：没读到不伸手、取前要空放前要有、做完等传感器跟上才记账、对不上判失败不记账、Home 前账实不符拦、设备失败原样带码、急停一起打断）、报警只能人工复位、DI/AI 防抖、EC、InitComponent（不动硬件、逐级递归、一个失败不耽误别的、E84 和驱动自己连）/ InitModule（= Home，不碰子组件）/ Abort、一趟搬运出错时报的错误码和参数、站点交接上下文（对象身份认轮次、取放两段的工厂和参数检查、换段准备只认本轮两段且在交互中、同站换槽走换段）、中途交接（站点入口只认开关、本轮这一段、交互中，没重写扩展点的被拒；一趟搬运、同站换槽、交换按站点开关分段，交接被拒 / 没做成 / 停着时被中止的收场，机械手没声明 `ITransferRobotStep` 落标记前就拒；机械手基类不自带分段能力、没给分段动作的伸臂当场拒）；主界面要的后端（系统设置的 LoadPort / 机械手名单、站点类型、整机模块（全部初始化先机械手再其余、被拒 / 没做成回未初始化，Auto / Manual 当场切拨自动派单，Stop 停止中等手上动作收完尾回停之前的地方，全部回片回不去的记下来，没配整机回 system.not_installed）、切晶圆尺寸挡的几种：没配、尺寸不对、有模块初始化过、LoadPort 以外有片、写不了 sc.xml 不换）；EC 值一变 EC 组件自己推 `EcItemDto`；整机自己推设备总状态（第一拍推、没变化不推、在做动作是运行中、做完推回）；LoadPort 载具组件（`Carrier` 节点必配、缺了开机抛；在位二选一 Query 两位 / Event；同一载具已认定后再 Map 保持认定；自动读码发起不成功接着试、到读码超时报读码失败；没配读头不算失败）、状态查询超时作废重发、动作没做成作废在途指令、关连接作废、LoadPort / RFID 断线重连、RFID 连不上不连累 LoadPort、帧通讯重连时旧接收泵只停自己（假通道 + 真 FCD 驱动，`GatedTransport` 测接收泵）；机械手设备状态查询（假通道 + 真锐洁驱动：一圈的顺序（订阅、报错、伺服、速度、每根轴）、没订上每圈订一次、订上了不再订、超时作废后同名查询还能再发、回来了写到查的那根轴 / 那个状态上并接着查、迟到的"没有报错"回复不当报错推送、关连接作废在途指令、SC `WaferEventEnabled` 关了不订阅、断线按间隔重连上以后从头查并重新订阅、Close 以后不再重连、动作等回复超时作废那条指令后同一条还能再发）；LoadPort 复位 / 中止落的状态（出错复位、打断 Load / Unload / Home / 夹紧、查不到门位是 NotInit，门开着没在动回 Loaded、门关落 Idle）、LoadPort 动作入口（状态 → 资源 → 互锁，服务层被拒原样回错误码 + 参数：没初始化 / 已经 Load 好回状态不允许带状态码、驱动没连上当场拒但中止照样能顶掉在途动作、复位被拒再问原因、终态翻成回包；没载具、E84 交接没走完不 Load；片没回齐不让手动 Unload、门开着的 Home，没初始化出发的 Home 不查；机型重写各动作联锁回自己的原因；Job 干完的自动 Unload 走入口）；停用的端口也挂载具（扫描、推状态不抛）；Load 的 Mapping 检查（槽数对不上不落账、交叉 / 叠片 / 认不出的槽账照落，都判失败报 Mapping 异常）；Job 做完自动 Unload（等机械手回 Loaded、片没回齐不卸、只认刚干完、EC 关着不卸改成拿 Load 时的槽图对账——片回别的口不误报、换槽多片报 Mapping 异常）；E84 Auto 下没走交接放上 / 拿走载具报警（正常交接、Manual、第一拍、超时锁住都不报）；Unload 带 Mapping 对账（SC AutoRunMapOnUnload 只管自动跑货：先发 CULOD 关门、关好再主动发 CLDMP 扫，一致就成功；少片、多片、交叉片判失败、报 Mapping 异常和账实不符、账不动；关门没成不扫、扫图没成 Unload 判失败；手动 Unload 不吃这个 SC，只发 CULOD、不扫不判账） |
| WaferLedgerSmoke | 晶圆账装配、原子操作、事件、并发抢槽、流水落库、报警、人工移账/删账、账一变推轻通知（`WaferLedgerChangedDto`，带变了的站点）、账单调整服务、存盘和开机恢复（`Bind` 时恢复；恢复前不写、LoadPort 不恢复、加工中记中止、退出时 `OnStopping` 最后存一次） |
| JobSmoke | 搬运管理（受理时的各项检查、两次操作抢一个槽、取片确认、WaferTask 顺序执行、站点和账都收尾才确认完成放锁、忙时拒绝不排队、没动手失败放锁 / 动过手失败留锁等确认、中止等待设备确认；同腔交换：受理检查、一槽两手一次占齐、取完旧片站点不收尾、放新片失败或取完旧片后中止整条挂起只列一次、按任意一片放锁；分段取放：开机查站点开了交接、机械手没声明 `ITransferRobotStep` 就报错，放片 / 取片 / 交换都分段，伸着等交接时机械手停在取片 / 放片伸着、不收回原点和下使能，交接没做成 / 被拒 / 伸着时被中止都就地停下整条挂起，Job 照样跑完）、LoadPort 的资源检查（账上有片的槽被搬运占着、账上的片归没结束的 Job 不让 Load；片归 Job 也不让手动 Unload、门开着的 Home）和 Job（E94 CJ / E40 PJ：建 Job 的检查和整个不留（含站点不支持要用的任务、一站的站点都用不了、工艺配方不在库里）、本地建 Job 跟 Host 一样先建 PJ 再建 CJ（建 CJ 被拒撤掉已建的 PJ、重发被正常的检查拦住）、任务表（一片一行：取片、放片、工艺……回片）、一篮两个 Sequence 和两步加工、站点组、转换号顺序、配方快照、回到别的 LoadPort、PJ 暂停 / 恢复、CJ 暂停只不启动新 PJ、CJ 停止、PJ 中止等设备确认、工艺没做成只停那一片（重做 / 标记完成，别的片照常跑）、片位被人改了、Job 的取片 / 放片失败（错误落在当前任务，放锁后从该任务继续，放片重做不重复取片）、Host 先建 PJ 再建 CJ（EAP 按载具号找 CJ、PJ）、料没到先建 PJ（载具 Load 好、接了 EAP 时槽图被认定才定片，同一载具的槽 / CJ 不能重，定不了报警、PJ 留在排队）、整机自动运行跟着在跑的 Job、整机停止走 Job 中止（停止中等工艺收完尾回空闲）、全部回片把腔里的片搬回来源 LoadPort 原槽、调度的交换和预取（没有开关、看机械手的手：预取只取不放并留一只空手、一个站只预取一片、旧片没去处或许可不对称不预取、单手机械手不预取不交换、中止旧片所属的 PJ 不打断交换且等交换收尾才 #16）、手臂取放分开（站点表 PickArms / PlaceArms：配了机械手没有的手开机就抛、机械手取放入口和搬运挑手分开查、推给界面的两张手臂表、两片带预取和交换跑一遍：进腔都用 1 号手、出腔都用 2 号手）、buffer（两台机械手中间一个 10 层架子配成进片段、出片段两个站：开机待命上线、全部初始化回待命、连续跑 8 片不卡死、进出各走各段、料盒 ↔ buffer 用 A 和 buffer ↔ 腔用 B、B 从进片段预取并只在腔门口交换（A 在 buffer 上不换）、两段在机械手里各是一个站号，槽号原样发）、并行工艺站轮着用（`PreferIdleStation` 默认关：关着一片一片都进配方在前的 PM1，开着按腔体自己记的最近开工时间 PM2、PM1、PM2 轮着；腔体开工片数跟着加）、Job / 搬运服务的错误码、CJ / PJ 一行进库带每片任务明细、重启后 Job 不接着跑库里记成中止）；机械手、LoadPort、腔体都是假的，扫描由测试一拍一拍推 |
| SequenceSmoke | 流程配方库：sc.xml 节点和参数、站点分组（sc 分组节点 + 机械手站点表；只实现 `IProcessStation` 的热板分组也要选配方）、新建/改名/保存/删除的各项检查、文件读写（坏文件跳过）、版本冲突、变更事件、服务错误码、工艺步骤的配方要在工艺配方库里、Host 按名字列 / 取（JSON）/ 认 JSON 样子 / 存（新建或覆盖，检查跟本地一样）/ 删和上报口（改名报旧名删 + 新名建） |
| ProcessRecipeSmoke | 工艺配方库：Host 按名字列 / 取（JSON，不带存 XML 用的属性）/ 存 / 删和上报口、sc.xml 节点和两张字段表（配方头、步骤）、字段表配错开机就报、步骤下拉的选项按腔体 Type 照 sc.xml 写的（Type 从所有 `IProcessStation` 收，热板也算）、配方头的 Type 和超时时间、Type 对不上的腔体挡下、新建/改名/保存/删除和按字段表的每一条检查、规整写法、字段都写进文件（老配方缺的按默认值补）、坏文件跳过、版本冲突、变更事件、配方对不对得上具体腔体、服务和字段表、腔体起工艺要配方在库里且对得上、没装库；晶圆尺寸（位置、到的上限 Max.8 / Max.12 跟着切，切尺寸写回真 sc.xml 只改那一个值、LF / CRLF 来回一字不差、没写 Size 的照缩进补一行） |
| GemCollectorSmoke | SV/EC/ALID/CEID/DV 编号表生成、保号、停用、恢复 |
| DataCenterSmoke | 日志文件解析和历史查询、日志组件转发（接上之前攒的补进来、只留最近 N 条、每条推、日志服务从它取最近一段）、报警复位和报警历史（报出、清掉报警组件都推 `AlarmDto`）、数据曲线采样（在自己的扫描里：配了 IsRun 才采、整周期一行不重复、停下时最后一批写进库）、信号名（SV、IO；DI 组件就是组件路径，不带 `__Di`；一个液位计只记一列）和日表、实时曲线在自己的扫描里推帧（带这一轮的标识、值跟信号表一样多）、gRPC 服务按契约自动认（一个契约一个实现） |
| HsmsSmoke / SecsSmoke | HSMS 链路组件对假 EAP（按 S/F 分发、S9F3 / F5 / F7、SxF0、闸门、回完再做、断线重连、端口冲突）；SECS-II 编解码、HSMS 握手和计时器 |
| EapSmoke | EAP 各标准对假 Host：配方管理 S7（列、取、问能不能下、下、删、全删、新名字按 JSON 样子分库、REMOTE 才收、配方变了的事件和 DV、本地编辑锁）、E30（通讯建立、控制状态、SV / EC / DV / 事件名单、Host 改 EC、报告定义和 S6F11 带的值、按需要报告、报警 S5F1 和报警事件、缓存断线进缓存 / 按先后发 / 清掉）、Trace（S2F23 收下 / 周期、SVID、REPGSZ 不对的码、取消、同号替换、离线全停，S6F1 按包发、SMPLN 和值的个数、不满一包也发、TRID 原样带回）、E39（类型、属性名、带条件查属性）、E87（读到号等 Host、槽图一律等 Host、第二次 PWC 比对槽图和给片号、Load 好就在取放、取消、ReCreate、读码失败 Host 给号、E87 不自己卸、干完靠 CarrierRelease、端口说不能卸时放行回 CAACK=2、Host 启停用、存取方式、预告 / 绑定这些不支持的回 CAACK=1）、E90（片对象跟着账建、挪、做、跳过、删）、E40 / E94（建、命令、查询翻成 Job 管理的命令，状态转换报事件）；LoadPort（带一个假的 `ICarrier`）、Job 管理是假的，晶圆账是真的 |
| RfidSmoke | FCD RFID 协议、握手、超时（假读头） |
| LogPipelineSmoke | 日志队列、LogHelper、LogViewModel（WPF） |
| ChamberSmoke | 腔体：动作入口跟 LoadPort、机械手一样 状态 → 资源 → 互锁，被拒带原因（设备动作在途时回零、起工艺、别的设备动作回状态不允许带状态码；停用的设备动作、回零、复位、中止回 module.disabled，复位 / 中止被拒再问原因；槽号超出片位数）；组件初始化照 sc 认设备（停用的也认）；照 sc 认设备（门、Bowl、卡盘、摆臂和它下面的 Lift、喷嘴；到位反馈、流量开关是 DI 组件子节点）、推送跟 sc 一样的结构、气缸三态、喷嘴流量 AO、卡盘 / 摆臂 Reach 和配方坐标换算（8 寸 / 12 寸各用自己的边缘示教位和半径）、有变化才推；设备手动动作（找不到、参数不对、指令没发出去、Manual 状态、在途拒绝、停止忙时照发、Abort 顶替、失败落 Error、轴走一遍、点动按住 / 续 / 松手 / 没续上自己停、停用）；平台默认整腔动作：回零先后、复位、中止停液停轴、按配方做工艺（Lift / Bowl 升、转速、摆到位置、按药液开喷嘴给流量、Time / Scan、收尾，没步骤 / 配方对不上 / 转速超限 / 超时 / 中止）、两根轴配了同一个命令块开机报错；开工计数（开工就算、挡下的不算，开工时间戳用开机以来的计时，按天写进临时默认库，新的计数读回来今天接着数、累计把各天加起来，写库只给那一天加 1、库里那行改成 100 后再开工是 101 不冲掉）和工艺进度百分比（第 1 步、第 2 步开喷时的值，收尾停在 99，做完变空）；部件寿命（次数型挂在气缸下盯开侧 DO、气缸不改：第一拍不算、DO 每变一次算一次、没变不算、读不到不算且重新读到那拍只记下，到警告次数报 Warn、到报警次数只报 Alarm1，人工复位后还到着再报，按"加一"写库、重装读回接着数、库里改成 100 再变一次是 101，清零连库一起清，次数配 0 不报；时间型：第一次开机库里没有从开机算并存库，到警告时间报 Warn、到报警时间只报 Alarm1，重装读回起算时间、关机那几天也算，清零改库，时钟往回调到起算之前算 0，时间配 0 不报）；虚拟 IO（下面挂虚拟 DO 5000~5999，按点号读写，开机是断，没配的点号读写翻都不行，不下发 PLC、IO 组件不知道它、PLC 断了照样读写；次数寿命先问虚拟 IO 有没有这个点号，有就读它、翻一次算一次，没有照旧读真 DO；点号越界装配时报、重号开机 Bind 时报）；起工艺前看供液（腔体 SC `Supply` 配供液模块名、逗号隔开，`Bind` 按名字经 `ISupplyAccess` 取，不分大小写、去空格、重复只取一次，配了没有开机报、停用的腔体不取；有一个没就绪挡下 `chamber.supply_not_ready`、腔体不动，没配的不看，开工后供液掉了照样做完、下次开工再看；EC `SupplyCheckEnabled` 默认开、关了不看、再开当场又看；供液用只实现接口的探针组件装进树），假 PLC 模拟气缸、阀、AO 和轴 |
| CdsSmoke | 供液：槽（Tank）照 sc 认液位 DI 信号（LL / L / H / HH / Overfill，没挂 = 没有这个点）、触发值照配的电平、按配了的信号算液位（Overfill 是最上面一个，亮了就是溢流；中间缺点按到了的最高那个算）、读不到算不知道（Overfill 亮了照样是溢流；信号 DiIndex 配 -1 当没有这个点）、信号对不上 / 液位高高 / 溢流由槽报（各自的 EC 防抖：满时间才报、断了重新计时，液位本身不等）且只能人工复位清；五个功能（补液、排液、换液、供液、停止 / 中止）开关哪组阀、状态怎么走（开机空闲、就绪只能补液得到、就绪时液位掉了回空闲、停止直接回空闲、有任何报警（含下面阀的到位超时）就出错阀全关、复位回空闲）、排空延时中间 LL 又亮重新等、换液先排后补、补液 / 排液超时、补液时 HH 亮当场关阀、供液中低于 LL 报警、SC FillTo（L / H，配错开机抛）、开始前的检查和 `tank.*` 错误码；推送（槽跟着 IPA 模块推 `IpaDto`：IPA 的状态 + 下面各槽的 `TankDto`，token 模块名、留存，有变化（第一拍也算）才推、没装映射组件不推；信号带名字和触发状态、没配是 null、读不到推 null，扫描外改的状态下一拍推；AI 液位信号经映射组件转成一样的信号）；按 `ISupplyModule` 找得到 IPA；能不能供液（按 `ISupplyAccess` 找得到 IPA；没初始化不算、空闲 + 有槽就绪才算，槽在扫描外变的下一拍才算）；引用机型模块（IPA 在机型层）；假 PLC 摆 DI、记 DO |
| AiSmoke | AI 助手：`Ai` 节点配错开机就报（地址没配 / 不是 http(s)、没配模型名、同时几个 < 1）；按 `IAi` 进容器、EC 默认值；报警不在当前报警里回 `ai.alarm_not_found` 不问模型；交给模型的材料（报警本身、来源下面的信号——数字量起始值和变化、模拟量最小最大报警时、现在的值，名字只是开头像的、别的部件的不带——和最近的告警错误日志），数据曲线按报警前后的时间段查；规矩（不编、不建议跳过联锁）和回答语言跟界面；EC 限日志条数、信号个数（配 0 不查数据曲线）、回答长度和随机程度给模型；空回答 / 出错 / 超时（含排队）的错误码，超时后照样能问；同时只让 SC `MaxConcurrent` 个在模型上算；服务（状态带模型名、回答转 `AiAnswerDto`、原样回错误码、没配回 `ai.not_installed`）；真走一遍微软 `Microsoft.Extensions.AI` 的 OpenAI 格式适配（本机 HttpListener 假模型服务：路径 `/v1/chat/completions`、占位密钥、模型名、两条消息、回答长度字段），模型服务停了当场报不重试；假模型实现 `IChatClient`、假日志、假数据曲线、带报警的探针组件 |
| IoIndexSmoke（不在 sln） | IO 点表下标和换算、PLC 门控、单点写、轴和执行器命令（气缸、阀的到位反馈是 DI 组件子节点 Opened / Closed / Feedback，配 -1 当没有；阀 NC / NO 开关写的值）、IO 组件在自己的扫描里按 EC 周期推整包 `IoDto`（骨架只建一次）、心跳（照 sc 装、按 `IHeartbeat` 找得到、第一拍写通、满了间隔写反面、写不成报警且等满间隔再写同一个值、恢复后报警留着人工复位清、点表里没有的 DO 报警、-1 不发） |
| ComponentScanSmoke（不在 sln） | 根组件按 sc 的 `IsRun` 起扫描（不分大小写、默认不起、停用不起、分组节点下的根照起、子组件跟父组件一条线程、重复起不多开）；开机各阶段（`InitAll` 按 InitOrder 再按 sc 先后、`Bind` / `OnStarted` 按 sc 先后先父后子、`StopAll` 倒着来、出错不耽误后面）、组件进容器（`ComponentLoader.Register`：按 xyz 接口登记，取一类全部按 sc 先后、停用的连同下面跳过，取一个拿最后登记的，没人实现的是 null，树本身也登记） |
| TankSceneSmoke（不在 sln，WPF） | 信号灯 SignalLamp（触发亮绿、报警位亮红、没触发灭、读不到不知道色、名字原样）；供液槽三维图（TankScene 绑 TankModel）：七个液位的液面、液体颜色、右上角的字和点；信号灯跟各自的 DI 不跟液位（对不上时各亮各的、读不到的那盏不知道色）、sc 没配的不画、没给槽 / 没收到推送一盏不画、换槽改绑且旧槽再变不影响；液面 0.2 s 过渡、中途换目标落最后一个、藏着直接落位、液位不知道不画液体；罐壁透明度依赖属性（外部设、绑定都传到三维件，图上没有滑块）、超范围不收；默认视角 / 正视图、前后两半罐壁跟相机分、滚轮缩放上下限；信号灯在图里、在罐右边、从下往上；英文字；没有绑定错误；`-- 目录` 出中英文图 |
| `*Visual3DSmoke`、ChamberSceneSmoke、LoadPortSceneSmoke（不在 sln，WPF） | 三维硬件组件（门 / Bowl / Lift 未知时停在行程中间、整块变红；Disk 没有右键菜单）和腔体三维图（ChamberScene：从部件推送按角色挑出部件并搭建、部件动作名的叫法、对盘心 / 接液杯、液柱落点、0.2 s 过渡、Bowl 和卡盘的高低、图下面的视角工具栏（默认视角、俯视）、重搭）；LoadPort 三维图（LoadPortScene：槽数跟 sc、片用 Disk 且就地换数据、点片选中、每个动作 0.5 s 且有先后、只亮在动的那块、门在半路、手动页显示模型的对接 / 门推法）；`-- 路径.png` 出图 |
| EventBusSmoke | 跨进程事件总线（`-- server` / `-- client` / `-- probe`，看输出） |

- 写法：顶层语句 `Program.cs`；`var checks = 0; void Check(bool condition, string message) { if (!condition) { throw new InvalidOperationException("FAIL: " + message); } checks++; }`
  （if 后面也要大括号、单独一行，switch 用老写法，见 SKILL.md 硬规矩 2）；
  按 `// N. 说明` 分节；最后一行 `PASS: N xxx checks (...)`；探针 / 假驱动写成文件末尾的 `sealed class`。
  等后台写库的轮询（查写进去了没有）要接住 `DbException` 当"还没写到"再查：池里早先开的连接还认旧表结构，组件开机刚建的表，
  赶上后台正在提交时会报"没有这张表"（2026-10-21 寿命冒烟 5 次挂 2 次）。
  失败就是未处理异常、退出码非 0。
- 容器（2026-10-18 起组件都在 IoC 里）：冒烟共用 `tools\SmokeShared\SmokeIoc.cs`（工程里 `<Compile Include="..\SmokeShared\SmokeIoc.cs" Link="SmokeIoc.cs" />`
  加 `Microsoft.Extensions.DependencyInjection` 包）。开头 `SmokeIoc.Reset()`（空容器，组件读 EC 属性就要经 IocHelper）；装树 `SmokeIoc.Load([...])`（装出来的进容器、换掉前一棵树），
  手搭的模块 `SmokeIoc.Tree(...)`，假件、直接 new 的单例、撤掉 `SmokeIoc.Use<IXxx>(x 或 null)`，用完还原；取用 `IocHelper.GetService<IXxx>()`。
  直接 new 的组件不会自己进容器，要被别人取到的显式 `Use`；装新树后前一棵树上还要用的单独 `Use`。组件开机时（`Bind` / `InitComponent`）取单例，
  冒烟里搭好容器再走开机步骤，换了单例要重新 `Bind`；服务构造时取，换了要重新 new。IO 组件开机准备时读点表、取 PLC：点表写进它的 `PointTableDirectory`（DI.csv / DO.csv / AI.csv / AO.csv）再 `InitComponent()`。
  只测装配校验、不该换容器里那棵树的直接用 `ComponentLoader.Load`。
- 数据：内存里造组件（`new LedgerManagerComponent()`、`SmokeIoc.Load([...])`）；要库的用临时 SQLite；不连设备（`FakeFrameCommunication`、`ProbeRobot` 这类假件）。
- 跑：仓库根目录 `dotnet run --project tools\<Name>`（先编译整个 sln；IoIndexSmoke、ComponentScanSmoke 单独编）。HsmsSmoke、EapSmoke 随机用 5600~5999 的端口，
  真宿主开着 EAP 链路（Hsms 启用、端口落在这一段）时别同时跑。
- **新功能、改了行为都要在对应冒烟里加检查**；冒烟测试里的代码也守同样的编码规范（不写花括号模式匹配等）。

## 4. 运行起来看

- 起法：先后端 `xyz.Core\Service\xyz.GrpcHost\bin\Debug\net10.0\xyz.GrpcHost.exe`（托盘图标：灰 = 启动中、绿 = 正常、红 = 失败，单实例），
  再客户端 `xyz.Core\Client\xyz.Client\bin\Debug\net10.0-windows\xyz.Client.exe`。编译前先关掉它们，否则 DLL 被占着拷不进去。
  用户开着、又不方便关的时候：把源码 `robocopy D:\Code <scratchpad>\fullbuild /E /XD bin obj .git .vs node_modules`（约 19 MB）拷出去，
  在副本里编整个 sln、跑冒烟——机型工程的部署目标是相对路径，只部署进副本；别在原目录编 RfidSmoke 这类引用机型工程的冒烟。
- 机型 DLL 要先部署（编译机型工程会自动部署），否则 sc.xml 里 `xyz._35021.*` 的 Type 找不到，后端起不来。
- PLC 仿真：本机没装 TwinCAT 时要有 `xyzAdsRouter` 服务（`tools\AdsRouter\install-service.ps1`，真机上别装），
  再起 `D:\仿真\统一仿真器\Start-35021.cmd`；顺序：路由 → 仿真器 → 后端 → 客户端。没有仿真器后端也能起，PLC 报通讯断开。
- 没有真设备时：LoadPort 的驱动、RFID 都连不上，开机日志各报一次，之后每 5 s（EC `ReconnectIntervalMs`）在后台重连、不刷日志；
  晶圆账槽位照登记（2026-10-06 起；以前 RFID 开不了整台 LoadPort 都不能动）。发动作会因为没连上失败、落 Error。机械手、腔体照常有。
- 看英文界面：语言取后端 sc.xml `System/Language`，**改运行目录那份**（`bin\...\Config\sc.xml`），起完后端立刻改回去；不要改源 sc.xml。
- 用 UI 自动化点菜单：底部导航两个 ListBox 的 AutomationId 是 `PrimaryMenuList`、`SecondaryMenuList`，用 `SelectionItemPattern.Select()` 选项。

## 5. 界面不连后端的离屏预览（改了页面或样式时用）

在 scratchpad 建一个 `net10.0-windows` WPF 小程序，ProjectReference 页面所在工程：
1. `new Application()`，按 App.xaml 的先后**一个一个**把资源字典 `MergedDictionaries.Add(new ResourceDictionary { Source = ... })`
   （BundledTheme 单独 `XamlReader.Parse`）。一次 Parse 整个字典会报"找不到 MaterialDesignOutlinedTextBox"。
2. `GrpcClientFactory.Initialize("http://127.0.0.1:9")`（只建通道），`IocHelper.ServiceProvider` 里只注册要看的 VM。
3. 页面放进 `Canvas` 再放进离屏窗口（`Left=-30000`）——直接当窗口内容会被屏幕宽度卡住，1980 宽的页面右边被裁。
4. 反射调 VM 的私有方法（如 `Apply`）喂示例数据；每次拍之前用 DispatcherTimer 等约 600 ms（Material 输入框的浮动提示是动画）。
5. 挂 `PresentationTraceSources.DataBindingSource` / `ResourceDictionarySource` 监听，绑定和资源错误应为 0。
6. `RenderTargetBitmap` 存 PNG，中英文各出一套；给用户看的截图放桌面一个子文件夹。
   用 `VisualBrush` 画的话要 1:1：`Stretch=None`、`ViewboxUnits=Absolute`、`Viewbox=(0,0,宽,高)`——默认的拉伸在有东西画出界
   （确认框、按钮阴影）时会把整页缩小一圈。独立窗口的弹窗（如选择弹窗）放到屏幕外 `Show()`，截窗口的根元素（卡片四周的阴影边也在里面）。
