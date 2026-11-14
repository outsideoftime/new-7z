param(
 [string]$Token=$env:GITEE_TOKEN,
 [string]$Repo='wgliangzero/new-7z',
 [string]$Tag,
 [string]$Name,
 [string]$NotesFile,
 [string[]]$Assets
)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
if(!$Token){throw 'Gitee access token required: pass -Token or set GITEE_TOKEN.'}
$version=([xml](Get-Content "$projectRoot/Directory.Build.props" -Raw)).Project.PropertyGroup.Version
if(!$Tag){$Tag="v$version"}
if(!$Name){$Name="New7z $version"}
$releaseDir=Join-Path $projectRoot 'artifacts/releases'
if(!$NotesFile){$NotesFile=Join-Path $releaseDir "RELEASE-NOTES-$version.md"}
if(!$Assets){$Assets=@(
 (Join-Path $releaseDir "New7z-$version-x64-Setup.exe"),
 (Join-Path $releaseDir 'SHA256SUMS.txt'),
 (Join-Path $releaseDir 'release-manifest.json'))}
$notes=Get-Content -LiteralPath $NotesFile -Raw
$api="https://gitee.com/api/v5/repos/$Repo"
$release=$null
try{$release=Invoke-RestMethod -Uri "$api/releases/tags/$Tag`?access_token=$Token" -Method Get}catch{$release=$null}
if(!$release){
 $release=Invoke-RestMethod -Uri "$api/releases?access_token=$Token" -Method Post -ContentType 'application/x-www-form-urlencoded' -Body @{
  tag_name=$Tag;name=$Name;body=$notes;target_commitish='main';prerelease='false'}
 Write-Host "Created release $Tag (id $($release.id))"
}else{Write-Host "Release $Tag already exists (id $($release.id))"}
$existing=@($release.assets | ForEach-Object {$_.name})
foreach($asset in $Assets){
 if(!(Test-Path -LiteralPath $asset)){throw "Missing asset: $asset"}
 $file=Get-Item -LiteralPath $asset
 if($existing -contains $file.Name){Write-Host "Skip existing $($file.Name)";continue}
 $result=Invoke-RestMethod -Uri "$api/releases/$($release.id)/attach_files?access_token=$Token" -Method Post -Form @{file=$file}
 Write-Host "Uploaded $($file.Name): $($result.browser_download_url)"
}
Write-Host "Gitee release: https://gitee.com/$Repo/releases/tag/$Tag"
