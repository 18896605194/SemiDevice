# 腔体三维摆放编辑控件

引用现有客户端三维组件，提供离线摆放、尺寸编辑和模拟动作，不执行设备指令。配套启动程序在 `../ChamberLayoutPreview`。

- 视角：三维、俯视、正视、后视、左视、右视，编辑和模拟均可切换；右键自由旋转、滚轮缩放。
- 摆放：X/Y/Z、安装朝向、局部 Home 角度；在当前视角平面拖动并网格吸附。正视拖动 X/Y，侧视拖动 Z/Y，俯视与三维斜视默认拖动 X/Z。
- 尺寸：摆臂长宽厚、回转座半径、接液杯半径和高度、Lift 宽高倍率、每路喷嘴的长度和管径倍率；卡盘、Bowl、腔门分别配置。
- 底座长、宽、厚独立调整，内部元件不随底座缩放。
- 臂长可自动计算或手动指定。手动模式保持固定长度；与盘心距离不同时显示提示，预览不会强制拉伸到中心。
- 尺寸采用三维示意单位；倍率只作用于对应组件。尺寸调整不写入轴示教或设备配置。
- 布局 XML Version=1 新增可选的 `BaseThickness`、`AutoLength` 和 `Size` 项。旧布局按原有默认尺寸加载。

宿主先注册 `AddChamberLayoutEditor()` 并配置 `IocHelper.ServiceProvider`，创建 `ChamberEditorPage` 后，集中调用注册的 `BaseViewModel.Init()`。合并平台主题、样式和本程序集的 `Localization/Strings.zh-CN.xaml`（或 en-US）。单个服务容器中使用一个编辑页面。

ToolBuilder 可通过 `ConfigureEmbedded()` 隐藏独立文件按钮，调用 `LoadDocument()` 传入布局；通过 `HasUnsavedChanges`、`DocumentChanged` 管理编辑状态。`PrepareSave()` 校验并返回 XML，宿主成功保存后调用 `AcceptSave()`。宿主负责设备路径变更、文件备份和冲突检查。

当前仓库尚未接入 ToolBuilder 主工程和业务界面的布局加载。模拟固定采用 Home → 边缘 → 中心三段示意，真实运行仍应使用设备推送的 Reach / EdgeReach。
