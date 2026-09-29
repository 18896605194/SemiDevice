#Requires -Version 5.1
#Requires -RunAsAdministrator
<#
.SYNOPSIS
    把 ADS 路由装成开机自启的 Windows 服务（xyzAdsRouter），或卸载它。

.DESCRIPTION
    只给没装 TwinCAT 的开发机用：顶替 TwinCAT 的路由，让仿真器（统一仿真器里的倍福 ADS 服务端）和后端能通信。
    真机上 TwinCAT 自带路由，不要装这个——两者都占 48898 端口。
    重复执行 = 重新发布并重装（改了代码以后再跑一遍即可）。

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\tools\AdsRouter\install-service.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\tools\AdsRouter\install-service.ps1 -Uninstall
#>
[CmdletBinding()]
param(
    [switch]$Uninstall,

    [string]$InstallDir = (Join-Path $env:ProgramData 'xyz\AdsRouter')
)

$ErrorActionPreference = 'Stop'
$serviceName = 'xyzAdsRouter'
$project = Join-Path $PSScriptRoot 'AdsRouter.csproj'

$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existing) {
    if ($existing.Status -ne 'Stopped') {
        Stop-Service -Name $serviceName -Force
        $existing.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(15))
    }

    & sc.exe delete $serviceName | Out-Null
    # 删服务是异步的，等它从服务表里消失再往下走，免得重建时报"已标记为删除"。
    for ($i = 0; $i -lt 30 -and (Get-Service -Name $serviceName -ErrorAction SilentlyContinue); $i++) {
        Start-Sleep -Milliseconds 500
    }
    Write-Host "已卸载旧服务 $serviceName"
}

if ($Uninstall) {
    if (Test-Path $InstallDir) {
        Remove-Item -Recurse -Force $InstallDir
    }
    Write-Host "已卸载，安装目录 $InstallDir 已删除"
    return
}

& dotnet publish $project -c Release -o $InstallDir --nologo -v q
if ($LASTEXITCODE -ne 0) {
    throw "发布失败（dotnet publish 退出码 $LASTEXITCODE）"
}

$exe = Join-Path $InstallDir 'AdsRouter.exe'
New-Service -Name $serviceName `
    -BinaryPathName "`"$exe`"" `
    -DisplayName 'xyz ADS 路由（仿真用）' `
    -Description '没装 TwinCAT 的开发机用的 ADS 路由，仿真器和后端经它通信；真机上由 TwinCAT 提供路由，不装本服务。' `
    -StartupType Automatic | Out-Null

# 挂了自动拉起：5 秒后重启，连续失败也一直拉。
& sc.exe failure $serviceName reset= 86400 actions= restart/5000/restart/5000/restart/5000 | Out-Null

Start-Service -Name $serviceName

$listening = $false
for ($i = 0; $i -lt 20 -and -not $listening; $i++) {
    Start-Sleep -Milliseconds 500
    $listening = [bool](Get-NetTCPConnection -LocalPort 48898 -State Listen -ErrorAction SilentlyContinue)
}

if (-not $listening) {
    throw "服务已启动，但 48898 端口没起来。看事件查看器 → Windows 日志 → 应用程序里 AdsRouter 的记录"
}

Write-Host "已安装并启动 $serviceName（开机自启），ADS 路由在 48898 端口监听；程序在 $InstallDir"
