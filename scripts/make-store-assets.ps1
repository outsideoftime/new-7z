param([string]$Source,[string]$Destination)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if(!$Source){$Source=Join-Path $root '图标/logo.png'}
if(!$Destination){$Destination=Join-Path $root 'src/New7z.App/Assets/Store'}
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force -Path $Destination | Out-Null
$image=[System.Drawing.Image]::FromFile((Resolve-Path -LiteralPath $Source).Path)
function Save-StoreImage([int]$width,[int]$height,[string]$name,[switch]$Contain)
{
  $bitmap=New-Object System.Drawing.Bitmap($width,$height)
  $graphics=[System.Drawing.Graphics]::FromImage($bitmap)
  $graphics.InterpolationMode=[System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
  $graphics.PixelOffsetMode=[System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
  $graphics.CompositingQuality=[System.Drawing.Drawing2D.CompositingQuality]::HighQuality
  $graphics.Clear([System.Drawing.Color]::Transparent)
  if($Contain)
  {
    $side=[Math]::Min($width,$height)
    $graphics.DrawImage($image,[int](($width-$side)/2),[int](($height-$side)/2),$side,$side)
  }
  else { $graphics.DrawImage($image,0,0,$width,$height) }
  $graphics.Dispose()
  $bitmap.Save((Join-Path $Destination $name),[System.Drawing.Imaging.ImageFormat]::Png)
  $bitmap.Dispose()
}
try
{
  Save-StoreImage 50 50 'StoreLogo.png'
  Save-StoreImage 44 44 'Square44x44Logo.png'
  Save-StoreImage 150 150 'Square150x150Logo.png'
  Save-StoreImage 310 150 'Wide310x150Logo.png' -Contain
}
finally { $image.Dispose() }
Write-Host "Store assets ready: $Destination"
