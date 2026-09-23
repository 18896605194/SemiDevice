# Add UTF-8 BOM to source text files that contain non-ASCII bytes but no BOM yet.
# Skips build output, vendor dirs, and extensions where BOM could break parsers (.json/.sln).
$root = 'D:\Code'
$exclude = '\\(bin|obj|\.git|\.vs|packages|node_modules|Libs|\.codex|tools)(\\|$)'
$exts = '.cs', '.xaml', '.md', '.resx', '.xml', '.config', '.csproj'

$changed = 0
$skipped = 0
Get-ChildItem -Path $root -Recurse -File -ErrorAction SilentlyContinue |
  Where-Object { $_.FullName -notmatch $exclude } |
  Where-Object { $exts -contains $_.Extension.ToLower() } |
  ForEach-Object {
    $path = $_.FullName
    $b = [System.IO.File]::ReadAllBytes($path)
    if ($b.Length -ge 3 -and $b[0] -eq 0xEF -and $b[1] -eq 0xBB -and $b[2] -eq 0xBF) { return }
    $hasNonAscii = $false
    foreach ($byte in $b) { if ($byte -ge 0x80) { $hasNonAscii = $true; break } }
    if (-not $hasNonAscii) { $skipped++; return }
    # verify strict UTF-8 before rewriting; broken files must not pass through silently
    $strict = New-Object System.Text.UTF8Encoding($false, $true)
    try { [void]$strict.GetString($b) } catch {
      Write-Output ('SKIPPED (invalid UTF-8): ' + $path.Replace($root, ''))
      return
    }
    $out = New-Object byte[] ($b.Length + 3)
    $out[0] = 0xEF; $out[1] = 0xBB; $out[2] = 0xBF
    [Array]::Copy($b, 0, $out, 3, $b.Length)
    [System.IO.File]::WriteAllBytes($path, $out)
    $changed++
  }
Write-Output ("BOM added: {0}   ascii-only skipped: {1}" -f $changed, $skipped)
