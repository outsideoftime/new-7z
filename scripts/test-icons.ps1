$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
Add-Type -TypeDefinition @'
using System.Runtime.InteropServices;
public static class New7zIconResources {
 [DllImport("shell32.dll", CharSet=CharSet.Unicode)]
 public static extern uint ExtractIconEx(string file,int index,System.IntPtr large,System.IntPtr small,uint count);
}
'@
$checks=@(foreach($relative in @('New7z.App.exe','New7z.Shell.dll','New7z.ShellHost.exe')){
 $file=Join-Path $projectRoot "artifacts/app/$relative"
 $count=[New7zIconResources]::ExtractIconEx($file,-1,[IntPtr]::Zero,[IntPtr]::Zero,0)
 if($count -lt 1){throw "No Windows icon resources found: $relative"}
 [pscustomobject]@{File=$relative;IconGroups=$count}
})
$ico=Join-Path $projectRoot 'artifacts/app/Assets/New7z.ico'
$reader=[IO.BinaryReader]::new([IO.File]::OpenRead($ico))
try{
 if($reader.ReadUInt16() -ne 0 -or $reader.ReadUInt16() -ne 1){throw 'Invalid ICO header.'}
 $count=$reader.ReadUInt16();if($count -ne 7){throw 'Expected seven ICO sizes.'}
 $sizes=@(for($i=0;$i -lt $count;$i++){
  $size=$reader.ReadByte();$reader.ReadBytes(15) | Out-Null
  if($size -eq 0){256}else{[int]$size}
 })
}finally{$reader.Dispose()}
[pscustomobject]@{Result='PASS';Resources=$checks;IconSizes=$sizes} | ConvertTo-Json -Depth 4 | Tee-Object -FilePath "$projectRoot/docs/icon-integration.json"
