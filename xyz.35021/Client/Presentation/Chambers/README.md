# 35021 腔体页面

完整手动页面是 `Client/Manual/Views/ChamberManualView.xaml`，直接嵌入同目录的 `ChamberDiagram.xaml` 作为中间腔体显示区域。轴、气缸和工艺操作复用现有手动面板及腔体服务。每个实际腔体分别注册项目 ViewModel，统一由客户端启动流程调用 `Init()`。

35021 当前每个腔体配置一个 Bowl1。项目代码没有为这些腔体增加 Bowl2 或 Bowl3。

## XAML 与 MVVM

ToolBuilder 导出普通 WPF `UserControl`，内部为 `Grid → Viewbox → Viewport3D`，实际控件直接嵌在可视树中。尺寸、父子关系和 ScaleTransform3D、RotateTransform3D、TranslateTransform3D 均写在界面内，控件实例不再放进 ResourceDictionary。尺寸和静态安装计算在导出时完成。

根界面按容器拉伸，Viewbox 采用 Uniform 等比例适配可用区域。视口的 1280×800 是设计时使用的逻辑坐标，不是显示器物理分辨率；WPF 同时处理不同 DPI 的显示缩放。背景、字体及外部操作区沿用项目已有主题和样式。

`ChamberManualViewModel.DiagramStates` 提供显示数据。导出的 XAML 直接绑定控件的依赖属性：

```xml
<threeD:ArmVisual3D Angle="{Binding DataContext.DiagramStates[Arm1].Angle, ElementName=ChamberRoot, Mode=OneWay}" />
<threeD:BowlVisual3D IsRaised="{Binding DataContext.DiagramStates[Bowl1].IsRaised, ElementName=ChamberRoot, Mode=OneWay}" />
<threeD:FluidPipeVisual3D IsFlowing="{Binding DataContext.DiagramStates[Arm1.Nozzle_DIW].IsFlowing, ElementName=ChamberRoot, Mode=OneWay}" />
```

角度数据直接取轴 CurrentPosition；如果项目需要将轴单位转换为显示角度，在项目 ViewModel 提供该数据。IsUnknown 保留气缸未知状态，卡盘 RotationSpeed 使用有效运动反馈的速度值。两路喷嘴各绑定各自的状态。

喷嘴放在 ArmVisual3D.Attachments 中，由控件自身的父子关系跟随摆角。安装高度在 XAML 中绑定 LiftVisual3D.MountHeight，喷液长度直接使用 StreamLength 参数。正式页面只加载 XAML 和处理视角交互，已移除 ChamberDiagramRuntime 及 Frame；没有页面级的安装、角度或喷液落点计算。各硬件控件自行管理显示动画及可见性。

## 编辑、导出与部署

腔体布局右侧的「画布 / XAML」切换显示画布和当前源码；修改位置、尺寸后源码同步更新，可选择和复制。「导出 XAML」导出当前腔体；「打开 XAML」导入工具生成的当前腔体布局并支持撤销，装机配置仍决定设备清单。

顶部保存同时输出工具编辑 XML 和显示 XAML，参与同一批冲突检查、备份及失败回滚。默认路径为：

`Config/ChamberLayouts/machine/Chamber.Chamber1.xaml`

同目录的 `.xml` 是工具继续编辑使用的数据。文件名按完整元件路径及 Uri.EscapeDataString 生成。后端项目构建时复制这两类文件；实际部署需要使用更新编译后的前后端。

客户端通过现有 `IChamberService.GetLayoutAsync(Module)` 的 `ChamberLayoutDto.Xaml` 取得 XAML，后端只读取文本。正式页用 ContentControl 嵌入加载的 UserControl，自动继承页面 ViewModel，三维控件通过 ElementName 绑定根界面的 DataContext。没有工具 XML 也能加载 XAML，只有旧 XML 时兼容生成显示界面；旧资源字典格式也兼容读取，下次导出统一使用 UserControl。没有已保存布局或装机组成改变时，项目 ViewModel 按后台实际设备生成默认 XAML；设备状态推送只更新绑定数据。布局加载失败保留当前画面并显示原因，页面的「重新加载布局」读取刚保存的文件。

## 项目控件

35021 当前没有自有 Arm、Bowl、喷嘴等三维外观，因此使用平台现有控件。

工具通过 `IChamberVisualProvider` 按 Kind、ComponentRoot、Component、ParentId、ChamberType 选取项目控件，导出时将其实际公共 ModelVisual3D 类型和外观依赖属性写进 XAML。正式页面直接实例化声明的类型，通过依赖属性绑定接收数据。

自有控件需要公开对应的尺寸与状态依赖属性。例如 Bowl 为 Radius/IsRaised/IsUnknown；Arm 为 Length/Width/Thickness/PivotRadius/Angle/IsMoving/IsError；喷嘴为 Length/StreamLength/IsFlowing。自有 Arm 提供 Attachments 集合，Lift 提供 MountHeight。正式客户端需携带声明类型所属的前台程序集。

ToolBuilder 自动寻找机型 `Client/Presentation/bin` 下的前台资源 DLL，部署环境也可在元件来源中加入。编辑预览与导出使用同一套控件来源，控件不执行设备指令。

## 验证

```powershell
dotnet run --project tools/ProjectChamberSmoke -c Release -- xyz.35021/Service/xyz.35021.Module/Config/machine.xml <输出目录>
```

检查普通 UserControl 导出、实际单 Bowl1、控件依赖属性绑定、XAML 变换、项目自有控件类型导出、独立喷嘴、可见性动画、旧格式兼容、布局接口与正式手动页。覆盖 1280×720、1920×1080、2560×1440、3840×2160、1080×1920 可用区域，以及 125%/150%/200% 的 DPI 渲染缩放。状态在内存中注入，不发送设备控制指令，不修改输入装机文件。通用多 Bowl 场景只验证清单扩展能力，不代表 35021 配有多 Bowl。
