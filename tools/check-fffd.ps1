# 1) find files containing literal U+FFFD bytes (EF BF BD)
$root = 'D:\Code'
$exclude = '\\(bin|obj|\.git|\.vs|packages|node_modules|Libs|\.codex|tools)(\\|$)'
Write-Output '--- files with U+FFFD bytes:'
Get-ChildItem -Path $root -Recurse -File -ErrorAction SilentlyContinue |
  Where-Object { $_.FullName -notmatch $exclude } |
  ForEach-Object {
    $b = [System.IO.File]::ReadAllBytes($_.FullName)
    $n = 0
    for ($i=0; $i -lt $b.Length-2; $i++) {
      if ($b[$i] -eq 0xEF -and $b[$i+1] -eq 0xBF -and $b[$i+2] -eq 0xBD) { $n++ }
    }
    if ($n -gt 0) { Write-Output ("  {0}  x{1}" -f $_.FullName.Replace($root,''), $n) }
  }

# 2) tag balance check for RobotManualControl.xaml
Write-Output '--- RobotManualControl.xaml tag balance:'
$xaml = [System.IO.File]::ReadAllText('D:\Code\xyz.Core\Client\xyz.Client.Manual\Views\RobotManualControl.xaml', [System.Text.Encoding]::UTF8)
$open = [regex]::Matches($xaml, '<(Grid|Border|StackPanel|ItemsControl|Viewbox|UniformGrid|ListBox|ComboBox|TextBox|Button|TextBlock|Ellipse|DataTemplate|materialDesign:Card|presentationControls:ChamberInfoCard|presentationControls:LoadPortInfoCard|presentationControls:Robot)(?=[\s>])')
$closeAll = [regex]::Matches($xaml, '</([A-Za-z:]+)>')
$selfClose = [regex]::Matches($xaml, '<([A-Za-z:]+)[^>]*/>')
$openTags = @{}
foreach ($m in $open) { $openTags[$m.Groups[1].Value] = 1 + [int]$openTags[$m.Groups[1].Value] }
$closeTags = @{}
foreach ($m in $closeAll) { $closeTags[$m.Groups[1].Value] = 1 + [int]$closeTags[$m.Groups[1].Value] }
$selfTags = @{}
foreach ($m in $selfClose) { if ($m.Value -notmatch '</') { $selfTags[$m.Groups[1].Value] = 1 + [int]$selfTags[$m.Groups[1].Value] } }
$names = ($openTags.Keys + $closeTags.Keys | Sort-Object -Unique)
foreach ($t in $names) {
  $o = [int]$openTags[$t]; $c = [int]$closeTags[$t]
  if ($o -ne $c) { Write-Output ("  UNBALANCED {0}: open={1} close={2}" -f $t, $o, $c) }
}
Write-Output '  (balanced tags omitted)'

# 3) doc folder contents
Write-Output '--- doc folder:'
Get-ChildItem -Path (Join-Path $root 'doc') -Recurse -File -ErrorAction SilentlyContinue | ForEach-Object { Write-Output ('  ' + $_.FullName.Replace($root,'')) }
