# ToolBuilder — 设备配置、腔体与管路布局工具

## 腔体布局

打开装机配置并加载元件来源后，在左侧选择腔体（例如 Chamber1），进入「腔体布局」Tab。布局直接使用 ToolBuilder 当前内存中的配置，尚未保存的元件变更也会同步到布局。

- 根据 DLL 中的 `IChamber`、`ISwingArm`、`IChamberLift`、`INozzle`、`ISpinMotor`、`IChamberDoor`、`IChamberBowl` 接口元数据识别设备，不执行后台元件，也不修改框架源码。
- 装机配置决定设备清单。配置了几个 Arm、Lift、喷嘴或 Bowl，就生成对应的独立图元；布局页只调整位置和尺寸，不提供额外的设备库。左侧元件树与画布选中项双向同步。
- Arm、其升降气缸、每一路喷嘴分别编辑；子元件位置相对所属 Arm，移动 Arm 时跟随。管路默认随臂长计算，也可在「尺寸」中关闭该选项并单独设置长度。
- 支持同一项目的多个腔体和不同 Type；切换腔体保留各自位置及未保存状态。Bowl1、Bowl2 等直接作为腔体下的同级元件，各自生成图元和独立升降模拟，不需要新增包裹节点。
- 「模拟动作」按当前元件显示：Arm 为 Home/中心，Lift、Bowl 为上升/下降，喷嘴为喷液，SpinMotor 为旋转，Door 为开/关。模拟只作用于编辑器画面。
- 顶部保存 / Ctrl+S 同时保存 machine.xml、腔体布局和管路布局；保留外部修改检查、备份及失败回滚。另存为携带已有布局。
- 腔体布局单独保存在配置目录的 `ChamberLayouts/<配置文件名（不含扩展名）>/<完整腔体路径>.xml`，路径经过文件名转义；例如 `ChamberLayouts/machine/Clean.Chamber1.xml`。不向 machine.xml 增加布局参数。设备运行界面的布局加载保持原样。

腔体编辑页源码在 `ToolBuilder.ChamberLayout`，作为 ToolBuilder 内部控件加载。验证在输入配置的副本上进行：

```powershell
.\app-v3\ToolBuilder.exe --chamber-test ..\..\xyz.35021\Service\xyz.35021.Module\Config\machine.xml .\qa\chamber ..\..\xyz.Core\Service\xyz.GrpcHost\bin\Debug\net10.0
```

检查元件独立性、模拟动作、树与画布同步、多层 Bowl、配置增删及改名、切换腔体、统一保存、外部冲突、失败回滚、另存为与重新加载，并生成真实 WPF 截图。

## 管路布局

在左侧选择含 TankElement 的模块（当前工程为 CDS → IPA），进入「管路布局」。选中槽元件本身仍进入 machine.xml 参数页。

- 首次按 machine.xml 中的槽生成布局；支持多个槽，当前 IPA 展示 Tank1、Tank2。
- 拖动槽或外部接口，点击连接口绘制管路，双击管线添加拐点，拖动拐点，Delete 删除拐点或管路。保留撤销/重做。
- 点击管路或其文字，右侧编辑文字、管径、壁厚、颜色、透明度、介质和动画等属性；文字随路径移动，留空隐藏。不显示固定接口文字。
- 顶部「收起元件树」和布局工具栏「收起属性」可以扩大画布。页面沿用 ToolBuilder 浅色主题，框架主题仅在图形编辑器局部使用。
- 切换 machine.xml 节点保留布局及待应用属性；顶部保存 / Ctrl+S 同时保存 machine.xml 和已编辑的布局。校验结果也展示布局错误。
- 布局位于所选 machine.xml 同目录的 `CdsLayouts/<配置文件名（不含扩展名）>/<元件树路径>.xml`，例如 `CdsLayouts/machine/CDS.IPA.xml`。文件名转义，布局路径不写入后端 machine.xml 参数。运行中的设备界面尚未接入此文件。
- 保存前检查外部修改，先准备本批所有文件，再替换；原文件放进各自目录的 `backup`。写入失败回滚已写入文件，保留未保存状态；若回滚也失败会明确提示备份位置。machine.xml 另存为会携带已有布局。
- machine.xml 槽名称或数量与已保存布局不一致时会阻止保存，避免把图元误关联到其他设备；目前不会自动删除或猜测重绑。
- 当前仅布局编辑与模拟预览，不含阀门，不连接 PLC、不执行 machine.xml 后端元件。

源码和程序位于 `tools/ToolBuilder`，工程：`ToolBuilder`（界面，程序 ToolBuilder.exe）、`ToolBuilder.Core`、`ToolBuilder.Smoke`。

集成验证：
```powershell
.\app-v3\ToolBuilder.exe --layout-test ..\..\xyz.35021\Service\xyz.35021.Module\Config\machine.xml .\qa\layout
```
验证在独立目录副本中进行，覆盖双槽绘制、属性、切换页面、保存加载、另存为、外部修改、写入回滚和真实 WPF 截图；不会修改输入 machine.xml。

独立 WPF 工具，按现有 xyz 架构编辑 `machine.xml`。源码和可运行程序位于 `tools/ToolBuilder`，machine.xml 元数据读取仍不执行后端元件；管路编辑页引用 ..\..\tools\CdsLayoutEditor，共用框架原生 TankVisual3D 和 FluidRouteVisual3D。

## 启动与选择文件

双击 `Start-ToolBuilder.cmd` 或 `app-v3\ToolBuilder.exe`。需要 Windows 和 .NET 10 Desktop Runtime；开发构建需要 .NET 10 SDK。以下命令从仓库的 tools/ToolBuilder 目录执行。

1. 点击「打开 machine.xml…」，选择任意位置的 `machine.xml`。该文件所在目录就是配置目录。
2. 未记录元件来源或记录已失效时，自动查找 machine.xml 同目录、上级部署目录、所属源码项目的最新编译输出，以及当前框架后端宿主的依赖目录；有效的手动来源优先保留。找不到时，点击顶部或添加元件窗口里的「元件来源…」，在弹出的窗口选择目录或 DLL，可以添加多个来源。目录读取本层 DLL 和 `Modules` 下的 DLL；选 DLL 时，其同目录 DLL 用于解析依赖。源码目录必须先编译成 DLL。
3. 左侧顶部「＋ 根元件」「＋ 根分组」直接添加 machine.xml 最外层节点，空 machine.xml 也可使用，不依赖选中节点。下方显示元件树：有 Type 的节点统一显示「元件」，没有 Type 的节点统一显示「分组」。每个节点用一行横向显示类别、名称和说明；长说明省略，悬停可查看完整路径和说明。选中节点会突出显示并展开下级。右侧编辑名称、Type、初始化顺序和 machine.xml 参数；子节点统一从左侧选择。元件从 `[Element]` 发现，配置项从 `[Machine]` 读取，包括基类公开属性、默认值、说明、分组、必填和枚举。
4. 点击「校验配置」，修正错误后保存。提示不会阻止保存；元件类型没有加载时，只能做基础校验，需要补选 DLL 后才能核对元件字段。

当前 xyz 工程示例：

- machine.xml 源文件：`..\..\xyz.35021\Service\xyz.35021.Module\Config\machine.xml`
- 服务端元件目录：`..\..\xyz.Core\Service\xyz.GrpcHost\bin\Debug\net10.0`，包含平台 DLL 和 `Modules\35021` 机型 DLL。
- 可额外选择机型输出目录：`..\..\xyz.35021\Service\xyz.35021.Module\bin\Debug\net10.0`。
- 现场配置应选择部署目录中的 machine.xml；实际后端配置目录由宿主旁的 `Paths.json` 指定。修改源码 machine.xml 是否进入部署目录由现有部署流程决定，工具不会自动同步多份文件。

工具会尝试发现 machine.xml 同目录和父目录的 DLL；保存每份 machine.xml 的元件来源及最近打开记录，下次可以重新选择。记录文件位于 `%LOCALAPPDATA%\ToolBuilder\preferences.json`。

## 编辑能力

- 右键节点可以添加根元件、添加子元件、添加分组、复制或删除。展开/收起直接操作节点箭头；左侧没有常驻工具按钮或元件来源面板。
- 添加窗口只有「元件」和「分组」两种类别。选择元件时，可搜索全部 DLL 元件（包括模块和驱动），选择类型，展示用途、DLL 来源及默认配置；确认后创建带 Type 和默认 Value 的元件。选择分组时，直接创建，不写入 Type 或元件默认参数，也不依赖 DLL。添加窗口不填写名称，创建时自动生成同级不重复的默认名称，随后选中新节点并聚焦主界面的「节点名称 (Name)」输入框，名称和参数在此统一修改。右键「添加分组」也使用此窗口，默认选择分组。
- 例如在 Robot1 下添加气缸：选择 Robot1 → 顶部「在 Robot1 下添加元件」→ 搜索「气缸」并选择 CylinderElement → 添加并配置，随后在主界面填写 Name 并设置实际 IO 索引。
- Type 可以从已加载元件中选择或手动填写；空 Type 保留现有配置节点语义。
- 所有布尔配置使用不可自由输入的 true/false 下拉框，包括 machine.xml 参数、配方必填与 Bool 默认值、管路反向流动/液芯/动画。没有元数据的原有 true/false 参数也提供下拉；已识别的字符串参数仍保留文本编辑。未配置值不会因打开页面而写入；配方 Bool 默认值可右键清除。
- 默认值来自加载 DLL 中的 [Machine] 声明；布尔选项、设定值和默认值统一显示为小写 true/false，编辑和新建参数按小写写入。已有节点的参数编辑区同时展示显式配置和代码默认值，只有编辑后才写入尚未配置的默认项；新创建元件会直接生成默认配置。「清除覆盖 / 删除参数」删除显式 Value，让已识别参数回到代码默认值。
- DI 元件的 TriggerValue 是 bool：true 表示高电平触发，false 表示低电平触发。只认 true/false，旧的 High/Low 写法不再兼容，工具校验和后端装配都按非法值报错。
- DI/DO/AI/AO 索引直接在文本框输入点号，不提供弹窗选点按钮；继续按参数类型和索引范围校验。
- 点击节点时默认显示它自身的参数；子元件统一从左侧选择，右侧不再显示重复的子元件列表。机械手示教点（Robot1 下 Size12 / Size8 分组里的 TeachPointElement，一个站点一个）按普通元件显示和校验：Number、Y、Direction、PickArms、PlaceArms 都从元件的 [Machine] 读，没有专用站点页（2026-10-24 删了）。
- 工艺配方的字段表（ProcessRecipe 元件下的 Fields 分组，一个子节点一个字段）：点 Fields 进「配方字段」页，一行一个字段 = 工艺配方页上的一列，从上到下就是从左到右。列：字段名（写进配方文件，别随便改）、中文名、英文名、类型（整数 / 小数 / 下拉 / 开关 / 文本）、单位、下限、上限、小数位、默认值、数据源、必填。整数、小数才能填单位和上下限，小数才能填小数位，下拉才能填数据源；换类型时自动清掉新类型用不上的参数。时间（Seconds）是步骤时长，字段名、类型、必填锁着，删不了。可以添加、删除、上移、下移。
- 下拉的数据源可以直接在格子里输入，也可以点「…」选：直接写选项用英文逗号隔开（如 `Time,Scan`）；`Devices:类型[.属性][@字段名]` 从腔体里装的设备取（如 `Devices:SwingArmElement` 取摆臂名，`Devices:NozzleElement.Chemical@Arm` 取所选摆臂上喷嘴的药液）。字段表的校验跟后端开机时一样：字段名格式、必须有 Seconds（小数）、上下限、小数位、必填、数据源写法、@ 跟的字段、默认值合不合规；工艺配方元件下没有 Fields 也拦下。
- 当前节点已有 Alarm 覆盖时显示对应编辑页。原有 Role 和其他未识别 XML 属性继续保留；当前普通装载器没有统一处理 Role/Alarm，所以不默认展示这类配置入口。
- 「XML 对照」并排显示打开/上次保存的原文和当前完整 XML，均只读。

复制腔体后请重新检查名称、IO 索引、PLC 数据路径和机械手示教点的名字。工具不会自动猜测接线、地址偏移或重写其他节点的引用，但校验会拦下重复占用：
- DO/AO 索引被多个元件同时使用、轴命令块（SendPlcDataPath）重复：算错误，不能保存（后者后端启动时会报「轴命令块路径重复」起不来）；
- DI/AI 索引、轴反馈块（ReceivePlcDataPath）重复：只提示，可能是有意共用。
重复的每一处都单独列出，双击定位到对应节点。

改元件名（带 Type 的节点）时，后端按元件全路径或模块名记的东西不会跟着改。工具打开文件时记下每个节点的原名，校验结果里逐条列出改名的影响，保存前再弹窗确认（默认按钮是"否"）：
- ec.xml：旧名字下有几个 EC 值、其中几个现场调过——后端按新名字重新生成默认值，旧值要手动搬；
- 编号：EC/SV/报警/事件编号按元件全路径分配，改名后停用旧号、分新号，EAP 那边对的编号会变；
- 数据曲线：列名按元件全路径生成，改名后从新列开始记，旧历史接不上；
- 机械手示教点：示教点名要跟模块名一致，不跟着改的话机械手到不了这个站点；
- IO 点表：Module/Component 列还写着旧名字（IO 页按它分组；绑定按点号，不受影响），仿真器那份点表也一样；
- machine.xml 里别处写的旧路径（如数据曲线的 Exclude）。
ec.xml 和编号表跟部署目录的 machine.xml 在一起，按旧名字数出具体个数；源码里的 machine.xml 旁边没有这些文件，只给出一般性提醒。分组节点（不带 Type）不进元件路径，改分组名不提示；新加、复制出来的节点不算改名；只改大小写不算改名；保存后新名字就是原名，不再重复提示。

## 保存和生效

覆盖文件前，把原文件备份到同级的 `backup` 子目录（带时间戳的 `.bak`，不会自动清理，多了可以整个删掉），随后原子替换；xyz 仓库已把 `Config/backup/` 加进 .gitignore。若打开的文件被其他程序改过，拒绝覆盖，可以重新打开或另存为。保存保留注释、未识别节点、属性和元件参数，采用 UTF-8 BOM 编码，换行符（CRLF/LF）保持原文件的写法；没改过的文件原样存回。新增、复制、追加参数、删除、移动的节点按原文件的缩进（machine.xml 是 Tab）各占一行，删除不留空行。

元件 DLL 仅使用 MetadataLoadContext 解析元数据，不执行元件构造函数、静态初始化、设备连接或扫描线程。无 Type 的预构造子元件无法仅从 DLL 元数据推断其构造关系，因此已有参数保留为原有参数。

machine.xml 装配参数通常需要重启后端生效；运行中可调的 EC 参数继续在原客户端 EC 设置页管理。本工具不启动后端，也不修改 `ec.xml`、GEM 编号表或点表 CSV。

快捷键：`Ctrl+O` 打开，`Ctrl+S` 保存，`Ctrl+Shift+S` 另存为。

## 构建与验证

执行 `build.ps1` 发布到 `app-v3`。开发机需要 .NET 10 SDK；发布为依赖运行时的版本。

核心功能验证：

```powershell
dotnet run --project .\ToolBuilder.Smoke -c Release -- ..\..\xyz.35021\Service\xyz.35021.Module\Config\machine.xml ..\..\xyz.Core\Service\xyz.GrpcHost\bin\Debug\net10.0
```

界面验证与截图（输入 machine.xml 只读，记录也不持久化）：

```powershell
.\app-v3\ToolBuilder.exe --preview ..\..\xyz.35021\Service\xyz.35021.Module\Config\machine.xml .\preview.png ..\..\xyz.Core\Service\xyz.GrpcHost\bin\Debug\net10.0
```

成功日志包括元件数量、上下文页面切换、元件选择窗口、Robot1 下新元件创建和保存重读验证；生成参数页、Robot1 自身参数页、示教点参数页（.teach-point.png）、配方字段页和数据源窗口、添加元件及添加分组窗口截图。核心验证使用专属临时目录进行读写，覆盖备份、外部修改冲突和现有配置往返验证，不修改输入 machine.xml。

当前启动脚本指向 app-v3，发布目录为本机生成，不纳入版本库。新测试：app-v3\ToolBuilder.exe --config-test <machine.xml> <输出目录>，覆盖根节点真实添加入口、自动发现元件、布尔下拉写入与保存重读。
