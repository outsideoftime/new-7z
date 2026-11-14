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
$zip=Join-Path $folder ((Split-Path $folder -Leaf)+'.zip')
Send-Request 'create-zip' @($first,$second) $zip
$out=Join-Path $folder ([IO.Path]::GetFileNameWithoutExtension($zip))
Send-Request 'extract-named' @($zip) (Join-Path $out '中文 B.txt')
foreach($source in @($first,$second)){
 $result=Join-Path $out (Split-Path $source -Leaf)
 if((Get-FileHash -LiteralPath $source).Hash -ne (Get-FileHash -LiteralPath $result).Hash){throw "Roundtrip mismatch: $source"}
}
$folders=Join-Path $folder 'folders'
$alpha=Join-Path $folders 'Alpha';$beta=Join-Path $folders 'Beta'
New-Item -ItemType Directory -Path $alpha,$beta -Force | Out-Null
Set-Content -LiteralPath (Join-Path $alpha 'a.txt') -Value 'Folder A.' -Encoding utf8
Set-Content -LiteralPath (Join-Path $beta 'b.txt') -Value 'Folder B.' -Encoding utf8
$multi=Join-Path $folders 'folders.zip'
Send-Request 'create-zip' @($alpha,$beta) $multi
function New-SmartFixture([string]$Name,[string[]]$Entries){
 $path=Join-Path $folder ($Name+'.zip')
 $zipFile=[IO.Compression.ZipFile]::Open($path,[IO.Compression.ZipArchiveMode]::Create)
 try{foreach($entryName in $Entries){
  $entry=$zipFile.CreateEntry($entryName)
  $writer=[IO.StreamWriter]::new($entry.Open())
  try{$writer.Write($entryName)}finally{$writer.Dispose()}
 }}finally{$zipFile.Dispose()}
 return $path
}
$singleFolder=New-SmartFixture 'single-folder' @('项目/a.txt','项目/nested/b.txt')
Send-Request 'extract-named' @($singleFolder) (Join-Path $folder '项目/nested/b.txt')
if(Test-Path -LiteralPath (Join-Path $folder 'single-folder')){throw 'Smart extraction added an unnecessary wrapper around a single folder.'}
$singleFile=New-SmartFixture 'single-file' @('loose.txt')
Send-Request 'extract-named' @($singleFile) (Join-Path $folder 'loose.txt')
if(Test-Path -LiteralPath (Join-Path $folder 'single-file')){throw 'Smart extraction added an unnecessary wrapper around a single file.'}
Send-Request 'extract-named' @($singleFile) (Join-Path $folder 'loose (2).txt')
if((Get-Content -LiteralPath (Join-Path $folder 'loose.txt') -Raw) -ne 'loose.txt'){throw 'Smart extraction replaced an existing file.'}
$batchFolder=New-SmartFixture 'batch-folder' @('Batch/child.txt')
$batchFiles=New-SmartFixture 'batch-files' @('first.txt','second.txt')
Send-Request 'extract-named' @($batchFolder,$batchFiles) (Join-Path $folder 'batch-files/second.txt')
if(!(Test-Path -LiteralPath (Join-Path $folder 'Batch/child.txt')) -or (Test-Path -LiteralPath (Join-Path $folder 'batch-folder'))){throw 'Batch Smart Extract did not resolve each archive separately.'}
if((Test-Path -LiteralPath (Join-Path $folder 'first.txt')) -or (Test-Path -LiteralPath (Join-Path $folder 'second.txt'))){throw 'Smart extraction scattered a multi-item archive into the current folder.'}
[pscustomobject]@{Result='PASS';Checks='Multi-file ZIP creation, Unicode and spaces, common-parent archive naming for multiple folders, Smart Extract single folder/file/multiple items/mixed batch, collision keeps both, SHA-256 roundtrip, request consumption, standalone task windows';Windows=$windowChecks;FixtureFolder=$folder} | ConvertTo-Json -Depth 4 | Tee-Object -FilePath "$projectRoot/docs/shell-integration.json"
}finally{foreach($process in $taskProcesses){$process.Refresh();if(!$process.HasExited){Stop-Process -Id $process.Id -ErrorAction SilentlyContinue}}}
