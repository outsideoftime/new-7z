param(
  [string]$Exe,
  [string]$OutDir,
  [string[]]$Languages=@('zh-CN','en-US'),
  [string[]]$Themes=@('light','dark')
)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if(!$Exe){$Exe=Join-Path $root 'artifacts/installer-payload-0.1.27-7dc3326d/New7z.App.exe'}
if(!(Test-Path -LiteralPath $Exe)){throw "App executable not found: $Exe"}
if(!$OutDir){$OutDir=Join-Path $root 'artifacts/store-assets/screenshots'}
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class Native {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out RECT value, int size);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
}
"@
[Native]::SetProcessDPIAware() | Out-Null
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$public=[Environment]::GetFolderPath('CommonDocuments')
$sevenZip=Join-Path $root '7zSdk/bin/x64/7zr.exe'
$logo=Join-Path $root '图标/logo.png'

function New-DemoContent([string]$Lang)
{
  if($Lang -eq 'zh-CN')
  {
    $folder=Join-Path $public 'New7z 演示'
    $archive=Join-Path $public '项目资料.7z'
    $zip=Join-Path $public '照片备份.zip'
    $names=@{readme='说明.txt';summary='季度总结.txt';note='备忘.md';reports='报告';images='图片';config='配置'}
    $readme='New7z 演示压缩包，包含示例文档、报告与图片。'
    $summary='季度汇总：创建 12 个压缩包，共压缩 340 MB。'
    $note="# 备忘`n- 支持 7z 与 ZIP`n- RAR 仅解压"
    $json='{ "主题": "深色", "语言": "中文" }'
  }
  else
  {
    $folder=Join-Path $public 'New7z Demo'
    $archive=Join-Path $public 'sample-project.7z'
    $zip=Join-Path $public 'photos-backup.zip'
    $names=@{readme='README.txt';summary='summary.txt';note='notes.md';reports='reports';images='images';config='config'}
    $readme='Sample project files for the New7z demo archive.'
    $summary='Quarterly summary: 12 archives created, 340 MB compressed.'
    $note="# Notes`n- 7z and ZIP are supported`n- RAR is extract-only"
    $json='{ "theme": "dark", "language": "en" }'
  }
  Remove-Item -LiteralPath $folder -Recurse -Force -ErrorAction SilentlyContinue
  New-Item -ItemType Directory -Force -Path (Join-Path $folder $names.reports),(Join-Path $folder $names.images),(Join-Path $folder $names.config) | Out-Null
  Set-Content -LiteralPath (Join-Path $folder $names.readme) -Value $readme -Encoding utf8
  Set-Content -LiteralPath (Join-Path $folder $names.reports $names.summary) -Value $summary -Encoding utf8
  Set-Content -LiteralPath (Join-Path $folder $names.reports $names.note) -Value $note -Encoding utf8
  Set-Content -LiteralPath (Join-Path $folder $names.config 'settings.json') -Value $json -Encoding utf8
  Copy-Item -LiteralPath $logo -Destination (Join-Path $folder $names.images 'logo.png') -Force
  Remove-Item -LiteralPath $archive,$zip -Force -ErrorAction SilentlyContinue
  Push-Location $folder
  try
  {
    & $sevenZip a $archive '*.*' -r -bso0 -bsp0 | Out-Null
    if($LASTEXITCODE){throw "Cannot create demo archive for $Lang"}
  }
  finally { Pop-Location }
  Compress-Archive -Path (Join-Path $folder $names.images '*') -DestinationPath $zip -Force
  Remove-Item -LiteralPath $folder -Recurse -Force
  return @{archive=$archive;zip=$zip}
}

$settingsPath=Join-Path $env:LOCALAPPDATA 'New7z\settings.json'
$settingsDir=Split-Path $settingsPath -Parent
New-Item -ItemType Directory -Force -Path $settingsDir | Out-Null
$hadSettings=Test-Path -LiteralPath $settingsPath
$backup=Join-Path ([IO.Path]::GetTempPath()) ('new7z-settings-'+[guid]::NewGuid().ToString('N')+'.json')
if($hadSettings){Copy-Item -LiteralPath $settingsPath -Destination $backup -Force}

function Write-Settings([string]$lang,[string]$theme,[string[]]$recent)
{
  $language=if($lang -eq 'zh-CN'){'zh'}else{'en'}
  $payload=[ordered]@{
    Theme=$theme
    Language=$language
    IconPath=$null
    FormatIcons=@{}
    AutoCheckUpdates=$false
    Recent=@($recent)
  }
  $payload | ConvertTo-Json -Depth 5 -Compress | Set-Content -LiteralPath $settingsPath -Encoding utf8NoBOM
}

function Wait-Window([Diagnostics.Process]$process)
{
  $timer=[Diagnostics.Stopwatch]::StartNew()
  while($timer.Elapsed.TotalSeconds -lt 20)
  {
    $process.Refresh()
    if($process.MainWindowHandle -ne [IntPtr]::Zero){return $process.MainWindowHandle}
    Start-Sleep -Milliseconds 250
  }
  throw "No main window for process $($process.Id)"
}

function Resize-Window([IntPtr]$handle)
{
  $rect=New-Object Native+RECT
  [Native]::GetWindowRect($handle,[ref]$rect) | Out-Null
  $width=$rect.Right-$rect.Left; $height=$rect.Bottom-$rect.Top
  $targetW=[Math]::Max($width,1420); $targetH=[Math]::Max($height,900)
  [Native]::SetWindowPos($handle,[IntPtr]::Zero,0,0,$targetW,$targetH,0x0002 -bor 0x0004 -bor 0x0010) | Out-Null
  Start-Sleep -Milliseconds 600
}

function Find-Element([System.Windows.Automation.AutomationElement]$root,[string[]]$names,[int]$timeoutSeconds=12)
{
  $timer=[Diagnostics.Stopwatch]::StartNew()
  while($timer.Elapsed.TotalSeconds -lt $timeoutSeconds)
  {
    foreach($name in $names)
    {
      $condition=[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,$name)
      $element=$root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$condition)
      if($element -ne $null){return $element}
    }
    Start-Sleep -Milliseconds 300
  }
  return $null
}

function Find-NavigationItem([System.Windows.Automation.AutomationElement]$root,[string[]]$names,[int]$timeoutSeconds=12)
{
  $timer=[Diagnostics.Stopwatch]::StartNew()
  while($timer.Elapsed.TotalSeconds -lt $timeoutSeconds)
  {
    foreach($name in $names)
    {
      $nameCondition=[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,$name)
      $typeCondition=[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::ListItem)
      $element=$root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.AndCondition]::new($nameCondition,$typeCondition))
      if($element -ne $null){return $element}
    }
    Start-Sleep -Milliseconds 300
  }
  return (Find-Element $root $names 2)
}

function Activate-Element([System.Windows.Automation.AutomationElement]$element)
{
  try
  {
    $pattern=$element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $pattern.Invoke()
    return
  }catch{}
  try
  {
    $pattern=$element.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
    $pattern.Select()
    return
  }catch{}
  $element.SetFocus()
  [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
}

function Set-OutputPath([System.Windows.Automation.AutomationElement]$root,[string]$value)
{
  $condition=[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::Edit)
  $timer=[Diagnostics.Stopwatch]::StartNew()
  while($timer.Elapsed.TotalSeconds -lt 10)
  {
    $edits=$root.FindAll([System.Windows.Automation.TreeScope]::Descendants,$condition)
    foreach($edit in $edits)
    {
      $valuePattern=$edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
      $current=$valuePattern.Current.Value
      if($current -match '\.(7z|zip)$')
      {
        $valuePattern.SetValue($value)
        Start-Sleep -Milliseconds 300
        return
      }
    }
    Start-Sleep -Milliseconds 300
  }
  throw 'Cannot find the output path field in the create dialog.'
}

function Click-Point([int]$x,[int]$y)
{
  [Native]::SetCursorPos($x,$y) | Out-Null
  Start-Sleep -Milliseconds 150
  [Native]::mouse_event(0x0002,0,0,0,[UIntPtr]::Zero)
  [Native]::mouse_event(0x0004,0,0,0,[UIntPtr]::Zero)
  Start-Sleep -Milliseconds 300
}

function Clear-DialogFocus([System.Windows.Automation.AutomationElement]$root,[string]$lang)
{
  $title=if($lang -eq 'zh-CN'){'新建压缩包'}else{'New archive'}
  $nameCondition=[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,$title)
  $typeCondition=[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::Text)
  $condition=[System.Windows.Automation.AndCondition]::new($nameCondition,$typeCondition)
  $element=$root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$condition)
  if($element -ne $null)
  {
    $rect=$element.Current.BoundingRectangle
    Click-Point ([int]($rect.X+$rect.Width/2)) ([int]($rect.Y+$rect.Height/2))
  }
}

function Capture-Window([IntPtr]$handle,[string]$file)
{
  [Native]::ShowWindow($handle,9) | Out-Null
  [Native]::SetForegroundWindow($handle) | Out-Null
  Start-Sleep -Milliseconds 700
  $rect=New-Object Native+RECT
  $dwm=[Native]::DwmGetWindowAttribute($handle,9,[ref]$rect,16)
  if($dwm -ne 0){[Native]::GetWindowRect($handle,[ref]$rect) | Out-Null}
  $w=$rect.Right-$rect.Left; $h=$rect.Bottom-$rect.Top
  $bitmap=New-Object System.Drawing.Bitmap $w,$h
  $graphics=[System.Drawing.Graphics]::FromImage($bitmap)
  $graphics.CopyFromScreen($rect.Left,$rect.Top,0,0,$bitmap.Size)
  $inset=16
  $cropRect=New-Object System.Drawing.Rectangle $inset,$inset,($w-2*$inset),($h-2*$inset)
  $crop=$bitmap.Clone($cropRect,$bitmap.PixelFormat)
  $crop.Save($file,[System.Drawing.Imaging.ImageFormat]::Png)
  $crop.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
  Write-Host "$file $($cropRect.Width)x$($cropRect.Height)"
}

function Start-Shot([string]$archive,[string]$action)
{
  if($archive){$process=Start-Process -FilePath $Exe -ArgumentList ('"'+$archive+'"') -PassThru}
  else{$process=Start-Process -FilePath $Exe -PassThru}
  try
  {
    $handle=Wait-Window $process
    Start-Sleep -Seconds 4
    Resize-Window $handle
    if($action -eq 'create' -or $action -eq 'settings')
    {
      $automation=[System.Windows.Automation.AutomationElement]::FromHandle($handle)
      if($action -eq 'create')
      {
        $names=if($script:currentLang -eq 'zh-CN'){@('新建压缩')}else{@('New archive')}
        $element=Find-Element $automation $names
        if($element -eq $null){throw 'Cannot find the New archive button.'}
        Activate-Element $element
        Start-Sleep -Seconds 2
        Set-OutputPath $automation $script:currentOutput
        Clear-DialogFocus $automation $script:currentLang
      }
      else
      {
        $names=@('Settings','设置')
        $element=Find-NavigationItem $automation $names
        if($element -eq $null){throw 'Cannot find the Settings navigation item.'}
        Activate-Element $element
        Start-Sleep -Seconds 2
      }
    }
    else
    {
      Start-Sleep -Seconds 2
    }
    return @{process=$process;handle=$handle}
  }
  catch
  {
    if(!$process.HasExited){Stop-Process -Id $process.Id -Force}
    throw
  }
}

$scenarios=@(
  @{key='home';archive=$null;action='home'},
  @{key='archive';archive='archive';action='view'},
  @{key='create';archive=$null;action='create'},
  @{key='settings';archive=$null;action='settings'}
)

try
{
  foreach($lang in $Languages)
  {
    $script:currentLang=$lang
    $script:currentOutput=if($lang -eq 'zh-CN'){Join-Path $public '示例.7z'}else{Join-Path $public 'sample.7z'}
    $demo=New-DemoContent $lang
    foreach($theme in $Themes)
    {
      Write-Settings $lang $theme @($demo.archive,$demo.zip)
      $folder=Join-Path $OutDir $lang
      $folder=Join-Path $folder $theme
      New-Item -ItemType Directory -Force -Path $folder | Out-Null
      foreach($scenario in $scenarios)
      {
        $archive=if($scenario.archive -eq 'archive'){$demo.archive}else{$null}
        $shot=Start-Shot $archive $scenario.action
        try
        {
          $index=[array]::IndexOf($scenarios,$scenario)+1
          $name=if($lang -eq 'zh-CN'){
            @{'home'='主页';'archive'='压缩包浏览';'create'='新建压缩';'settings'='设置'}[$scenario.key]
          }else{$scenario.key}
          $file=Join-Path $folder ("{0:d2}-{1}.png" -f $index,$name)
          Capture-Window $shot.handle $file
        }
        finally
        {
          if(!$shot.process.HasExited){Stop-Process -Id $shot.process.Id -Force}
          Start-Sleep -Milliseconds 800
        }
      }
    }
    Remove-Item -LiteralPath $demo.archive,$demo.zip -Force -ErrorAction SilentlyContinue
  }
}
finally
{
  if($hadSettings){Copy-Item -LiteralPath $backup -Destination $settingsPath -Force}
  else{Remove-Item -LiteralPath $settingsPath -Force -ErrorAction SilentlyContinue}
  Remove-Item -LiteralPath $backup -Force -ErrorAction SilentlyContinue
}
Write-Host "Screenshots ready: $OutDir"
