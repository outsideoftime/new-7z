$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
$vswhere='C:/Program Files (x86)/Microsoft Visual Studio/Installer/vswhere.exe'
$vsRoot=& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if(!$vsRoot){throw 'Visual Studio C++ build tools were not found.'}
$vcvars=Join-Path $vsRoot 'VC/Auxiliary/Build/vcvars64.bat'
$output=Join-Path $projectRoot 'artifacts/tests/drag-extraction'
New-Item -ItemType Directory -Path $output -Force | Out-Null
Push-Location $projectRoot
try {
  & cmd /c "`"$vcvars`" >nul && cl /nologo /std:c++17 /EHsc /O2 /MT /utf-8 /DUNICODE /D_UNICODE tests\native\DragExtraction.cpp /Fo`"$output\DragExtraction.obj`" /Fe:`"$output\DragExtraction.exe`" /link ole32.lib oleaut32.lib shell32.lib user32.lib urlmon.lib runtimeobject.lib"
  if($LASTEXITCODE){throw 'Drag extraction test build failed.'}
  & "$output/DragExtraction.exe" "$projectRoot/artifacts/native/x64/7z.dll"
  if($LASTEXITCODE){throw 'Drag extraction tests failed.'}
}finally{Pop-Location}
