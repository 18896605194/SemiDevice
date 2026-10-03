# Bowl 三维组件检查

在 Windows 上从仓库根目录运行：

```powershell
dotnet run --project tools/BowlVisual3DSmoke
```

传入 PNG 绝对路径生成实际 WPF 的 1、2、3 级并排预览：

```powershell
dotnet run --project tools/BowlVisual3DSmoke -- D:\Code\artifacts\bowl-wpf-levels.png
```

Scene.xaml 提供 HeightLevel 和 IsRaised 的独立绑定示例。检查使用隐藏窗口宿主和模拟状态，
不连接硬件。验证三级高度、底面和直径保持、薄壁空心结构、网格绕序、升降及反向、动作高亮、
选中状态保留、安装变换与多实例独立性、隐藏/拆卸停止动画，以及绑定和非法等级校验。
失败返回非零退出码。
