# strict XML load of the robot manual XAML to pinpoint first well-formedness error
try {
  [xml]$x = [System.Xml.XmlDocument]::new()
  $x.Load('D:\Code\xyz.Core\Client\xyz.Client.Manual\Views\RobotManualControl.xaml')
  Write-Output 'XML OK'
} catch {
  Write-Output ('XML ERROR: ' + $_.Exception.Message)
  if ($_.Exception.InnerException) { Write-Output ('  inner: ' + $_.Exception.InnerException.Message) }
}

# locate invalid-UTF8 byte runs in the two damaged .cs files
foreach ($f in @(
  'D:\Code\xyz.Core\Client\xyz.Client\Menus\PlatformMenuProvider.cs',
  'D:\Code\xyz.35021\Client\ServiceCollectionExtensions.cs')) {
  Write-Output ('=== ' + $f)
  $b = [System.IO.File]::ReadAllBytes($f)
  $strict = New-Object System.Text.UTF8Encoding($false, $true)
  try { [void]$strict.GetString($b); Write-Output '  valid UTF-8' }
  catch {
    # decode incrementally to find positions
    $decoder = $strict.GetDecoder()
    $buf = New-Object char[] 65536
    $pos = 0; $line = 1
    $i = 0
    while ($i -lt $b.Length) {
      try {
        $used = 0; $usedB = 0
        $decoder.Convert($b, $i, 1, $buf, 0, $buf.Length, $false, [ref]$usedB, [ref]$used, [ref]$true)
        if ($b[$i] -eq 10) { $line++ }
        $i++
      } catch {
        Write-Output ("  invalid byte 0x{0:X2} at offset {1} (line ~{2})" -f $b[$i], $i, $line)
        $i++
      }
    }
  }
}
