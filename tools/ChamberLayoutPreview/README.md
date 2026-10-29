# 腔体三维编辑器预览

```powershell
dotnet run --project tools/ChamberLayoutPreview/ChamberLayoutPreview.csproj
```

在右侧“安装 / 尺寸 / 模拟动作”页签切换编辑内容。场景上方独立选择“三维 / 俯视 / 正视 / 后视 / 左视 / 右视”，默认为三维；右键可自由旋转视角，视角切换不改变模拟开关或布局。摆臂默认自动计算长度，取消尺寸页的自动选项后可输入固定臂长。从组件列表或三维场景选中“底盘”，使用统一属性面板调整它的位置、朝向和长宽厚。按“默认视角”重新适配放大后的底盘。

“导入设备配置”离线读取当前框架的 machine.xml 并选择腔体，生成默认布局；“打开布局 / 保存布局”读写独立 XML，不覆盖 machine.xml。

左侧“组件库”选择摆臂总成、Lift、接液杯、喷嘴、卡盘、Bowl、腔门或底盘，再点击“添加组件”。组件自动编号，新增操作支持撤销、重做及保存。需要关联实际设备时，在安装页填写相对腔体的元件路径；添加布局组件不会自动创建硬件元件或复制 PLC 地址。

```powershell
dotnet build tools/ChamberLayoutPreview/ChamberLayoutPreview.csproj
tools/ChamberLayoutPreview/bin/Debug/net10.0-windows/ChamberLayoutPreview.exe --self-test <输出目录>
```

自检覆盖安装几何、撤销、取消、文件往返、手动臂长、独立尺寸、旧布局兼容与非法输入。输出 `editor-checks.txt`、测试 XML 和 WPF 原生截图。`--render <输出目录>` 仅生成截图。程序异常写入执行文件旁的 `chamber-editor-error.log`。
