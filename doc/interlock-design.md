# Interlock（互锁）层设计讨论记录

日期：2026-09-20
状态：设计阶段，未开始编码

## 背景

框架里 DiSensorComponent、AiSensorComponent 这类组件绑的是 PLC 真实点位地址（DiIndex、AiIndex），
它们负责读硬件、判报警。未来的互锁卡控本质上是一堆 if 判断：动作能不能继续，取决于若干个真/假条件。
这些条件的来源五花八门（DI 电平、AI 上下限、模块状态、晶圆账、报警），需要一层把它们统一成布尔值。

讨论初期叫"虚拟 IO"，最后定名为 **Interlock**：虚拟 IO 描述的是手段，Interlock 描述的是用途。

## 命名

| 概念 | 名字 | 说明 |
|---|---|---|
| 整层 | `Interlock` | 文件夹、命名空间、配置文件 interlock.xml |
| 布尔点 | `Condition` | 一个条件，不是互锁本身。"机械手 Idle"是条件 |
| 规则 | `InterlockRule` | 守哪个动作、要求哪个条件、不满足报什么 |
| 表 | `InterlockTable` | 读条件、查规则的统一入口，静态 `Current` 单例（与 WaferManager 等一致） |

## 分层

```
IO 点表
  ├── 真实 DI/DO/AI/AO   有 PLC 地址，驱动轮询后缓存快照
  └── Condition          没有地址，由规则算出来
```

- 真实点与 Condition 对消费方长得一样：给名字，返回 `bool?`。
- Condition 不借用 DiSensor.IsTriggered / AiSensor.IsOutOfRange。那两个是报警线，阈值、极性和互锁经常不同。
  报警组件与 Interlock 层互不依赖，都直接吃原始 IO。
- 前提：真实 IO 层必须是驱动轮询后缓存快照，Read 不去问 PLC。IO 层现在还没接，设计时一起定。

## 读取时机：用到时读，不自己扫

Condition 是纯函数，`Read()` 现算现返回，不存值，不开扫描线程。源都已经在内存里（PLC 快照、模块 State、晶圆账、报警），
算一次是微秒级。

仍需周期性读的情况由需要的一方在自己的扫描里读，不是 Interlock 层扫：

- 动作在途的持续互锁：模块自己的 50ms OnScan 里每周期读一下，断了就中止。搬运目前没这个需求。
- 防抖 / 持续 N 毫秒：需要历史，第一版不做，需要防抖的信号在源头做。
- 迟滞是例外：AiRange 条件记一个上次输出的 bool，读的时候带上算，不需要扫描。

## Condition 的种类

```csharp
public abstract class Condition
{
    public string Name { get; init; } = "";
    public abstract bool? Read();   // null = 源不可用
}
```

| 规则 | 类 | 配置项 | Read 的算法 |
|---|---|---|---|
| DI 电平 | DiLevelCondition | Di, ActiveHigh | 读 DI，等于有效电平为 true |
| AI 区间 | AiRangeCondition | Ai, Min, Max, Hysteresis | 在 [Min, Max] 内为 true，带迟滞 |
| 组件状态 | StateCondition | Path, Property, Equals | 按 FullPath 找组件，反射读属性，比较 |
| 晶圆账 | WaferCondition | Location, Slot, Present | 调 WaferManager.HasWafer，Present=false 时取反 |
| 报警 | AlarmCondition | Path, Active | 调 AlarmComponent.HasAlarmUnder，Active=false 时取反 |
| 组合 | AndCondition / OrCondition / NotCondition | Inputs | 引用其他条件名，三值逻辑 |

### 三值逻辑（null 要传下去）

- And：有一个 false 就 false；否则有一个 null 就 null；否则 true
- Or：有一个 true 就 true；否则有一个 null 就 null；否则 false
- Not：null 还是 null
- 互锁层收到 null 一律按不放行，并记下链条里哪个条件为 null（区分没接线还是驱动掉线）

### 细节

- 迟滞：在区间内时要出到 [Min−H, Max+H] 之外才翻 false；在区间外时要进到 [Min+H, Max−H] 之内才翻 true。
- StateCondition 的 Equals 写名字（如 `Idle`），加载时把 ModuleState / LoadPortState 这类常量类的字段名解析成数值，运行时比 int。
- 组合条件的 Inputs 在全部条件注册完后再解析成引用，同时 DFS 查环，有环加载失败。
- 按 FullPath 找组件需要一个全局根表：ComponentLoader 已有 roots 列表，补一个 `Find(fullPath)` 沿 FindChild 逐级下钻。
  现在只有 `FindChild` 找直接子级。

## 配置文件

沿用 sc.xml 的 Setting/Value/Type 方言，但单独一个 interlock.xml。条件是全局的、跨模块引用，不归任何模块层级。
加载器可复用 ModuleConfig 反序列化和反射灌值逻辑，只是产出的不是 ComponentBase。

```xml
<Setting Name="Conditions">
  <Setting Name="Robot.Idle" Type="xyz.Components.Interlock.StateCondition">
    <Value Name="Path" Value="Robot" />
    <Value Name="Property" Value="State" />
    <Value Name="Equals" Value="Idle" />
  </Setting>
  <Setting Name="Robot.NoAlarm" Type="xyz.Components.Interlock.AlarmCondition">
    <Value Name="Path" Value="Robot" />
    <Value Name="Active" Value="False" />
  </Setting>
  <Setting Name="N2PressureOk" Type="xyz.Components.Interlock.AiRangeCondition">
    <Value Name="Ai" Value="3" />
    <Value Name="Min" Value="0.4" />
    <Value Name="Max" Value="0.8" />
    <Value Name="Hysteresis" Value="0.02" />
  </Setting>
  <Setting Name="CanTransfer" Type="xyz.Components.Interlock.AndCondition">
    <Value Name="Inputs" Value="Robot.Idle,Robot.NoAlarm,N2PressureOk" />
  </Setting>
</Setting>
<Setting Name="Rules">
  <Setting Name="TransferRequiresRobotIdle" Type="xyz.Components.Interlock.InterlockRule">
    <Value Name="Action" Value="Transfer" />
    <Value Name="Require" Value="CanTransfer" />
  </Setting>
</Setting>
```

## 互锁分两层

以"手动取放片要求机械手 Idle"为例：

1. **代码层硬约束**：站点的 `CanPrepare`（State 等于锚点态）、机械手 Pick/Place 的迁移表。设备自身的安全边界，写死，现场改不掉。
2. **配置层互锁**：Interlock 规则。在硬约束之上加现场规则，可按机型不同配置，价值是在接单处提前挡下并给出清楚原因，
   而不是单子跑到一半被设备拒绝。

两层都会挡"机械手非 Idle"。

## 检查点

- TransferManager 接搬运单的入口（目前还是待接，见 TransferManager.OnScan 的注释）：
  先查规则表里挂在 Transfer 动作上的条件，读到 true 才建 TransferRoutine，否则拒单并返回没满足的条件名。
  手动、自动下的单走同一个口，都被覆盖。
- 通用做法：模块的 Begin 里查该动作对应的规则。

## 边界

- "源槽有片、目标槽为空"这类和单子内容相关的条件写不进静态条件名，留在代码里由 TransferManager 查晶圆账。
  Interlock 只管与单子内容无关的全局条件。以后真需要按单参数化再考虑占位符。
- 单子在跑的过程中不需要互锁层持续扫：机械手中途报警，Pick 操作自己会失败，TransferRoutine 就停了。

## 落地清单（待做）

xyz.Components 下新建 Interlock 文件夹：

1. `Condition.cs` 基类
2. 各条件类各一个文件（DiLevel、AiRange、State、Wafer、Alarm、And、Or、Not）
3. `InterlockRule.cs`
4. `InterlockTable.cs`（静态 Current，Register，Read(name)，Check(action)）
5. `InterlockLoader.cs`（读 interlock.xml，解析引用，查环）
6. `IIoDriver` 接口给真实点用，先放空实现；DiSensor / AiSensor 的 ReadDi / ReadAi 以后改成走它
7. ComponentLoader 补 `Find(fullPath)`
8. 一个 smoke 工具验证组合条件和三值逻辑

## 同日其他改动（已提交代码）

- AxisComponent：轴按 PLC 数据块数组名绑定，新增 SC `SendPlcDataPath` / `ReceivePlcDataPath`；
  `SpinTimeoutMs` 挪到 SpinMotorComponent（到速只有卡盘有），`SpeedTolerance` 留基类；
  新增回零超时、运动超时、停止超时、驱动器报错四条报警。SpinMotorComponent 新增到速超时报警。
- sc.xml：两个腔体的 SpinMotor、Arm1 节点补上两个 PLC 数据路径，值留空。
- Collectors 文件夹按变量种类分成 Alarm / Dv / Ec / Event / Sv 子文件夹，命名空间未变。
