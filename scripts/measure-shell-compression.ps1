param([Parameter(Mandatory)][string]$Source,[string]$NewAppDirectory='artifacts/performance-app')
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
$Source=[IO.Path]::GetFullPath($Source)
$oldApp=Join-Path $projectRoot 'artifacts/app'
$newApp=[IO.Path]::GetFullPath((Join-Path $projectRoot $NewAppDirectory))
$root=Join-Path $projectRoot ('artifacts/performance-shell-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root -Force | Out-Null
$requests=Join-Path $env:LOCALAPPDATA 'New7z/Requests'
New-Item -ItemType Directory -Path $requests -Force | Out-Null
$rows=@()
foreach($round in 1..2){foreach($profile in @('old-app','new-app')){
 $folder=Join-Path $root "$profile-$round"
 New-Item -ItemType Directory -Path $folder -Force | Out-Null
 Copy-Item -LiteralPath $Source -Destination $folder -Recurse
 $inputFolder=Join-Path $folder (Split-Path $Source -Leaf)
 $expected=$inputFolder+'.zip'
 $request=Join-Path $requests ([guid]::NewGuid().ToString('N')+'.json')
 @{Action='create-zip';Paths=@($inputFolder)} | ConvertTo-Json | Set-Content -LiteralPath $request -Encoding utf8NoBOM
 $app=if($profile -eq 'old-app'){$oldApp}else{$newApp}
 # Measure the same --shell-request entry point including WinUI startup, not synthetic engine time.
 $timer=[Diagnostics.Stopwatch]::StartNew()
 $process=Start-Process -FilePath "$app/New7z.App.exe" -ArgumentList "--shell-request `"$request`"" -WorkingDirectory $app -WindowStyle Hidden -PassThru
 try{
  while(!(Test-Path -LiteralPath $expected)){
   Start-Sleep -Milliseconds 20;$process.Refresh()
   if($process.HasExited -or $timer.Elapsed.TotalSeconds -gt 60){throw 'Shell benchmark did not produce output.'}
  }
  $timer.Stop()
  $rows+=[pscustomobject]@{Profile=$profile;Round=$round;Seconds=$timer.Elapsed.TotalSeconds;OutputBytes=(Get-Item -LiteralPath $expected).Length}
  $rows[-1] | ConvertTo-Json -Compress
  [Text.Encoding]::RegisterProvider([Text.CodePagesEncodingProvider]::Instance)
  $zipStream=[IO.File]::OpenRead($expected)
  $zip=[IO.Compression.ZipArchive]::new($zipStream,[IO.Compression.ZipArchiveMode]::Read,$false,[Text.Encoding]::GetEncoding((Get-Culture).TextInfo.OEMCodePage))
  try{
   foreach($file in Get-ChildItem -LiteralPath $inputFolder -Recurse -File){
    $relative=[IO.Path]::GetRelativePath($inputFolder,$file.FullName).Replace('\','/')
    $name=(Split-Path $inputFolder -Leaf)+'/'+$relative
    $entry=$zip.GetEntry($name);if(!$entry){throw 'Missing ZIP benchmark entry.'}
    $stream=$entry.Open()
    try{$hash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream))}finally{$stream.Dispose()}
    if($hash -ne (Get-FileHash -LiteralPath $file.FullName).Hash){throw 'ZIP benchmark SHA-256 mismatch.'}
   }
  }finally{$zip.Dispose();$zipStream.Dispose()}
 }finally{$process.Refresh();if(!$process.HasExited){Stop-Process -Id $process.Id}}
}}
$result=[pscustomobject]@{Source=$Source;Method='Application shell-request entry including startup; output is a benchmark copy, not the original folder. Not measured Explorer menu latency.';Verification='All ZIP contents SHA-256 checked';Rows=$rows}
$result | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath "$root/results.json"
$result | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath "$projectRoot/docs/performance-shell.json"
