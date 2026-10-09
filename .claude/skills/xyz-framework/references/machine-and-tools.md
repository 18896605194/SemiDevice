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
- 后端 `Service\xyz.35021.Module`（→ xyz.Modules、xyz.Shared）：
  - `Loadport\LoadPortModule : BaseLoadPortModule, ILoadPort`、`Robot\RobotModule : BaseRobotModule, IRobot`、`Clean\ChamberModule : BaseChamberModule`，
    类上 `[Component(description: "...")]`；机械手、腔体的动作在各自 `Operation\` 目录（`XxxOperation : ModuleOperation<ActionStep>`，步骤 SendCommand / WaitCommand）。
  - **LoadPort 的动作、状态查询、在位、重连、推送都在平台**（2026-10-06，用户："以后很多设备都要用"）：`BaseLoadPortModule` 的 7 个动作有默认实现
    （`LoadPortCommandOperation`：一条驱动指令一个动作），35021 的 `LoadPortModule` 是空的，**留着给机型扩展**（用户定的）——
    哪个动作不一样就重写那一个，平台没有的设备在这儿加。新机型照样建一个空类继承 `BaseLoadPortModule`。
  - `Config\IO\{DI,DO,AI,AO}.csv` 点表（表头 `Index,Module,Component,Name,Tag,Description`，AI/AO 加量程列）；跟 PLC 仿真器的
    `Machines\35021\IO` 是同一份，改了两边一起改。
  - `DeployToHost`（AfterBuild）：把 DLL 拷到 `GrpcHost\bin\<配置>\net10.0\Modules\35021`，点表拷到宿主 `Config\IO`。
- `Shared\xyz.35021.Shared`：机型错误码（`xyz._35021.Shared.Errors.ErrorCodes`，现在是空的）。
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
| OperationWaitSmoke | 模块操作等待/超时/中止、LoadPort 动作和模式、E87/E84 交接、机械手取放改账、报警只能人工复位、DI/AI 防抖、EC、Init/Abort、一趟搬运出错时报的错误码和参数；主界面要的后端（系统设置的 LoadPort / 机械手名单、站点类型、设备总状态的模式、整机 Auto / Manual / Stop）；LoadPort 载具组件（`Carrier` 节点必配、缺了开机抛；在位二选一 Query 两位 / Event；同一载具已认定后再 Map 保持认定；自动读码发起不成功接着试、到读码超时报读码失败；没配读头不算失败）、状态查询超时作废重发、动作没做成作废在途指令、关连接作废、LoadPort / RFID 断线重连、RFID 连不上不连累 LoadPort、帧通讯重连时旧接收泵只停自己（假通道 + 真 FCD 驱动，`GatedTransport` 测接收泵）；LoadPort 复位 / 中止落的状态（出错复位、打断 Load / Unload / Home / 夹紧、查不到门位是 NotInit，门开着没在动回 Loaded、门关落 Idle）、Load 联锁（没载具、机型重写 `LoadInterlock`） |
| WaferLedgerSmoke | 晶圆账装配、原子操作、事件、并发抢槽、流水落库、报警、人工移账/删账、账单调整服务、存盘和开机恢复（恢复前不写、LoadPort 不恢复、加工中记中止、退出最后存一次） |
| JobSmoke | 搬运管理（受理时的各项检查、两次操作抢一个槽、取片确认、WaferTask 顺序执行、站点和账都收尾才确认完成放锁、忙时拒绝不排队、没动手失败放锁 / 动过手失败留锁等确认、中止等待设备确认）和 Job（E94 CJ / E40 PJ：建 Job 的检查和整个不留（含站点不支持要用的任务、一站的站点都用不了、工艺配方不在库里）、本地建 Job 跟 Host 一样先建 PJ 再建 CJ（建 CJ 被拒撤掉已建的 PJ、重发被正常的检查拦住）、任务表（一片一行：取片、放片、工艺……回片）、一篮两个 Sequence 和两步加工、站点组、转换号顺序、配方快照、回到别的 LoadPort、PJ 暂停 / 恢复、CJ 暂停只不启动新 PJ、CJ 停止、PJ 中止等设备确认、工艺没做成只停那一片（重做 / 标记完成，别的片照常跑）、片位被人改了、Job 的取片 / 放片失败（错误落在当前任务，放锁后从该任务继续，放片重做不重复取片）、Host 先建 PJ 再建 CJ（EAP 按载具号找 CJ、PJ）、料没到先建 PJ（载具 Load 好、接了 EAP 时槽图被认定才定片，同一载具的槽 / CJ 不能重，定不了报警、PJ 留在排队）、整机停止走 Job 中止、Job / 搬运服务的错误码、CJ / PJ 一行进库带每片任务明细、重启后 Job 不接着跑库里记成中止）；机械手、LoadPort、腔体都是假的，扫描由测试一拍一拍推 |
| SequenceSmoke | 流程配方库：sc.xml 节点和参数、站点分组（sc 分组节点 + 机械手站点表）、新建/改名/保存/删除的各项检查、文件读写（坏文件跳过）、版本冲突、变更事件、服务错误码、工艺步骤的配方要在工艺配方库里、Host 按名字列 / 取（JSON）/ 认 JSON 样子 / 存（新建或覆盖，检查跟本地一样）/ 删和上报口（改名报旧名删 + 新名建） |
| ProcessRecipeSmoke | 工艺配方库：Host 按名字列 / 取（JSON，不带存 XML 用的属性）/ 存 / 删和上报口、sc.xml 节点和字段表、字段表配错开机就报、下拉按数据源取选项（直接写的、从腔体部件取的、跟着别的字段走的，几个腔体合起来）、新建/改名/保存/删除和按字段表的每一条检查、规整写法、字段都写进文件（老配方缺的按默认值补）、坏文件跳过、版本冲突、变更事件、配方对不对得上具体腔体、服务和字段表、腔体起工艺要配方在库里且对得上、没装库 |
| GemCollectorSmoke | SV/EC/ALID/CEID/DV 编号表生成、保号、停用、恢复 |
| DataCenterSmoke | 日志文件解析和历史查询、报警复位和报警历史 |
| HsmsSmoke / SecsSmoke | HSMS 链路组件对假 EAP（按 S/F 分发、S9F3 / F5 / F7、SxF0、闸门、回完再做、断线重连、端口冲突）；SECS-II 编解码、HSMS 握手和计时器 |
| EapSmoke | EAP 各标准对假 Host：配方管理 S7（列、取、问能不能下、下、删、全删、新名字按 JSON 样子分库、REMOTE 才收、配方变了的事件和 DV、本地编辑锁）、E30（通讯建立、控制状态、SV / EC / DV / 事件名单、Host 改 EC、报告定义和 S6F11 带的值、按需要报告、报警 S5F1 和报警事件、缓存断线进缓存 / 按先后发 / 清掉）、E39（类型、属性名、带条件查属性）、E87（读到号等 Host、槽图一律等 Host、第二次 PWC 比对槽图和给片号、Load 好就在取放、取消、ReCreate、读码失败 Host 给号、AutoUnload 关着时 CarrierRelease、Host 启停用、存取方式、预告 / 绑定这些不支持的回 CAACK=1）、E90（片对象跟着账建、挪、做、跳过、删）、E40 / E94（建、命令、查询翻成 Job 管理的命令，状态转换报事件）；LoadPort（带一个假的 `ICarrier`）、Job 管理是假的，晶圆账是真的 |
| RfidSmoke | FCD RFID 协议、握手、超时（假读头） |
| LogPipelineSmoke | 日志队列、LogHelper、LogViewModel（WPF） |
| ChamberSmoke | 腔体部件：照 sc 生成的通用部件清单（[PartKind] / [LiveValue]）、气缸三态、喷嘴 / 旋转 / 摆臂 Reach、有变化才推；部件手动动作（找不到、没有这个动作、参数不对、指令没发出去、Manual 状态、在途拒绝、停止类忙时照发、Abort 顶替、失败落 Error、轴走一遍、点动按住 / 续 / 松手 / 没续上自己停、停用），假 PLC 模拟气缸和轴 |
| IoIndexSmoke（不在 sln） | IO 点表下标和换算、PLC 门控、单点写、轴和执行器命令 |
| `*Visual3DSmoke`、ChamberSceneSmoke（不在 sln，WPF） | 三维硬件组件（门 / Bowl / Lift 未知时停在行程中间并高亮）和腔体三维图（ChamberScene：从通用部件推送认部件并搭建、对盘心 / 接液杯、液柱落点、0.2 s 过渡、Bowl 和卡盘的高低、图下面的视角工具栏（默认视角、俯视）、重搭）；`-- 路径.png` 出图 |
| EventBusSmoke | 跨进程事件总线（`-- server` / `-- client` / `-- probe`，看输出） |

- 写法：顶层语句 `Program.cs`；`var checks = 0; void Check(bool condition, string message) { if (!condition) { throw new InvalidOperationException("FAIL: " + message); } checks++; }`
  （if 后面也要大括号，见 SKILL.md 硬规矩 4）；
  按 `// N. 说明` 分节；会改静态 `X.Current` 的用完还原；最后一行 `PASS: N xxx checks (...)`；探针 / 假驱动写成文件末尾的 `sealed class`。
  失败就是未处理异常、退出码非 0。
- 数据：内存里造组件（`new WaferManagerComponent()`、`ComponentLoader.Load([...])`）；要库的用临时 SQLite；不连设备（`FakeFrameCommunication`、`ProbeRobot` 这类假件）。
- 跑：仓库根目录 `dotnet run --project tools\<Name>`（先编译整个 sln；IoIndexSmoke 单独编）。HsmsSmoke、EapSmoke 随机用 5600~5999 的端口，
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
