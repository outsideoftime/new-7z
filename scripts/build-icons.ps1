$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
Add-Type -AssemblyName System.Drawing
$folder=Join-Path $projectRoot 'src/New7z.App/Assets'
New-Item -ItemType Directory -Path $folder -Force | Out-Null
$source=[Drawing.Image]::FromFile((Join-Path $projectRoot '图标/logo.png'))
try {
 $images=@(foreach($size in @(16,24,32,48,64,128,256)){
  $bitmap=[Drawing.Bitmap]::new($size,$size)
  $graphics=[Drawing.Graphics]::FromImage($bitmap)
  $buffer=[IO.MemoryStream]::new()
  try {
   $graphics.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
   $graphics.DrawImage($source,0,0,$size,$size)
   $bitmap.Save($buffer,[Drawing.Imaging.ImageFormat]::Png)
   [pscustomobject]@{Size=$size;Bytes=$buffer.ToArray()}
  } finally {$buffer.Dispose();$graphics.Dispose();$bitmap.Dispose()}
 })
 $writer=[IO.BinaryWriter]::new([IO.File]::Create((Join-Path $folder 'New7z.ico')))
 try {
  $writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]$images.Count)
  $offset=6+16*$images.Count
  foreach($entry in $images){
   $dimension=if($entry.Size -eq 256){0}else{$entry.Size}
   $writer.Write([byte]$dimension);$writer.Write([byte]$dimension);$writer.Write([uint16]0)
   $writer.Write([uint16]1);$writer.Write([uint16]32)
   $writer.Write([uint32]$entry.Bytes.Length);$writer.Write([uint32]$offset)
   $offset+=$entry.Bytes.Length
  }
  foreach($entry in $images){$writer.Write([byte[]]$entry.Bytes)}
 } finally {$writer.Dispose()}
} finally {$source.Dispose()}
Write-Host 'Generated Windows icon (16, 24, 32, 48, 64, 128, 256 px).'
