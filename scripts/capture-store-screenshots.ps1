param(
  [string]$Exe,
  [string]$OutDir,
  [ValidateSet('en','zh')][string]$Lang='en',
  [string]$Prefix='en'
)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if(!$Exe){$Exe=Join-Path $root 'artifacts/app/New7z.App.exe'}
if(!$OutDir){$OutDir=Join-Path $root 'artifacts/store-shots'}
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class Native {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out RECT value, int size);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
}
"@
[Native]::SetProcessDPIAware() | Out-Null
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# Public, English-named sample content so no personal paths appear in the shots.
$demo='C:\Users\Public\Documents\New7z Demo'
Remove-Item -LiteralPath $demo -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path "$demo\reports","$demo\images","$demo\config" | Out-Null
Set-Content -LiteralPath "$demo\README.txt" -Encoding utf8 -Value 'Sample project files for the New7z demo archive.'
Set-Content -LiteralPath "$demo\reports\summary.txt" -Encoding utf8 -Value 'Quarterly summary: 12 archives created, 340 MB compressed.'
Set-Content -LiteralPath "$demo\reports\notes.md" -Encoding utf8 -Value '# Notes\n- 7z and ZIP are supported\n- RAR is extract-only'
Set-Content -LiteralPath "$demo\config\settings.json" -Encoding utf8 -Value '{ "theme": "dark", "language": "en" }'
Copy-Item -LiteralPath (Join-Path $root '图标/logo.png') -Destination "$demo\images\logo.png" -Force
$sevenZip=Join-Path $root '7zSdk/bin/x64/7zr.exe'
$archive='C:\Users\Public\Documents\sample-project.7z'
Remove-Item -LiteralPath $archive -Force -ErrorAction SilentlyContinue
Push-Location $demo
try { & $sevenZip a $archive '*.*' -r | Out-Null } finally { Pop-Location }
$zip='C:\Users\Public\Documents\photos-backup.zip'
Remove-Item -LiteralPath $zip -Force -ErrorAction SilentlyContinue
Compress-Archive -Path "$demo\images\*" -DestinationPath $zip -Force
Remove-Item -LiteralPath $demo -Recurse -Force -ErrorAction SilentlyContinue

$settingsPath=Join-Path $env:LOCALAPPDATA 'New7z\settings.json'
$backup=Join-Path $OutDir 'settings.backup.json'
Copy-Item -LiteralPath $settingsPath -Destination $backup -Force
function Update-Settings([string]$language,[object]$recent){
  $json=Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
  $json.Language=$language
  $json.Recent=$recent
  ($json | ConvertTo-Json -Depth 5 -Compress) | Set-Content -LiteralPath $settingsPath -Encoding utf8NoBOM
}
function Capture([string]$name,[string]$argument){
  if($argument){ $p=Start-Process -FilePath $Exe -ArgumentList ('"'+$argument+'"') -PassThru }
  else { $p=Start-Process -FilePath $Exe -PassThru }
  Start-Sleep -Seconds 8
  $p.Refresh()
  $h=$p.MainWindowHandle
  if($h -eq [IntPtr]::Zero){ Stop-Process -Id $p.Id -Force; throw "No main window for $name" }
  [Native]::ShowWindow($h,9) | Out-Null
  [Native]::SetForegroundWindow($h) | Out-Null
  Start-Sleep -Milliseconds 1500
  $r=New-Object Native+RECT
  $dwm=[Native]::DwmGetWindowAttribute($h,9,[ref]$r,16)
  if($dwm -ne 0){ [Native]::GetWindowRect($h,[ref]$r) | Out-Null }
  $w=$r.Right-$r.Left; $hh=$r.Bottom-$r.Top
  $bmp=New-Object System.Drawing.Bitmap $w,$hh
  $g=[System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($r.Left,$r.Top,0,0,$bmp.Size)
  $file=Join-Path $OutDir $name
  $inset=16
  $cropRect=New-Object System.Drawing.Rectangle $inset,$inset,($w-2*$inset),($hh-2*$inset)
  $crop=$bmp.Clone($cropRect,$bmp.PixelFormat)
  $crop.Save($file,[System.Drawing.Imaging.ImageFormat]::Png)
  $crop.Dispose()
  $g.Dispose(); $bmp.Dispose()
  Stop-Process -Id $p.Id -Force
  Write-Host "$file $($cropRect.Width)x$($cropRect.Height)"
}
try{
  Update-Settings $Lang @($archive,$zip)
  Capture "$Prefix-01-home.png" $null
  Capture "$Prefix-02-archive.png" $archive
}
finally{
  Copy-Item -LiteralPath $backup -Destination $settingsPath -Force
}
