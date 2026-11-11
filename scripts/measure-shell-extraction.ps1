param(
 [Parameter(Mandatory)][string]$Source,
 [Parameter(Mandatory)][string]$ExpectedManifest,
 [string]$AppDirectory='artifacts/app'
)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
$Source=[IO.Path]::GetFullPath($Source)
$AppDirectory=[IO.Path]::GetFullPath($AppDirectory)
$expected=@(Get-Content -LiteralPath $ExpectedManifest -Raw | ConvertFrom-Json)
if(!$expected.Count){throw 'A manifest with at least one file is required.'}
$root=Join-Path $projectRoot ('artifacts/extraction-shell-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root -Force | Out-Null
$archive=Join-Path $root ('sample'+[IO.Path]::GetExtension($Source))
Copy-Item -LiteralPath $Source -Destination $archive
$destination=Join-Path $root 'sample'
$requests=Join-Path $env:LOCALAPPDATA 'New7z/Requests'
New-Item -ItemType Directory -Path $requests -Force | Out-Null
$request=Join-Path $requests ([guid]::NewGuid().ToString('N')+'.json')
@{Action='extract-named';Paths=@($archive)} | ConvertTo-Json | Set-Content -LiteralPath $request -Encoding utf8NoBOM
$last=$expected | Sort-Object Index | Select-Object -Last 1
$lastPath=Join-Path $destination $last.Path
$timer=[Diagnostics.Stopwatch]::StartNew()
$process=Start-Process -FilePath (Join-Path $AppDirectory 'New7z.App.exe') -ArgumentList "--shell-request `"$request`"" -WorkingDirectory $AppDirectory -WindowStyle Hidden -PassThru
try{
 while(!(Test-Path -LiteralPath $lastPath)){
  Start-Sleep -Milliseconds 20;$process.Refresh()
  if($process.HasExited -or $timer.Elapsed.TotalSeconds -gt 120){throw 'Application extraction did not produce the final file.'}
 }
 $timer.Stop()
 foreach($item in $expected){
  $path=Join-Path $destination $item.Path
  if((Get-Item -LiteralPath $path).Length -ne $item.Length -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $item.Sha256){throw "Extraction SHA-256 mismatch: $($item.Path)"}
 }
 if(@(Get-ChildItem -LiteralPath $destination -File -Recurse).Count -ne $expected.Count){throw 'Unexpected output file count.'}
 $result=[pscustomobject]@{AppDirectory=$AppDirectory;Seconds=$timer.Elapsed.TotalSeconds;Files=$expected.Count;Verified='All files SHA-256 checked against manifest';Method='Actual shell-request entry, from launch until final verified file is committed; includes WinUI startup. No UI automation or screenshot.';Output=$destination}
 $result | ConvertTo-Json | Tee-Object -FilePath (Join-Path $root 'result.json')
}finally{
 $process.Refresh();if(!$process.HasExited){Stop-Process -Id $process.Id}
 if(Test-Path -LiteralPath $request){Remove-Item -LiteralPath $request -Force}
}
