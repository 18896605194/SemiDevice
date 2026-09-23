$files = @(
 'D:\Code\xyz.Core\Client\Common\xyz.Client.Presentation\Localization\Strings.zh-CN.xaml',
 'D:\Code\xyz.Core\Client\Common\xyz.Client.Presentation\Controls\Robot.xaml.cs',
 'D:\Code\xyz.Core\Service\xyz.Components\ComponentBase.cs',
 'D:\Code\xyz.Core\Client\xyz.Client\ViewModels\MainViewModel.cs',
 'D:\Code\tools\OperationWaitSmoke\Program.cs',
 'D:\Code\xyz.Core\Service\xyz.GrpcHost\HostTrayIcon.cs',
 'D:\Code\xyz.Core\Service\xyz.Modules\E84\E84Component.cs',
 'D:\Code\xyz.Core\Service\xyz.Modules\Robot\BaseRobotModule.cs',
 'D:\Code\xyz.Core\Service\xyz.Modules\Loadport\BaseLoadPortModule.cs',
 'D:\Code\xyz.Core\Service\xyz.Modules\Clean\BaseChamberModule.cs',
 'D:\Code\xyz.Core\Service\xyz.Modules\Transfer\TransferManager.cs',
 'D:\Code\xyz.Core\Service\xyz.Modules\Transfer\TransferStep.cs',
 'D:\Code\xyz.Core\Service\xyz.Components\Wafers\WaferProcessState.cs',
 'D:\Code\doc\interlock-design.md',
 'D:\Code\xyz.35021\Client\ServiceCollectionExtensions.cs',
 'D:\Code\xyz.Core\Client\xyz.Client\Menus\PlatformMenuProvider.cs'
)
foreach ($f in $files) {
  Write-Output ('=== ' + (Split-Path $f -Leaf))
  $t = [System.IO.File]::ReadAllText($f, [System.Text.Encoding]::UTF8)
  $lines = $t -split "`n"
  $shown = 0
  for ($i=0; $i -lt $lines.Count -and $shown -lt 3; $i++) {
    $ln = $lines[$i].TrimEnd()
    if ($ln -match '[\u9327\u9352\u9422\u93FF\u9A9E\u9357\u941B\u9374\u928B\u941C\u9230\u95B0\uFFFD]') {
      $s = $ln.Trim()
      if ($s.Length -gt 100) { $s = $s.Substring(0,100) }
      Write-Output ("  L$($i+1): $s")
      $shown++
    }
  }
}
