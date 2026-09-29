# AdsRouter —— 开发机用的 ADS 路由

没装 TwinCAT 的开发机用它顶替 TwinCAT 的路由服务，仿真器（统一仿真器里的倍福 ADS 服务端）和后端（`BeckhoffPlcComponent` 的 `AdsClient`）都经它通信。
用的是倍福官方 NuGet 包 `Beckhoff.TwinCAT.Ads.TcpRouter`，协议跟 TwinCAT 的路由一样，**后端代码和配置不用为它改任何东西**。

**真机上不要装**：TwinCAT 自带路由，两者都占 48898 端口。

## 安装（开机自启服务）

管理员 PowerShell：

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\AdsRouter\install-service.ps1
```

- 服务名 `xyzAdsRouter`，开机自动启动，挂了 5 秒后自动拉起。
- 程序发布到 `C:\ProgramData\xyz\AdsRouter`（`-InstallDir` 可改）。
- 改了代码再跑一遍就是重装。卸载加 `-Uninstall`。
- 本机 AmsNetId 默认取本机 IPv4 + `.1.1`；要固定就在安装目录放 `appsettings.json`：`{ "NetId": "192.168.1.10.1.1" }`。

## 联调顺序

1. 路由（服务，开机就在）。
2. 统一仿真器按 35021 机型启动：`D:\仿真\统一仿真器\Start-35021.cmd`。
3. 后端 sc.xml 的 PLC 节点 `Host` = `Local`，启动后端和客户端。
