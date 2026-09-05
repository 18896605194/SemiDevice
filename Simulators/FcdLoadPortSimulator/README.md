# FcdLoadPort (富创得 LP300) 仿真器

xyz 框架的设备仿真体系第一个成员：**协议级** FcdLoadPort (富创得 LP300) 仿真器。
实现 Fortrend LP300 B 类 ASCII 串口协议（`s00...;\r`），上位机跑**真协议驱动**通过虚拟串口对连接，
和连真设备完全同一套代码路径。

## 启动

```
dotnet run --project Simulators/FcdLoadPortSimulator
```

多实例（LoadPort1/LoadPort2 各一个）用 `--data` 指定独立数据目录：

```
FcdLoadPortSimulator.exe --data D:\SimData\lp300-1     # 串口 COM5
FcdLoadPortSimulator.exe --data D:\SimData\lp300-2     # 串口 COM7
```

每个实例的 `config.json` / `responses.json` / `fault.*` 旗标 / `log.txt` / `crash.log` 都落在自己的数据目录。

也可在统一仿真器(SimulatorHub)中作为页签运行,一次开多台 LoadPort;独立 exe 仍可单独使用。

## 虚拟串口对接

用 com0com 等工具建串口对（如 COM5↔COM6），仿真器开一端（`config.json` 的 `DefaultPort`），
上位机驱动连另一端。波特率 9600 8N1（`config.json` 可改）。

## 应答表（核心）

界面中间的表格每行一条指令，收到后照表回复：

| 列 | 含义 |
|---|---|
| 指令 | 上位机发来的 TYPE:NAME，如 `MOV:CLOAD` |
| 第一次回复 (ACK) | 收到后**立即**回，如 `ACK:CLOAD` |
| 第二次回复 - INF | 延迟后（默认 1000ms 可调）回的完成帧，多帧用 `\|` 分隔，如 Load 先推 `INF:MAPDT/<25槽>` 再推 `INF:CLOAD` |
| 第二次回复 - ABS | 异常帧，如 `ABS:CLOAD/0301` |
| 类型 | 第二次实际发 INF 还是 ABS —— **想模拟故障切到 ABS 即可** |

ACK/INF/ABS/类型 四列可双击修改，`[保存配置]` 写入 `responses.json`（也可直接改文件后`[重新加载]`）。
`[Map 全 P]/[Map 全 E]` 一键把所有 mapping 回复置为 25 槽全有片/全空片。

## FOUP 状态区

- 三个勾选框实时改写 `GET:STATE` 的 64 字符状态串：在位(byte1) / 放好(byte2) / 硬件报警(byte8)
- `[放 FOUP]/[取 FOUP]` 置位后**主动推送** `INF:PODON` / `INF:PODOF` 事件帧
- 门信号有副作用：CLOAD/CLMPO 后 DoorOpen(byte43)=O；CULOD/CULDK/ORGSH/RESET 后门关 —— 与真机语义一致

## 故障注入（无人值守冒烟）

数据目录放旗标文件即生效（500ms 轮询），删除即恢复：

| 旗标文件 | 效果 |
|---|---|
| `fault.devicealarm` | 置位 GET:STATE 硬件报警位(byte8) |
| `fault.garbage` | 所有应答改发乱码帧 `@@GARBAGE@@` |
| `fault.noinf` | 只回 ACK，INF 永不到来（上位机动作等不到终态） |

## 其他

- `[协议自测]`：不依赖串口的协议分发/封帧自测（19 个用例），结果进日志区
- 收发日志按颜色区分：接收=绿 发送=蓝 系统=灰 错误=红 自测=黄 故障注入=橙；`[主动发帧]` 可手工从仿真器侧发任意帧
- 未处理异常落盘 `crash.log`，运行日志同步落盘 `log.txt`

## 协议速查（LP300 ASCII v2.4.4，B 类帧 `s00<TYPE>:<NAME>[/数据];\r`）

- MOV（ACK → INF）：ORGSH 回零 / CLOAD 装载+映射 / CULOD 卸载 / CLDMP 映射 / CULDK 关门 / CULYD 上锁 / CUDCL 解除对接 / CLDYD 对接 / CLDOP 开门解锁 / CLMPO 开门+映射 / PODCL、PODOP 夹紧松开 / RESUM、PAUSE、ABORT
- SET（ACK → INF，回抄参数）：RESET / OUPUT / E84EN / E84ES
- GET（单帧 ACK+数据）：STATE(64字符) / MAPDT / MAPRD(映射) / VERSN / OUPUT
- NAK 错误码：0103 指令不支持 / 0107 格式错误
- 映射字符：E=空 P=有片 2=交叉 W=双重

## 源起

功能复刻自 XM.Single2 的 FcdLoadPortSimulator（`D:\XM_CODE\Xtrim-SingleWafer\XM.Single2\仿真\仿真源码`），
界面用 xyz.Client.Presentation 深色工业风样式重写（WPF）。
