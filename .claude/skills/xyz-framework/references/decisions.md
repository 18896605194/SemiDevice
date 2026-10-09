# 已经跟用户定下来的设计（不要改回去）

改到相关功能前先看这里。每条后面是定下来的日期。

## 代码
- **大括号一律不省略**（2026-10-03）：`if` / `else` / `for` / `foreach` / `while` 后面只有一句（`return`、`continue`、`throw`、赋值）也要换行加大括号，
  不写 `if (version != _transitionVersion) return;` 这种单行（用户点名）；lambda 里的 if、冒烟测试的 Check 也一样。
  2026-10-03 用户说"全部改掉"：全仓清了一遍（不带括号 53 处 + 挤在一行的 `{ ... }` 27 处，含 `lock`、`try/catch/finally`）；
  当时另一个会话还在做的 3D 组件（`Controls\ThreeD`）和 `tools\*Visual3DSmoke` 没动，那边还有 63 处，等那块做完再清。
  （2026-10-03 腔体三维页做完时这两处也清了。）
- **注册扩展类叫 ServiceExtensions**（2026-10-03）：各工程根目录的 `ServiceCollectionExtensions.cs` 全部改名 `ServiceExtensions.cs`，类名同名。
- **不写花括号模式匹配**（2026-10-02）：`x is { } y`、`is not { } y`、`is { Prop: v }`、列表模式 `is [..]`、switch 里的属性模式都不要；
  先取局部变量再显式判空、判属性，判空用 `is null` / `is not null`。全框架改过一遍。
- **参数化**（2026-09-30）："该是 sc 就是 sc，该是 ec 就是 ec"：开关、落哪个库、保留天数、采样周期、查询上限进 sc.xml；
  批量大小、积压告警、推送周期、超时、防抖进 ec.xml。代码里不留魔法数。
- **开关量配置一律 bool**（2026-10-02）：涉及 High/Low 的配置全部改成 bool，不做兼容。
- **组件 DLL 目录**（2026-10-02）：顶层只有 Attributes / Collectors / Components / Enums / Interfaces / Models；顶层目录 = 命名空间；
  `Components\<类别>` 只归类，命名空间固定 `xyz.Components.Components`；纯数据类进 Models；不建按领域的顶层目录。

## 组件初始化 InitComponent 和模块初始化 InitModule（2026-10-09，用户定的名和分工）
- **起因**（用户："组件调用子组件的时候，不需要什么写死声明，应该就是通过框架获取所有的子组件"）：LoadPort / Robot 的 `Open()` 里手写找读头、E84、驱动再逐个开，
  子组件清单散在好几处；sc.xml 里多挂一个要连接的设备，没人去开它也不报错。腔体早就是框架走树的写法（部件表照组件树扫），LoadPort / Robot 补成一样。
- **两个名字，两档事**：`ComponentBase.InitComponent()` = **不动硬件**的开机初始化（连接、登记晶圆账、挂事件、E84 输出回安全态）；
  `BaseModule.InitModule()` = **动硬件**的模块初始化（回原点，返回 `ModuleOperation?`），人或调度才调、开机不调。
  原来的 `Init()` 一个名字两件事（组件递归 + 模块 = Home），拆开了；`BaseModule.Open()`、`IE84.Open()`、三个驱动组件上的 `Open()` 都没有了，连接写进各自的 `InitComponent()`。
  （没直接拿原来的 `Init()` 装连接：它开机不调、模块里等于 Home 会动硬件；`Open/Close` 这个名字又被气缸、阀的开合动作占着，所以另起名。）
- **InitComponent 的规矩**：按 InitOrder 先子后己、递归到每一层，一个子组件没做成不耽误别的、汇总返回 bool；开机由宿主对每个模块调一次，
  **父组件不点名子组件**；组件有事就重写并调 base（要赶在子组件前做的写在调 base 之前，如 LoadPort 先挂驱动事件、登记槽位，再让基类去连）。
  装机停用的组件自己拦（模块的 `IsEnable`、E84 的 `IsEnable`），因为基类递归会带着它。读头、驱动这一次没连上返回 false 只为开机日志看得到，之后后台重连。
- **InitModule 的规矩**：`BaseModule` 里默认返回 null；LoadPort / Robot / Chamber 基类里 = `Home()`（Home 就是它们的初始化）；不递归子组件。
  部件（轴、气缸）怎么回零、什么先后是机型的事，写在 Home 操作里去驱动。`ILoadPort` / `IRobot` 里的 `Init()` 改叫 `InitModule()`。
- **轴不重写 InitComponent**（用户："轴的不需要重写 init，后面即使写也是写 InitComponent，然后也不会有动作的"）：原来轴的 `Init()` 是回零，删了；以后轴的组件初始化也不带动作。
- **没动的**：模块的 `Close()` 还是手写（`_rfid?.Close(); _driver?.Close();`，现在宿主没有调用点）；PLC、IO、HSMS、轴的 `Open(IPlc)` 仍由装配层按类型调；
  `_driver` / `_rfid` / `E84` / `Carrier` 这些模块要用它能力的子组件，模块还是要取一个句柄（只能点名，框架没法代劳）；`InitModule` 还没有界面或服务入口（服务调的是 `Home()`）。

## 界面
- **模块状态徽标**（2026-09-29）：模块状态一律用 `ModuleStateBadge` 整条圆角色块（灰未初始化 / 蓝动作中 / 绿就绪 / 黄中止中 / 红报错），
  不写"状态：xxx"文字；小卡片用 `IsCompact`。色调由各模块显示模型按自己的状态码归类（`ModuleStates`），控件不认码。
  例外：账单调整页的位置列表**不显示**模块状态（2026-10-03）。
- **通用输入框**（2026-09-29）：所有输入用 `InputTextBox` 绑 `Value`；不合法红框提示、不把错误值交给后台、Value 保留上次合法值；
  边输入边只查格式，范围在回车/失焦时查（"范围 2~20 想输 10，敲 1 就报错"不能出现）；文本没有范围；界面上写的范围优先于 EC；什么都不配就是普通输入框。
- **页面启动时全部渲染**（2026-09-30）：PageHost 常驻全部页面，切菜单只切可见性；启动慢约 6 s 是接受的代价；页面感知显示用 IsVisibleChanged。
- **右上角**（2026-10-02）：时间块、用户块带边框圆角（深底），右边注销 / 复位 / 关蜂鸣器三个按钮竖排同宽，有报警时复位整块变红；中文按钮字用中文。
- **设置菜单**：EC 设置 1、账单调整 2、用户管理 3、角色管理 4。EC 设置是设置下第一个子菜单。

## 数据曲线 / 实时曲线（2026-09-30）
- 只记 sc.xml 配置了的：组件上能画成曲线的 SV + 组件绑定的 IO（`Di/Do/Ai/Ao…Index`，-1 不算）；点表有、sc 没绑的不记（"这个非常重要"）。
- 列名由 sc 生成：SV = 组件全路径.属性名；IO = 组件全路径.配置项名去掉 Index。勾选树 = 库里的列 = sc 结构，不显示旧列。
- 宽表：每秒一行（Time = UTC 毫秒 + 全部值），按天一张 `DataRecord_yyyyMMdd`，Io 库（io.db、WAL），保留 30 天；每秒都记，读不到存 NULL，曲线断开不画 0。
- 开关量存 1/0，曲线、悬停框、信息表显示 1/0（IO 页仍显示 True/False）。
- 图只有左边一根纵轴，所有曲线（模拟量、开关量）按真实数值画在这一根轴上，不分轴、不分道、不偏移；悬停弹数值框；中键框选放大；图上不放操作提示文字。
- 最多同时画几条 = sc.xml DataChart 节点 `QueryMaxSignals`（20）。
- Safety 组件（用户起的名）：整机安全信号（急停、门、漏液、烟感、气源/排风），一个信号一个 DiSensor/AiSensor 子节点；报警先都关着。

## 晶圆账 / 账单调整页（2026-10-02 ~ 10-03）
- **所有画片的地方都以晶圆账为准**（2026-10-03，用户："这个是不是都得通知一下"）：腔体页、LoadPort 页、机械手页（手指和调度图卡片）、
  账单调整页口径一致。做法是模块的状态推送带上账（`LedgerSlots`，由 `WaferLedgerSnapshot.SlotsOf` 生成），每拍扫描比较、账一变下一拍就推；
  客户端颜色统一用 `WaferStates.Of`。机械手手指账上没有、传感器有片时画"在途"色（账实不符）；LoadPort 没登记账时才退回按 Mapping 画。
  新加画片的界面也照这个来，不要再直接用传感器或 Mapping 结果画片。
- 位置 = 所有机械手 sc.xml `Stations` 合并 + 机械手自己（手指也是槽位）；多台机械手共用的站点只列一次；没登记槽位的不列。
- 页面：源、目标两栏对称（左位置列表按机械手 / LoadPort / 腔体 / 其他分组，右选中位置的槽位表）+ 调整记录（最近 50 条）；
  位置再多也只是列表变长。LoadPort 槽大号在上（25 → 01）。
- 人工移账 / 删账只改系统账、设备不动作；校验不过不报晶圆账报警，原因显示给人看；每次调整记一条记录（操作人、原因）到 `wafer_adjustment`。
- 2026-10-03 用户要求：位置列表不显示模块状态、名字要看得清（不再整行变淡）；**按片号搜索整个去掉**（界面和逻辑都删）；
  工具栏的"从 / 到"显示框和"清除选择"按钮去掉；目标栏空槽数写"空位 N / N empty"。
- 2026-10-03 加了**新建账单（补账）**：右栏选空槽 → 确认框里只填片号（必填），其余默认正常片、未处理、没有批次和载具号；
  片号已经在账上的拒绝（让人用移动）。右栏选位置就默认选中第一个空槽（新建、移动马上能点）；左栏源不默认，要人自己点那一片。
- 用户明确押后：设备动作中禁止改账（安全联锁）、按角色限制谁能用。
- **存盘和开机恢复（2026-10-05 做了）**：晶圆账的存盘和开机恢复写在 WaferManagerComponent 里（用户："直接就是写在账单管理里面"）；
  开机只放回腔体、机械手上的片，**LoadPort 以开机 Mapping 为准**（那一篮按 Mapping 重建片，不恢复，用户："肯定是以 map 的为准"，跟上次的账对不对得上先不管）。
  Job 的状态不在账里，由 JobManager 自己存；**重启后 Job 不能接着跑**（用户定的），见「Job」一节。

## 流程配方页（配方 → 流程配方，2026-10-03）
- 菜单：配方下 流程配方 `Recipe.Sequence` 1、工艺配方 `Recipe.Process` 2。流程配方是机台级的（片走哪些站点），
  工艺配方是腔体级的（腔体里怎么做），流程配方的每个工艺步骤引用一个工艺配方。
- 左边列表只有编号、名称两列；个数是 sc.xml `Sequence` 节点的 `Capacity`（99，`[SCEditor]`，"99 这种东西都是 sc 里可以编辑的"），
  目录、名称长度也是 SC。列表标题条上 新建 / 重命名 / 删除，填编号名称马上生效；**没有页面顶部工具栏**。
- 右边分块、每块带标题条（"功能区域划分要明显"）：基本信息（编号、名称、创建人/时间、修改人/时间、版本只读，说明可改）
  → 流程步骤 → 路线预览（保留）。
- **新建、重命名框只有一个名称输入框**（2026-10-03）：标题带编号（"新建流程配方 06""重命名流程配方 01"），下面就是名称框（重命名带出原名并选中）；
  不再在上面用大字再显示一遍编号名称（用户："上面一个大大的名字，下面还可以修改，很不对"）。删除、放弃修改框没有输入框，保留大字说明动的是哪一个。
- **保存在流程步骤标题条最右**：`[有没保存的修改] [添加] [删除] [保存]`，基本信息标题条只有标题（2026-10-03 看了真软件后改的：
  原来放基本信息标题条，看着像只存基本信息；保存存的是整个流程配方——说明 + 全部步骤，平时改的也多是步骤）。
- 步骤号就是普通数字 1、2、3，不加圆圈、不写"取片"这类字。第 1 步和最后一步是 LoadPort（可多选），**默认从哪来回哪去**；
  源 LoadPort 没勾在最后一步里时，放到勾了的 LoadPort 的同号槽（支持 LoadPort1 取、LoadPort2 放）。
  不要"回原槽"选项、槽位规则下拉、备注。
- 步骤块标题条只有 添加 / 删除：点一行选中，添加插在它后面、删除删它；不要每行的 ↑↓✕。添加 = 公共选择弹窗选 sc 的站点分组，**不默认选 Chamber**。
- 分组名、模块名照 sc.xml 原样显示（"Chamber"，不翻成"腔体"），正常字号；可选的分组、站点都来自 sc.xml。
- 工艺配方列用公共选择框 `PickerBox`（文本 + "…" 弹窗），宽 260，**不用下拉框**；选择框、选择弹窗做成公共的，宽高可设。
  选择弹窗就是一个普通窗口，不包 DialogHost、不动主窗口（"本质上就是一个窗口"）。
- 配方框只能从工艺配方库里选（2026-10-04 工艺配方库做好后改的；弹窗列 编号、名称、说明、时长），保存时前后端都查配方在不在库里；
  后端没装工艺配方库时才放开手输、不查。

## 工艺配方页（配方 → 工艺配方，2026-10-04，按样稿 v5 做的）
- **单独一套，不跟流程配方页共用**（用户："虽然跟 sequence 界面有点像，recipe 这里还是要自己单独搞页面……不要共用"）：
  页面、ViewModel、显示模型、后端组件（`ProcessRecipeComponent`）、服务、DTO 都是自己的；只用公共的样式、控件、选择弹窗。
- 布局跟流程配方页一样分块：左边编号、名称列表（新建 / 重命名 / 删除在列表标题条上），右边基本信息 → 工艺步骤。**没有工艺预览**（"不需要留"）。
  新建、重命名框同流程配方页（标题带编号，只有名称框）。
- 工艺步骤标题条最右：`[合计 N s] [有没保存的修改] [添加] [删除] [保存]`；合计不能超过腔体的工艺超时（EC ProcessTimeout）。
- **字段可以自己配（2026-10-04）**：每一步有哪些列、每列叫什么、什么类型（整数、小数、下拉、开关、文本）、单位、上下限、小数位、默认值、必填、
  下拉的选项，都在 sc.xml 的字段表（`ProcessRecipe.Fields`）里配，用 ScEdit 的「配方字段」页改；工艺配方页按它生成，不改代码。
  - 下拉要绑数据源，数据源可以直接输入：写选项（`Time,Scan`），或从腔体部件取（`Parts:ArmAxisComponent`、`Parts:NozzleComponent.Chemical@Arm`）；
    ScEdit 里格子旁边的"…"帮着选。摆臂、药液不是单独的类型，就是绑了数据源的下拉。
  - **字段之间不配关系**（用户："这个不需要"）：每一步所有列都显示，不出液的步骤药液、流量那几格空着（不是必填）；原来写死的跨字段检查（Scan 两头不能一样）不要了。
  - **字段表全机一份**：8 个腔、1–4 一样、5–8 一样时，页面、字段表都一样，"本质上也就是数据源不一样"——编辑时下拉列所有腔合起来的，
    用到具体腔体（流程配方勾的腔体、腔体起工艺）时再查这个腔有没有。
  - **喷嘴喷的东西不单独加字段**（用户："即使背面可以冲洗也就是一个喷嘴药液"）：背面冲洗、N2 这类都在药液里选、流量填在流量里。
  - 时间（`Seconds`）固定保留（合计时长靠它）；字段作用到哪个设备（AO 等）这一版不配，做腔体执行时再定（AO 本来就配在 sc 腔体下面）。
- 现在 sc.xml 里配的 9 列：步骤、时间 s、转速 rpm、**摆臂 → 药液 → 流量 L/min → 方式 → 位置 → 到 → 速度 mm/s**。
  - 摆臂、药液、方式都是**下拉框**，样子跟项目里的下拉框一致（`ToolbarComboBoxStyle`）——这几个是短的固定选项，用下拉；从库里选才用选择框。
  - 药液只列这条摆臂上的喷嘴（照 sc.xml 喷嘴的 Chemical 原样，35021：Arm1 上 DIW、SC1）。
  - 摆臂选"—"（不选）= 这一步不喷，摆臂在 Home。
  - **方式只有 Time 和 Scan**（中英文都这么写，不写"定点""扫描"）：Time 停在一个位置喷；Scan 在"位置"和"到"两个位置之间按"速度"来回扫，
    扫到这一步时间到（用户问过"150-150、150 到 30 啥意思"，所以不写成箭头；字段可配以后"到""速度"是单独的两列）。
  - **流量要加**（用户："每一步要不要加流量？……要增加的"）：0.1 ~ 3 L/min（字段表里配）。
  - 位置是晶圆坐标：0 = 从 Home 摆过去先到的晶圆边缘，150 = 晶圆中心（见下面腔体手动页的摆臂示教位）。
- 字段、范围、下拉选项都来自后端（sc.xml），界面不写死；检查前后端一样，提示文字用同一套错误码。
- 腔体手动页的配方框换成选择框（点"…"从工艺配方库里选），原来那个灰着的"选择"按钮删了。
- 用户押后（"先不管"）：流程配方在用的工艺配方不让删、不让改名。
- 还没做：腔体按工艺配方的步骤真的执行（35021 的 Process 还是定时模拟）；字段作用到设备（喷嘴流量设定 AO 等）还没接。

## 腔体手动页（三维，2026-10-03）
- **直接三维**（"为什么要放二维的，直接就是三维的"）：左边是 `ChamberScene`，二维 `Chamber` 俯视控件已删。
  样子照 Codex 的 ChamberPreview 预览（底座、Bowl、旋转盘、腔门，摆臂左右两个安装位，Home 接液杯）。
- **sc.xml 的 Bowl 先只留一层 `Bowl1`**（原来是 Bowl 分组下 Bowl1~3）：名字跟点表、仿真器一样，点表和仿真器都不改，
  Bowl2、Bowl3 的点留在点表里没接组件（用户："仿真那边有 bowl1 的话，这边 sc 里面直接改成 bowl1 不就好了"）。
  代码按"腔体下名字以 Bowl 开头的第一个气缸"认。三维 Bowl 一个环，升起时底边不动、侧壁拉长（预览那版"连续侧壁"）。
- **摆臂示教位**（用户讲定的）：回零后轴在 0 位 = Home（仿真器也是）；配方用晶圆坐标——从 Home 摆过去先到的**第一个边缘 = 0、
  晶圆中心 = 150**；EC `Edge` / `Center` 存示教的实际轴位置（比如 Edge = 100、Center = 200），没示教默认 Edge = 0、Center = 150。
  手动页没放示教（参考图上没有），在 EC 设置页改。
- **三维里的动画一律 0.2 s，写死**：摆臂收到新位置的过渡（`ArmTransitionMilliseconds`）、Lift、Bowl、门的全行程过渡都是 0.2 s
  （液体流动、转盘转动是持续的速度，不算过渡）。
- **Bowl 降下时上沿比旋转盘（SpinMotor 卡盘）低，升起来才比它高**（用户点名）：降下露出盘面好取放片，升起围住盘面挡液。
  三维里卡盘架在主轴上（`DiskVisual3D.SpindleHeight`），盘底 0.32，Bowl 1 级降下上沿 0.275、升起 0.825。
- 摆臂组件推 `Reach`（0 = Home，1 = Center）和 `EdgeReach`（Edge / Center），
  三维按 Home → 边缘 → 中心分两段画（轴在 Edge 时喷嘴画在盘边、在 Center 时正对盘心）；没示教（Edge 跟 Home 重合）就一段。
- **Start 按钮先一直能点**（空配方在日志里提示），用户：先不管，后面按腔体状态设计。
- **页面照用户给的参考图排（2026-10-04）**——之前三版样稿（部件卡片一列堆下去、照 sc 树的列表 + 原地展开、按功能分区）都被否，
  用户最后直接给了图（"你按照这个界面来写"）。一整张卡：
  左边是"腔体视图"（三维图 + 图下面一条视角工具栏，带图标；用户后来说只留默认视角、俯视，左转、右转、放大都不要——拖动旋转、滚轮缩放还在；回到默认的那个**不叫"复位"**，
  跟整腔操作、轴上的复位（设备清错）重名，用户让改名）→ 轴页签（一根轴一个，右边 ">" 切下一根）
  → 选中轴的状态（当前位置、当前速度 + 伺服就绪 / 已回零 / 运动中 / 已到位 / 故障五盏灯）→ 参数（目标位置、移动速度、点动速度、步距）
  → 两行四列按钮（回零、移动、中止、复位 / 点动+、点动-、步进+、步进-）；
  右栏是腔体名 + 状态色条 → 启用 / 模式 / 报错 / 当前配方 → 工艺 → 整腔操作（回零、中止、复位）→ 气缸表（名字、状态、升 / 降）。
- **不浪费地方、按钮正常大小**（用户看了第一版："很浪费，按钮都好大"）：右栏固定宽（不按比例拉宽），按钮、输入框用固定的正常尺寸、不拉满；
  宽屏上轴的状态、参数、按钮三块并排成一行（参考图是窄屏，三块上下叠），省下的高度给三维图。
- 照图时用户选的：**阀（喷嘴）和传感器这页先不放**；按钮字**走语言包**（中文界面显示中文，不照图用英文）；参数框**加一个步距**
  （步进要用，图上没有）；腔体状态**保持整条色块徽标**（不照图改成"● 空闲"）。图上没有的也不做：示教、伺服开关；
  图的标题"布局示意"、底下"当前部件：Chamber1.Arm1"当成示意图外框。页签、气缸表按 sc 的先后（图上是按字母排的）。
- **部件是通用的**（用户："最好是通用的设计，未来可以扩展"）：组件类标 `[PartKind]`、属性标 `[LiveValue]`、方法标 `[ManualAction]`，
  后端 `PartCatalog` 照 sc 扫出来推 `ModulePartsDto`、按"部件路径 + 方法名 + 参数"调；sc 里加 Arm2 就多一个页签、多一行 Lift，
  加新种类的硬件只写组件和界面模板，推送和动作接口都不用改。
- **气缸三态**（用户定义："命令下去了，到位信号没有亮……那就是 unknown 状态"）：命令发到哪一侧就看那一侧到没到位，没到是未知，
  上电两个线圈都没通时只看到位反馈。气缸表显示升到位 / 降到位 / 未知；**三维跟反馈走，未知画在行程中间并高亮**，到位后再走到头
  （不是命令一发出去就画到头——那样没到位也画成到位了）。
- **气缸一律叫升 / 降**（参考图上门也是 Up / Down；门本来就是上升打开、下降关闭）：开侧 = 升、关侧 = 降。
- **点动按住动、松开停**（用户："就是传统的 jog 运动"）：按住期间界面每 200 ms 续一次，腔体 EC `HoldTimeoutMs`（默认 1000）内没续上
  （界面断了、客户端退了）自己停；按住期间腔体在"手动中"，别的普通动作发不进来；**中止（轴停止）腔体忙也照发**。
- **联锁还没做**（Bowl 升着、Lift 没升也能摆臂），是用户知道并押后的。
- 不注册逐帧事件（用户："这个肯定得改，不然又性能问题"）：液柱只在摆角 / Lift 高度 / 出液变化时重算。
- 用户说**不用管**的：开着阀摆过去液柱穿过 Bowl 壁落到底座（"开着阀门就是这样"）；液柱落点常数（"就是动画，不会修改"）；
  Lift 下 + Bowl 上位时摆臂穿 Bowl 壁（"先不管"）。

## 主界面（菜单 Main，2026-10-04，照用户给的图做）
- **布局照图**：左"系统操作"（系统状态、当前模式、报警条数，Auto / Manual / Stop / Reset 两行两列）、中"整机调度"（上排腔体卡、中间机械手、
  下排 LoadPort 卡）、右边 LoadPort 页签（载具 | 映射完成 | 槽位，LotID，Sequence 选择框，创建 Job / 启动 Job，槽位表 槽位 / Wafer / 状态 / Sequence / ⊕ ⊖）。
- 用户点名的重点：**以后变的是中间的调度界面和右边几个 LoadPort**。所以右边页签照 sc.xml 的 LoadPort 生成；中间默认照机械手站点表自动摆
  （公共控件 DispatchMap，Robot 手动页也换成了它），机型要别的摆法按 `ClientViewKeys.MainDispatch` 注册自己的整块换掉，主界面别处不动。
- 系统状态用**整条色块徽标**（不照图写"空闲 ●"，用户选的，跟腔体页一样）。
- **Job 先只做界面**（2026-10-04 用户选的），2026-10-05 后端 Job 做好后接上：创建 Job 把这一页签的 LotID 和各槽的 Sequence 交给后端（2026-10-07 改为 SC 配置 CJ 启动方式，默认建好等启动），
  这个 LoadPort 上有没删的 CJ 时点不了；启动 Job 在 Auto 下、这个 LoadPort 上的 CJ 是 WAITINGFORSTART 时能点（发 CJStart）。按钮位置、样子没动，
  Job 列表 / 详情界面还没做（要先给参考图或出样稿）。
- **⊕ ⊖ 的意思**（用户讲的）：⊕ 给这一槽单独选一个 Sequence（公共选择弹窗），⊖ 把这一槽的 Sequence 清空。上面的 Sequence 框一选就给全篮能做的片套上
  （之后放上、Mapping 出来的片也套它），再用 ⊕ ⊖ 单独改；交叉片、叠片、状态不明的没有 ⊕ ⊖。
- 界面字：用户说中文还是英文"无所谓，多语言双份就行，到时候自己改语言包"——中文包先照图写（Auto / Manual / Stop / Reset、LotID、Sequence、Wafer、Job 用英文）。
- 自己定的（交付时说了）：Auto / Manual = 开 / 关搬运管理的自动派单；Stop 不弹确认（跟各手动页的中止一样）；Reset = 右上角那个整机复位。
- **Stop 走 Job 中止**（2026-10-04 用户定的）：关自动调度、中止搬运操作，在跑的 Job 全部走中止（等设备确认、核对片位），不直接删 Job；
  在给 Job 做工艺的腔体、正在搬运的机械手由 Job / 搬运管理自己收场，别的正在动的模块照旧直接发中止（先认出哪些在给 Job 做工艺，再让 Job 中止）。
- **全部回片按钮**（2026-10-05 加的）2026-10-06 删了（用户："回片这个你先把现有的也删除吧，整体回片先不做"）。腔体卡片没有值的字段整行不显示（图上就是空的，Robot 手动页跟着一样）；LoadPort 卡片的六盏灯改成真数据（以前写死）。

## LoadPort 在位和驱动恢复（2026-10-06，用户定的）
- **在位二选一做成 SC**（用户："事件和这个状态查询的结果，二选一"）：`PresenceSource` = Query（状态查询）/ Event（PODON / PODOF），**默认 Query**
  （2026-10-09 起这个 SC 在 `Carrier` 子组件上，见下面「载具收进 CarrierComponent」）。
  以前只认事件、状态查询只给界面看：开机时盒子已经在端口上就认不到（重启后要收片、建 Job 都走不通）。
- **Query 怎么判**（用户："这两个都是亮的才是放好了，这两个都灭了，才是被拿走了"）：在位、到位两位都亮 = 放好，都灭 = 拿走，
  一亮一灭不算变化（保持原判断），查不到也保持。所以不另加防抖（读错一位不会被当成拿走）。界面上的"在位"跟后台用同一个判出来的值。
- **驱动的问题一起改**（用户："这次一起改肯定要改的"）：回复丢了指令永远占着在途位 → 超时作废；断线不重连 → 按 EC 间隔后台重连；
  RFID 连不上整台 LoadPort 不能动 → 照常开 LoadPort，读头后台重连。做法见 backend.md §3、§4。
- **E84 装没装做成 SC `IsEnable`**（用户："本机跑的时候不要E84，这边没必要加载的吧"）：False = 端口当没有 E84（不打开、不扫、不读写 IO）；
  跟 EC `E84Enabled`（装了以后现场在线开关交接，默认关）分开。仓库里的 sc.xml 两个 E84 配 False（本机没接搬运车），真机装了改 True。
  数据曲线照旧按 sc 记 E84 的 IO 点（它不认任何组件的 IsEnable），不想要在 DataChart 的 Exclude 里加 `LoadPort1.E84,LoadPort2.E84`。
- **LoadPort 给多设备复用：收进平台，不单独拆 DLL**（2026-10-06，用户："以后很多设备都要用"）。用户原想把 LoadPort 从驱动到通信底层拆成一个 DLL；
  没拆的原因：通信层机械手、RFID 也在用；Job、机械手、流程配方、E87 直接用 `BaseLoadPortModule` / `ILoadPort`，单拆会循环引用；驱动到通信底层
  本来就是独立的 `xyz.Drivers.dll`（不引用任何工程）。真正挡复用的是每个机型要抄一份 LoadPort 动作——所以 7 个动作收进平台（默认实现），
  **35021 的 `LoadPortModule` 留着（空类），给机型自己的设备、动作扩展**（用户定的）。机械手、腔体以后多机型时照这个收。
- 改名（2026-10-06，用户提的）：合成的在位叫 `IsCarrierArrived`（载具到了，现在是 `Carrier.IsArrived`，DTO 里仍叫 `IsCarrierArrived`），状态查询的原始位叫 `IsPresent` / `IsPlaced`，
  `LoadPortStatus` 的开关量一律 `Is` 开头（设备自己的自动模式位叫 `IsDeviceAutoMode`，跟模块的 `IsAutoMode` 分开）。
- 还押着的：开着 EAP 时，开机已在端口上的盒子在 EAP 接上之前就判到了，
  到达、读码回调会丢（E87 只知道端口有盒、没有载具对象），开 EAP 之前要补"EAP 接上时把端口上已有的载具补报一遍"。

## LoadPort 复位 / 中止落的状态、没载具不 Load（2026-10-08，对照老 CTC 后用户定的）
- **手动动作不跟 Job 挂钩**（用户："我只是手动为啥要和 job 关联"）：手动页的 Load / Unload / Home / Reset / Abort 不看有没有 Job，
  服务层不加卡控（用户："LoadPortService 卡控肯定不是加在这里，这里只是手动的"），检查都在模块里。CTC 后台也不认 Job。
- **复位只清错、中止只停，落的状态照实际**（以前一律落 Idle，门开着却点不了 Unload、机械手也进不来，只能再 Load 重建账，Job 里这一盒的片就对不上了；
  机械手从 LoadPort 取片失败卡在取放中，也只能这样出来）。用户选的"a1 小改"，CTC 的做法（Loaded 不当模块状态、直接读门 / Dock / 夹紧传感器，a2）没选。
- **Idle 一律当"门关好、没 Load"**（用户："这里的 idle 其实是 unload 状态"；E87 放行后 Idle + 有盒报可以取走，本地 `LocalTransferState` 也按它判等取走），
  门不确定就不能落 Idle → **落 NotInit 要人 Home**（用户："这两种都落 NotInit""直接修改状态机就好了"）。写法：
  状态表写最保守的——Error / NotInit 复位 → NotInit（用户："reset 之后状态应该是未初始化"，照 CTC Error + Reset → Init）；Loaded 复位 → NotInit；
  中止只有 Idle → Idle、Error → Error，其余（NotInit、打断 Load / Unload / Home / 夹紧松开、Loaded、交互环）→ NotInit。
  模块 `Begin` 记下动作前的状态，Reset / Abort 做成后 `SetStateByDoor` 只往松里改：动作前在 Loaded 或交互环（门没在动）的，状态查询门开且载具在 → Loaded，
  门关 → Idle，查不到 / 门在半路 / 载具不在 → 保持 NotInit。
- **没载具不 Load**（用户："其实就是那个到位信号"）：做成 Load 联锁虚方法 `LoadInterlock()`（用户提的"搞一个 LoadInterlock 虚方法给默认实现"；
  我改过一次 CanLoad，用户要改回 LoadInterlock，别再改名），返回 true = 放行，默认看 `Carrier.IsArrived`，机型重写加条件；在 `Begin` 里调（不放 `Load()` 里，
  机型重写 Load 忘了调就漏了），手动、E87、机型的 Load 都过。手动点了回笼统的"动作被拒"，没另加错误码。
- 我自己定的（交付时说了）：没加"有搬运在用这个口就不回 Loaded"——要查搬运管理会跟模块锁互相拿锁，35021 只有一台机械手，
  两台机械手共用一个 LoadPort 又在对方手伸在盒里时点中止才会撞。

## LoadPort 的载具收进 CarrierComponent（2026-10-09，用户："专门搞一个 Carrier 的组件，LoadPort 里关于载具的东西都放进去，实现 ICarrier"）
- **结构**：`xyz.Modules\Loadport\CarrierComponent : ComponentBase, ICarrier`，sc.xml 每个 LoadPort 下一个 `Carrier` 节点
  （`Type="xyz.Modules.CarrierComponent"`，写在 RFID 节点后面，同一拍读头先出结果、载具紧跟着取）。**必配**：端口 `InitComponent()` 开头 `FindChild<ICarrier>()`、
  `Attach` 后存进 `Carrier`，找不到抛 `InvalidOperationException`，开机就暴露（停用的端口也一样）。2026-10-09 用户定：不在属性 getter 里懒找，写在 InitComponent 里
  （开机先组件初始化、再起扫描和服务，所以别处用到时已经挂好；冒烟里的探针端口也要先 InitComponent 再用 Carrier）。
  **端口只认 `ICarrier`、对外也只给 `ICarrier`**（用户："万一以后换了这个组件"、"不要再搞一个接口"）：端口要调的那几个口和 `Info`、`IsAccepted` 都在 `ICarrier` 里
  （`CarrierInfo` 因此在 `xyz.Components\Models`），换载具组件就是写一个实现 `ICarrier` 的组件、改 sc.xml 的 Type，端口不动；代价是 EAP 也看得见端口专用的口，
  接口里分了一段注明"EAP 别调"。读码器同理（2026-10-09 用户："这个确实得抽取出来"，名字用户定）：端口和载具只认 `ICarrierIdReader`
  （`ReadCarrierIdTimeout` / `StartReadCarrierId` / `GetCarrierIdResult` / `Close`，在 `xyz.Components\Interfaces`），平台的 `RfidDriverComponent` 实现它；
  换 RFID 牌子写品牌壳，换扫码枪这类非 RFID 的就实现这个接口，都只改 sc.xml 的 Type。端口的组件引用（`_driver`、`_rfid`、`_carrier`）一律在 InitComponent 里找出来存下，
  不写每次现找的属性。`ICarrier` 在 `xyz.Components\Interfaces`，经 `ILoadPort.Carrier` 拿到；
  `ILoadPort` 里原来的 `IsCarrierArrived` / `CarrierId` / `SlotMap` / `ReadCarrierId` / `SetCarrierId` / `UpdateCarrierStatus` / `NoteCarrierComplete` 删了（不留重复）。
- **分工的标准：主语是载具的进 Carrier，主语是端口的留下，两边都沾的由端口组合**。进 Carrier 的有：载具快照 `CarrierInfo`（`Info`）和它的锁、
  在位判断的**规则**（SC `PresenceSource`：Query 两位全亮到、全灭走、一亮一灭或查不到保持；Event 看 PODON/PODOF）和到达 / 拿走的边沿处理、读码（SC `AutoReadCarrierId`、
  收读头结果）、改号和核对进度写回（`SetId` / `UpdateStatus`）、Mapping 落晶圆账、取放状态（Load 好进取放、Unload 好 / 动作失败记中断、`NoteComplete`）、
  载具这一半的 E87 上报（到达、拿走、读码成功 / 失败、槽图读到、取放开始 / 结束、干完）、SV `IsArrived` / `CarrierId`、事件 `FoupArrived` / `FoupRemoved`。
  留在端口的有：`SlotCount`（站点的槽数，Carrier 在到达时拿它当 `Capacity`）、`IsLoaded`、`IsCarrierReady`（= 启用 && `Carrier.IsArrived` && `IsLoaded` && `Carrier.IsAccepted`，
  门开没开是端口的事）、`LocalTransferState`、E84 桥、`E87Callback` / `E84Callback` / `E84Provider` 三个口子属性（回调都带 `ILoadPort port`，E87 按引用认端口；
  Carrier 的上报经端口在 `Attach` 时交给它的入队口发，跟端口自己的上报同一条线，先后不乱）、读头和驱动的连接（打开、重连）。
- **端口喂给 Carrier 的口只有这几个**（在 `ICarrier` 的"端口调的"那段里，别再加）：`Attach(port, 读码器, E87 入队口)`（读码器是端口 InitComponent 里 `FindChild<ICarrierIdReader>()` 找到的，没配为 null）、`Sense(isPresent, isPlaced)`（每拍两个传感器位，null = 没查到）、
  `SetDeviceReportedPlaced(placed)`、`NoteMapped(slotMap)`、`NoteLoaded()`、`NoteUnloaded()`、`NoteFault()`。端口的 `SetDeviceReportedPlaced`、`UpdateSlotMap` 保留，只是一行转发
  （机型有别的上报路子、自己重写 Load 的，照旧调它们）。Carrier 没有父引用，跟 E84 一样由端口驱动；锁序固定：模块锁在外、载具锁在内，Carrier 持锁时不调模块。
- **槽图认定状态只往前走**（用户：同一载具 Verified 后再 Map，**保持已认定**）：还没读过（NotRead）才转 Read，Host 已经认定（或在等、或判了不过）的，
  再 Map 一次（比如 Unload 之后又 Load）只更新槽图和账，不动认定状态；重置只靠载具拿走或 Host 的 CarrierReCreate。
  以前每次 Load 都写回 Read，而 E87 槽图机在 Verified 收 Read 不转换，两边对不上，`IsCarrierReady` 会永久为 false。
  已知后果：再 Map 会整篮重建晶圆账，E90 的片对象只在槽图认定那一刻按账建，重建出来的新片不会补建（没处理，用到再说）。
- **读码没发起成功不再静默**：到位后自动读码，读头断线重连中、或上一次还没读完时 `StartReadCarrierId` 返回 false——以前就这么算了，Host 干等一个永远不来的 ID。
  现在接着每拍试，试到读头的 EC `ReadCarrierIdTimeout` 还不行，记中断日志、按读码失败报 `CarrierIdReadFailed`（Host 就能带端口号给号或取消）；
  载具走了、别处已经有读码结果就不试了。**没配读头的端口不算失败**（本来就不读码，ID 由 Host 给）。
- **搬家换号**（用户："这个肯定是一起搬"）：SV、事件按组件路径采集编号，现在是 `LoadPortN.Carrier.IsArrived` / `.CarrierId` / `.FoupArrived` / `.FoupRemoved`，
  生成了新编号，原来 `LoadPortN.CarrierId` 等的老编号停用保号。数据曲线里这两列换成新列。EAP 当时还没上线，所以趁那时候搬。
- **留着不动**（用户："后续要支持 cyclerun"）：`IsCycle`、`CycleRunTotal` 两个 EC 没人用，但**不删**，连同 `PickOrder` 一起原样留在 LoadPort 上、没搬
  （它们是"怎么处理这一盒的片"的策略，不是载具生命周期）。
- **别做大**：一个端口一个，只管"这个端口上的这一盒"；不做载具表、工厂、ID 生成器，也不是 E39 的 Carrier 对象（那个用户砍过，别借机加回来）。
- **写法**（用户："用一个变量接收一下"）：用到载具的地方取一次存起来——端口里是缓存的 `_carrier` 字段，`E87Port.Carrier` 建端口时取一次，E87 三个状态机构造时接成 `_carrier`，
  其它地方用局部变量；别到处写 `device.Carrier.Xxx` 一长串。

## SECS / HSMS / E84（2026-10-02）
- S9 只由设备端发；主机端收到不认识的消息回 SxF0 中止事务；被动端独占绑定，HSMS 端口不能和 Rpc 端口相同。
- E84：设备端就是框架里的 `E84Component`（还在做）；SecsSim 只模拟天车（OHT）一侧，接 E84 这件事先不做。

## EAP 接入的统一做法（2026-10-05，用户："以后这一套架构都这么设计"）
- 照 LoadPort 现成的做法：凡是要给 EAP 用的设备侧对象（LoadPort、Job，以后的晶圆跟踪、设备性能跟踪……）都开同样三个口子，EAP 侧只做 SECS 翻译：
  1. **命令接口**：EAP 直接调设备侧的接口，接口按对象起名 `I + 组件名`（LoadPort 是 `ILoadPort`，Job 是 `IJobManager`），
     跟本地界面的服务调同一个接口、过同一套检查，不给 EAP 另开一条进设备的路。调用当场回受理结果（被拒带错误码），后面的进展走回调和状态。
  2. **上报口**：设备侧挂回调接口属性，**按 SEMI 标准号起名**：`IE87Callback` / `E87Callback`、`IE84Callback` / `E84Callback`；
     Job 是 `IE40Callback` / `E40Callback`（PJ）和 `IE94Callback` / `E94Callback`（CJ）。没接 EAP 时为 null，设备照常跑；
     所有上报共用 EAP 的派发组件 `EapNotifierComponent`（sc.xml Eap 下的 `Notifier`，单例 `Current`）一条线程按发生顺序发（单读者 Channel，不占扫描线程、不拿模块锁，积压只告警不丢），设备侧不再各自 new、各起线程（2026-10-07 用户："做成组件，直接拿他的单例"；原来每个对象一条线程，不同来源报给 Host 的先后会乱）。
  3. **反查口**：要 Host 拿主意的事，设备侧问 provider，同样按标准号起名（`IE84Provider` / `E84Provider`），为 null 时按本地规则自己判断。
     没有要问的就不开（Job 现在不开：载具核验归 E87 → LoadPort，Host 命令收不收归 E30 控制状态）。
- 真正的状态只在设备侧存一份，EAP 侧不另记一份当真；Host 的决定（确认载具 ID、确认槽图、Job 命令）都经设备侧接口写回（例：`Carrier.SetId` 后 ID 状态为已核验）。
- SEMI 状态机放哪看它是什么：设备自己的执行状态（E40 的 PJ、E94 的 CJ）放在设备侧（JobManager），不接 EAP 本地也要用；
  纯粹跟 Host 核对的过程（E87 的 ID / 槽图核验、端口搬运状态等）放在 EAP 侧，由回调推进。
- 不学 CTC：它 FA 层、调度层各一套 Job，靠轮询加 `Task.Delay` 同步，Host 的 PJ 暂停 / 恢复都没接通。
- **EAP 组件写在组件层**（2026-10-05 用户定的）：xyz.Components 里一个 SEMI 标准一个组件（E30 / E87 / E40 / E94），设备侧的命令接口和上报口
  （ILoadPort、IJobManager、IE87 / IE84 / IE40 / IE94 回调、IE84Provider）放 **xyz.Components\Interfaces**，EAP 组件才看得到——**已经挪好了**（2026-10-05）。
  用户的选择：EAP 走接口拿到的动作对象跟本机一样是 `ModuleOperation`（不另造"小接口"），所以 ModuleOperation 一组也挪到组件层
  （`Components\Operations`）；组件层改成引用 xyz.Shared（错误码、Job DTO），内部成员只对 xyz.Modules 开放。

## EAP 各标准（2026-10-05 做完，用户："开始吧"，顺序 E30（连 E39、HSMS 按 S/F 分发）→ E87 → E90 → E40 / E94；E116 以后再说）
做法在 backend.md §4「EAP」。下面是做的时候**我替用户定的**（交付时说了，等用户确认；用户改了就改这里）：
- **结构**：sc.xml 新增 `Eap` 节点（`EapComponent`），`Hsms` 挪到它下面（原来在顶层；链路状态 SV 的编号会换新的，老的停用保号），
  下面 `E30` / `E39` / `E87` / `E90` / `E40` / `E94` 一个标准一个组件。原来 HsmsComponent 里的 GEM 答话（S1F13、S1F3、S5F1……）全挪进 E30，
  HsmsComponent 只管链路和按 S/F 分发。MDLN / SOFTREV 从 Hsms 节点挪到 E30 节点。
- **E30 默认**：开机 OFF-LINE / HOST OFF-LINE（等 Host 发 S1F17 上线），上线进 REMOTE；缓存默认什么都不缓存（Host 用 S2F43 指定才缓存，
  免得不懂缓存的 Host 重连后收不到事件）；S2F31 对时默认只答收下、不改本机时钟（SC `ApplyHostTime`）；S2F41 本机没有远程命令，一律回 HCACK=1；
  Host 的动作命令（载具动作、建 Job、Job 命令、改 / 建 / 删对象）要 ON-LINE REMOTE，查询 ON-LINE 就行，S2F15 改 EC 在 LOCAL 也收。
- **E87 照老 CTC 重写**（2026-10-08，用户："按照老的 CTC 那种来写"，参考微信文件夹 CTC 的 `FrameworkLocal\FACore_GTX\E87FA`）：
  - **结构**（用户选"拆成几个状态机类"）：每个 LoadPort 一个 `E87Port`，挂 6 个小状态机（搬运、存取方式、关联、载具 ID、槽图、取放），
    共用 E87 里的小基类 `E87StateMachine`（转换表 + 进状态时按"从哪来"报事件；框架的 BaseStateMachine 在模块层，组件层用不到）。
    载具状态就是端口上这几个状态机的状态，没有单独的载具对象和载具表；设备的事实（在位、槽图、槽数、忙闲）直接问 LoadPort。
    E87 只自己记载具号（拿走时 LoadPort 已清号，#21 还要带）和"Host 取消 / 放行了这一盒"。
  - **流程照 CTC**：读到号一律等 Host；ID 认定就 Load（没有开关，`AutoLoad` SC 删了）；槽图一律等 Host，设备不自己认定（#13 删了）；
    第二次 ProceedWithCarrier 带了槽图就跟读到的比，对不上回 CAACK=3，片号表当场写晶圆账；ID 阶段带的槽图 / 片号表不用（记日志）。
  - **IN ACCESS 改回 Load 好就算**（用户选"照 CTC"，LoadPort 模块在 Load 完成时调 `Carrier.NoteLoaded`，原来叫 `MarkInAccess`）：后果是 Load 以后 Host 不能取消，
    槽图核对不过要操作员 Unload（CTC 也这样）。取放过、没判完成就 Unload 的记中断，这条不变。
  - **Host 动作**：ProceedWithCarrier、CancelCarrier / CancelCarrierAtPort（不在取放才收，卸下来、放行、取消关联）、
    CarrierRelease（不在取放才收；AutoUnload 关着时干完的靠它卸）、CarrierReCreate（等取、没取放过的才收：删对象、重新读码）、
    S3F25 启停用 + 改存取方式、S3F27。端口在动作（Load 中）时取消、放行回 CAACK=2。
  - **三处保留我们自己的做法**（用户比过 CTC 后同意）：状态机不开线程，在 E87 锁里同步转；E84 来问搬运状态（`IE84Provider`），不在状态机里开关 HO_AVBL；
    EC `PortPollMs` 定时查，每个状态都查（CTC 只在两个状态里查，会卡住）。设备停用、出错在 E87 里报挡着，停用只由 Host 说了算（CTC 一样）。
  - **删掉的不要加回去**（2026-10-08 用户砍的）：Bind / CancelBind、CarrierNotification / CancelCarrierNotification、端口预约、载具号重复检查（改成记日志不建）、
    E39 的 Carrier / Port 对象、端口 SV、夹紧 / 松开事件和 Homed 回调、用途属性。E84 的东西保留（用户："E84的东西要保留的"）。
  - 不支持：读写载具标签（S3F29 / 31）、内部缓冲设备才有的动作。还没做：EAP 起来时端口上已有的盒子补报（见上面 LoadPort 那条）。
- **E90**：片对象在槽图认定（料到了）以后才建，Host 给的片号先写进晶圆账再建；工艺状态照账：完成 → PROCESSED、没做成 → REJECTED、中止 → ABORTED，
  出去转了一圈没做就回来的 → SKIPPED。Job 结束时没投的片**不**写成跳过（账上一改，这些片就建不了新 Job 了）。没有读片号的设备，片号核对不做。
- **E40 / E94**：建 PJ 的料只收一个载具加槽号，**料可以还没到**（2026-10-08 用户定的，对标 CTC；见「Job」的"料没到先建 PJ"）；
  不支持配方参数、PJ 暂停事件、CJ 改回片地方（MtrlOutSpec 只能空 = 回原槽）。
- LoadPort 上原来声明了没人报的"FOUP 到达 / 移除"事件，现在经 E30 真的报了（组件基类加了 `RaiseEvent`）。
- **还没做**：GEM 控制状态的界面（E30 要求操作员看得到在线 / 离线、本地 / 远程，能切；后端接口 `RequestOnline` / `RequestOffline` / `RequestRemote` 有了），
  要先出样稿；E116。

## Host 远程管配方（E30 工艺程序管理 S7，2026-10-07，用户："因为远程 eap 肯定会控制的"）
- **用户定的三条**：流程配方、工艺配方**都归 Host 管**；配方内容**原样传 JSON**（不做 S7F23 / F25 带格式的参数）；
  REMOTE 时锁不锁本地编辑**做成 SC**（用户："这个做成一个 sc 不就好了"）——`Eap.Recipe.LockLocalEditInRemote`，默认 False（本地照样改、改了报 Host）。
- **配方号不加前缀**（用户："先都不需要，全部拿掉，未来需要的话也是在 sc 里面对应组件里面增加"）：配方号就是配方名。
  两个库同名的不管（用户："流程配方 SC1 是不可能存在的"），不加"两个库名字不许重"的检查；Host 下的新名字看 JSON 样子分库（这一条绕不开）。
- **SEMI 分工**（用户问过"谁管"）：E94 管 CJ、E40 管 PJ、E87 管载具、E90 管片的路径转移和片的工艺状态、E30 管配方增删改（S7）；
  腔体一级的工艺过程（哪个腔、哪一步）是 E157，还没做，客户 GEM 要求里有再做。
- **接口拆分**（2026-10-07）：流程配方、工艺配方分别用 `ISequenceComponent` / `IProcessRecipeComponent`，方法按各自对象命名；
  上报口用 `IE30Callback` 的 `SequenceChanged` / `ProcessRecipeChanged`，变更类型用 `ChangeKind`。
- Eap 下单独一个组件 `Recipe`（`E30RecipeComponent`，E30 已经 1800 行不再往里塞）；PPBODY 发 B（UTF-8），收 A / B；
  S7F17 空表照标准全删；"配方变了"本地、Host 改的都报；改名报旧名删了 + 新名建了。
- 库里配方文件还是 XML（`001.xml`），JSON 只是给 Host 的格式；Job、腔体用的是配方快照，Host 改了配方不影响在跑的。

## Job（SEMI E94 CJ / E40 PJ，2026-10-04 ~ 10-05 做的，2026-10-06 按用户的设计重做，需求文档《通用 Job 组件功能需求文档》v0.1）
- **结构**（2026-10-06 用户定的）：Job 组件 `JobManager` 管着 CJ 管理、PJ 管理（各有接口 `ICjManager` / `IPjManager`，各管自己的队列和 SEMI 状态机；
  转换表就写在各自里面，跟 LoadPort 的状态表一个写法，不另造通用转换表类），CJ / PJ 建好、状态变化都报 EAP；下面挂**任务组件**
  （sc.xml `Task`，平台给基类 `BaseTaskComponent`，**机型自己写一个继承它**，35021 是 `TaskComponent`）和**调度引擎**（`Scheduler`，换 Type 换策略）。
  搬运管理 `TransferManager` 还是手动、Job、人工恢复共用的唯一执行口，**不学 CTC 拆 AutoTransfer / ManualTransfer**。
  **JobManager 里面有 CJ 管理、PJ 管理**（用户："应该是 jobmanager 里面有 PjManager 和 cjmanager，你的设计反了"）：两个管理不拿 JobManager，只管自己的队列、状态机、命令，转了发事件；
  建 Job、给设备发中止、任务表收场、告诉 LoadPort、报 EAP 这些牵扯别处的事都在 JobManager。推动转换的枚举叫 `ControlStateAction` / `ProcessStateAction`
  （用户定的名，跟 LoadPortAction 一个意思）。
- **命名**（2026-10-07 用户逐个点名改的，别改回去）：类型名写全不用 SEMI 缩写——`ProcessJobState` / `ControlJobState`、`ProcessJobCommand` / `ControlJobCommand`、
  `ControlJobAction`、`ControlJobEnding`；方法名直白——建叫 `Create`（查完直接进队列，不分 TryBuild + Add）、按 LoadPort 找叫 `FindByLoadPort`（原来叫 `On`）、
  CJ / PJ 管理按后续参考工程使用对象状态动作；对外通用命令入口为 `ExecuteControlJobCommandAsync` / `ExecuteProcessJobCommandAsync`（`IJobManager`）；状态变了的事件叫 `StateChanged`，
  报 EAP 的叫 `ProcessJobStateChanged` / `ControlJobStateChanged`（原来都叫 Transitioned）。CJ 后续按用户要求改为字典状态表，其他分支用传统 switch 语句（不用 switch 表达式）；
  转换号、E39 名字的 80 / `?*~>:` 直接写数字加注释，不另起常量；自动转换转到不再转为止，不设轮数上限。
- **任务表**（用户的设计）：建 PJ 时照流程配方给每片生成一行任务，一行 = 一片的整个周期——从 LoadPort 取片、放进腔体、腔体做工艺、从腔体取回、放回 LoadPort，
  每个都是一个任务，按顺序走。**取和放一起定**：一趟搬运 = 取 + 放（目标空着、占住了才取）。多个站点的是**站点组**，放片那一刻在组里挑一个能放的。
  **站点自己声明支持的任务**（`ITransferStation.SupportedTasks`；框架给基础任务 `StationTaskAction` Pick / Place / Process：LoadPort 取放，腔体取放 + 工艺），
  要用到的站点不支持就不建；以后加新站点（对准器、冷却台）模块自己声明、自己执行，生成规则不一样在机型的任务组件里重写。
  任务的状态 `WaferTaskState`：Waiting（用户把 Pending 改的名）/ Running / Done / Error / Cancelled。
  **直接按 WaferTask 执行**（2026-10-07 用户继续简化）：当前设备操作挂在任务本身的 `Operation` 上，调度从每行当前任务收进度，不再维护 `Move` / `Work` 及两份执行列表。
  取片先定好并占住放片目标，实际取片确认后取片格完成、操作引用转到放片格，工艺收尾后工艺格完成；一行同时只有一个任务 Running。
  **取消搬运单层**（用户："搬运单层可以合并"、"这个搬运单不需要这个概念"）：不留订单、单号、回执、搬运结果模型或历史，也不另排队；搬运执行口直接返回现有设备操作。
  晶圆搬运失败保留的资源按晶圆内部标识人工释放。**不设机内片数上限**（用户明确否掉该参数）：由目标空槽与设备占用自然限制投片。
- **先简洁、需要时再扩展**（2026-10-07 用户："现在就是要把job整个以及调度做的简洁，后面需要再扩展"）：保留实际使用的 CJ / PJ、每片任务行、设备操作和存库流程，不提前加策略层、执行记录层、占用快照类。
  调度只选目标、启动当前任务、回写状态；占用以搬运管理的记录为准，不再重复维护本拍槽位 / 机械手集合，手臂选择不在调度和搬运里各做一次。
  站内任务直接调用站点自身校验的 `StartTask`；日志直接放在任务状态方法里，不保留仅为日志转发的任务事件。双机械手可并行服务不同站点，同一站点仍互斥。
- **任务出错不跳过**（用户："工艺失败怎么能判定跳过呢，肯定是 error，然后后面的继续等待"）：工艺没做成、动过手的搬运失败、片不在该在的地方 → 那一格 Error，
  这片后面的任务等着；**别的片照常跑，整机不停**（用户："能够执行的任务肯定是接着跑"）。人恢复好以后在界面点**重做**（退回等着做）或**标记完成**
  （人做完了；取放要片在账上正好在该在的地方），接着跑。人手动做了什么系统不去认（用户："完全是手动操作，不用管"）。
  腔体出错后自动下线先不做（属于报警之后的动作，以后放报警那边做）。救片以后也走任务：片走一个固定的流程，跟正常任务一样上锁、做站点交互、记账（还没做）。
- **一套 Job 模型，状态照 SEMI**（用户点名）：PJ 照 E40（0 QUEUED/POOLED … 4 PROCESS COMPLETE、6 PAUSING、7 PAUSED、8 STOPPING、9 ABORTING、
  10 STOPPED、11 ABORTED，转换 #1~#18），CJ 照 E94（0 QUEUED … 5 COMPLETED，转换 #1~#13，命令值 1 Start … 8 HOQ，Action 0 SaveJobs / 1 RemoveJobs）。
  CJ 没有停止中 / 中止中状态，收了 Stop / Abort 标着、等 PJ 都结束再走 #11 / #12。
- **暂停**：PJ 暂停 = 停投新片，机内的片照常做完回片（PAUSING），机内没这个 PJ 的片了才 PAUSED，可恢复（用户："肯定是照常跑完"）。
  **CJ 暂停严格照 E94**（用户："那就按照标准做"）：只是不再启动新的 PJ，在跑的 PJ 照常投片做完；要马上停投片就暂停 PJ。本地按钮、Host 一样。
- **停止**：不投新片、机内的走完回片、没投的记未执行，不能恢复。**中止**：中止搬运和工艺操作，设备中止做完（确认了）、在途动作都结束、
  片位都确定（没有出错等人处理的、没有保留资源等确认的搬运操作）才结束（#16）。
- **Auto / Manual / Stop**（用户定的）：Job 随时能建，启动要 Auto；运行中切 Manual 不派新动作（在途的做完）；主界面 Stop 走 Job 中止（见主界面一节）。
- **本地和 Host 一样建**（用户："host 那边和本机自己就是调用的方法还不一样？……如果是这样的话，你的肯定是错的"）：都是先建 PJ（不归任何 CJ，排着）、
  再建 CJ 按顺序收进来，调同样的方法、过同一套检查；PJ 给了 LoadPort 按它找、没给按载具号找。本地一篮按 Sequence 分 PJ（同一个 Sequence 的片一个 PJ），
  整篮一个 CJ，名字用 LotID；中途被拒撤掉已建的 PJ。一个 LoadPort 同时一个没删的 CJ，完成后检测到载具拿走才删（#13）。
- **CJ / PJ 按载具号查**（用户："肯定是按照载具号查询""这个是给那个 eap 用的"）：`IJobManager` 上加按载具号找 CJ / PJ；CJ / PJ 管理里按 Job 名找的还留着（Host、界面下命令带的是 Job 名）。
- **别再加回来的东西**（2026-10-06 用户逐条否掉的，"过度了"）：命令队列类（命令当场执行，跟扫描线程用同一把锁）、请求号去重（重发靠正常的检查拦：名字在用、
  LoadPort 上有 Job、状态不收这个命令）、自己的结果类（用 `HandleResult`）、`IWaferOwnership` 这种接口、Factory / Environment / Book / Store / Restart
  这些拆出去的类（并回 JobManager、CJ / PJ 管理）、partial 拆文件、通用转换表类、同时跑几个 CJ 的上限和 CJ / PJ 个数上限（一个 LoadPort 一个 CJ、一片只归一个 PJ，
  个数自然有数）、"在等什么"的原因、每片的结果（看晶圆账 `WaferInfo.ProcessState`）、任务的开始 / 结束时间、手动标记、模拟标记、任务组件里收搬运单结果（单号对任务、"取片做完"事件）、
  PJ 自己的配方结构（直接存流程配方快照 `SequenceData`）、记谁建的（Local / Host，存了没人看）、能从任务表推出来的显示字段（有没有出错、当前第几个任务、路线几站）。
- 自己定的（交付时说了）：腔体要 Online 才派片（LoadPort、机械手不看）；回片槽建 PJ 时定（源 LoadPort 在最后一步里回原槽，
  不在就放最后一步勾的、载具在的第一个 LoadPort 的同号槽）。
- **Job 页不另开一级菜单**（2026-10-05 用户定的）：放在主界面下面做二级菜单——总览（就是原来的主界面，开机还是先到它）、Job。
  页面样稿 v2 在桌面 `Job页样稿-v2`（CJ → PJ → 晶圆 三块从上到下，按钮在各自标题条上），等用户确认；样稿里的"出故障红条 + 恢复入口"要改成每一格任务的重做 / 标记完成。
- **Job 存库**（2026-10-07 用户："界面上都是显示的数据库的数据……该存库就存库"）：CJ、PJ 各一张表一个 Job 一行，实体继承现成的 `BaseEntity`（用户："目前的应该有可以给你用的"），
  每片的任务明细放 PJ 那一行的 JSON 里（不另开一片一行的表）；内存里不留历史（`TrimHistory`、EC `HistoryKeepCount`、全貌里的 History、`job_snapshot` 都删了），历史查库。
- **重启以后**（2026-10-05 用户定的，行业通常也这么收场）：Job 不接着跑；库里上次没做完的记成中止（标着重启）；机内的片人到现场确认片位后收回，
  再重新建 Job；没做的片要不要做、做了一半的返工还是报废由 MES / 工程师定。**全部回片 2026-10-06 删了**（用户："整体回片先不做"）。
- **料没到先建 PJ + 等 E87 槽图认定**（2026-10-08 用户定的，对标 CTC，两件一起做）：Host 按载具号建 PJ 时载具不在端口上、或还不能取片，
  PJ 照样建、排队（报 #1），只记载具号和要的槽号（`ProcessJob.Slots`，空 = 料到了取全部有片的槽），任务行先空着；CJ 能收这种 PJ，口（`ControlJob.LoadPort`）先空着、
  按载具号认，同一载具不能有两个 CJ（`job.carrier_busy`）、同一载具的槽不能两个 PJ 要（`job.slot_claimed`）；指定了 LoadPort 的 CJ 不收没定片的 PJ。
  扫描里每拍：载具到了、`IsCarrierReady` 就定片（跟当场建同一段检查，生成任务行、登记片归属、给 CJ 填口）；CJ 要下面的 PJ 都定了片才转执行 / 等启动。
  **定不了片**（用户选 A）：PJ 留在排队、报警（JobManager 的 `MaterialUnusableAlarm`，报警文字固定，哪个 PJ、什么原因写 Job 日志，同样的原因只记一次），
  等人或 Host 收场（停掉 CJ 连带删排队的 PJ，或取消没归 CJ 的 PJ）；每拍还会再试，复位了没解决会再报。
  **槽图认定**：`BaseLoadPortModule.IsCarrierReady` 加一条——接了 EAP（`E87Callback` 不为空）时槽图要被 Host 认定（`SlotMapStatus == Verified`）才能取放，
  没接 EAP 读到就算。后果：Hsms 启用但 Host 没连上时 E87 一直等 Host，Job 也跟着等（本地跑把 Hsms 关掉）。EAP 的 SECS 翻译层（S16 / S14）见「EAP 各标准」。
  还没做：Job 页（样稿 v2 等确认）、救片任务、
  设备动作中禁止改账的联锁（用户押后）。腔体按工艺配方真执行（照每一步去转、摆臂、喷液）用户说不做。
- **Job 创建接口（2026-10-07 用户明确）**：PJ 独立创建，参数包含 LoadPort、PJ 名、槽位集合、Sequence、LotId；提供独立取消 PJ。CJ 创建接收 LoadPort 和已有 PJ 的名称集合（一个 CJ 可以有多个 PJ）。CreateJob 使用已经创建的 PJ，内部创建 CJ 并关联，不重新生成 PJ 和任务。CJ/PJ 管理提供明确的停止、中止、暂停、恢复方法，仍使用各自状态机；Pick、Place 保持独立 WaferTask。
- **创建参数与 PJ 启动配置（2026-10-07 用户明确）**：不用 ProcessJobSpec / ControlJobSpec 这类参数包装，内部方法直接收参数；PJ 的 AutoStart 由 SC `ProcessJobAutoStart` 配置，不放在创建请求或每个 PJ 对象里。本地和 EAP 统一使用设备配置。JobService 按区域管理，用传统 if / return，不封装 NotInstalled / Reply。
- **Job 命令入口（2026-10-07 用户：重复就重复先，没关系）**：去掉 Execute / WithProcessJob 的委托包装，各入口直接写检查、锁超时、执行、发布、异常处理；PJ 创建合并到 CreateProcessJobAsync，不为减少重复增加调用层次。保留原有锁和状态机行为。
- **设备状态判断归模块（2026-10-07 用户明确）**：载具是否可取放片由 BaseLoadPortModule.IsCarrierReady 判断（模块启用、载具到位、IsLoaded；2026-10-08 加：接了 EAP 时槽图被 Host 认定）；JobManager 直接读取模块结果，不在 Job 内重复解释 LoadPort 状态码。
- **CJ 简化（2026-10-07 用户明确）**：删除 CJ / PJ 的 CarrierInstance，不额外比较载具对象 GUID；完成的 CJ 在检测到来源 LoadPort 载具不在位后删除。CJ 的 AutoStart 也由 SC ControlJobAutoStart 配置（默认 False），从运行对象、创建参数和请求 DTO 删除；E94 StartMethod 不覆盖 SC。
- **Job 发布简化（2026-10-07 用户明确）**：删除 _dirty、_version、_publishedTasks 及任务表的 Version / Touch 计数；JobListDto 不带版本号。扫描直接发布当前快照，命令执行后也直接发布，存库仍通过已有后台线程合并最新记录。
- **CJ 按载具号查询（2026-10-07 用户明确）**：从 CjManager 管理的真实 CJ 对象查，不能查发布快照；JobManager 加锁调用 FindByCarrier 并返回 DTO 副本。
- **CJ 参考结构统一（2026-10-07 用户明确）**：按桌面参考工程 ControlJobManager / ControlJobStateMachine / BaseStateMachine 统一，替代之前的逐命令查字典设计。`Dictionary<string, CjEntity>` 保存私有实体（CJ + 独立状态机），外部只拿 ControlJobs；Add 只注册、Queue 入队、Get 按 ID 查询、Remove 检查 ID 和对象引用，旧对象不能操作同 ID 新对象。不保留订单计数，不依赖 PJ；外部命令及 PJ 协调在 JobManager。
- **CJ 状态表和基类（2026-10-07 用户确认参考）**：CjStateMachine 不持有 Job，继承 BaseStateMachine<TState,TAction>，类内 BuildTransitions 建立字典，key=(state,action)，value=StateTransition<TState>。基类提供 CurrentState、OnStateChanged、StateChange，并执行 OnEntry / PreCheck / ProcessState / Execute / TargetState / OnExit / ErrorHandler；管理器用对象参数提交 Queue / Select / Activate / Pause / Resume / Complete / Abort / FinishAbort / Rollback 等动作，订阅通知同步 CJ 状态和时间。不向 JobManager 暴露状态机，不提供自动 Advance。
- **CJ 内部与 E94 状态（2026-10-07 用户同意一起统一）**：内部枚举按参考 Created=0、Queued=1、Selected=2、Executing=3、Paused=4、Aborting=5、Aborted=6、Completed=7、WaitingForStart=8；补全本设备的等待 Start、Stop 收尾、排队删除及终态删除路径。Created / Aborting / Aborted 为业务状态，不能据此认定更符合 E94。DTO.State 用内部编号，DTO.E94State 单独映射标准六状态：中止收尾期间保持原 ACTIVE 状态，Aborted 报 COMPLETED=5；事件转换号、历史数据库保持原 E94 编号。保留 ControlStateAction 用户命名。
- **CJ 顺序（2026-10-07 用户明确）**：CJ 字典保持 Add 顺序，删除 HeadOfQueue 接口及重排逻辑，不主动调整；外部 HOQ 命令明确拒绝，不伪装成执行成功。
- **CJ 转换号（2026-10-07 用户明确）**：删除 ControlJob.TransitionNumber；转换号属于本次转换通知，不缓存进 CJ 对象。CjManager 在通知时用局部变量确定编号，通过 StateChanged(ControlJob, ControlJobState, int e94TransitionNumber) 传给 JobManager 上报；不新增事件参数类。CompletedBy / EndedBy 仍记录完成及删除原因。
- **PJ 与 CJ 风格统一（2026-10-07 用户授权修改）**：PjManager 使用 Dictionary<string,PjEntity> 保存 PJ 与独立 PjStateMachine；私有实体不对外暴露，接口用 Add / Remove / Get / ProcessJobs，动作接收 ProcessJob 对象。Add 只登记，Queue 入队并登记晶圆归属，Remove 检查对象引用并释放归属。状态机继承同一个 BaseStateMachine，转换表写在类内，保留现有 E40 状态数值与手动启动、暂停、停止、中止收尾行为；新增内部 Created=-1，不上报 Host。暂停恢复目标留在独立状态机，不存进 ProcessJob。删除 PjManager 的 Execute、公开 Fire、Advance、NextTrigger、AbortLoose、UpdatePermissions；任务进度、PJ 启动条件、设备收尾与行许可由 JobManager 处理。StateChanged 传 (ProcessJob,ProcessJobState,int e40TransitionNumber)，编号只随本次通知传递，不存成运行对象字段。
- **JobManager 跟随优化（2026-10-07 用户授权）**：使用传统方法体和区域管理，明确命令方法各自直接执行 CJ/PJ 对象动作，EAP 编号入口仅选择对应方法；不增加委托执行包装或 partial 拆文件。扫描分为 PJ 推进、CJ 推进、任务许可，保留先 PJ 后 CJ 及设备收尾判据。创建检查 Queue 结果，失败撤销本次登记、任务或关联，保留先前创建的 PJ；无数据变化的创建校验失败不发布。删除 `_snapshot` 缓存，查询在 Job 锁内生成独立 DTO。
