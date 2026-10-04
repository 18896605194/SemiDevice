# 腔体三维图检查（ChamberScene）

在 Windows 上从仓库根目录运行：

```powershell
dotnet run --project tools/ChamberSceneSmoke
```

传入 PNG 绝对路径生成工艺位总装图（Bowl 升起、Arm1 在工艺位出液、Arm2 在 Home 出液）：

```powershell
dotnet run --project tools/ChamberSceneSmoke -- D:\Code\artifacts\chamber-scene.png
```

不连后端，部件推送直接喂 `ChamberPartsModel`（跟后端推的通用部件 `ModulePartsDto` 一样：路径、种类、类名、数据）。
检查使用隐藏窗口宿主，验证：从推送里认出门 / Bowl / 卡盘 / 摆臂（Lift、喷嘴）并搭建（左右两条摆臂）、工艺位两路喷嘴中点正对盘心并落到盘面、
Home 喷嘴在自己的接液杯内并落进杯里（出液时杯亮）、0.2 s 过渡（中途换目标不跳、隐藏时直接落位、不留动画时钟）、
Lift / 门 / Bowl 跟到位反馈（未知停在行程中间并高亮、升到位带动摆臂和液柱）、旋转转向看实际转速正负、摆臂动作高亮、
图下面的视角工具栏（只有默认视角、俯视：按钮的字，俯视和默认视角改相机视角、视野）、组成变化重搭和解绑、多于两条摆臂只画两条并提示。
失败返回非零退出码。
