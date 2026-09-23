# Scan source files for UTF-8-read-as-GBK double-encoded mojibake and non-UTF8 files
$root = 'D:\Code'
$exclude = '\\(bin|obj|\.git|\.vs|packages|node_modules|Libs|\.codex)(\\|$)'
# Typical mojibake marker chars (utf8 bytes of common Chinese misread as GBK):
# 鐨(的) 鍒(列/到) 涓(一/中) 鐢(用) 鑿(菜) 骞(平) 鍗(单) 鐘(状) 鏄(是) 澶(多/大) 鐣(留) 鍚(含/启) 閰(配) 鐣岀(界面)
$markers = [char]0x9428, [char]0x9352, [char]0x4E2D, [char]0x9422, [char]0x93FF, [char]0x9A9E, [char]0x9357, [char]0x941B, [char]0x9374, [char]0x928B, [char]0x941C, [char]0x9230, [char]0x95B0, [char]0x5D18
$markerStr = -join $markers
$results = @()
Get-ChildItem -Path $root -Recurse -File -ErrorAction SilentlyContinue |
  Where-Object { $_.FullName -notmatch $exclude } |
  Where-Object { $_.Extension -match '^\.(cs|xaml|csproj|sln|xml|config|json|md|resx|sql|axml|settings)$' } |
  ForEach-Object {
    $bytes = [System.IO.File]::ReadAllBytes($_.FullName)
    $text = $null
    try { $text = [System.Text.Encoding]::UTF8.GetString($bytes); $strict = $true } catch { $strict = $false }
    if (-not $strict) {
      # had invalid UTF-8 sequences? GetString is lenient; detect replacement chars
      $text = [System.Text.Encoding]::UTF8.GetString($bytes)
    }
    $count = 0
    foreach ($m in $markers) { $count += ([regex]::Matches($text, [regex]::Escape([string]$m))).Count }
    if ($text.Contains([char]0xFFFD)) { $results += [pscustomobject]@{File=$_.FullName.Replace($root,''); Issue='HAS-REPLACEMENT-CHAR'; Hits=$count} }
    elseif ($count -ge 3) { $results += [pscustomobject]@{File=$_.FullName.Replace($root,''); Issue='MOJIBAKE'; Hits=$count} }
  }
$results | Sort-Object -Property @{Expression='Hits';Descending=$true} | Format-Table -AutoSize
Write-Output ("TOTAL: " + $results.Count)
