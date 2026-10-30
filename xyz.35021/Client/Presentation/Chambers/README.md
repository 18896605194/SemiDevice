# 35021 腔体页面

正式页面位于 `Client/Manual/Views/ChamberManualView`，通过项目 `ServiceExtensions` 接管平台已经按后台清单注册的 `Manual.<模块名>` 入口。每个腔体一个项目 ViewModel，统一由客户端启动流程调用 `Init()`。

项目页面复用平台的手动操作面板，通过 `SceneTemplate` 放入本项目的 `Controls/ChamberView`。轴操作、气缸操作和工艺操作继续使用现有腔体服务；项目图按元件全路径绑定完整的 `ChamberDeviceDataDto`，不会从布局文件增加设备。

35021 当前的 machine.xml 包含 Chamber1～Chamber4，每个腔体配置一个 Bowl1。项目代码没有向这些腔体加入 Bowl2 或 Bowl3。

## 布局读取与部署

ToolBuilder 保存的位置、尺寸、父子装配关系采用 Version=3 的 ChamberLayout XML。配置路径保持为：

`Config/ChamberLayouts/machine/<完整腔体路径>.xml`

例如 `Config/ChamberLayouts/machine/Chamber.Chamber1.xml`。文件名按 `Uri.EscapeDataString` 转义，与 ToolBuilder 一致。

机型后端项目构建时随 machine.xml 部署这些布局；输入/输出时间检查保留运行目录中更新的现场文件。正式客户端通过 `IChamberService.GetLayoutAsync(Module)` 取得 XML，不需要启动 ToolBuilder，也不直接访问后端磁盘。

没有布局文件时，项目图根据后台实际设备生成默认排列。重新加载失败时保留当前布局并显示原因。页面中的“重新加载布局”可读取刚保存的文件；连接恢复后也会重新读取。

## 项目控件优先

平台提供 `IChamberVisualProvider`、`ChamberPartVisual` 和 `ChamberVisualCatalog`。机型在自己的前台 Presentation 程序集中提供公共、无参构造的 `IChamberVisualProvider` 实现，即可同时供项目页面和 ToolBuilder 发现。

提供器收到 `ChamberVisualContext`，其中有 Kind、ComponentRoot、Component、ParentId、ChamberType，可以按项目的腔体类型和设备路径选择控件。返回自有的 `ChamberPartVisual` 适配器；没有对应控件时返回 null，目录会使用平台控件补齐。项目控件无需继承某个具体的平台 Arm/Bowl 类，可在适配器内部组合自己的 ModelVisual3D。

适配器负责应用尺寸与真实反馈、提供运动附件挂载点及当前长度/高度；几何运动时发出 GeometryChanged，让装在它上面的控件同步。控件不执行设备指令。

35021 当前还没有自定义 Arm、Bowl、喷嘴等三维外观，因此默认使用平台现有外观。项目组合、正式页面及布局选择已在 35021 中独立落地。

ToolBuilder 从装机文件所属机型的 `Client/Presentation/bin` 或已选元件来源中寻找 `*.Client.Presentation.dll`。部署环境中可明确将机型前台资源 DLL 加入工具的元件来源。编辑预览和正式页面使用同一套提供器契约。

## 验证

从仓库根目录执行：

```powershell
dotnet run --project tools/ProjectChamberSmoke -- xyz.35021/Service/xyz.35021.Module/Config/machine.xml <输出目录>
```

测试使用装机文件生成实际设备快照，验证单个 Bowl1、项目控件优先与平台补齐、真实状态绑定、布局路径和接口、菜单接管、正式页面与编辑器预览，并生成截图。设备快照在内存中注入；不启动后端设备、不发送控制指令、不修改输入装机文件。通用多 Bowl 的单元场景仅检查组合器扩展能力，不作为 35021 装机配置或正式截图。
