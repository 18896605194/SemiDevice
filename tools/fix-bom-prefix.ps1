# Repair the broken EF BB BD prefix written by add-bom.ps1 (typo: 0xBD instead of 0xBF).
# - Normal files: EF BB BD -> EF BB BF (true UTF-8 BOM)
# - Runtime-loaded configs (sc.xml / ec.xml): strip the 3-byte prefix to restore original bytes
$root = 'D:\Code'
$exclude = '\\(bin|obj|\.git|\.vs|packages|node_modules|Libs|\.codex)(\\|$)'
$exts = '.cs', '.xaml', '.md', '.resx', '.xml', '.config', '.csproj'
$runtimeConfigs = '\\xyz\.Configs\\Config\\(sc|ec)\.xml$'

$fixedBom = 0; $stripped = 0; $untouched = 0
Get-ChildItem -Path $root -Recurse -File -ErrorAction SilentlyContinue |
  Where-Object { $_.FullName -notmatch $exclude } |
  Where-Object { $exts -contains $_.Extension.ToLower() } |
  ForEach-Object {
    $path = $_.FullName
    $b = [System.IO.File]::ReadAllBytes($path)
    if ($b.Length -lt 3 -or $b[0] -ne 0xEF -or $b[1] -ne 0xBB -or $b[2] -ne 0xBD) { return }
    if ($path -match $runtimeConfigs) {
      $out = New-Object byte[] ($b.Length - 3)
      [Array]::Copy($b, 3, $out, 0, $out.Length)
      [System.IO.File]::WriteAllBytes($path, $out)
      $stripped++
      Write-Output ('stripped (runtime config): ' + $path.Replace($root, ''))
    } else {
      $b[2] = 0xBF
      [System.IO.File]::WriteAllBytes($path, $b)
      $fixedBom++
    }
  }
Write-Output ("fixed to real BOM: {0}   stripped runtime configs: {1}" -f $fixedBom, $stripped)
