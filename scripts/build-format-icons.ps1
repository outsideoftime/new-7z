$ErrorActionPreference='Stop'
# Regenerates Assets/Formats icons from the format icon sheet. Requires Windows PowerShell 5.1;
# System.Drawing becomes a compile reference there, while PowerShell 7 needs extra assemblies.
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

public static class FormatIconExtractor
{
    public static int[][] FindIcons(string input)
    {
        using (var source = new Bitmap(input))
        {
            int w = source.Width, h = source.Height;
            var data = source.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var buffer = new byte[data.Stride * h];
            Marshal.Copy(data.Scan0, buffer, 0, buffer.Length);
            source.UnlockBits(data);
            double r, g, b;
            Background(buffer, data.Stride, w, h, out r, out g, out b);
            var mask = new bool[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int p = y * data.Stride + x * 4;
                    double dr = buffer[p + 2] - r, dg = buffer[p + 1] - g, db = buffer[p] - b;
                    mask[y * w + x] = Math.Sqrt(dr * dr + dg * dg + db * db) > 78;
                }
            var seen = new bool[w * h];
            var stack = new Stack<int>();
            var boxes = new List<int[]>();
            for (int i = 0; i < mask.Length; i++)
            {
                if (!mask[i] || seen[i]) continue;
                int minX = w, minY = h, maxX = -1, maxY = -1, area = 0;
                stack.Push(i); seen[i] = true;
                while (stack.Count > 0)
                {
                    int p = stack.Pop();
                    int x = p % w, y = p / w;
                    area++;
                    if (x < minX) minX = x; if (x > maxX) maxX = x;
                    if (y < minY) minY = y; if (y > maxY) maxY = y;
                    if (x > 0 && mask[p - 1] && !seen[p - 1]) { seen[p - 1] = true; stack.Push(p - 1); }
                    if (x < w - 1 && mask[p + 1] && !seen[p + 1]) { seen[p + 1] = true; stack.Push(p + 1); }
                    if (y > 0 && mask[p - w] && !seen[p - w]) { seen[p - w] = true; stack.Push(p - w); }
                    if (y < h - 1 && mask[p + w] && !seen[p + w]) { seen[p + w] = true; stack.Push(p + w); }
                }
                if (area > 1500) boxes.Add(new int[] { minX, minY, maxX - minX + 1, maxY - minY + 1, area });
            }
            boxes.Sort((a, c) => c[4].CompareTo(a[4]));
            return boxes.ToArray();
        }
    }

    public static byte[] Extract(string input, int x, int y, int w, int h, double padding, int size)
    {
        using (var source = new Bitmap(input))
        {
            double r, g, b;
            SampleCorners(source, out r, out g, out b);
            int content = Math.Max(w, h);
            int side = content + (int)Math.Round(content * padding) * 2;
            int cx = x + w / 2, cy = y + h / 2;
            int x0 = cx - side / 2, y0 = cy - side / 2;
            using (var square = new Bitmap(side, side, PixelFormat.Format32bppArgb))
            {
                var data = square.LockBits(new Rectangle(0, 0, side, side), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                var pixels = new byte[data.Stride * side];
                for (int j = 0; j < side; j++)
                    for (int i = 0; i < side; i++)
                    {
                        int sx = x0 + i, sy = y0 + j;
                        int p = j * data.Stride + i * 4;
                        if (sx < 0 || sy < 0 || sx >= source.Width || sy >= source.Height) continue;
                        Color c = source.GetPixel(sx, sy);
                        double dr = c.R - r, dg = c.G - g, db = c.B - b;
                        double alpha = (Math.Sqrt(dr * dr + dg * dg + db * db) - 26) / (78 - 26);
                        if (alpha <= 0) continue;
                        if (alpha > 1) alpha = 1;
                        pixels[p] = (byte)Clamp((c.B - b * (1 - alpha)) / alpha);
                        pixels[p + 1] = (byte)Clamp((c.G - g * (1 - alpha)) / alpha);
                        pixels[p + 2] = (byte)Clamp((c.R - r * (1 - alpha)) / alpha);
                        pixels[p + 3] = (byte)Math.Round(alpha * 255);
                    }
                Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
                square.UnlockBits(data);
                using (var scaled = new Bitmap(size, size, PixelFormat.Format32bppArgb))
                using (var graphics = Graphics.FromImage(scaled))
                {
                    graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    graphics.DrawImage(square, 0, 0, size, size);
                    using (var stream = new MemoryStream())
                    {
                        scaled.Save(stream, ImageFormat.Png);
                        return stream.ToArray();
                    }
                }
            }
        }
    }

    private static double Clamp(double v) { return v < 0 ? 0 : (v > 255 ? 255 : v); }

    private static void Background(byte[] buffer, int stride, int w, int h, out double r, out double g, out double b)
    {
        r = 0; g = 0; b = 0; int samples = 0;
        int[] xs = { 0, w - 8 }; int[] ys = { 0, h - 8 };
        for (int yi = 0; yi < 2; yi++)
            for (int xi = 0; xi < 2; xi++)
                for (int dy = 0; dy < 8; dy++)
                    for (int dx = 0; dx < 8; dx++)
                    {
                        int i = ys[yi] + dy, j = xs[xi] + dx;
                        if (i >= h) i = h - 1; if (j >= w) j = w - 1;
                        int p = i * stride + j * 4;
                        b += buffer[p]; g += buffer[p + 1]; r += buffer[p + 2]; samples++;
                    }
        r /= samples; g /= samples; b /= samples;
    }

    private static void SampleCorners(Bitmap bitmap, out double r, out double g, out double b)
    {
        r = 0; g = 0; b = 0; int samples = 0;
        int[] xs = { 0, bitmap.Width - 8 }; int[] ys = { 0, bitmap.Height - 8 };
        for (int yi = 0; yi < 2; yi++)
            for (int xi = 0; xi < 2; xi++)
                for (int dy = 0; dy < 8; dy++)
                    for (int dx = 0; dx < 8; dx++)
                    {
                        int i = ys[yi] + dy, j = xs[xi] + dx;
                        if (i >= bitmap.Height) i = bitmap.Height - 1;
                        if (j >= bitmap.Width) j = bitmap.Width - 1;
                        Color c = bitmap.GetPixel(j, i);
                        b += c.B; g += c.G; r += c.R; samples++;
                    }
        r /= samples; g /= samples; b /= samples;
    }
}
'@ -ReferencedAssemblies ([System.Drawing.Bitmap].Assembly.Location)

$projectRoot=Split-Path $PSScriptRoot -Parent
$collection=Join-Path $projectRoot '图标/流畅渐变压缩文件图标合集.png'
$folder=Join-Path $projectRoot 'src/New7z.App/Assets/Formats'
New-Item -ItemType Directory -Path $folder -Force | Out-Null

$boxes=[FormatIconExtractor]::FindIcons($collection)
if($boxes.Count -lt 10){throw "Format icon sheet has $($boxes.Count) tiles; expected at least 10."}
$largest=$boxes[0]
$grid=@($boxes[1..9] | Sort-Object { [int]($_[1]/200) }, { $_[0] })
$names=@('7z','rar','tar','gz','bz2','xz','iso','zst','xar')
$map=@{ zip=$largest }
for($i=0;$i -lt $names.Count;$i++){$map[$names[$i]]=$grid[$i]}
$supported=@('7z','zip','rar','tar','gz','bz2','xz','iso')

foreach($name in $supported){
 $box=$map[$name]
 if($null -eq $box){throw "Format icon $name was not found in the sheet."}
 $png=[FormatIconExtractor]::Extract($collection,$box[0],$box[1],$box[2],$box[3],0.06,256)
 $uiPng=[FormatIconExtractor]::Extract($collection,$box[0],$box[1],$box[2],$box[3],0.06,128)
 [IO.File]::WriteAllBytes((Join-Path $folder "$name.png"),$uiPng)
 $image=[Drawing.Image]::FromStream([IO.MemoryStream]::new($png))
 try {
  $frames=@(foreach($size in @(16,24,32,48,64,128,256)){
   $bitmap=[Drawing.Bitmap]::new($size,$size)
   $graphics=[Drawing.Graphics]::FromImage($bitmap)
   $buffer=[IO.MemoryStream]::new()
   try {
    $graphics.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode=[Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.DrawImage($image,0,0,$size,$size)
    $bitmap.Save($buffer,[Drawing.Imaging.ImageFormat]::Png)
    [pscustomobject]@{Size=$size;Bytes=$buffer.ToArray()}
   } finally {$buffer.Dispose();$graphics.Dispose();$bitmap.Dispose()}
  })
  $path=Join-Path $folder "$name.ico"
  $writer=[IO.BinaryWriter]::new([IO.File]::Create($path))
  try {
   $writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]$frames.Count)
   $offset=6+16*$frames.Count
   foreach($entry in $frames){
    $dimension=if($entry.Size -eq 256){0}else{$entry.Size}
    $writer.Write([byte]$dimension);$writer.Write([byte]$dimension);$writer.Write([uint16]0)
    $writer.Write([uint16]1);$writer.Write([uint16]32)
    $writer.Write([uint32]$entry.Bytes.Length);$writer.Write([uint32]$offset)
    $offset+=$entry.Bytes.Length
   }
   foreach($entry in $frames){$writer.Write([byte[]]$entry.Bytes)}
  } finally {$writer.Dispose()}
 } finally {$image.Dispose()}
 Write-Host "Generated format icon $name.ico"
}
