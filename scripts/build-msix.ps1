param(
  [switch]$SkipNative,
  [switch]$RequireStoreIdentity
)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
$manifestPath=Join-Path $projectRoot 'src/New7z.App/Package.appxmanifest'
$env:MSBuildEnableWorkloadResolver='false'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
Push-Location $projectRoot
$originalManifest=$null
try
{
  if(!$SkipNative){& "$PSScriptRoot/build-native.ps1"}
  & "$PSScriptRoot/build-shell.ps1"
  & "$PSScriptRoot/make-store-assets.ps1"

  $version=(Select-Xml -Path 'Directory.Build.props' -XPath '/Project/PropertyGroup/Version').Node.InnerText.Trim()
  if(!$version){throw 'Version is missing from Directory.Build.props.'}
  $originalManifest=Get-Content -LiteralPath $manifestPath -Raw
  $stamped=[regex]::Replace($originalManifest,'(<Identity[^>]*?Version=")[0-9.]+(")','${1}'+$version+'.0${2}')
  Set-Content -LiteralPath $manifestPath -Value $stamped -Encoding utf8NoBOM

  if($RequireStoreIdentity)
  {
    if($stamped -match 'Publisher="CN=0{8}-0{4}-0{4}-0{4}-0{12}"')
    {
      throw 'Package.appxmanifest still uses the placeholder Publisher. Copy Name, Publisher and PublisherDisplayName from Partner Center > Product identity before producing a Store upload.'
    }
  }

  & dotnet publish 'src/New7z.App/New7z.App.csproj' -c Release -p:Platform=x64 -p:StorePackage=true -p:TrimRelease=false --self-contained true
  if($LASTEXITCODE){throw 'MSIX publish failed.'}

  $msix=Get-ChildItem -LiteralPath 'artifacts/msix' -Filter '*.msix' -Recurse | Sort-Object LastWriteTime -Descending | Select-Object -First 1
  $upload=Get-ChildItem -LiteralPath 'artifacts/msix' -Filter '*.msixupload' -Recurse -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
  if(!$msix){throw 'No MSIX was produced.'}
  Write-Host "MSIX:   $($msix.FullName)"
  if($upload){Write-Host "Upload: $($upload.FullName)"}
  Write-Host "Version: $version"
}
finally
{
  if($originalManifest -ne $null){Set-Content -LiteralPath $manifestPath -Value $originalManifest -Encoding utf8NoBOM}
  Pop-Location
}
