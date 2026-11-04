param([string]$AppDirectory)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
if(!$AppDirectory){$AppDirectory=Join-Path $projectRoot 'artifacts/app'}
$folder=Join-Path $projectRoot ('artifacts/shell-tests-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $folder -Force | Out-Null
$first=Join-Path $folder '说明 A.txt';$second=Join-Path $folder '中文 B.txt'
Set-Content -LiteralPath $first -Value 'New7z Explorer integration: first source.' -Encoding utf8
Set-Content -LiteralPath $second -Value '多选、中文和空格路径测试。' -Encoding utf8
$requests=Join-Path $env:LOCALAPPDATA 'New7z/Requests'
$windowChecks=[Collections.Generic.List[object]]::new()
$taskProcesses=[Collections.Generic.List[Diagnostics.Process]]::new()
New-Item -ItemType Directory -Path $requests -Force | Out-Null
function Send-Request([string]$Action,[string[]]$Paths,[string]$Expected){
 $request=Join-Path $requests ([guid]::NewGuid().ToString('N')+'.json')
 @{Action=$Action;Paths=@($Paths)} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $request -Encoding utf8NoBOM
 $process=Start-Process -FilePath "$AppDirectory/New7z.ShellHost.exe" -ArgumentList "--shell-request `"$request`"" -WindowStyle Hidden -PassThru
 $timer=[Diagnostics.Stopwatch]::StartNew()
 while(!(Test-Path -LiteralPath $Expected)){
  Start-Sleep -Milliseconds 200;$process.Refresh()
  if($process.HasExited -and $process.ExitCode -ne 0){throw "Shell launcher $Action failed: $($process.ExitCode)"}
  if($timer.Elapsed.TotalSeconds -gt 30){throw "Shell action $Action did not create expected output: $Expected"}
 }
 if(Test-Path -LiteralPath $request){throw 'Shell request was not consumed.'}
 $requestProcess=Get-CimInstance Win32_Process -Filter "Name='New7z.App.exe'" | Where-Object {$_.CommandLine.Contains($request)}
 if(@($requestProcess).Count -ne 1){throw 'Cannot uniquely identify the shell task process.'}
 $taskProcess=Get-Process -Id $requestProcess.ProcessId
 $taskProcesses.Add($taskProcess)
 if($taskProcess.MainWindowTitle -notlike 'New7z · *'){throw "Expected a standalone task window, got: $($taskProcess.MainWindowTitle)"}
 $windowChecks.Add([pscustomobject]@{Action=$Action;ProcessId=$taskProcess.Id;WindowTitle=$taskProcess.MainWindowTitle})
}
try{
$zip=Join-Path $folder '归档.zip'
Send-Request 'create-zip' @($first,$second) $zip
$out=Join-Path $folder '归档'
Send-Request 'extract-named' @($zip) (Join-Path $out '中文 B.txt')
foreach($source in @($first,$second)){
 $result=Join-Path $out (Split-Path $source -Leaf)
 if((Get-FileHash -LiteralPath $source).Hash -ne (Get-FileHash -LiteralPath $result).Hash){throw "Roundtrip mismatch: $source"}
}
[pscustomobject]@{Result='PASS';Checks='Multi-file ZIP creation, Unicode and spaces, named-folder extraction, SHA-256 roundtrip, request consumption, standalone task windows';Windows=$windowChecks;FixtureFolder=$folder} | ConvertTo-Json -Depth 4 | Tee-Object -FilePath "$projectRoot/docs/shell-integration.json"
}finally{foreach($process in $taskProcesses){$process.Refresh();if(!$process.HasExited){Stop-Process -Id $process.Id -ErrorAction SilentlyContinue}}}
