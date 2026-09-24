# requires: powershell -NoProfile -ExecutionPolicy Bypass -File tools/probes/check-ui-keys.ps1
# OWNER / GATE STATUS (AF3, 2026-09-22): standalone offline probe, owner = whoever touches
#   ResPaths.cs UI keys / lands a Sprites/Ui png / edits tools/probes/copy-ui-assets.py.
#   tools/verify.ps1 does NOT call it (grep: 0 hits) => it is NOT part of the main gate.
# $PSScriptRoot resolves $root correctly wherever this probe is invoked from.


# This file is intentionally ASCII-only (Windows PowerShell 5.1 reads a BOM-less .ps1 as
# ANSI/GBK, which corrupts CJK literals and breaks parsing). Every path below is ASCII.
#
# WHAT IT CHECKS (offline, seconds):
#   A) every UI key registered in client/Assets/Scripts/Core/ResPaths.cs resolves to a file
#      that actually exists on disk  (the key -> file direction)
#   B) every landed PNG under Assets/Resources/Sprites/Ui (except loading_bg.png) is reached
#      by some reference -- a ResPaths key OR a path built in a .cs file  (the file -> key
#      direction: catches "copied but never referenced")
#   C) the landed set is exactly the set named by tools/probes/copy-ui-assets.py
#      (catches "the copy script and the registry drifted apart")
#   D) every Sprites/Ui path referenced from ANY .cs under Assets/Scripts resolves to a file on
#      disk  (the reference -> file direction). A/B/C only read ResPaths.cs, so a path built
#      OUTSIDE it was invisible: CrUiStyle.cs builds its logo / bar-fill as
#      ResPaths.UiFrame(ResPaths.UiBarsDir, SrcLoading, 15) -- the probe called those frames
#      "unreferenced" and a later片 deleted two of them (loading_out 015 / 028) while CrUiStyle
#      still pointed at them (regression D75). D resolves every UiFrame(...) call and every
#      "Sprites/Ui/..." string literal found in any .cs file, and requires the file to exist.
#
# Exit 0 = all four hold. Exit 1 = at least one does not.
$ErrorActionPreference = 'Stop'

$root    = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$resPath = Join-Path $root 'client\Assets\Scripts\Core\ResPaths.cs'
$uiRoot  = Join-Path $root 'client\Assets\Resources\Sprites\Ui'
$copyPy  = Join-Path $root 'tools\probes\copy-ui-assets.py'

if (-not (Test-Path $resPath)) { Write-Output "FAIL ResPaths.cs not found: $resPath"; exit 1 }
if (-not (Test-Path $uiRoot))  { Write-Output "FAIL UI root not found: $uiRoot";  exit 1 }

$src = [System.IO.File]::ReadAllText($resPath, [System.Text.Encoding]::UTF8)

# ---- resolve the purpose-dir and source-dir constants -----------------------
$dirMap = @{}
foreach ($m in [regex]::Matches($src, 'public const string (Ui\w+Dir) = "([^"]+)";')) {
  $dirMap[$m.Groups[1].Value] = $m.Groups[2].Value
}
$srcMap = @{}
foreach ($m in [regex]::Matches($src, 'public const string (UiSrc\w+) = "([^"]+)";')) {
  $srcMap[$m.Groups[1].Value] = $m.Groups[2].Value
}
Write-Output ("constants: purpose dirs = " + (($dirMap.Keys | Sort-Object) -join ',') + " ; source dirs = " + (($srcMap.Keys | Sort-Object) -join ','))

# ---- every property that goes through UiFrame(...) --------------------------
$rows = @()
foreach ($m in [regex]::Matches($src, 'public static string (\w+) \{ get \{ return UiFrame\((\w+), (\w+), (\d+)\); \} \}')) {
  $key = $m.Groups[1].Value
  $d   = $dirMap[$m.Groups[2].Value]
  $s   = $srcMap[$m.Groups[3].Value]
  $f   = [int]$m.Groups[4].Value
  if (-not $d) { Write-Output ("FAIL cannot resolve purpose constant " + $m.Groups[2].Value + " for " + $key); exit 1 }
  if (-not $s) { Write-Output ("FAIL cannot resolve source constant " + $m.Groups[3].Value + " for " + $key); exit 1 }
  $rel = "Sprites/Ui/$d/$s/frame_{0:D3}" -f $f
  $abs = Join-Path $uiRoot (($d + '/' + $s + '/frame_{0:D3}.png' -f $f) -replace '/', '\')
  $rows += [pscustomobject]@{ Key = $key; Rel = $rel; Abs = $abs; Exists = (Test-Path $abs) }
}

if ($rows.Count -eq 0) { Write-Output 'FAIL no UiFrame(...) key found in ResPaths.cs'; exit 1 }

Write-Output ''
Write-Output 'key	Sprites/Ui path	file exists'
foreach ($r in $rows) { Write-Output ("{0}`t{1}`t{2}" -f $r.Key, $r.Rel, $r.Exists) }

$missing = @($rows | Where-Object { -not $_.Exists })
Write-Output ''
Write-Output ("A) keys = {0} ; files present = {1} ; files MISSING = {2}" -f $rows.Count, ($rows.Count - $missing.Count), $missing.Count)

# ---- D-compute: resource paths built inside .cs files (also feeds B) --------
# Every `const string NAME = "value";` found in any .cs, so a source-atlas name that is only a
# local const (e.g. CrUiStyle.cs `private const string SrcLoading = "loading_out";`) resolves.
$scriptsRoot = Join-Path $root 'client\Assets\Scripts'
$csFiles = @(Get-ChildItem $scriptsRoot -Recurse -File -Filter *.cs)
$constMap = @{}
foreach ($f in $csFiles) {
  $ct = [System.IO.File]::ReadAllText($f.FullName, [System.Text.Encoding]::UTF8)
  foreach ($m in [regex]::Matches($ct, 'const string (\w+)\s*=\s*"([^"]+)"')) {
    $constMap[$m.Groups[1].Value] = $m.Groups[2].Value
  }
}
$csRefs = New-Object System.Collections.Generic.HashSet[string]
foreach ($f in $csFiles) {
  $ct = [System.IO.File]::ReadAllText($f.FullName, [System.Text.Encoding]::UTF8)
  # (a) any `<something>.UiFrame(<dir>, <src>, N)` call (the ResPaths.cs definition has no dot)
  foreach ($m in [regex]::Matches($ct, '\.UiFrame\(\s*([\w.]+)\s*,\s*([\w.]+)\s*,\s*(\d+)\s*\)')) {
    $dTok = $m.Groups[1].Value.Split('.')[-1]
    $sTok = $m.Groups[2].Value.Split('.')[-1]
    $fr   = [int]$m.Groups[3].Value
    $d = $dirMap[$dTok]; if (-not $d) { $d = $constMap[$dTok] }
    $s = $srcMap[$sTok]; if (-not $s) { $s = $constMap[$sTok] }
    if ($d -and $s) { [void]$csRefs.Add(('Sprites/Ui/{0}/{1}/frame_{2:D3}' -f $d, $s, $fr)) }
  }
  # (b) literal "Sprites/Ui/<purpose>/<src>/<frame>" strings
  foreach ($m in [regex]::Matches($ct, '"(Sprites/Ui/[A-Za-z0-9_/\-]+)"')) {
    $lit = $m.Groups[1].Value -replace '\.png$', ''
    if ($lit -match '^Sprites/Ui/[^/]+/[^/]+/[^/]+$') { [void]$csRefs.Add($lit) }
  }
}
$csRefList = @($csRefs | Sort-Object)
$csMissing = @($csRefList | Where-Object {
  -not (Test-Path (Join-Path $uiRoot (($_.Substring('Sprites/Ui/'.Length) + '.png') -replace '/', '\')))
})

# ---- B) file -> key ---------------------------------------------------------
$onDisk = @(Get-ChildItem $uiRoot -Recurse -File -Filter *.png |
            Where-Object { $_.Name -ne 'loading_bg.png' } |
            ForEach-Object { ($_.FullName.Substring($uiRoot.Length).TrimStart('\') -replace '\\', '/') } |
            Where-Object { $_ -match '^(Panels|Buttons|Bars|Slots|Icons)/' })
$keyed = @($rows | ForEach-Object { $_.Rel.Substring('Sprites/Ui/'.Length) + '.png' })
# a path referenced from a .cs file counts as a reference too (D); this is NOT a relaxation of
# B -- the original B simply had no way to see such a reference, which is what let AB3 delete a
# still-referenced frame (D75). The FAIL conditions below are unchanged.
$keyed = @(@($keyed) + @($csRefList | ForEach-Object { $_.Substring('Sprites/Ui/'.Length) + '.png' }) | Sort-Object -Unique)
$orphan = @($onDisk | Where-Object { $keyed -notcontains $_ })
$unbacked = @($keyed | Where-Object { $onDisk -notcontains $_ })
Write-Output ("B) landed png reachable by a key = {0} ; landed but unreferenced = {1}" -f ($onDisk.Count - $orphan.Count), $orphan.Count)
if ($orphan.Count)    { Write-Output ('   unreferenced: ' + ($orphan -join ', ')) }
if ($unbacked.Count)  { Write-Output ('   key without file on disk: ' + ($unbacked -join ', ')) }

# ---- C) registry vs copy pipeline ------------------------------------------
$py = [System.IO.File]::ReadAllText($copyPy, [System.Text.Encoding]::UTF8)
$pyRows = @()
foreach ($m in [regex]::Matches($py, '\("(ui_\w+)",\s*(\d+),\s*"(\w+)"\)')) {
  $pyRows += [pscustomobject]@{ Src = $m.Groups[1].Value; Frame = [int]$m.Groups[2].Value; Key = $m.Groups[3].Value }
}
$pyKeys  = @($pyRows | ForEach-Object { $_.Key } | Sort-Object)
$csKeys  = @($rows   | ForEach-Object { $_.Key } | Sort-Object)
$keyDiff = @(Compare-Object $pyKeys $csKeys)
Write-Output ("C) copy-script entries = {0} ; ResPaths keys = {1} ; difference = {2}" -f $pyRows.Count, $rows.Count, $keyDiff.Count)
if ($keyDiff.Count) { $keyDiff | ForEach-Object { Write-Output ('   ' + $_.SideIndicator + ' ' + $_.InputObject) } }

# ---- D) reference -> file (paths built inside .cs files) --------------------
Write-Output ("D) .cs-referenced Sprites/Ui paths = {0} ; missing on disk = {1}" -f $csRefList.Count, $csMissing.Count)
if ($csMissing.Count) { Write-Output ('   missing: ' + ($csMissing -join ', ')) }

$fail = 0
if ($missing.Count)  { $fail++; Write-Output 'FAIL A: some keys point at files that do not exist' }
if ($orphan.Count)   { $fail++; Write-Output 'FAIL B: some landed PNGs are not referenced by any key' }
if ($unbacked.Count) { $fail++; Write-Output 'FAIL B: some keys have no file on disk' }
if ($keyDiff.Count)  { $fail++; Write-Output 'FAIL C: copy pipeline and registry disagree' }
if ($csMissing.Count){ $fail++; Write-Output 'FAIL D: a .cs file references a Sprites/Ui path that does not exist' }
Write-Output ''
Write-Output ("===== SUMMARY: FAIL={0}  keys={1}  landed={2} =====" -f $fail, $rows.Count, $onDisk.Count)
exit $(if ($fail -gt 0) { 1 } else { 0 })
