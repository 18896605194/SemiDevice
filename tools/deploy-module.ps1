#Requires -Version 5.1
<#
.SYNOPSIS
    把机型模块部署到平台侧（客户端壳 / 后端宿主）的 Modules\<机型> 目录。

.DESCRIPTION
    依赖方向约定：机型项目引用平台，平台不引用机型项目。
    平台侧启动时只扫自己输出目录下的 Modules\<机型>\，所以机型编译完由本脚本把 DLL 放进去，
    项目文件之间零引用。客户端对应 xyz.Client，服务端对应 xyz.GrpcHost。

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\tools\deploy-module.ps1 -Module 35021

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\tools\deploy-module.ps1 -Module 35021 -Target Service
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Module,

    [ValidateSet('Client', 'Service', 'All')]
    [string]$Target = 'All',

    [string]$Configuration = 'Debug',

    [string]$RepoRoot
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
    $RepoRoot = (Resolve-Path (Join-Path $scriptDirectory '..')).Path
}

function Deploy-Module {
    param(
        [string]$Label,
        [string]$SourceBinRoot,
        [string]$DllName,
        [string]$DestinationBinRoot
    )

    if (-not (Test-Path $SourceBinRoot)) {
        throw "$Label 机型模块还没编译：$SourceBinRoot 不存在。"
    }

    $dll = Get-ChildItem -Path $SourceBinRoot -Recurse -Filter $DllName | Select-Object -First 1
    if (-not $dll) {
        throw "在 $SourceBinRoot 下找不到 $DllName，先编译机型模块。"
    }

    $destinationOutput = Get-ChildItem -Path $DestinationBinRoot -Directory -Filter 'net*' -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if (-not $destinationOutput) {
        throw "平台侧还没编译：$DestinationBinRoot 下找不到输出目录（net*）。"
    }

    $moduleDirectory = Join-Path $destinationOutput.FullName "Modules\$Module"
    New-Item -ItemType Directory -Force -Path $moduleDirectory | Out-Null

    # 整份输出（含机型依赖）一起拷，模块自身就能独立加载。
    Copy-Item -Path (Join-Path $dll.Directory.FullName '*.dll') -Destination $moduleDirectory -Force
    Copy-Item -Path (Join-Path $dll.Directory.FullName '*.pdb') -Destination $moduleDirectory -Force

    Write-Host "已部署 $Label 机型模块 $Module -> $moduleDirectory"
}

if ($Target -eq 'Client' -or $Target -eq 'All') {
    Deploy-Module -Label '客户端' `
        -SourceBinRoot (Join-Path $RepoRoot "xyz.$Module\Client\bin\$Configuration") `
        -DllName "xyz.$Module.Client.dll" `
        -DestinationBinRoot (Join-Path $RepoRoot "xyz.Core\Client\xyz.Client\bin\$Configuration")
}

if ($Target -eq 'Service' -or $Target -eq 'All') {
    Deploy-Module -Label '服务端' `
        -SourceBinRoot (Join-Path $RepoRoot "xyz.$Module\Service\xyz.$Module.Module\bin\$Configuration") `
        -DllName "xyz.$Module.Module.dll" `
        -DestinationBinRoot (Join-Path $RepoRoot "xyz.Core\Service\xyz.GrpcHost\bin\$Configuration")
}
