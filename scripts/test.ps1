$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
$env:MSBuildEnableWorkloadResolver='false'
Push-Location $projectRoot
try {& dotnet run --project tests/New7z.Integration -c Release;if($LASTEXITCODE){throw 'Engine integration tests failed.'}}finally{Pop-Location}
