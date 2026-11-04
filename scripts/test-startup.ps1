param([string]$AppDirectory,[int]$TimeoutSeconds=20)
$ErrorActionPreference='Stop'
if(!$AppDirectory){$AppDirectory=Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/app'}
$AppDirectory=[IO.Path]::GetFullPath($AppDirectory)
foreach($name in @('New7z.App.exe','New7z.App.pri','resources.pri','App.xbf','MainWindow.xbf','ShellTaskWindow.xbf','7z.dll','New7z.Native.dll','Assets/logo.png','Assets/New7z.ico')){
  if(!(Test-Path -LiteralPath (Join-Path $AppDirectory $name))){throw "Published resource is missing: $name"}
}
$logPath=Join-Path $env:TEMP 'New7z-error.log'
$previousLength=if(Test-Path -LiteralPath $logPath){(Get-Item -LiteralPath $logPath).Length}else{0}
$appProcess=Start-Process -FilePath (Join-Path $AppDirectory 'New7z.App.exe') -WorkingDirectory $AppDirectory -WindowStyle Hidden -PassThru
$timer=[Diagnostics.Stopwatch]::StartNew()
while($timer.Elapsed.TotalSeconds -lt $TimeoutSeconds){
  Start-Sleep -Milliseconds 250
  $appProcess.Refresh()
  if($appProcess.HasExited){throw "Published app exited during startup (exit code $($appProcess.ExitCode))."}
  if($appProcess.MainWindowHandle -ne 0){break}
}
if($appProcess.MainWindowHandle -eq 0){throw 'Published app did not create a main window before the timeout.'}
# Catch delayed XAML initialization failures rather than merely observing a process.
Start-Sleep -Seconds 5
$appProcess.Refresh()
if($appProcess.HasExited){throw 'Published app exited after creating its window.'}
$currentLength=if(Test-Path -LiteralPath $logPath){(Get-Item -LiteralPath $logPath).Length}else{0}
if($currentLength -gt $previousLength){throw "The app wrote a new error during startup. See $logPath"}
[pscustomobject]@{Result='PASS';ProcessId=$appProcess.Id;WindowTitle=$appProcess.MainWindowTitle;Application=(Join-Path $AppDirectory 'New7z.App.exe')} | Format-List
