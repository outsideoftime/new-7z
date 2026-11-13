param([switch]$SkipNative,[switch]$Run)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
$env:MSBuildEnableWorkloadResolver='false'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
Push-Location $projectRoot
try {
 if(!$SkipNative){& "$PSScriptRoot/build-native.ps1"}
 & "$PSScriptRoot/build-shell.ps1"
 & dotnet publish src/New7z.App/New7z.App.csproj -c Release -p:Platform=x64 --self-contained true -o "$projectRoot/artifacts/app"
 if($LASTEXITCODE){throw 'App publish failed.'}
 Copy-Item -LiteralPath "$projectRoot/artifacts/native/x64/New7z.ShellHost.exe" -Destination "$projectRoot/artifacts/app/New7z.ShellHost.exe" -Force
 foreach($resource in @('New7z.EngineHost.exe','New7z.EngineHost.dll','New7z.EngineHost.deps.json','New7z.EngineHost.runtimeconfig.json','Assets/FileTypes/csharp.png','Licenses/FileTypeIcons-MIT.txt')){
   if(!(Test-Path -LiteralPath "$projectRoot/artifacts/app/$resource")){throw "Published worker or icon resource missing: $resource"}
 }
 foreach($resource in @('New7z.App.pri','resources.pri','App.xbf','MainWindow.xbf','ShellTaskWindow.xbf','7z.dll','New7z.Native.dll','New7z.Shell.dll','New7z.ShellHost.exe','Assets/logo.png','Assets/New7z.ico')){
   if(!(Test-Path -LiteralPath "$projectRoot/artifacts/app/$resource")){throw "Published resource missing: $resource"}
 }
 Copy-Item -LiteralPath "$projectRoot/7zSdk/Source/DOC/readme.txt" -Destination "$projectRoot/artifacts/app/Licenses/7-Zip-Source-Readme.txt" -Force
 Set-Content -LiteralPath "$projectRoot/artifacts/app/Licenses/SOURCE.txt" -Value '7-Zip 26.05 unmodified engine source: https://github.com/ip7z/7zip/releases/tag/26.05 ; local source: 7zSdk/Source. New7z.Native.cpp is an application adapter. See LGPL.txt and 7-Zip-License.txt.' -Encoding utf8
 Write-Host "Ready: $projectRoot/artifacts/app/New7z.App.exe"
 if($Run){Start-Process -FilePath "$projectRoot/artifacts/app/New7z.App.exe" -WindowStyle Hidden}
}finally{Pop-Location}
