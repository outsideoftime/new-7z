param(
  [string]$Logo,
  [string]$OutDir
)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if(!$Logo){$Logo=Join-Path $root '图标/logo.png'}
if(!$OutDir){$OutDir=Join-Path $root 'artifacts/store-assets/logos'}
if(!(Test-Path -LiteralPath $Logo)){throw "Logo not found: $Logo"}
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$source=[System.Drawing.Image]::FromFile((Resolve-Path -LiteralPath $Logo).Path)

function New-BrandedCanvas([int]$width,[int]$height)
{
  $bitmap=New-Object System.Drawing.Bitmap($width,$height)
  $graphics=[System.Drawing.Graphics]::FromImage($bitmap)
  $graphics.SmoothingMode=[System.Drawing.Drawing2D.SmoothingMode]::HighQuality
  $graphics.InterpolationMode=[System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
  $graphics.PixelOffsetMode=[System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
  $graphics.CompositingQuality=[System.Drawing.Drawing2D.CompositingQuality]::HighQuality
  $graphics.TextRenderingHint=[System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
  $rect=New-Object System.Drawing.Rectangle(0,0,$width,$height)
  $brush=New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect,[System.Drawing.Color]::FromArgb(8,42,92),[System.Drawing.Color]::FromArgb(14,108,224),45)
  $graphics.FillRectangle($brush,$rect)
  $brush.Dispose()
  $halo=New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(22,120,200,255))
  $graphics.FillEllipse($halo,[int]($width*0.58),[int](-$height*0.10),[int]($width*0.72),[int]($width*0.72))
  $graphics.FillEllipse($halo,[int](-$width*0.20),[int]($height*0.72),[int]($width*0.64),[int]($width*0.64))
  $halo.Dispose()
  return @{bitmap=$bitmap;graphics=$graphics}
}

function Draw-Centered([System.Drawing.Graphics]$graphics,[System.Drawing.Image]$image,[double]$x,[double]$y,[double]$size)
{
  $graphics.DrawImage($image,[int]$x,[int]$y,[int]$size,[int]$size)
}

function Draw-Text([System.Drawing.Graphics]$graphics,[string]$text,[string]$family,[double]$size,[System.Drawing.Color]$color,[double]$y,[double]$height,[int]$width)
{
  $font=New-Object System.Drawing.Font($family,[single]$size,[System.Drawing.FontStyle]::Regular,[System.Drawing.GraphicsUnit]::Pixel)
  $brush=New-Object System.Drawing.SolidBrush($color)
  $format=New-Object System.Drawing.StringFormat
  $format.Alignment=[System.Drawing.StringAlignment]::Center
  $format.LineAlignment=[System.Drawing.StringAlignment]::Center
  $rect=New-Object System.Drawing.RectangleF([single]0,[single]$y,[single]$width,[single]$height)
  $graphics.DrawString($text,$font,$brush,$rect,$format)
  $format.Dispose(); $brush.Dispose(); $font.Dispose()
}

function Save-Poster([int]$width,[int]$height,[string]$lang,[string]$file)
{
  $canvas=New-BrandedCanvas $width $height
  $graphics=$canvas.graphics
  $logoSize=$width*0.46
  Draw-Centered $graphics $source (($width-$logoSize)/2) ($height*0.27) $logoSize
  Draw-Text $graphics 'New7z' 'Segoe UI Semibold' ($width*0.095) ([System.Drawing.Color]::White) ($height*0.62) ($height*0.11) $width
  if($lang -eq 'zh-CN')
  {
    Draw-Text $graphics '创建 7z 与 ZIP · 打开更多格式' 'Microsoft YaHei UI' ($width*0.034) ([System.Drawing.Color]::FromArgb(216,236,255)) ($height*0.74) ($height*0.06) $width
  }
  else
  {
    Draw-Text $graphics 'Create 7z and ZIP · Open more formats' 'Segoe UI' ($width*0.034) ([System.Drawing.Color]::FromArgb(216,236,255)) ($height*0.74) ($height*0.06) $width
  }
  $canvas.bitmap.Save($file,[System.Drawing.Imaging.ImageFormat]::Png)
  $graphics.Dispose(); $canvas.bitmap.Dispose()
  Write-Host "$file $($width)x$($height)"
}

function Save-BoxArt([int]$width,[int]$height,[string]$lang,[string]$file)
{
  $canvas=New-BrandedCanvas $width $height
  $graphics=$canvas.graphics
  $logoSize=$width*0.48
  Draw-Centered $graphics $source (($width-$logoSize)/2) ($height*0.18) $logoSize
  Draw-Text $graphics 'New7z' 'Segoe UI Semibold' ($width*0.10) ([System.Drawing.Color]::White) ($height*0.70) ($height*0.10) $width
  if($lang -eq 'zh-CN')
  {
    Draw-Text $graphics '创建 7z 与 ZIP · 打开更多格式' 'Microsoft YaHei UI' ($width*0.032) ([System.Drawing.Color]::FromArgb(216,236,255)) ($height*0.81) ($height*0.05) $width
  }
  else
  {
    Draw-Text $graphics 'Create 7z and ZIP · Open more formats' 'Segoe UI' ($width*0.032) ([System.Drawing.Color]::FromArgb(216,236,255)) ($height*0.81) ($height*0.05) $width
  }
  $canvas.bitmap.Save($file,[System.Drawing.Imaging.ImageFormat]::Png)
  $graphics.Dispose(); $canvas.bitmap.Dispose()
  Write-Host "$file $($width)x$($height)"
}

function Save-Tile([int]$size,[string]$file)
{
  $canvas=New-BrandedCanvas $size $size
  $graphics=$canvas.graphics
  $logoSize=$size*0.72
  Draw-Centered $graphics $source (($size-$logoSize)/2) (($size-$logoSize)/2) $logoSize
  $canvas.bitmap.Save($file,[System.Drawing.Imaging.ImageFormat]::Png)
  $graphics.Dispose(); $canvas.bitmap.Dispose()
  Write-Host "$file $($size)x$($size)"
}

try
{
  foreach($lang in @('zh-CN','en-US'))
  {
    Save-Poster 720 1080 $lang (Join-Path $OutDir "poster-9x16-720x1080-$lang.png")
    Save-Poster 1440 2160 $lang (Join-Path $OutDir "poster-9x16-1440x2160-$lang.png")
    Save-BoxArt 1080 1080 $lang (Join-Path $OutDir "boxart-1x1-1080x1080-$lang.png")
    Save-BoxArt 2160 2160 $lang (Join-Path $OutDir "boxart-1x1-2160x2160-$lang.png")
  }
  foreach($size in @(300,150,71))
  {
    Save-Tile $size (Join-Path $OutDir "tile-$($size)x$($size).png")
  }
}
finally
{
  $source.Dispose()
}
Write-Host "Store logos ready: $OutDir"
