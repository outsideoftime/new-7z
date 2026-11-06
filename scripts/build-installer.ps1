param([string]$IsccPath,[string]$SigningCertificateThumbprint,[switch]$Untrimmed)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
$version=([xml](Get-Content "$projectRoot/Directory.Build.props" -Raw)).Project.PropertyGroup.Version
if(!$IsccPath){
 foreach($candidate in @("$projectRoot/artifacts/tools/InnoSetup/ISCC.exe",'C:/Program Files (x86)/Inno Setup 6/ISCC.exe','C:/Program Files/Inno Setup 7/ISCC.exe')){if(Test-Path -LiteralPath $candidate){$IsccPath=$candidate;break}}
}
if(!$IsccPath){throw 'Inno Setup compiler not found. Pass -IsccPath.'}
$release=Join-Path $projectRoot 'artifacts/releases'
$payload=Join-Path $projectRoot ('artifacts/installer-payload-'+$version+'-'+[guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path $release,$payload -Force | Out-Null
Push-Location $projectRoot
try{
 if(!(Test-Path -LiteralPath "$projectRoot/artifacts/native/x64/7z.dll")){& "$PSScriptRoot/build-native.ps1"}
 & "$PSScriptRoot/build-shell.ps1"
 $trim=if($Untrimmed){'false'}else{'true'}
 & dotnet publish src/New7z.App/New7z.App.csproj -c Release -p:Platform=x64 "-p:TrimRelease=$trim" --self-contained true -o $payload
 if($LASTEXITCODE){throw 'Release publish failed.'}
 # Debugger-only runtime files and WinUI UI strings for languages this app never uses.
 Get-ChildItem -LiteralPath $payload -File | Where-Object {$_.Name -match '^(mscordaccore.*|mscordbi|Microsoft\.DiaSymReader\.Native\.amd64)\.dll$'} | Remove-Item -Force
 foreach($directory in Get-ChildItem -LiteralPath $payload -Directory){
  if($directory.Name -notmatch '^[a-z]{2,3}(-[A-Za-z]{2,8})*$' -or $directory.Name -in @('zh-CN','en-US')){continue}
  $kept=@(Get-ChildItem -LiteralPath $directory.FullName -Recurse -File -Force | Where-Object {$_.Extension -ne '.mui'})
  if($kept.Count -eq 0){Remove-Item -LiteralPath $directory.FullName -Recurse -Force}
 }
 Copy-Item -LiteralPath 'artifacts/native/x64/New7z.ShellHost.exe' -Destination "$payload/New7z.ShellHost.exe" -Force
 New-Item -ItemType Directory -Path "$payload/Licenses/Source" -Force | Out-Null
 Copy-Item -LiteralPath "$projectRoot/LICENSE" -Destination "$payload/Licenses/New7z-MIT.txt" -Force
 Copy-Item -LiteralPath "$projectRoot/THIRD-PARTY-NOTICES.md" -Destination "$payload/Licenses/THIRD-PARTY-NOTICES.md" -Force
 $compilerLicense=Join-Path (Split-Path $IsccPath -Parent) 'License.txt'
 if(Test-Path -LiteralPath $compilerLicense){Copy-Item -LiteralPath $compilerLicense -Destination "$payload/Licenses/InnoSetup-License.txt" -Force}
 Copy-Item -LiteralPath '7zSdk/7z2605-src.7z' -Destination "$payload/Licenses/Source/7z2605-src.7z" -Force
 Copy-Item -LiteralPath '7zSdk/Source/DOC/readme.txt' -Destination "$payload/Licenses/7-Zip-Source-Readme.txt" -Force
 Copy-Item -LiteralPath 'native/New7z.Native.cpp','scripts/build-native.ps1' -Destination "$payload/Licenses/Source" -Force
 Set-Content -LiteralPath "$payload/Licenses/SOURCE.txt" -Encoding utf8 -Value 'Unmodified 7-Zip 26.05 source archive is included in Source/7z2605-src.7z. Original: https://github.com/ip7z/7zip/releases/tag/26.05. New7z dynamically loads 7z.dll; replacement with an ABI-compatible build is allowed. Native bridge and build script are included for reference. Source archive SHA-256 is listed in the release manifest.'
 $nuget=Join-Path $env:USERPROFILE '.nuget/packages'
 Copy-Item -LiteralPath "$nuget/microsoft.windowsappsdk.winui/1.8.260803003/license.txt" -Destination "$payload/Licenses/WindowsAppSDK-License.txt" -Force
 $notice=Get-ChildItem "$nuget/microsoft.windowsappsdk.foundation/1.8.260803002" -Filter NOTICE.txt -File | Select-Object -First 1
 if($notice){Copy-Item -LiteralPath $notice.FullName -Destination "$payload/Licenses/WindowsAppSDK-NOTICE.txt" -Force}
 $runtime=Get-ChildItem "$nuget/microsoft.netcore.app.runtime.win-x64" -Directory | Sort-Object {[version]$_.Name} -Descending | Select-Object -First 1
 foreach($notice in @('LICENSE.TXT','THIRD-PARTY-NOTICES.TXT')){
  $found=Get-ChildItem -LiteralPath $runtime.FullName -File -Recurse | Where-Object {$_.Name -ieq $notice} | Select-Object -First 1
  if($found){Copy-Item -LiteralPath $found.FullName -Destination "$payload/Licenses/DotNet-$notice" -Force}
 }
 $identity=Join-Path $payload 'ShellIdentity'
 New-Item -ItemType Directory -Path "$identity/Assets" -Force | Out-Null
 Copy-Item -LiteralPath 'shell/AppxManifest.xml' -Destination "$identity/AppxManifest.xml" -Force
 $manifestText=Get-Content -LiteralPath "$identity/AppxManifest.xml" -Raw
 $manifestText=[regex]::Replace($manifestText,'(<Identity[^>]*Version=")[0-9.]+(")',('${1}'+$version+'.0${2}'))
 Set-Content -LiteralPath "$identity/AppxManifest.xml" -Value $manifestText -Encoding utf8
 Copy-Item -LiteralPath '图标/logo.png' -Destination "$identity/Assets/logo.png" -Force
 $sdk=Get-ChildItem 'C:/Program Files (x86)/Windows Kits/10/bin' -Directory | Where-Object {$_.Name -match '^10\.' -and (Test-Path -LiteralPath "$($_.FullName)/x64/makeappx.exe")} | Sort-Object {[version]$_.Name} -Descending | Select-Object -First 1
 if(!$sdk){throw 'Windows SDK MakeAppx not found.'}
 $identityInput=Join-Path $projectRoot ('artifacts/identity-input-'+[guid]::NewGuid().ToString('N'))
 New-Item -ItemType Directory -Path "$identityInput/Assets" -Force | Out-Null
 Copy-Item -LiteralPath "$identity/AppxManifest.xml" -Destination "$identityInput/AppxManifest.xml"
 Copy-Item -LiteralPath "$identity/Assets/logo.png" -Destination "$identityInput/Assets/logo.png"
 $identityPackage=Join-Path $projectRoot 'artifacts/New7z.ContextMenu.msix'
 & "$($sdk.FullName)/x64/makeappx.exe" pack /o /h SHA256 /d $identityInput /p $identityPackage /nv 2>&1 | Out-File -LiteralPath "$release/makeappx-build.log" -Encoding utf8
 if($LASTEXITCODE){throw 'Identity package build failed.'}
 Copy-Item -LiteralPath $identityPackage -Destination "$identity/New7z.ContextMenu.msix" -Force
 if($SigningCertificateThumbprint){
  $cert=Get-Item -LiteralPath "Cert:/CurrentUser/My/$SigningCertificateThumbprint"
  if(!$cert.HasPrivateKey -or $cert.Subject -ne 'CN=New7z'){throw 'Signing requires an explicitly selected certificate with private key and subject CN=New7z.'}
  foreach($file in @("$identity/New7z.ContextMenu.msix","$payload/New7z.App.exe","$payload/New7z.Shell.dll","$payload/New7z.ShellHost.exe")){
   & "$($sdk.FullName)/x64/signtool.exe" sign /fd SHA256 /sha1 $SigningCertificateThumbprint $file
   if($LASTEXITCODE){throw 'Code signing failed.'}
  }
  Set-Content -LiteralPath "$identity/signed-package.marker" -Value 'Explicit certificate signing enabled.'
 }elseif(Test-Path -LiteralPath "$identity/signed-package.marker"){Remove-Item -LiteralPath "$identity/signed-package.marker"}
 foreach($required in @('New7z.App.exe','New7z.App.pri','resources.pri','MainWindow.xbf','ShellTaskWindow.xbf','7z.dll','New7z.Native.dll','New7z.Shell.dll','New7z.ShellHost.exe','Assets/New7z.ico','Assets/Formats/7z.ico','Assets/Formats/zip.ico','Licenses/LGPL.txt','Licenses/Source/7z2605-src.7z')){if(!(Test-Path -LiteralPath (Join-Path $payload $required))){throw "Release payload missing: $required"}}
 $hashes=@(Get-ChildItem -LiteralPath $payload -File -Recurse | Where-Object Extension -ne '.pdb' | Sort-Object FullName | ForEach-Object {[pscustomobject]@{File=[IO.Path]::GetRelativePath($payload,$_.FullName);SHA256=(Get-FileHash -LiteralPath $_.FullName).Hash;Size=$_.Length}})
 $payloadBytes=[Text.Encoding]::UTF8.GetBytes(($hashes | ConvertTo-Json -Depth 4 -Compress))
 $buildId=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($payloadBytes)).Substring(0,12).ToLowerInvariant()
 if((Test-Path "$payload/onnxruntime.dll") -or (Test-Path "$payload/DirectML.dll")){throw 'Unused AI/ML runtime leaked into the installer.'}
 [pscustomobject]@{Version=$version;Author='New7z';License='MIT (own code), third-party licenses retained';BuildId=$buildId;Architecture='x64';Trimmed=(!$Untrimmed);Signed=[bool]$SigningCertificateThumbprint;Files=$hashes} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath "$release/release-manifest.json" -Encoding utf8
 Set-Content -LiteralPath "$projectRoot/artifacts/latest-installer-payload.txt" -Value $payload
 & $IsccPath /Q "/DAppVersion=$version" "/DBuildId=$buildId" "/DPayloadDir=$payload" "/DReleaseDir=$release" installer/New7z.iss
 if($LASTEXITCODE){throw 'Installer compilation failed.'}
 $setup=Join-Path $release "New7z-$version-x64-Setup.exe"
 if($SigningCertificateThumbprint){& "$($sdk.FullName)/x64/signtool.exe" sign /fd SHA256 /sha1 $SigningCertificateThumbprint $setup;if($LASTEXITCODE){throw 'Installer signing failed.'}}
 $hash=(Get-FileHash -LiteralPath $setup).Hash
 Set-Content -LiteralPath "$release/SHA256SUMS.txt" -Encoding ascii -Value "$hash  $([IO.Path]::GetFileName($setup))"
 Write-Host "Installer: $setup"
 Write-Host "SHA256: $hash"
}finally{Pop-Location}
