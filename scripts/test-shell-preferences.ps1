$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
$vsRoot=& 'C:/Program Files (x86)/Microsoft Visual Studio/Installer/vswhere.exe' -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if(!$vsRoot){throw 'Visual Studio C++ tools not found.'}
$vcvars=Join-Path $vsRoot 'VC/Auxiliary/Build/vcvars64.bat'
$output=Join-Path $projectRoot 'artifacts/native/x64'
Push-Location $projectRoot
try{
 & cmd /c "`"$vcvars`" >nul && cl /nologo /std:c++17 /EHsc /O2 /MT /utf-8 tests\New7z.ShellPreferences.cpp /Fo`"$output\ShellPreferences.Tests.obj`" /link /OUT:`"$output\ShellPreferences.Tests.exe`" shell32.lib ole32.lib uuid.lib advapi32.lib"
 if($LASTEXITCODE){throw 'Shell preference test build failed.'}
 & "$output/ShellPreferences.Tests.exe" "$output/New7z.Shell.dll" "$projectRoot/artifacts/preferences-fixture" | Tee-Object -FilePath "$projectRoot/docs/shell-preferences-tests.txt"
 if($LASTEXITCODE){throw 'Shell preference tests failed.'}
}finally{Pop-Location}
