$root = 'D:\Code'
$exclude = '\\(bin|obj|\.git|\.vs|packages|node_modules|Libs|\.codex|tools)(\\|$)'
$files = Get-ChildItem -Path $root -Recurse -File -ErrorAction SilentlyContinue |
  Where-Object { $_.FullName -notmatch $exclude } |
  Where-Object { $_.Extension -match '^\.(cs|xaml)$' }
$stats = @{}
$noBomWithNonAscii = @()
foreach ($f in $files) {
  $b = [System.IO.File]::ReadAllBytes($f.FullName)
  $hasBom = ($b.Length -ge 3 -and $b[0] -eq 0xEF -and $b[1] -eq 0xBB -and $b[2] -eq 0xBF)
  # any byte >= 0x80?
  $nonAscii = $false
  for ($i=0; $i -lt $b.Length; $i++) { if ($b[$i] -ge 0x80) { $nonAscii = $true; break } }
  $key = if ($hasBom) {'BOM'} else {'NO-BOM'}
  if (-not $nonAscii) { $key += '-ascii-only' }
  $stats[$key] = 1 + ($stats[$key] | ForEach-Object { $_ -as [int] })
  if (-not $hasBom -and $nonAscii) { $noBomWithNonAscii += $f.FullName.Replace($root,'') }
}
$stats.GetEnumerator() | Sort-Object Name | ForEach-Object { Write-Output ("{0} = {1}" -f $_.Key, $_.Value) }
Write-Output '--- NO-BOM files containing non-ASCII (these show as mojibake in VS zh-CN):'
$noBomWithNonAscii | ForEach-Object { Write-Output ("  " + $_) }
