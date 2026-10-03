# ChamberBase 装配检查

在 Windows 上从仓库根目录执行：

```powershell
dotnet run --project tools/ChamberBaseVisual3DSmoke
```

可选 PNG 路径，输出实际 WPF 的独立底座与硬件装配图：

```powershell
dotnet run --project tools/ChamberBaseVisual3DSmoke -- D:\Code\artifacts\chamber-base-wpf.png
```

使用隐藏窗口和模拟反馈，不连接硬件。Scene.xaml 展示 Bowl、HomeCup、Lift、Arm 和嵌套管路的装配。
检查尺寸绑定、上表面安装基准、尺寸变化保留子项和位置、整体旋转平移传递到嵌套管子、
各硬件状态独立、整套移除和重新挂载时管路时钟的停止/恢复、隐藏页面、绑定保留和非法尺寸。
预览不包含尚未实现的 SpinMotor。失败返回非零退出码。
