# 腔体三维图检查（ChamberScene）

在 Windows 上从仓库根目录运行：

```powershell
dotnet run --project tools/ChamberSceneSmoke
```

传入 PNG 绝对路径生成工艺位总装图（Bowl 升起、Arm1 在工艺位出液、Arm2 在 Home 出液）：

```powershell
dotnet run --project tools/ChamberSceneSmoke -- D:\Code\artifacts\chamber-scene.png
```

不连后端，部件状态直接喂 `ChamberPartsModel`（跟后端推的 `ChamberPartsDto` 一样）。检查使用隐藏窗口宿主，验证：
按部件组成搭建（门、Bowl、旋转盘、左右两条摆臂）、按钮组照 sc 路径和语言包、工艺位两路喷嘴中点正对盘心并落到盘面、
Home 喷嘴在自己的接液杯内并落进杯里（出液时杯亮）、0.3 s 过渡（中途换目标不跳、隐藏时直接落位、不留动画时钟）、
Lift 升起带动摆臂和液柱、门 / Bowl / 旋转 / 动作高亮跟状态、组成变化重搭和解绑、多于两条摆臂只画两条并提示。
失败返回非零退出码。
