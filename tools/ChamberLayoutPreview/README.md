# 腔体三维编辑器预览

```powershell
dotnet run --project tools/ChamberLayoutPreview/ChamberLayoutPreview.csproj
```

在右侧“安装 / 尺寸 / 模拟动作”页签切换编辑内容。摆臂默认自动计算长度，取消尺寸页的自动选项后可输入固定臂长。左下角调整底座长宽厚，按“默认视角”重新适配放大后的底座。

“导入设备配置”离线读取当前框架的 machine.xml 并选择腔体，生成默认布局；“打开布局 / 保存布局”读写独立 XML，不覆盖 machine.xml。

```powershell
dotnet build tools/ChamberLayoutPreview/ChamberLayoutPreview.csproj
tools/ChamberLayoutPreview/bin/Debug/net10.0-windows/ChamberLayoutPreview.exe --self-test <输出目录>
```

自检覆盖安装几何、撤销、取消、文件往返、手动臂长、独立尺寸、旧布局兼容与非法输入。输出 `editor-checks.txt`、测试 XML 和 WPF 原生截图。`--render <输出目录>` 仅生成截图。程序异常写入执行文件旁的 `chamber-editor-error.log`。
