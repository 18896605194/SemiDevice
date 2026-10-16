# Create desktop shortcut for HotShot
# ASCII-only script to avoid encoding issues
$ErrorActionPreference = "Stop"
$Project = "D:\Code\HotShot"
$DefaultPy = "C:\Program Files\Xiaomi MiMo\resources\runtimes\win32-x64\python\python.exe"
$Python = $env:MIMO_PYTHON
if (-not $Python) { $Python = $DefaultPy }
if (-not (Test-Path $Python)) { $Python = $DefaultPy }
$Pythonw = Join-Path (Split-Path $Python -Parent) "pythonw.exe"
if (-not (Test-Path $Pythonw)) { $Pythonw = $Python }

$Desktop = [Environment]::GetFolderPath("Desktop")
$LnkPath = Join-Path $Desktop "HotShot.lnk"

$Wsh = New-Object -ComObject WScript.Shell
$Shortcut = $Wsh.CreateShortcut($LnkPath)
$Shortcut.TargetPath = $Pythonw
$Shortcut.Arguments = '"' + $Project + '\main.py"'
$Shortcut.WorkingDirectory = $Project
$Shortcut.WindowStyle = 1
$Shortcut.Description = "HotShot screenshot tool (Ctrl+Alt+A)"
$Shortcut.Save()

Write-Host "Desktop shortcut created: $LnkPath"
Write-Host "Target: $Pythonw $Project\main.py"

# Also put a copy named with Chinese on desktop if possible
$LnkPath2 = Join-Path $Desktop ([string]([char]0x95EA) + [char]0x622A + " HotShot.lnk")
Copy-Item $LnkPath $LnkPath2 -Force
Write-Host "Alias shortcut: $LnkPath2"
