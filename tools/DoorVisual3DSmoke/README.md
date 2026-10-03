# Door 上下升降门检查

在 Windows 上从仓库根目录执行：

```powershell
dotnet run --project tools/DoorVisual3DSmoke
```

可选 PNG 路径生成实际 WPF 的关闭、打开过程中、完全打开总装图：

```powershell
dotnet run --project tools/DoorVisual3DSmoke -- D:\Code\artifacts\door-wpf-states.png
```

使用隐藏窗口和模拟 IsOpen 反馈，不连接硬件。Scene.xaml 把门装在底座前侧、Bowl 外侧。
门板上升到门框上方后打开，下降关闭；驱动放在门框外侧，门洞完全空出。尺寸和安装位置为示意。
检查布尔绑定、开关和途中反向、自动高亮及选择保持、门框和缸体固定、冻结网格复用、
Lift/安装位置独立、初始打开、隐藏页面和移除整套底座时清除动画。失败返回非零退出码。

