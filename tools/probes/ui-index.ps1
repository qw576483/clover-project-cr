<#
.SYNOPSIS
    ui-index.ps1 - Build labelled contact sheets for the original (A) UI sprite
    atlases, so every anonymous numbered frame can be identified by eye.

.DESCRIPTION
    Task T4 "original UI asset index".
    Source (read only) : <ProjRoot>/原版资源/cr-assets-png/assets/sc/<dir>/*.png
    Output (throw-away): <ProjRoot>/.ai-tmp/screenshots/<dir>_pNN_fXXXX-YYYY.png
                         <ProjRoot>/.ai-tmp/screenshots/ui-index-manifest.tsv

    Almost every frame in these atlases is a SMALL element sitting on a LARGE
    fully transparent canvas (ui_out = 1663x2810).  A plain thumbnail of the raw
    canvas would render the element invisible, so each cell draws the frame's
    non-transparent bounding box, scaled up to fill the cell, and prints below
    it: the frame number, the ORIGINAL canvas size and the bbox size.
    The bbox scan is done in compiled C# (Add-Type) because a PowerShell
    per-pixel loop over 4.6M pixels x 914 frames would not finish.

    This file is a JUDGEMENT ASSET (delete it and the same frames could not be
    re-identified) => it lives in tools/probes/ and is committed.
    The PNGs it emits are throw-away evidence => they go to .ai-tmp/screenshots/
    and must never enter Assets/.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools/probes/ui-index.ps1

.EXAMPLE
    # one directory, zoomed in (5 columns x 5 rows = 25 cells per page)
    powershell -NoProfile -ExecutionPolicy Bypass -File tools/probes/ui-index.ps1 `
        -Dirs ui_out -Cols 5 -Rows 5 -Start 400 -Count 50
#>
param(
    [string[]] $Dirs = @('ui_out', 'ui_arena_out', 'ui_battle_end_out', 'loading_out', 'ui_chest_out', 'tutorial_out', 'ui_spells_out'),
    [string]   $ProjRoot = '',
    [string]   $SrcRoot = '',
    [string]   $OutDir = '',
    [int]      $Cols = 10,
    [int]      $Rows = 10,
    [int]      $Start = -1,
    [int]      $Count = -1,
    [switch]   $OnlyList
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

if (-not $ProjRoot) {
    # this script lives at <ProjRoot>/tools/probes/ui-index.ps1
    $ProjRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
}
if (-not $SrcRoot) {
    $SrcRoot = Join-Path $ProjRoot '原版资源\cr-assets-png\assets\sc'
}
if (-not $OutDir) {
    $OutDir = Join-Path $ProjRoot '.ai-tmp\screenshots'
}
if (-not (Test-Path $SrcRoot)) { throw "source atlas directory not found: $SrcRoot" }
if (-not (Test-Path $OutDir))  { New-Item -ItemType Directory -Force -Path $OutDir | Out-Null }

# ---------------------------------------------------------------- bbox scanner
$scanCode = @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class UiScan
{
    // Returns {minX, minY, width, height} of pixels with alpha > 8 (all zeros if fully transparent).
    public static int[] AlphaBounds(Bitmap src)
    {
        int w = src.Width, h = src.Height;
        using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(bmp))
            {
                g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                g.DrawImage(src, new Rectangle(0, 0, w, h));
            }
            var rect = new Rectangle(0, 0, w, h);
            var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                byte[] buf = new byte[data.Stride * h];
                Marshal.Copy(data.Scan0, buf, 0, buf.Length);
                int minX = w, minY = h, maxX = -1, maxY = -1;
                for (int y = 0; y < h; y++)
                {
                    int row = y * data.Stride;
                    for (int x = 0; x < w; x++)
                    {
                        if (buf[row + x * 4 + 3] > 8)
                        {
                            if (x < minX) minX = x;
                            if (x > maxX) maxX = x;
                            if (y < minY) minY = y;
                            if (y > maxY) maxY = y;
                        }
                    }
                }
                if (maxX < 0) return new int[] { 0, 0, 0, 0 };
                return new int[] { minX, minY, maxX - minX + 1, maxY - minY + 1 };
            }
            finally { bmp.UnlockBits(data); }
        }
    }
}
'@
if (-not ('UiScan' -as [type])) {
    Add-Type -TypeDefinition $scanCode -ReferencedAssemblies 'System.Drawing'
}

# ---------------------------------------------------------------- cell metrics
$cellW   = 240
$cellH   = 280
$pad     = 6
$imgW    = $cellW - 2 * $pad          # 228
$imgH    = 200
$labelH  = $cellH - $imgH - 2 * $pad  # 68
$margin  = 18
$gap     = 6
$headerH = 78

$perPage = $Cols * $Rows
$pageW   = 2 * $margin + $Cols * $cellW + ($Cols - 1) * $gap
$pageH   = $headerH + 2 * $margin + $Rows * $cellH + ($Rows - 1) * $gap

$fontNum   = New-Object System.Drawing.Font('Consolas', 14, [System.Drawing.FontStyle]::Bold)
$fontStamp = New-Object System.Drawing.Font('Consolas', 24, [System.Drawing.FontStyle]::Bold)
$fontMeta  = New-Object System.Drawing.Font('Consolas', 11, [System.Drawing.FontStyle]::Regular)
$fontHead  = New-Object System.Drawing.Font('Consolas', 20, [System.Drawing.FontStyle]::Bold)
$fontHead2 = New-Object System.Drawing.Font('Consolas', 13, [System.Drawing.FontStyle]::Regular)
$brushNum  = [System.Drawing.Brushes]::White
$brushMeta = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 172, 180, 196))
$brushHead = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 235, 240, 250))
$brushBg   = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 58, 60, 68))
$brushImg  = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 96, 98, 108))
$brushStamp   = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 255, 226, 96))
$brushStampBg = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(210, 20, 22, 28))
$penGrid   = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 28, 30, 36)), 1

$manifestHeader = "dir`tframe`tindex`tcanvas_w`tcanvas_h`tbbox_x`tbbox_y`tbbox_w`tbbox_h`tno_alpha"
# merge with an existing manifest: rows of the directories processed THIS run are replaced,
# rows of other directories are kept => the manifest stays complete however the run is split.
$kept = New-Object System.Collections.Generic.List[string]
$manifestPath = Join-Path $OutDir 'ui-index-manifest.tsv'
if (Test-Path $manifestPath) {
    foreach ($ln in [System.IO.File]::ReadAllLines($manifestPath)) {
        if (-not $ln -or $ln -eq $manifestHeader) { continue }
        $owner = ($ln -split "`t")[0]
        if ($Dirs -notcontains $owner) { $kept.Add($ln) }
    }
}
$manifest = New-Object System.Collections.Generic.List[string]
$manifest.Add($manifestHeader)          # header always on line 1
foreach ($ln in $kept) { $manifest.Add($ln) }

$grandTotal = 0
$sheetPaths = New-Object System.Collections.Generic.List[string]

foreach ($dir in $Dirs) {
    $dirPath = Join-Path $SrcRoot $dir
    if (-not (Test-Path $dirPath)) { Write-Warning "missing dir: $dirPath"; continue }

    # numeric frame ordering (files are *_sprite_<n>.png, zero padding is not uniform)
    $frames = @()
    foreach ($f in (Get-ChildItem -Path $dirPath -Filter '*.png' -File)) {
        if ($f.Name -match '_sprite_(\d+)\.png$') {
            $frames += [pscustomobject]@{ Index = [int]$Matches[1]; Path = $f.FullName; Name = $f.Name }
        } else {
            Write-Warning "unexpected file name (skipped): $($f.Name)"
        }
    }
    $frames = $frames | Sort-Object Index
    if ($Start -ge 0) { $frames = $frames | Where-Object { $_.Index -ge $Start } }
    if ($Count -ge 0) { $frames = @($frames | Select-Object -First $Count) }

    Write-Host ("[{0}] {1} frames" -f $dir, $frames.Count)
    $grandTotal += $frames.Count
    if ($OnlyList) { continue }

    $nPages = [int][Math]::Ceiling($frames.Count / [double]$perPage)
    for ($p = 0; $p -lt $nPages; $p++) {
        $slice = @($frames | Select-Object -Skip ($p * $perPage) -First $perPage)
        $f0 = $slice[0].Index
        $f1 = $slice[-1].Index

        $page = New-Object System.Drawing.Bitmap($pageW, $pageH)
        $g = [System.Drawing.Graphics]::FromImage($page)
        $g.Clear([System.Drawing.Color]::FromArgb(255, 22, 24, 29))
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit

        $g.DrawString(("{0}   page {1}/{2}   frames {3:D3}-{4:D3}   cells {5}   ({6} cols x {7} rows)" -f `
            $dir, ($p + 1), $nPages, $f0, $f1, $slice.Count, $Cols, $Rows), $fontHead, $brushHead, $margin, 12)
        $g.DrawString("thumbnail = non-transparent bbox, scaled to fit 200x228   |   1663x2810 = ORIGINAL canvas px   |   b: w x h = bbox px", `
            $fontHead2, $brushMeta, $margin, 44)

        for ($i = 0; $i -lt $slice.Count; $i++) {
            $col = $i % $Cols
            $row = [int][Math]::Floor($i / $Cols)
            $cx = $margin + $col * ($cellW + $gap)
            $cy = $headerH + $margin + $row * ($cellH + $gap)

            $g.FillRectangle($brushBg, $cx, $cy, $cellW, $cellH)
            $g.DrawRectangle($penGrid, $cx, $cy, $cellW - 1, $cellH - 1)

            $fr = $slice[$i]
            $bmp = $null
            try {
                $bmp = [System.Drawing.Bitmap]::FromFile($fr.Path)
                $cw = $bmp.Width
                $ch = $bmp.Height
                $bb = [UiScan]::AlphaBounds($bmp)
                $hasAlpha = ($bb[2] -gt 0 -and $bb[3] -gt 0)

                $g.FillRectangle($brushImg, $cx + $pad, $cy + $pad, $imgW, $imgH)
                if ($hasAlpha) {
                    $bw = $bb[2]; $bh = $bb[3]
                    $scale = [Math]::Min($imgW / [double]$bw, $imgH / [double]$bh)
                    $dw = [int][Math]::Max(1, [Math]::Round($bw * $scale))
                    $dh = [int][Math]::Max(1, [Math]::Round($bh * $scale))
                    $dx = $cx + $pad + [int](($imgW - $dw) / 2)
                    $dy = $cy + $pad + [int](($imgH - $dh) / 2)
                    $dest = New-Object System.Drawing.Rectangle($dx, $dy, $dw, $dh)
                    $g.DrawImage($bmp, $dest, [float]$bb[0], [float]$bb[1], [float]$bw, [float]$bh, [System.Drawing.GraphicsUnit]::Pixel)
                    $dest = $null
                } else {
                    $g.DrawString('(fully transparent)', $fontMeta, $brushNum, $cx + $pad + 34, $cy + $pad + 90)
                }

                # frame number ALSO stamped inside the image area: the label below the
                # cell is easy to lose track of when scanning 100 cells, which silently
                # shifts every description by one frame.
                $stamp = ("#{0:D3}" -f $fr.Index)
                $sg = $g.MeasureString($stamp, $fontStamp)
                $g.FillRectangle($brushStampBg, $cx + $pad + 2, $cy + $pad + 2, $sg.Width + 6, $sg.Height)
                $g.DrawString($stamp, $fontStamp, $brushStamp, $cx + $pad + 5, $cy + $pad + 2)

                $ly = $cy + $pad + $imgH + 2
                $g.DrawString(("#{0:D3}" -f $fr.Index), $fontNum, $brushNum, $cx + $pad, $ly)
                $g.DrawString(("{0}x{1}" -f $cw, $ch), $fontMeta, $brushMeta, $cx + $pad + 62, $ly + 3)
                $g.DrawString(("bbox {0}x{1}" -f $bb[2], $bb[3]), $fontMeta, $brushMeta, $cx + $pad, $ly + 22)

                $manifest.Add(("{0}`t{1:D3}`t{2}`t{3}`t{4}`t{5}`t{6}`t{7}`t{8}`t{9}" -f `
                    $dir, $fr.Index, $fr.Index, $cw, $ch, $bb[0], $bb[1], $bb[2], $bb[3], $(if ($hasAlpha) { 0 } else { 1 })))
            } finally {
                if ($bmp) { $bmp.Dispose() }
            }
        }

        $g.Dispose()
        $outPng = Join-Path $OutDir ("{0}_p{1:D2}_f{2:D3}-{3:D3}.png" -f $dir, ($p + 1), $f0, $f1)
        $page.Save($outPng, [System.Drawing.Imaging.ImageFormat]::Png)
        $page.Dispose()
        $sheetPaths.Add($outPng)
        Write-Host ("   -> {0}" -f $outPng)
    }
}

if (-not $OnlyList) {
    [System.IO.File]::WriteAllLines($manifestPath, $manifest, (New-Object System.Text.UTF8Encoding $false))
    Write-Host ""
    Write-Host ("manifest : {0}  ({1} rows)" -f $manifestPath, ($manifest.Count - 1))
    Write-Host ("sheets   : {0}" -f $sheetPaths.Count)
    foreach ($s in $sheetPaths) { Write-Host ("   " + $s) }
}
Write-Host ("frames scanned: {0}" -f $grandTotal)
