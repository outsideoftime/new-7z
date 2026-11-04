param([string]$AppDirectory)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
if(!$AppDirectory){$AppDirectory=Join-Path $projectRoot 'artifacts/app'}
$AppDirectory=[IO.Path]::GetFullPath($AppDirectory)
$fixture=Join-Path $projectRoot ('artifacts/popup-tests-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture -Force | Out-Null
$source=Join-Path $fixture '说明.txt';Set-Content -LiteralPath $source -Value 'Popup close behavior fixture.' -Encoding utf8
$badArchive=Join-Path $fixture '损坏.zip';Set-Content -LiteralPath $badArchive -Value 'Not an archive.'
$requests=Join-Path $env:LOCALAPPDATA 'New7z/Requests'
New-Item -ItemType Directory -Path $requests -Force | Out-Null
$registryPath='Software\New7z\Explorer'
$previous=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($registryPath)
$keyExisted=$null -ne $previous
$hadValue=$previous -and $previous.GetValueNames().Contains('AutoCloseOnSuccess')
$oldValue=if($hadValue){$previous.GetValue('AutoCloseOnSuccess')}
$oldKind=if($hadValue){$previous.GetValueKind('AutoCloseOnSuccess')}
if($previous){$previous.Dispose()}
$processes=[Collections.Generic.List[Diagnostics.Process]]::new()
$checks=[Collections.Generic.List[string]]::new()
function Set-Policy([bool]$AutoClose){
 $key=[Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($registryPath)
 try{$key.SetValue('AutoCloseOnSuccess',[int]$AutoClose,[Microsoft.Win32.RegistryValueKind]::DWord)}finally{$key.Dispose()}
}
function Start-Task([string]$Action,[string]$Path){
 $request=Join-Path $requests ([guid]::NewGuid().ToString('N')+'.json')
 @{Action=$Action;Paths=@($Path)} | ConvertTo-Json | Set-Content -LiteralPath $request -Encoding utf8NoBOM
 $process=Start-Process -FilePath (Join-Path $AppDirectory 'New7z.App.exe') -ArgumentList "--shell-request `"$request`"" -WorkingDirectory $AppDirectory -WindowStyle Hidden -PassThru
 $processes.Add($process);return $process
}
function Wait-Output([Diagnostics.Process]$Process,[string]$Output){
 $timer=[Diagnostics.Stopwatch]::StartNew()
 while(!(Test-Path -LiteralPath $Output)){
  Start-Sleep -Milliseconds 100;$Process.Refresh()
  if($Process.HasExited -or $timer.Elapsed.TotalSeconds -gt 30){throw "Missing task output: $Output"}
 }
}
try{
 Set-Policy $false
 $manual=Start-Task 'create-zip' $source
 Wait-Output $manual (Join-Path $fixture '说明.zip')
 Start-Sleep -Seconds 1;$manual.Refresh()
 if($manual.HasExited -or $manual.MainWindowTitle -notlike 'New7z · *'){throw 'Manual close did not retain the standalone result window.'}
 $checks.Add('manual: successful task retains standalone window')
 Set-Policy $true
 $automatic=Start-Task 'create-7z' $source
 Wait-Output $automatic (Join-Path $fixture '说明.7z')
 if(!$automatic.WaitForExit(10000)){throw 'Successful task did not automatically close.'}
 if($automatic.ExitCode -ne 0){throw 'Automatic close exited with an error.'}
 $checks.Add('automatic: successful task exits normally after producing archive')
 $failed=Start-Task 'extract-named' $badArchive
 $timer=[Diagnostics.Stopwatch]::StartNew()
 do{Start-Sleep -Milliseconds 200;$failed.Refresh()}while(!$failed.HasExited -and $failed.MainWindowHandle -eq 0 -and $timer.Elapsed.TotalSeconds -lt 20)
 Start-Sleep -Seconds 2;$failed.Refresh()
 if($failed.HasExited -or $failed.MainWindowTitle -notlike 'New7z · *'){throw 'Failure window was automatically closed.'}
 $checks.Add('automatic: failed archive keeps result window')
}finally{
 foreach($process in $processes){$process.Refresh();if(!$process.HasExited){Stop-Process -Id $process.Id -ErrorAction SilentlyContinue}}
 $key=[Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($registryPath)
 try{if($hadValue){$key.SetValue('AutoCloseOnSuccess',$oldValue,$oldKind)}else{$key.DeleteValue('AutoCloseOnSuccess',$false)};$empty=$key.ValueCount -eq 0 -and $key.SubKeyCount -eq 0}finally{$key.Dispose()}
 if(!$keyExisted -and $empty){[Microsoft.Win32.Registry]::CurrentUser.DeleteSubKey($registryPath,$false)}
}
[pscustomobject]@{Result='PASS';Checks=$checks;OriginalClosePolicyRestored=$true;Fixture=$fixture} | ConvertTo-Json -Depth 4 | Tee-Object -FilePath "$projectRoot/docs/popup-close-tests.json"
