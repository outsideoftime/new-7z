$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
$vsRoot=& 'C:/Program Files (x86)/Microsoft Visual Studio/Installer/vswhere.exe' -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if(!$vsRoot){throw 'Visual Studio C++ tools not found.'}
$vcvars=Join-Path $vsRoot 'VC/Auxiliary/Build/vcvars64.bat'
$output=Join-Path $projectRoot 'artifacts/native/x64'
New-Item -ItemType Directory -Path $output -Force | Out-Null
Push-Location $projectRoot
try{
 & "$PSScriptRoot/build-icons.ps1"
 & cmd /c "`"$vcvars`" >nul && rc /nologo /fo `"$output\New7z.Shell.res`" native\New7z.Shell.rc"
 if($LASTEXITCODE){throw 'Shell icon resource build failed.'}
 & cmd /c "`"$vcvars`" >nul && cl /nologo /LD /std:c++17 /EHsc /O2 /MT /utf-8 native\New7z.Shell.cpp /Fo`"$output\New7z.Shell.obj`" /link /DEF:native\New7z.Shell.def /OUT:`"$output\New7z.Shell.dll`" /IMPLIB:`"$output\New7z.Shell.lib`" `"$output\New7z.Shell.res`" shell32.lib shlwapi.lib ole32.lib uuid.lib advapi32.lib"
 if($LASTEXITCODE){throw 'Shell extension build failed.'}
 & cmd /c "`"$vcvars`" >nul && cl /nologo /std:c++17 /EHsc /O2 /MT /utf-8 native\New7z.ShellHost.cpp /Fo`"$output\New7z.ShellHost.obj`" /link /SUBSYSTEM:WINDOWS /OUT:`"$output\New7z.ShellHost.exe`" `"$output\New7z.Shell.res`" shlwapi.lib shell32.lib"
 if($LASTEXITCODE){throw 'Shell launcher build failed.'}
}finally{Pop-Location}

