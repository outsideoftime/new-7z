param(
 [ValidateSet('Install','Remove','Repair','Associate')][string]$Action='Install',
 [Parameter(Mandatory)][string]$InstallRoot,
 [string]$RuntimeDirectory,
 [ValidateSet('1','0')][string]$EnableMenu='1',
 [ValidateSet('1','0')][string]$EnableAssociations='1',
 [switch]$ClassicOnly
)
$ErrorActionPreference='Stop'
$InstallRoot=[IO.Path]::GetFullPath($InstallRoot).TrimEnd('\')
$statePath=Join-Path $InstallRoot 'installation.json'
$classId='{642CF40A-831E-42A9-AC63-F5A0ABDD5C18}'
$compressToClsid='{7AE6900F-6EB0-44A2-9CA1-DB2F7EF352AF}'
$blockedPath='Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked'
$classes=[Microsoft.Win32.Registry]::CurrentUser
function Owned-Path([string]$Path){return $Path -and $Path.StartsWith($InstallRoot+'\',[StringComparison]::OrdinalIgnoreCase)}
function Remove-Classic {
 foreach($ownedClassId in @($classId,'{642CF40A-831E-42A9-AC63-F5A0ABDD5C17}')){
 $class=$classes.OpenSubKey("Software\Classes\CLSID\$ownedClassId\InprocServer32")
 $owned=$false
 if($class){try{$owned=Owned-Path ([string]$class.GetValue(''))}finally{$class.Dispose()}}
 if(!$owned){continue}
 foreach($type in @('*','Directory')){
  $path="Software\Classes\$type\shell\New7z"
  $key=$classes.OpenSubKey($path)
  $remove=$false
  if($key){try{$remove=$key.GetValue('ExplorerCommandHandler') -eq $ownedClassId}finally{$key.Dispose()}}
  if($remove){$classes.DeleteSubKeyTree($path,$false)}
 }
 $classes.DeleteSubKeyTree("Software\Classes\CLSID\$ownedClassId",$false)
 }
}
function Register-Classic {
 $server=$classes.CreateSubKey("Software\Classes\CLSID\$classId\InprocServer32")
 try{$server.SetValue('',"$RuntimeDirectory\New7z.Shell.dll");$server.SetValue('ThreadingModel','Apartment')}finally{$server.Dispose()}
 foreach($type in @('*','Directory')){
  $key=$classes.CreateSubKey("Software\Classes\$type\shell\New7z")
  try{$key.SetValue('MUIVerb','New7z');$key.SetValue('Icon',"$RuntimeDirectory\New7z.Shell.dll,-101");$key.SetValue('ExplorerCommandHandler',$classId);$key.SetValue('CommandStateSync','')}finally{$key.Dispose()}
 }
}
function Notify-Explorer {
 if(!('New7z.ShellNotify' -as [type])){Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; namespace New7z { public static class ShellNotify { [DllImport("shell32.dll")] public static extern void SHChangeNotify(uint e,uint f,IntPtr a,IntPtr b); } }'}
 [New7z.ShellNotify]::SHChangeNotify(0x08000000,0x1000,[IntPtr]::Zero,[IntPtr]::Zero)
}
# Mirrors ExplorerPreferences.cs: remove only the block entry New7z itself wrote.
function Restore-WindowsCompressMenu {
 $preferences=$classes.OpenSubKey('Software\New7z\Explorer',$true)
 if(!$preferences){return}
 $restored=$false
 try{
  $backup=$preferences.OpenSubKey('WindowsCompressMenuBackup')
  if($backup){
   try{
    $marker=$backup.GetValue('Marker')
    $blocked=$classes.OpenSubKey($blockedPath,$true)
    if($blocked){
     try{
      $current=$blocked.GetValue($compressToClsid,$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
      if($current -is [string] -and $current -ceq $marker){
       if($backup.GetValue('HadPreviousValue') -eq 1){
        $kind=[Microsoft.Win32.RegistryValueKind]$backup.GetValue('PreviousKind',[int][Microsoft.Win32.RegistryValueKind]::String)
        $previous=$backup.GetValue('PreviousValue',$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
        if($null -ne $previous){$blocked.SetValue($compressToClsid,$previous,$kind)}
       }else{$blocked.DeleteValue($compressToClsid,$false)}
       $restored=$true
      }
     }finally{$blocked.Dispose()}
    }
   }finally{$backup.Dispose()}
   $preferences.DeleteSubKeyTree('WindowsCompressMenuBackup',$false)
  }elseif($preferences.GetValue('HideWindowsCompressMenu') -eq 1){
   # The backup was lost; remove an orphaned entry only when New7z wrote it.
   $blocked=$classes.OpenSubKey($blockedPath,$true)
   if($blocked){
    try{
     $current=$blocked.GetValue($compressToClsid,$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
     if($current -is [string] -and $current.StartsWith('New7z:',[StringComparison]::Ordinal)){
      $blocked.DeleteValue($compressToClsid,$false);$restored=$true
     }
    }finally{$blocked.Dispose()}
   }
  }
  # Only rewrite the preference when this uninstall restored the menu or the value exists,
  # so uninstalling never adds a value to a user's untouched settings.
  if($restored -or $null -ne $preferences.GetValue('HideWindowsCompressMenu')){
   $preferences.SetValue('HideWindowsCompressMenu',0,[Microsoft.Win32.RegistryValueKind]::DWord)
  }
 }finally{$preferences.Dispose()}
}
$formats=@('.7z','.zip','.rar','.tar','.gz','.xz','.bz2','.iso')
function Format-ProgId([string]$extension){return 'New7z.'+$extension.Substring(1)}
function Test-OwnedProgId([string]$progId){
 $command=$classes.OpenSubKey("Software\Classes\$progId\shell\open\command")
 if(!$command){return $false}
 try{return ([string]$command.GetValue('')).StartsWith('"'+$InstallRoot+'\',[StringComparison]::OrdinalIgnoreCase)}finally{$command.Dispose()}
}
function Register-Associations {
 $appExe="$RuntimeDirectory\New7z.App.exe"
 $application=$classes.CreateSubKey('Software\Classes\Applications\New7z.App.exe')
 try{
  $application.SetValue('FriendlyAppName','New7z')
  $command=$application.CreateSubKey('shell\open\command');try{$command.SetValue('','"'+$appExe+'" "%1"')}finally{$command.Dispose()}
  $types=$application.CreateSubKey('SupportedTypes');try{foreach($extension in $formats){$types.SetValue($extension,'')}}finally{$types.Dispose()}
 }finally{$application.Dispose()}
 $cap=$classes.CreateSubKey('Software\New7z\Capabilities')
 try{
  $cap.SetValue('ApplicationName','New7z');$cap.SetValue('ApplicationDescription','New7z 压缩、解压和归档预览');$cap.SetValue('ApplicationIcon',"$appExe,0")
  $associations=$cap.CreateSubKey('FileAssociations')
  try{foreach($extension in $formats){
   $progId=Format-ProgId $extension
   $prog=$classes.CreateSubKey("Software\Classes\$progId")
   try{
    $prog.SetValue('','New7z '+$extension.Substring(1).ToUpperInvariant()+' 压缩包')
    $icon=$prog.CreateSubKey('DefaultIcon');try{$icon.SetValue('',"$RuntimeDirectory\Assets\Formats\"+$extension.Substring(1)+'.ico')}finally{$icon.Dispose()}
    $command=$prog.CreateSubKey('shell\open\command');try{$command.SetValue('','"'+$appExe+'" "%1"')}finally{$command.Dispose()}
   }finally{$prog.Dispose()}
   $associations.SetValue($extension,$progId)
   $openWith=$classes.CreateSubKey("Software\Classes\$extension\OpenWithProgids")
   try{
    $openWith.DeleteValue('New7z.Archive',$false)
    $openWith.SetValue($progId,[byte[]]@(),[Microsoft.Win32.RegistryValueKind]::None)
   }finally{$openWith.Dispose()}
  }}finally{$associations.Dispose()}
 }finally{$cap.Dispose()}
 $registered=$classes.CreateSubKey('Software\RegisteredApplications')
 try{$registered.SetValue('New7z','Software\New7z\Capabilities')}finally{$registered.Dispose()}
}
function Remove-Associations {
 $owned=@()
 foreach($extension in $formats){
  $progId=Format-ProgId $extension
  if(Test-OwnedProgId $progId){$owned+=$progId;$classes.DeleteSubKeyTree("Software\Classes\$progId",$false)}
 }
 # Versions before 0.1.8 used one ProgID for every archive type.
 if(Test-OwnedProgId 'New7z.Archive'){$owned+='New7z.Archive';$classes.DeleteSubKeyTree('Software\Classes\New7z.Archive',$false)}
 if($owned.Count -eq 0){return}
 foreach($extension in $formats){
  $openWith=$classes.OpenSubKey("Software\Classes\$extension\OpenWithProgids",$true)
  if($openWith){try{$openWith.DeleteValue((Format-ProgId $extension),$false);$openWith.DeleteValue('New7z.Archive',$false)}finally{$openWith.Dispose()}}
 }
 $registered=$classes.OpenSubKey('Software\RegisteredApplications',$true)
 if($registered){try{if($registered.GetValue('New7z') -eq 'Software\New7z\Capabilities'){$registered.DeleteValue('New7z',$false)}}finally{$registered.Dispose()}}
 $classes.DeleteSubKeyTree('Software\New7z\Capabilities',$false)
}
function Remove-Modern([bool]$OnlyOwned){
 $previous=$null
 if($OnlyOwned -and (Test-Path -LiteralPath $statePath)){$previous=Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json}
 foreach($package in Get-AppxPackage -Name 'New7z.ContextMenu'){
  if($package.Publisher -ne 'CN=New7z'){continue}
  $owned=(Owned-Path $package.InstallLocation) -or ($previous -and (Owned-Path $previous.RuntimeDirectory) -and $previous.PackageFullName -eq $package.PackageFullName)
  if($OnlyOwned -and !$owned){continue}
  Remove-AppxPackage -Package $package.PackageFullName -ErrorAction Stop
 }
}
function Remove-OpenWith {
 $path='Software\Classes\Applications\New7z.App.exe'
 $key=$classes.OpenSubKey($path+'\shell\open\command')
 $owned=$false
 if($key){try{$owned=([string]$key.GetValue('')).StartsWith('"'+$InstallRoot+'\',[StringComparison]::OrdinalIgnoreCase)}finally{$key.Dispose()}}
 if($owned){$classes.DeleteSubKeyTree($path,$false)}
}
try{
 if($Action -eq 'Remove'){
  Remove-Modern $true;Remove-Classic;Remove-OpenWith;Remove-Associations;Restore-WindowsCompressMenu;Notify-Explorer;exit 0
 }
 $RuntimeDirectory=[IO.Path]::GetFullPath($RuntimeDirectory)
 if((!(Owned-Path $RuntimeDirectory) -and $RuntimeDirectory -ne $InstallRoot) -or !(Test-Path -LiteralPath "$RuntimeDirectory/New7z.App.exe")){throw 'Invalid application runtime directory.'}
 if($Action -eq 'Associate'){Register-Associations;Notify-Explorer;exit 0}
 if($Action -eq 'Repair'){
  $modernRegistered=$null
  try{$modernRegistered=Get-AppxPackage -Name 'New7z.ContextMenu' | Where-Object Publisher -eq 'CN=New7z' | Select-Object -First 1}catch{}
  Remove-Classic
  if(!$modernRegistered){Register-Classic}
  Notify-Explorer;exit 0
 }
 $mode='disabled';$modernError=$null
 Remove-Classic
 if($EnableMenu -eq '1'){
  if(!$ClassicOnly){
   try{
    $signed=Test-Path -LiteralPath "$RuntimeDirectory/ShellIdentity/signed-package.marker"
    $developer=[Microsoft.Win32.Registry]::LocalMachine.OpenSubKey('SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock')
    $devEnabled=$false
    if($developer){try{$devEnabled=$developer.GetValue('AllowDevelopmentWithoutDevLicense') -eq 1}finally{$developer.Dispose()}}
    if(!$signed -and !$devEnabled){throw 'Modern menu needs a trusted signed identity package; using the compatible menu.'}
    Remove-Modern $false
    if($signed){Add-AppxPackage -Path "$RuntimeDirectory/ShellIdentity/New7z.ContextMenu.msix" -ExternalLocation $RuntimeDirectory -ErrorAction Stop}
    else {Add-AppxPackage -Register "$RuntimeDirectory/ShellIdentity/AppxManifest.xml" -ExternalLocation $RuntimeDirectory -ErrorAction Stop}
    $mode='modern'
   }catch{$modernError=$_.Exception.Message}
  }
  if($mode -ne 'modern'){
   # Do not block the modern Explorer COM class or modify Windows' own compression commands.
   Remove-Modern $true
   Register-Classic
   $mode='classic'
  }
 }else{Remove-Modern $true}
 $packageFullName=$null
 if($EnableAssociations -eq '1'){Register-Associations}else{Remove-Associations;Remove-OpenWith}
 Notify-Explorer
 if($mode -eq 'modern'){$packageFullName=(Get-AppxPackage -Name New7z.ContextMenu | Where-Object Publisher -eq 'CN=New7z' | Select-Object -First 1).PackageFullName}
 $stateVersion=$null
 if($RuntimeDirectory){$stateVersion=(Split-Path $RuntimeDirectory -Leaf).Split('-')[0]}
 if(!$stateVersion -and (Test-Path -LiteralPath $statePath)){$stateVersion=((Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json).Version)}
 if(!$stateVersion){$stateVersion='unknown'}
 $state=[ordered]@{Version=$stateVersion;RuntimeDirectory=$RuntimeDirectory;MenuMode=$mode;AssociationsRegistered=($EnableAssociations -eq '1');PackageFullName=$packageFullName;ModernMenuDetail=$modernError;InstalledAt=[DateTimeOffset]::Now.ToString('o')}
 [IO.File]::WriteAllText($statePath,($state | ConvertTo-Json -Depth 4),[Text.UTF8Encoding]::new($true))
 exit 0
}catch{
 [IO.File]::WriteAllText((Join-Path $InstallRoot 'integration-error.txt'),$_.Exception.ToString(),[Text.UTF8Encoding]::new($true))
 exit 1
}
