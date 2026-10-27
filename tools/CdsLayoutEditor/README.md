# CDS 管路编辑控件

供 `D:\tools\ToolBuilder` 的管路布局页使用。与 `../CdsLayoutPreview` 共用编辑器源码及 XAML，复用框架的 `TankVisual3D`、`FluidRouteVisual3D` 和 `SignalLamp`，避免原型和正式工具出现两套绘图行为。

宿主传入 `LayoutDocument` 与局部主题资源，再调用 `ConfigureEmbedded()`，隐藏独立工具的文件按钮。通过 `PrepareSave()` 校验并取得布局 XML，通过 `AcceptSave()` 接收宿主保存结果；宿主统一管理 machine.xml、布局备份、外部修改冲突及失败回滚。

支持多个槽，拖动时只更新轻量路径，松开后才重建变化的管路网格。工具只编辑布局和模拟状态，不执行 machine.xml 后端元件、不连接设备。

ToolBuilder 源码、发布及检查说明见 `D:\tools\ToolBuilder\README.md`。
