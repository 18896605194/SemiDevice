$root = 'D:\Code'
$exclude = '\\(bin|obj|\.git|\.vs|packages|node_modules|Libs|\.codex)(\\|$)'
$exts = '.cs', '.xaml', '.md', '.resx', '.xml', '.config', '.csproj'
$strict = New-Object System.Text.UTF8Encoding($false, $true)

$invalid = 0; $noBomNonAscii = 0; $bomCount = 0; $total = 0
Get-ChildItem -Path $root -Recurse -File -ErrorAction SilentlyContinue |
  Where-Object { $_.FullName -notmatch $exclude } |
  Where-Object { $exts -contains $_.Extension.ToLower() } |
  ForEach-Object {
    $total++
    $b = [System.IO.File]::ReadAllBytes($_.FullName)
    $hasBom = ($b.Length -ge 3 -and $b[0] -eq 0xEF -and $b[1] -eq 0xBB -and $b[2] -eq 0xBF)
    if ($hasBom) { $bomCount++ }
    try { [void]$strict.GetString($b) }
    catch { $invalid++; Write-Output ('INVALID UTF-8: ' + $_.FullName.Replace($root, '')); return }
    if (-not $hasBom) {
      foreach ($byte in $b) {
        if ($byte -ge 0x80) { $noBomNonAscii++; Write-Output ('NO-BOM with Chinese: ' + $_.FullName.Replace($root, '')); break }
      }
    }
  }
Write-Output ("total={0} bom={1} invalidUTF8={2} noBomNonAscii={3}" -f $total, $bomCount, $invalid, $noBomNonAscii)

# XAML still parses
try { $x = New-Object System.Xml.XmlDocument; $x.Load('D:\Code\xyz.Core\Client\xyz.Client.Manual\Views\RobotManualControl.xaml'); Write-Output 'RobotManualControl.xaml: XML OK' }
catch { Write-Output ('RobotManualControl.xaml: XML ERROR ' + $_.Exception.Message) }

# repaired files sample
$t = [System.IO.File]::ReadAllText('D:\Code\xyz.Core\Client\xyz.Client\Menus\PlatformMenuProvider.cs', [System.Text.Encoding]::UTF8)
Write-Output ('PlatformMenuProvider L6: ' + ($t -split "`r?`n")[5].Trim())
$t2 = [System.IO.File]::ReadAllText('D:\Code\xyz.35021\Client\ServiceCollectionExtensions.cs', [System.Text.Encoding]::UTF8)
Write-Output ('ServiceCollectionExtensions L11: ' + ($t2 -split "`r?`n")[10].Trim())
