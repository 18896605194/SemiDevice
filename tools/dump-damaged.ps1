foreach ($f in @(
  'D:\Code\xyz.Core\Client\xyz.Client\Menus\PlatformMenuProvider.cs',
  'D:\Code\xyz.35021\Client\ServiceCollectionExtensions.cs')) {
  Write-Output ('=== ' + $f)
  $t = [System.IO.File]::ReadAllText($f, [System.Text.Encoding]::UTF8)
  $lines = $t -split "`r?`n"
  for ($i=0; $i -lt $lines.Count; $i++) {
    if ($lines[$i].Contains([char]0xFFFD)) {
      $marked = $lines[$i].Replace([string][char]0xFFFD, '<<<>>>')
      Write-Output ("L$($i+1): $marked")
    }
  }
}
