param([string]$PayloadDirectory,[string]$IsccPath)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
if(!$PayloadDirectory){$PayloadDirectory=(Get-Content "$projectRoot/artifacts/latest-installer-payload.txt" -Raw).Trim()}
if(!$IsccPath){$IsccPath="$projectRoot/artifacts/tools/InnoSetup/ISCC.exe"}
$manifest=Get-Content "$projectRoot/artifacts/releases/release-manifest.json" -Raw | ConvertFrom-Json
$fixture=Join-Path $projectRoot ('artifacts/install-test-'+[guid]::NewGuid().ToString('N'))
$install=Join-Path $fixture '中文 安装目录'
$testId='{6869BFEF-78AC-4D2E-9420-316BCBCBE318}'
$uninstallRegistry="HKCU:/Software/Microsoft/Windows/CurrentVersion/Uninstall/${testId}_is1"
if(Test-Path -LiteralPath $uninstallRegistry){throw 'A previous test installation exists; refusing to overwrite it.'}
New-Item -ItemType Directory -Path $fixture -Force | Out-Null
# A distinct product ID protects the user's actual New7z uninstall registration.
& $IsccPath /Q "/DAppVersion=$($manifest.Version)" "/DBuildId=$($manifest.BuildId)" "/DPayloadDir=$PayloadDirectory" "/DReleaseDir=$fixture" '/DInstallerAppId={{6869BFEF-78AC-4D2E-9420-316BCBCBE318}' '/DSetupBaseName=New7z-IntegrationTest' "$projectRoot/installer/New7z.iss"
if($LASTEXITCODE){throw 'Cannot compile isolated test installer.'}
$SetupPath=Join-Path $fixture 'New7z-IntegrationTest.exe'
$checks=[Collections.Generic.List[string]]::new()
$registry=[Microsoft.Win32.Registry]::CurrentUser
$ownKeys=@('Software\Classes\Applications\New7z.App.exe','Software\Classes\CLSID\{642CF40A-831E-42A9-AC63-F5A0ABDD5C17}','Software\Classes\CLSID\{642CF40A-831E-42A9-AC63-F5A0ABDD5C18}','Software\Classes\*\shell\New7z','Software\Classes\Directory\shell\New7z','Software\Classes\New7z.Archive','Software\New7z\Capabilities')
$ownKeys+=@('.7z','.zip','.rar','.tar','.gz','.xz','.bz2','.iso' | ForEach-Object {"Software\Classes\New7z$_"})
$backups=@()
foreach($path in $ownKeys){
 $key=$registry.OpenSubKey($path)
 if($key){$key.Dispose();$backup=Join-Path $fixture ('registry-'+$backups.Count+'.reg');& reg.exe export "HKCU\$path" $backup /y | Out-Null;if($LASTEXITCODE){throw 'Cannot snapshot existing integration.'};$backups+=,[pscustomobject]@{Path=$path;File=$backup}}
}
# Snapshot only our values in shared Windows keys, never the whole shared key.
$valueBackups=@()
$sharedValues=@([pscustomobject]@{Path='Software\RegisteredApplications';Name='New7z'})
$sharedValues+=@('.7z','.zip','.rar','.tar','.gz','.xz','.bz2','.iso' | ForEach-Object {[pscustomobject]@{Path="Software\Classes\$_\OpenWithProgids";Name='New7z.Archive'}})
$sharedValues+=@([pscustomobject]@{Path='Software\New7z\Explorer';Name='HideWindowsCompressMenu'},[pscustomobject]@{Path='Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked';Name='{7AE6900F-6EB0-44A2-9CA1-DB2F7EF352AF}'})
foreach($entry in $sharedValues){
 $key=$registry.OpenSubKey($entry.Path);$had=$key -and $key.GetValueNames().Contains($entry.Name)
 $valueBackups+=,[pscustomobject]@{Path=$entry.Path;Name=$entry.Name;Had=$had;Value=$(if($had){$key.GetValue($entry.Name)});Kind=$(if($had){$key.GetValueKind($entry.Name)})}
 if($key){$key.Dispose()}
}
$oldPackages=@(Get-AppxPackage -Name New7z.ContextMenu)
$installedStatePath=Join-Path $env:LOCALAPPDATA 'Programs/New7z/installation.json'
$originalState=if(Test-Path -LiteralPath $installedStatePath){Get-Content $installedStatePath -Raw | ConvertFrom-Json}
$shortcut=Join-Path ([Environment]::GetFolderPath('Programs')) 'New7z.lnk'
$shortcutBackup=Join-Path $fixture 'New7z.lnk.backup'
if(Test-Path -LiteralPath $shortcut){Copy-Item -LiteralPath $shortcut -Destination $shortcutBackup}
function Preferences([switch]$ExceptCompressMenu) {
 $key=$registry.OpenSubKey('Software\New7z\Explorer')
 if(!$key){return '<absent>'}
 try{$names=@($key.GetValueNames() | Sort-Object);if($ExceptCompressMenu){$names=@($names | Where-Object {$_ -ne 'HideWindowsCompressMenu'})};return (@($names | ForEach-Object {"$_=$($key.GetValue($_))"}) -join ';')}finally{$key.Dispose()}
}
$preferencesBefore=Preferences -ExceptCompressMenu
$appProcess=$null
function Check([bool]$Condition,[string]$Name){if(!$Condition){throw "FAIL: $Name"};$checks.Add($Name);Write-Host "PASS $Name"}
function Run-Setup([string]$Extra,[string]$Log){
 $arguments='/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /NOICONS /NOCLOSEAPPLICATIONS /DIR="'+$install+'" /TASKS="shellmenu,fileassoc" /LOG="'+(Join-Path $fixture $Log)+'" '+$Extra
 $process=Start-Process -FilePath $SetupPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
 if(!$process.WaitForExit(120000)){throw 'Installer timed out.'}
 Check ($process.ExitCode -eq 0) "setup exit code ($Log)"
}
function Uninstall {
 $uninstaller=Join-Path $install 'Uninstall/unins000.exe'
 if(Test-Path -LiteralPath $uninstaller){
  $process=Start-Process -FilePath $uninstaller -ArgumentList ('/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG="'+(Join-Path $fixture 'uninstall.log')+'"') -WindowStyle Hidden -PassThru
  if(!$process.WaitForExit(60000)){throw 'Uninstaller timed out.'}
  if($process.ExitCode -ne 0){throw "Uninstall failed: $($process.ExitCode)"}
 }
}
try{
 Run-Setup '' 'install-modern.log'
 $state=Get-Content -LiteralPath "$install/installation.json" -Raw | ConvertFrom-Json
 Check ($state.MenuMode -in @('modern+classic','classic')) 'compatible menu always registered'
 $runtime=$state.RuntimeDirectory
 Check ($runtime.StartsWith($install+'\',[StringComparison]::OrdinalIgnoreCase)) 'runtime is inside selected directory'
 foreach($file in @('New7z.App.exe','New7z.Core.dll','resources.pri','MainWindow.xbf','ShellTaskWindow.xbf','7z.dll','New7z.Native.dll','New7z.Shell.dll','Assets/New7z.ico','Licenses/Source/7z2605-src.7z','Licenses/New7z-MIT.txt','Installer/Integrate-Shell.ps1')){Check (Test-Path -LiteralPath (Join-Path $runtime $file)) "installed $file"}
 $server=Get-ItemPropertyValue -LiteralPath 'HKCU:/Software/Classes/CLSID/{642CF40A-831E-42A9-AC63-F5A0ABDD5C18}/InprocServer32' -Name '(default)'
 Check ($server -eq "$runtime\New7z.Shell.dll") 'compatible COM uses separate class and installed DLL'
 & "$projectRoot/artifacts/native/x64/ShellPreferences.Tests.exe" --registered $server
 Check ($LASTEXITCODE -eq 0) 'installed COM activation and icon resource'
 Check ((Get-ItemPropertyValue -LiteralPath 'HKCU:/Software/RegisteredApplications' -Name New7z) -eq 'Software\New7z\Capabilities') 'Windows Default Apps registration'
 Check ((Get-ItemPropertyValue -LiteralPath 'HKCU:/Software/Classes/New7z.7z/shell/open/command' -Name '(default)') -eq ('"'+$runtime+'\New7z.App.exe" "%1"')) 'archive open command quotes Unicode and space paths'
 foreach($extension in @('.7z','.zip','.rar','.tar','.gz','.xz','.bz2','.iso')){
  Check ((Get-ItemPropertyValue -LiteralPath 'HKCU:/Software/New7z/Capabilities/FileAssociations' -Name $extension) -eq "New7z$extension") "default-app capability $extension"
  $key=$registry.OpenSubKey("Software\Classes\$extension\OpenWithProgids")
  try{Check ($key.GetValueNames().Contains("New7z$extension")) "Open With registration $extension"}finally{$key.Dispose()}
  Check ((Get-ItemPropertyValue -LiteralPath "HKCU:/Software/Classes/New7z$extension/DefaultIcon" -Name '(default)') -eq "$runtime\Assets\Formats\$($extension.Substring(1)).ico") "format icon $extension"
 }
 $repairScript="$runtime/Installer/Integrate-Shell.ps1"
 $registry.DeleteSubKeyTree('Software\Classes\Directory\shell\New7z',$false)
 & "$env:WINDIR/System32/WindowsPowerShell/v1.0/powershell.exe" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $repairScript -Action Repair -InstallRoot $install -RuntimeDirectory $runtime
 Check ($LASTEXITCODE -eq 0 -and (Test-Path -LiteralPath 'HKCU:/Software/Classes/Directory/shell/New7z')) 'settings repair restores missing folder menu'
 $registry.DeleteSubKeyTree('Software\New7z\Capabilities',$false)
 & "$env:WINDIR/System32/WindowsPowerShell/v1.0/powershell.exe" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $repairScript -Action Associate -InstallRoot $install -RuntimeDirectory $runtime
 Check ($LASTEXITCODE -eq 0 -and (Test-Path -LiteralPath 'HKCU:/Software/New7z/Capabilities/FileAssociations')) 'settings repair restores Default Apps capabilities'
 $source=Join-Path $fixture 'source';New-Item -ItemType Directory -Path $source | Out-Null
 Set-Content -LiteralPath "$source/中文 说明.txt" -Value 'Published app text and image verification.' -Encoding utf8
 Copy-Item -LiteralPath "$projectRoot/图标/logo.png" -Destination "$source/预览.png"
 $archive=Join-Path $fixture '验证.7z'
 & "$projectRoot/7zSdk/bin/x64/7zr.exe" a $archive $source -mx=1 | Out-Null
 if($LASTEXITCODE){throw 'Verification archive creation failed.'}
 $reportPath=Join-Path $fixture 'published-ui.json'
 $start=[Diagnostics.ProcessStartInfo]::new("$runtime/New7z.App.exe")
 $start.WorkingDirectory=$runtime;$start.UseShellExecute=$false;$start.WindowStyle=[Diagnostics.ProcessWindowStyle]::Hidden
 foreach($arg in @('--verify-ui',$archive,$reportPath)){$start.ArgumentList.Add($arg)}
 $start.Environment['PATH']="$env:WINDIR\System32;$env:WINDIR";$start.Environment['DOTNET_ROOT']=Join-Path $fixture 'no-dotnet';$start.Environment['DOTNET_MULTILEVEL_LOOKUP']='0'
 $appProcess=[Diagnostics.Process]::Start($start)
 if(!$appProcess.WaitForExit(45000)){throw 'Published UI diagnostic timed out.'}
 Check ($appProcess.ExitCode -eq 0 -and (Test-Path $reportPath)) 'installed app starts with bundled runtime and no SDK on PATH'
 $appProcess=$null;$report=Get-Content $reportPath -Raw | ConvertFrom-Json
 Check ($report.Result -eq 'PASS' -and $report.ArchiveBinding -and $report.Text -and $report.Image) 'trimmed settings, XAML bindings, text and image preview'
 Run-Setup '/CLASSICMENU=1' 'upgrade-classic.log'
 $state=Get-Content -LiteralPath "$install/installation.json" -Raw | ConvertFrom-Json
 Check ($state.MenuMode -eq 'classic') 'compatible-only install works without modern package'
 Check ((Get-ItemPropertyValue -LiteralPath $uninstallRegistry -Name DisplayVersion) -eq $manifest.Version) 'upgrade retains one test uninstall registration'
 Uninstall
 Check (!(Test-Path -LiteralPath $uninstallRegistry)) 'uninstall removes only test product registration'
 foreach($path in @('HKCU:/Software/Classes/*/shell/New7z','HKCU:/Software/Classes/Directory/shell/New7z','HKCU:/Software/Classes/New7z.Archive','HKCU:/Software/New7z/Capabilities')){Check (!(Test-Path -LiteralPath $path)) "uninstall removes $path"}
 foreach($extension in @('.7z','.zip','.rar','.tar','.gz','.xz','.bz2','.iso')){Check (!(Test-Path -LiteralPath "HKCU:/Software/Classes/New7z$extension")) "uninstall removes ProgID $extension"}
 $key=$registry.OpenSubKey('Software\RegisteredApplications');try{Check (!$key.GetValueNames().Contains('New7z')) 'uninstall removes owned Default Apps entry'}finally{$key.Dispose()}
 Check (!(Test-Path -LiteralPath "$runtime/New7z.App.exe")) 'uninstall removes installed executable'
 Check ((Preferences -ExceptCompressMenu) -eq $preferencesBefore) 'upgrade and uninstall preserve menu preferences'
 $compressPreference=$registry.OpenSubKey('Software\New7z\Explorer')
 try{$compressValue=if($compressPreference){$compressPreference.GetValue('HideWindowsCompressMenu')}else{$null}}finally{if($compressPreference){$compressPreference.Dispose()}}
 Check ($null -eq $compressValue -or $compressValue -eq 0) 'uninstall clears the Windows compression menu preference'
}finally{
 if($appProcess){$appProcess.Refresh();if(!$appProcess.HasExited){Stop-Process -Id $appProcess.Id -ErrorAction SilentlyContinue}}
 try{Uninstall}finally{
  foreach($path in $ownKeys){$registry.DeleteSubKeyTree($path,$false)}
  foreach($backup in $backups){& reg.exe import $backup.File | Out-Null;if($LASTEXITCODE){throw 'Cannot restore original integration.'}}
  foreach($entry in $valueBackups){
   if($entry.Had){$key=$registry.CreateSubKey($entry.Path);try{$key.SetValue($entry.Name,$entry.Value,$entry.Kind)}finally{$key.Dispose()}}
   else{$key=$registry.OpenSubKey($entry.Path,$true);if($key){try{$key.DeleteValue($entry.Name,$false)}finally{$key.Dispose()}}}
  }
  foreach($package in $oldPackages){
   if((Split-Path $package.InstallLocation -Leaf) -eq 'ShellIdentity'){$external=Split-Path $package.InstallLocation -Parent;$identity=Join-Path $package.InstallLocation 'AppxManifest.xml'}
   elseif($originalState -and $originalState.PackageFullName -eq $package.PackageFullName){$external=$originalState.RuntimeDirectory;$identity=Join-Path $external 'ShellIdentity/AppxManifest.xml'}
   elseif($package.InstallLocation -eq (Join-Path $projectRoot 'artifacts/shell-package')){$external=Join-Path $projectRoot 'artifacts/app';$identity=Join-Path $package.InstallLocation 'AppxManifest.xml'}
   else{throw "Cannot locate original package runtime: $($package.InstallLocation)"}
   Add-AppxPackage -Register $identity -ExternalLocation $external -ErrorAction Stop
  }
  if(Test-Path -LiteralPath $shortcutBackup){Copy-Item -LiteralPath $shortcutBackup -Destination $shortcut -Force}
 }
}
[pscustomobject]@{Result='PASS';Checks=$checks;Fixture=$fixture;Installer=$SetupPath;OriginalIntegrationRestored=$true} | ConvertTo-Json -Depth 4 | Tee-Object -FilePath "$projectRoot/docs/installer-tests.json"
