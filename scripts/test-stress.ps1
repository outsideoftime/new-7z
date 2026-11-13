param([int]$LargeMiB=64,[int]$FileCount=10000,[string]$OutputDirectory)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
if(!$OutputDirectory){$OutputDirectory=Join-Path $projectRoot 'artifacts/tests/stress'}
Push-Location $projectRoot
try{
 & dotnet run --project tests/New7z.Performance -c Release -- --stress $OutputDirectory $LargeMiB $FileCount
 if($LASTEXITCODE){throw 'Archive stress tests failed.'}
}finally{Pop-Location}
