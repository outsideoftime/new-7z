param([switch]$Remove)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
$app=Join-Path $projectRoot 'artifacts/app'
if($Remove){Get-AppxPackage -Name 'New7z.ContextMenu' | Remove-AppxPackage;Write-Host 'New7z Explorer menu removed for the current user.';return}
foreach($required in @('New7z.Shell.dll','New7z.ShellHost.exe','New7z.App.exe','resources.pri','ShellTaskWindow.xbf','Assets/New7z.ico')){
 if(!(Test-Path -LiteralPath "$app/$required")){throw "Build the shell extension and app first. Missing: $required"}
}
$package=Join-Path $projectRoot 'artifacts/shell-package'
New-Item -ItemType Directory -Path "$package/Assets" -Force | Out-Null
Copy-Item -LiteralPath "$projectRoot/shell/AppxManifest.xml" -Destination "$package/AppxManifest.xml" -Force
Copy-Item -LiteralPath "$projectRoot/图标/logo.png" -Destination "$package/Assets/logo.png" -Force
Add-AppxPackage -Register "$package/AppxManifest.xml" -ExternalLocation $app
Get-AppxPackage -Name 'New7z.ContextMenu' | Select-Object Name,PackageFullName,Status
