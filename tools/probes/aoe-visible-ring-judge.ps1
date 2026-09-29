<#
  cr/tools/probes/aoe-visible-ring-judge.ps1 -- pixel-level judge for the SPLASH-RANGE RING.

  PURPOSE
    Judge the splash-range ring drawn by CR.View.PlacementIndicator (splash mode; original art
    Sprites/Effects/RangeRing frame f221, radius taken from CardInfo.aoe_radius_milli).
    It answers two things on a full-screen shot: "is the whole ring visible at a glance" and
    "is its on-screen radius the official one".

  METHOD (canonical)
    Per angle, walk a RADIAL PROFILE and take the luminance peak over
      r in [0.90, 1.15] x nominal half-axis
    (centre searched on a small grid with the same fractional profile). The script prints the
    "mean peak-radius factor" so the band position is reproducible.
    DO NOT replace this with sampling a FIXED ellipse: the bright band sits at ~1.008 x the
    nominal radius, so a 1-2 px miss lands inside or outside the band and the reading collapses
    (measured: fixed-ellipse sampling gave 43/72 bright angles for the SAME image that the
    radial profile scores 72/72). Fixed-ellipse and radial-profile numbers are NOT the same
    quantity name.

  PASS THRESHOLDS (all must hold; shot must be full screen, no crop and no zoom)
    * angles>=200L = 72/72            a full, evenly bright ring (a half ring reads about 42/72)
    * contrast band/bg >= 1.5 : 1

  ROUND READING (2026-09-29, evidence .ai-tmp/screenshots/aoe-visible/ring-12.png, 1080x1920)
    avgL 250.2 / minL 241.1 / maxL 255 ; background 153.2 ; contrast 1.63 : 1
    centre (png 206,946) ; half-axes (120,90) px = 2.00 tiles = official valkyrie 2000 milli-tile,
    difference 0.  mean peak-radius factor 1.008.
    (The same judge run by a second measurer landed on 1.61:1 and peak half-axes (121.3,91.0) px.)

  REPRODUCTION KIT
    driver : .ai-tmp/drivers/aoe-visible-probe.cs -- entries AoeVisibleProbe.Enter / Battle / Run.
             Run writes .ai-tmp/screenshots/aoe-visible/ring-N.png and .ai-tmp/test/aoe-visible-run.tsv.
             Its RING rows carry: sprite[rect], localScale, world pos, sortingOrder, echo scale/order,
             Screen w/h, screen centre (cX,cY) and half-axes (xR,yR). Feed those here as
             -Cx <cX>  -CyPng <ScreenHeight - cY>  -A <xR>  -B <yR>   (screen origin is bottom-left,
             png origin is top-left, hence the flip of y).
    shot   : .ai-tmp/screenshots/aoe-visible/ring-12.png  (captured with CR.dll 15:55:35)
    BEFORE re-running the driver: run `unity command recompile`, then check that
    client/Library/ScriptAssemblies/CR.dll is NEWER than the .cs sources -- otherwise the capture
    silently runs on the old assembly and reports a false pass.

  USAGE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools/probes/aoe-visible-ring-judge.ps1 `
        -Img <absolute path to a 1080x1920 png> -Cx <screen x> -CyPng <height - screen y> `
        -A <half-axis x px> -B <half-axis y px>
#>
param(
    [Parameter(Mandatory=$true)][string]$Img,
    [Parameter(Mandatory=$true)][double]$Cx,
    [Parameter(Mandatory=$true)][double]$CyPng,
    [Parameter(Mandatory=$true)][double]$A,
    [Parameter(Mandatory=$true)][double]$B,
    [int]$SearchX = 4,
    [int]$SearchY = 4,
    [int]$Step = 2
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$bmp = New-Object System.Drawing.Bitmap ($Img)
$W = $bmp.Width
$H = $bmp.Height
$rect = New-Object System.Drawing.Rectangle 0, 0, $W, $H
$data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$stride = $data.Stride
$buf = New-Object byte[] ($stride * $H)
[System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $buf, 0, $buf.Length)
$bmp.UnlockBits($data)

function LAt([int]$x, [int]$y) {
    if ($x -lt 0 -or $y -lt 0 -or $x -ge $W -or $y -ge $H) { return -1.0 }
    $o = $y * $stride + $x * 4
    return 0.299 * $buf[$o + 2] + 0.587 * $buf[$o + 1] + 0.114 * $buf[$o]
}

$NA = 72
$cos = New-Object double[] $NA
$sin = New-Object double[] $NA
for ($i = 0; $i -lt $NA; $i++) {
    $t = 2.0 * [Math]::PI * $i / $NA
    $cos[$i] = [Math]::Cos($t)
    $sin[$i] = [Math]::Sin($t)
}
# radial profile offsets (fraction of the half-axis): 0.90 .. 1.15 in 0.01 steps
$fracs = @(-0.10, -0.09, -0.08, -0.07, -0.06, -0.05, -0.04, -0.03, -0.02, -0.01, 0.0, 0.01, 0.02, 0.03, 0.04, 0.05, 0.06, 0.07, 0.08, 0.09, 0.10, 0.11, 0.12, 0.13, 0.14, 0.15)

$bestScore = -1.0; $bestX = 0; $bestY = 0
for ($dy = -$SearchY; $dy -le $SearchY; $dy += $Step) {
    for ($dx = -$SearchX; $dx -le $SearchX; $dx += $Step) {
        $cx = $Cx + $dx; $cy = $CyPng + $dy
        $sum = 0.0
        for ($i = 0; $i -lt $NA; $i++) {
            $c = $cos[$i]; $s = $sin[$i]
            $peak = -1.0
            foreach ($fr in $fracs) {
                $x = [int][Math]::Round($cx + $A * (1.0 + $fr) * $c)
                $y = [int][Math]::Round($cy - $B * (1.0 + $fr) * $s)
                $l = LAt $x $y
                if ($l -gt $peak) { $peak = $l }
            }
            $sum += $peak
        }
        if (($sum / $NA) -gt $bestScore) { $bestScore = $sum / $NA; $bestX = $cx; $bestY = $cy }
    }
}

$band = @(); $bgnd = @(); $peakFr = @(); $white = 0
for ($i = 0; $i -lt $NA; $i++) {
    $c = $cos[$i]; $s = $sin[$i]
    $peak = -1.0; $pk = 1.0
    foreach ($fr in $fracs) {
        $x = [int][Math]::Round($bestX + $A * (1.0 + $fr) * $c)
        $y = [int][Math]::Round($bestY - $B * (1.0 + $fr) * $s)
        $l = LAt $x $y
        if ($l -gt $peak) { $peak = $l; $pk = 1.0 + $fr }
    }
    $band += $peak
    $peakFr += $pk
    if ($peak -ge 200.0) { $white++ }
    foreach ($sc in @(0.60, 0.72, 1.30, 1.45)) {
        $x = [int][Math]::Round($bestX + $A * $sc * $c)
        $y = [int][Math]::Round($bestY - $B * $sc * $s)
        $l = LAt $x $y
        if ($l -ge 0) { $bgnd += $l }
    }
}
$bandAvg = ($band | Measure-Object -Average).Average
$bandMax = ($band | Measure-Object -Maximum).Maximum
$bandMin = ($band | Measure-Object -Minimum).Minimum
$bgAvg = ($bgnd | Measure-Object -Average).Average
$frAvg = ($peakFr | Measure-Object -Average).Average
Write-Output ("img=$Img  size=${W}x${H}")
Write-Output ("nominal centre(png)=(" + $Cx + "," + $CyPng + ")  half=(" + $A + "," + $B + ")  method=radial profile 0.90..1.15 x half")
Write-Output ("BEST centre(png)=($bestX,$bestY)")
Write-Output ("ring band (peak of the radial profile, 72 angles): avgL=" + [Math]::Round($bandAvg, 1) + " minL=" + [Math]::Round($bandMin, 1) + " maxL=" + [Math]::Round($bandMax, 1) + "  angles>=200L=" + $white + "/" + $NA)
Write-Output ("mean peak-radius factor=" + [Math]::Round($frAvg, 3) + "  => mean peak half-axis px=(" + [Math]::Round($A * $frAvg, 1) + "," + [Math]::Round($B * $frAvg, 1) + ")")
Write-Output ("background(0.60/0.72/1.30/1.45 x half): avgL=" + [Math]::Round($bgAvg, 1))
Write-Output ("contrast band/bg = " + [Math]::Round($bandAvg / $bgAvg, 2) + " : 1")
$sx0 = $bestX - $A; $sx1 = $bestX + $A; $sy0 = $bestY - $B; $sy1 = $bestY + $B
Write-Output ("ring bbox(png, origin top-left): x " + [Math]::Round($sx0, 0) + ".." + [Math]::Round($sx1, 0) + "   y " + [Math]::Round($sy0, 0) + ".." + [Math]::Round($sy1, 0))
Write-Output ("ring bbox(unity screen, origin bottom-left): y " + [Math]::Round($H - $sy1, 0) + ".." + [Math]::Round($H - $sy0, 0))
$pass = ($white -eq $NA) -and (($bandAvg / $bgAvg) -ge 1.5)
Write-Output ("VERDICT: " + $(if ($pass) { "PASS" } else { "FAIL" }) + "  (need angles>=200L=72/72 and contrast>=1.5:1)")
