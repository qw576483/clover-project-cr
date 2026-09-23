# CR-T3 re-collection chain (one Play session, ~2.5 min): re-take BOTH evidence sets after a concurrent
# edit moved the file I verified (HudPanel.cs 22:59:30 -> 23:59:39, CR-T2's card-art crop fix), because
# "evidence must be newer than the verified object".
#   part A (numeric) : tools/probes/cr-t3-drag-chain.ps1 rows, i.e. DIAG hit test + press/drag/release ->
#                      C2S -> server accepted (liveUnits + hand) and the illegal release rejection text
#   part B (visual)  : the frame BEFORE EndDrag with the ghost at the pointer and the disc green/red,
#                      plus the contact sheet + the pixel assertions (tools/probes/cr-t3-contact.py)
$ErrorActionPreference = 'Continue'
$root = 'C:\Work\Server\f-v2\clover-project-cr'
$proj = "$root\client"
$dev = $PSScriptRoot
$tmp = "$root\.ai-tmp\test"
$probe = "$dev\CR-T3-probe.cs"
$state = "$dev\CR-T3-state.cs"
$inject = "$dev\CR-T3-inject.cs"
$scene = "$dev\CR-T3-scene.cs"
$step = "$tmp\T3-step.txt"
$diag = "$tmp\T3-diag.txt"
$label = "$tmp\T3-label.txt"
$shot = "$tmp\T3-shot.txt"
$injf = "$tmp\T3-inject.txt"
$log = "$tmp\CR-T3-recollect.out.txt"

function Log([string]$m) { Add-Content -Path $log -Value $m -Encoding UTF8 }
function RunUnity([string]$ua, [int]$t) {
    $cmd = "unity $ua --project-path '$proj'"
    $job = Start-Job -ScriptBlock { param($c) Invoke-Expression $c 2>&1 | Out-String } -ArgumentList $cmd
    if (Wait-Job $job -Timeout $t) { $out = Receive-Job $job } else { Stop-Job $job; $out = "TIMEOUT-$t" }
    Remove-Job $job -Force
    return $out
}
function EvalFile([string]$f) { return (RunUnity "command eval_file --file `"$f`"" 180) }
function Sample([string]$name, [string]$shotName) {
    Set-Content -Path $label -Value $name -Encoding ascii
    Set-Content -Path $shot -Value $shotName -Encoding ascii
    return (EvalFile $state)
}
function Inject([string]$a) { Set-Content -Path $injf -Value $a -Encoding ascii; return (EvalFile $inject) }
function Grab([string]$s, [string]$pattern) {
    $m = [regex]::Match($s, $pattern)
    if ($m.Success) { return ($m.Groups[1].Value + "," + $m.Groups[2].Value) }
    return ""
}
# battle entry is flaky (~1 in 3 leaves a play session whose engine statics are gone) -> retry <=3
function EnterBattle() {
    Log ("editor_stop: " + (RunUnity 'command editor_stop' 180))
    Start-Sleep -Seconds 4
    Log ("open Boot: " + (EvalFile $scene))
    Start-Sleep -Seconds 3
    Set-Content -Path $step -Value '0' -Encoding ascii
    Log ("editor_play: " + (RunUnity 'command editor_play' 300))
    Start-Sleep -Seconds 40
    foreach ($s in 0,1,2,3) { Log "probe step $s`n$(EvalFile $probe)"; Start-Sleep -Seconds 3 }
    for ($i = 0; $i -lt 20; $i++) {
        $null = EvalFile $probe
        if ((Test-Path $diag) -and ((Get-Content $diag -Raw) -match 'END DIAG')) { return $true }
        Start-Sleep -Seconds 4
    }
    return $false
}

# Team rule (2026-09-23): the VERIFIED OBJECT is the compile product the session actually runs,
# i.e. Library\ScriptAssemblies\CR.dll - not just the source mtime (source 01:00:20 vs dll 01:02:18
# proved the gap is real: 118 s). Everything below therefore has to be NEWER than CR.dll.
function Rev([string]$p) { if (Test-Path $p) { return (Get-Item $p).LastWriteTime } return 'n/a' }
Set-Content -Path $log -Value '' -Encoding utf8
$runStart = Get-Date
$hudRev = Rev "$root\client\Assets\Scripts\UI\Panels\HudPanel.cs"
$resRev = Rev "$root\client\Assets\Scripts\Core\ResPaths.cs"
$styRev = Rev "$root\client\Assets\Scripts\UI\CrUiStyle.cs"
$dllRev = Rev "$root\client\Library\ScriptAssemblies\CR.dll"
Log "=== CR-T3 re-collection $(Get-Date -Format o) ==="
Log "VERIFIED-OBJECT REVS: HudPanel.cs=$hudRev | ResPaths.cs=$resRev | CrUiStyle.cs=$styRev | CR.dll=$dllRev"
Log "RUN START: $runStart  (all evidence below must be NEWER than CR.dll) ==="
$ok = $false
for ($attempt = 1; $attempt -le 3; $attempt++) {
    Log "########## battle entry attempt ${attempt} $(Get-Date -Format o) ##########"
    if ((Test-Path $diag) -and ((Get-Content $diag -Raw) -match 'END DIAG')) { Remove-Item $diag -Force }
    if (EnterBattle) { $ok = $true; break }
    Log "attempt ${attempt}: no DIAG (play session without engine) - retrying"
}
if (-not $ok) { Log 'FATAL no DIAG after 3 attempts'; return }
Log "DIAG:`n$(Get-Content $diag -Raw)"

# ---- part A: numeric chain (same rows as cr-t3-drag-chain.ps1) ----
Log ("A1 DIAG-only warm sample:`n" + (Sample 'A1-diag' ''))
Log "A2 legal  : $((Inject 'direct-one:540,720,540,900'))"
Start-Sleep -Seconds 1
Log ("A3 after-legal:`n" + (Sample 'A3-after-legal' ''))
Start-Sleep -Seconds 3
Log "A4 illegal: $((Inject 'direct-one:540,1440,540,1300'))"
Start-Sleep -Seconds 1
Log ("A5 after-illegal:`n" + (Sample 'A5-after-illegal' ''))

# ---- part B: visuals with the pose held (baseline first, no ghost/disc) ----
Start-Sleep -Seconds 2
Log ("B0 baseline:`n" + (Sample 'B0-baseline' 're-00-baseline'))
Start-Sleep -Milliseconds 1200
$shots = "$root\.ai-tmp\screenshots"
$frames = @()
foreach ($pair in @(@('legal', '540,700'), @('illegal', '540,1300'))) {
    $name = $pair[0]; $pt = $pair[1]
    Log "B pose ${name}: $((Inject "pose:$pt"))"
    Start-Sleep -Milliseconds 700
    $s = Sample "B-$name" "re-01-ghost-$name"
    Log "B sample ${name}`n$s"
    $gs = Grab $s 'ghostScreen=\((-?\d+),(-?\d+)\)'
    $ds = Grab $s 'indicator\.visible=\w+ world=\(-?[\d.]*,-?[\d.]*\) screen=\((-?\d+),(-?\d+)\)'
    Log "B coords ${name} ghostScreen=$gs discScreen=$ds"
    $frames += "$name`:$shots\CR-T3-re-01-ghost-$name.png`:$gs`:$ds"
    Start-Sleep -Milliseconds 900
}
Log "B end gesture: $((Inject 'end:540,700'))"
Start-Sleep -Seconds 1
Log ("B after-end:`n" + (Sample 'B-after-end' ''))

$py = "$dev\cr-t3-contact.py"
$base = "$shots\CR-T3-re-00-baseline.png"
$sheet = "$shots\CR-T3-re-contact-sheet.png"
$pargs = @($py, '--out', $sheet, '--baseline', $base)
foreach ($f in $frames) { $pargs += '--frame'; $pargs += $f }
Log ("python " + ($pargs -join ' '))
Log ((& python @pargs 2>&1 | Out-String))

# Post-condition: the frame leg is the one that can be LOST silently (Play mode can stop mid-chain -
# this happened on the 4th collection's attempt 1: every B-phase sample read PLAY=0 and no png was
# written). State it explicitly so a bad run is visible in one line instead of being discovered later.
$dllAfter = Rev "$root\client\Library\ScriptAssemblies\CR.dll"
if ($dllAfter -gt $runStart) {
    Log "POSTCONDITION build=STALE -> CR.dll recompiled at $dllAfter, AFTER this run started at $runStart : this session ran an OLDER build, so its evidence does NOT cover the current CR.dll. Re-run needed."
} else {
    Log "POSTCONDITION build=OK (CR.dll $dllAfter <= run start $runStart)"
}
$missing = @()
foreach ($n in @('CR-T3-re-00-baseline.png','CR-T3-re-01-ghost-legal.png','CR-T3-re-01-ghost-illegal.png','CR-T3-re-contact-sheet.png')) {
    $p = "$root\.ai-tmp\screenshots\$n"
    if (-not (Test-Path $p)) { $missing += "$n=MISSING" }
    elseif (((Get-Item $p).LastWriteTime -lt $runStart) -or ((Get-Item $p).LastWriteTime -lt $dllAfter)) { $missing += "$n=OLDER($((Get-Item $p).LastWriteTime))" }
}
if ((Test-Path $diag) -and ((Get-Content $diag -Raw) -match 'END DIAG')) { Log 'POSTCONDITION diag=OK' } else { Log 'POSTCONDITION diag=MISSING' }
if ($missing.Count -eq 0) { Log 'POSTCONDITION frames=OK (all four newer than this run start)' }
else { Log ('POSTCONDITION frames=BAD -> ' + ($missing -join ', ') + ' ; a fresh session is needed') }
Log "=== done $(Get-Date -Format o) ==="
