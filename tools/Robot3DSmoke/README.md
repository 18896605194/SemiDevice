# Robot3D 控件检查

从仓库根目录运行：

```powershell
dotnet run --project tools/Robot3DSmoke
```

可选传入输出目录，生成真实 WPF 的二维 / 三维直伸式、蛙式对照图：

```powershell
dotnet run --project tools/Robot3DSmoke -- D:\Common\artifacts\robot3d
```

使用隐藏 WPF 宿主，不启动服务、不连接设备。
`Scene.xaml` 直接声明 `Robot3D`，绑定方式与 `Robot` 相同，也包括不设置任何属性的默认入口。
检查属性绑定和 DataContext、共享帧解算、晶圆状态和片号、直伸 / 蛙式切换、网格复用与法线、
先收回再转向 / 平移再伸出的顺序、换手顺序、页面隐藏与控件移除后的资源释放、重挂载、视角与输入有效性。
失败返回非零退出码。
