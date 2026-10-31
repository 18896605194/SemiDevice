# CDS 组件与管路预览（工具原型）

编辑源码通过 `ToolBuilder.CdsLayout` 内部控件库接入 ToolBuilder 的「CDS 布局」Tab；本项目保留独立预览宿主和显示验证入口。已支持与腔体一致的普通 UserControl XAML 查看、打开和导出，见 [CDS XAML 使用说明](CDS-XAML.md)。未修改后台协议或 35021 项目页面。
直接复用平台 `TankVisual3D`、`FluidRouteVisual3D`、`SignalLamp`。阀门、泵、加热器和其他可复用显示件已移至公共前端 `xyz.Client.Presentation/Controls/Fluid`，命名空间为 `xyz.Client.Presentation.Controls.Fluid`，导出的界面不引用工具程序集。

装机配置决定槽、阀门和液位信号的清单。每个阀门绑定自己的完整组件路径，布局不能增加装机硬件。
预览启动时管路数为零：阀组不能说明真实管路数量、串并联关系及走向。
管路必须由布局明确指定起点、终点和途经点；外部接口只是布局连接点，不是新增装机组件。
“载入示例连线”打开随工具附带的 `Example.xml`，其 12 段连线仅是显示示例，不代表 35021 实际工艺管路。

```powershell
dotnet run --project D:\Code\tools\CdsAssemblyPreview\CdsAssemblyPreview.csproj
```

默认只读 `D:\Code\xyz.35021\Service\xyz.35021.Module\Config\machine.xml` 的 `CDS.IPA`。
可通过 `--machine <path> --module <full-path>` 更换药液模块。
Tank、阀门、Sensor、外部接口和管路均可独立选中，右侧采用与腔体工具一致的三个页签：

- **安装布局**：拖动或输入 X/Y 调整位置。管路移动中段，端点保持连接原组件；方形拐点可单独拖动或输入坐标。
- **显示参数**：组件可调整宽度、高度、旋转、文字；Tank 可调槽壁透明度，Sensor 可设置报警显示。管路可调外径、管壁厚度、转弯半径、透明度、液芯比例、颜色、流动速度及方向。
- **模拟动作**：Tank 可模拟补液/排液/供液及液位，阀门可独立打开/关闭/未知（也可双击），Sensor 可独立模拟触发/未触发/未知。

参数提交后立即预览，另存布局及重新打开会保留位置和显示参数；模拟状态不保存到布局。
启用“连接管路”后点击两个蓝色端口创建一段管路，点击组件本体仍可选中并移动。添加外部接口、清空连线、打开及另存预览布局均只作用于布局。
接口在同一水平高度或同一垂直线上时直接连接直管；拖动组件接近对齐时会吸附接口并清除旧拐点。只有接口错开时才生成折线，仍可手动移动管路和拐点。
未连接管路的控件也支持垂直中心线、左边和右边吸附，拖动时显示参考线，松开后消失；已连接的管口对齐优先。
选中 Tank、阀门、Sensor、接口或管路后，可用键盘方向键每次微调 1 个显示单位，Shift + 方向键每次移动 10 个单位。方向键微调不受网格或吸附干扰；输入参数、操作下拉框时仍保留输入控件自己的方向键行为。鼠标拖动继续支持。
新增外部接口后自动选中，可通过「删除所选」或画布 Delete 键删除接口及连接它的管路，也支持单独删除管路；装机硬件不通过此入口删除，输入框内的 Delete 继续编辑文字。
「安装布局」中的撤销/重做按钮以及 Ctrl+Z / Ctrl+Y（或 Ctrl+Shift+Z）恢复布局编辑。组件、管路、拐点的一次拖动合并为一步；参数修改、接口及连线增删、清空、重置和导入也可恢复。输入框中的快捷键继续处理输入文字。每个模块保留最多 100 步，保存后仍保留历史；选中、缩放和模拟反馈不计入历史。装机清单重新协调后建立新基线，历史不会恢复已从装机配置删除的组件。
切换到「模拟动作」会立即隐藏选中高亮、辅助线和拐点，保留操作对象与动作反馈；回到安装布局或显示参数时恢复编辑标记。
所有状态都是工具本地模拟，不连接 PLC/RPC，不构造设备元件，不写 IO。

工具布局采用 Version=2，在旧版槽/管路 XML 的基础上增加独立 `Valve`、`Sensor` 图元及显示参数；旧版 Version=1 布局仍可读取，缺少的装机组件会自动生成默认位置。支持独立 AI、类型适配的硬件图元与正视管口。
Version=2 用于 ToolBuilder 继续编辑；同名 XAML 用于项目普通界面和 MVVM 绑定。正式项目 CDS 页面尚未接入，工具未创建新的后台读取接口。
未来新增组件时，组件清单仍来自装机配置，增加对应显示适配及连接口定义，管路仍由布局描述。

## 基础硬件控件库

同一窗口新增“基础硬件控件库”页签，首批 23 种硬件外观及 1 个漏液 DI 显示示例，共 24 个预览项。该页是独立预览，不把未来硬件加入当前装机或 CDS 布局。

| 分类 | 控件 |
| --- | --- |
| 储存与输送 | 储液槽、离心泵、隔膜泵、计量泵、搅拌器 |
| 加热与换热 | 管路加热器、浸入式加热器、换热器 |
| 阀门与调压 | 气动开关阀、手动阀、比例调节阀、三通阀、止回阀、调压阀、泄压阀 |
| 过滤与缓冲 | 过滤器、脉动阻尼器 |
| 测量与检测 | 流量计、压力表、温度探头、连续液位计、电导率探头、pH 探头、漏液 DI 信号 |

点击卡片后在右侧修改读数、显示单位、目标值及正常/运行、停止、故障、未知状态。泵、搅拌器及加热器具有运行动画；隐藏页签后停止动画。未知读数显示“—”，真实零值显示“0”。右侧预览支持拖动、方向键、宽高和旋转调整。数值及范围均为演示数据，修改单位仅改变显示文字。

公共前端 `Controls/Fluid/HardwareVisual.cs` 是独立 WPF 显示控件：

| 属性 | 用途 |
| --- | --- |
| `Kind` | `FluidHardwareKind`，选择硬件外观 |
| `State` | `Unknown / Stopped / Active / Fault`，来自外部绑定的显示状态 |
| `Reading` | `double?`，当前测量值；`null` 表示未知 |
| `Target` | `double?`，可选目标值 SP |
| `Unit` | 当前显示单位 |
| `Width / Height / RenderTransform` | 标准 WPF 尺寸、旋转等外观属性 |
| `IsSelected` | 选中状态；原生 Tank 使用其选中高亮 |

```xml
<fluid:HardwareVisual Kind="FlowMeter"
    Reading="{Binding Flow}" Unit="L/min" State="{Binding VisualState}" />
<fluid:HardwareVisual Kind="InlineHeater"
    Reading="{Binding Temperature}" Target="{Binding TemperatureTarget}"
    Unit="°C" State="{Binding HeaterVisualState}" />
```

槽体复用平台 `TankVisual3D`，开关阀使用公共 `ValveSymbol`。离心泵、管路加热器使用继承 `HardwareVisual3D` 的三维显示件；漏液直接复用平台 `SignalLamp`，与 HH、LL 的 DI 显示方式相同。其余外观为公共控件的矢量绘制。显示件不执行开阀、启泵或加热命令，由项目现有接口把反馈值绑定到依赖属性；不能凭控件库清单推断已安装硬件。

## 泵与加热器三维预览

“泵与加热器预览”页签提供两个可独立交互的三维件，沿用平台硬件基类的金属灰、蓝色运行高亮和红色故障反馈。鼠标拖动旋转视角，滚轮缩放，双击恢复默认视角，也可使用默认视角、正视图按钮。“显示内部结构”开关可观察泵叶轮及加热芯。

- 泵：当前转速与目标转速独立绑定，可模拟运行、停止、故障及未知；叶轮按当前转速演示转动。
- 加热器：当前温度、目标温度和输出百分比独立绑定，可模拟加热、停止、故障及未知；加热芯随输出显示热态。
- 漏液：仅显示 DI 触发、未触发、未知三态，与旁边的 HH、LL 使用同一 `SignalLamp`。触发显示报警红，未知保留 `null`。

公共前端 `Controls/Fluid/PumpPreviewVisual3D.cs` 提供 `IsRunning`、`Speed`、`ShowInternals` 依赖属性；`HeaterPreviewVisual3D.cs` 提供 `IsHeating`、`PowerFraction`、`Temperature`、`TargetTemperature`、`ShowInternals` 依赖属性。两者复用平台基类的材质、选中及故障属性，由 `ProcessHardwareScene` 承担相机交互和动画生命周期，页面隐藏后停止动画。

启动时增加 `--process-preview` 直接进入该页。所有数据仍为本地演示，既不写装机配置，也不向 CDS 布局增加组件。显示控件可由项目直接引用公共前端程序集。

## CDS 管路上的 2D 组合预览

“CDS 管路 · 2D 预览”页签只演示 `Tank1 → V1 → P1 → H1 → V2 → OUT` 的显示组合。P1 和 H1 使用同一泵、加热器模型的固定正交正视图；`ProcessHardwareScene.ViewMode=Front2D` 固定相机，`ThreeD` 保留原来的三维查看方式。管口来自同一模型的投影坐标，调整宽高或平面旋转后，连接管线继续跟随管口。

本页的单槽、两个阀门、泵和加热器均为独立示例数据，不从当前 35021 装机推断新增设备，不进入原 CDS 装机组件视图，也不提供装机或正式布局保存。原来的装机清单、CDS 编辑页和布局 XML 格式不变。

点击“运行示例”观察流动、泵和加热器状态；选中图元后可拖动、用方向键微调位置（Shift 每次 10 单位），或在右侧修改坐标、尺寸、平面角度、管径与内部结构显示。双击阀门切换开关；右侧“模拟动作”可单独切换泵、加热器的状态、读数及目标值。关闭任一阀门、停止泵、泵反馈未知或故障时，示例管路停止流动。

使用 `--pipeline-preview` 直接打开并运行该示例。自检输出 `pipeline-checks.txt`，同时输出 `cds-pipeline-running.png`、`cds-pipeline-solid.png`、`cds-pipeline-fault.png` 及独立管路图 `cds-pipeline-diagram.png`。

首批清单参考以下厂商资料，并按供液/清洗场景归类；它是可扩展的基础清单，行业硬件没有固定总数：

- [Bürkert 半导体应用](https://www.burkert-usa.com/en/industries-solutions/solutions/applications/application-solutions-for-semiconductor-manufacturing)：阀门、流量、压力、温度、液位及液体分析。
- [Entegris 光刻化学液输送泵](https://www.entegris.com/shop/en/USD/Products/Fluid-Management/Process-Monitoring/Process-Control/Photochemical-Dispense-Pumps/c/photochemicaldispensepumps)：输送、过滤与测量组合。
- [GEMÜ 调压阀、止回阀和过滤器](https://www.gemu-group.com/en/products/valve-technology/pressure-control-valves-check-valves-and-strainers/)及[阀门产品](https://www.gemu-group.com/en/)：阀型、调压与脉动阻尼。
- [Process Technology 管路加热器](https://www.processtechnology.com/products/inline-heaters/chemheat-inline-heater/)：半导体湿法工艺的管路加热外观与应用分类。
- [Process Technology 高纯加热器](https://www.processtechnology.com/high-purity-heating-solutions-for-semiconductor-applications/)：管路、槽体浸入式加热分类。

启动时增加 `--catalog` 可直接进入控件库。自检另外输出 `hardware-checks.txt`、各分类预览图和 24 张独立预览图片；`process-checks.txt` 验证泵、加热器三维动作及漏液 DI，`process-solid.png`、`process-running.png`、`process-fault.png` 展示对应状态。

```powershell
dotnet build D:\Code\tools\CdsAssemblyPreview\CdsAssemblyPreview.csproj
Start-Process -FilePath D:\Code\tools\CdsAssemblyPreview\bin\Debug\net10.0-windows\CdsAssemblyPreview.exe -ArgumentList '--self-test','D:\tools\ToolBuilder\qa\cds-assembly-preview' -WindowStyle Hidden -Wait
```

输出实际组件视图、显式示例连线视图、独立阀门视图、管路参数视图以及 `checks.txt`。自检通过真实输入控件提交和按钮命令验证编辑、模拟及布局保存重载。
