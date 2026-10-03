# Disk 二维/三维功能对照检查

在 Windows 上从仓库根目录执行：

```powershell
dotnet run --project tools/DiskVisual3DSmoke
```

可选 PNG 路径生成共用片数据的二维/三维对比图：

```powershell
dotnet run --project tools/DiskVisual3DSmoke -- D:\Code\artifacts\disk-wpf-parity.png
```

使用隐藏窗口、模拟片数据及测试命令，不连接设备。
覆盖全部十种状态色与二维一致、Label 和槽位文字回退、数据变更、空片和常显盘面、
菜单显示规则、命令参数、CreateEnable/DeleteEnable 和 CanExecute、实际菜单执行、
双向旋转/停止保留位置、网格复用、二维实例独立、隐藏/移除/恢复，以及三维表面的射线命中。
菜单测试调用现有菜单处理和 MenuItem 点击路径，未通过系统鼠标自动化弹出菜单。
Scene.xaml 保持 Viewport3D 命中测试开启，可作为集成样例。
