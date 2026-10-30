#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try
{
    dotnet publish .\ToolBuilder\ToolBuilder.csproj -c Release --no-self-contained -o .\app-v8
    if ($LASTEXITCODE -ne 0)
    {
        throw 'ToolBuilder publish failed.'
    }
    Write-Host "Ready: $PSScriptRoot\app-v8\ToolBuilder.exe"
}
finally
{
    Pop-Location
}
