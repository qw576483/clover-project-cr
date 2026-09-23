# CR-T3 performance-class evidence: capture the frame BEFORE EndDrag so the ghost card at the pointer
# and the placement disc (green = own legal zone / red = river-enemy half) are visible, then measure them.
#
# How the pose is held without a real mouse button:
#   `pose:<x>,<y>` in CR-T3-inject.cs runs the production BeginDrag + MoveGhost (that is what draws the
#   ghost and calls BattleViewRoot.ShowPlacement), then clears HudPanel._dragging so PollDrag's
#   "button state vs drag state" guard does not cancel it before the screenshot is taken.
#
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File tools\probes\cr-t3-visual.ps1
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
$log = "$tmp\CR-T3-visual.out.txt"

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

Set-Content -Path $log -Value '' -Encoding utf8
Log "=== CR-T3 visual chain $(Get-Date -Format o) ==="
# Entering a battle is flaky on this machine (observed ~1 in 3): the first eval of a session can
# trigger a script recompile / domain reload that leaves a play session whose engine statics are gone
# (the probe then reports station=NULL while Application.isPlaying is true). Retry, <=3 attempts.
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

$ok = $false
for ($attempt = 1; $attempt -le 3; $attempt++) {
    Log "########## battle entry attempt ${attempt} $(Get-Date -Format o) ##########"
    if ((Test-Path $diag) -and ((Get-Content $diag -Raw) -match 'END DIAG')) { Remove-Item $diag -Force }
    if (EnterBattle) { $ok = $true; break }
    Log "attempt ${attempt}: no DIAG (play session without engine) - retrying"
}
if (-not $ok) { Log 'FATAL no DIAG after 3 attempts'; return }
$diagTxt = Get-Content $diag -Raw
Log "DIAG:`n$diagTxt"

$shots = "$root\.ai-tmp\screenshots"
$baseTxt = Sample 'vis-00-baseline' 'vis-00-baseline'
Log "baseline sample:`n$baseTxt"
Start-Sleep -Milliseconds 1200

$legal = '540,700'      # tile (9,11.67) own half -> legal
$illegal = '540,1300'   # tile (9,21.67) enemy half -> illegal
$frames = @()
foreach ($pair in @(@('legal', $legal), @('illegal', $illegal))) {
    $name = $pair[0]; $pt = $pair[1]
    Log ("pose $name : " + (Inject "pose:$pt"))
    Start-Sleep -Milliseconds 700
    $s = Sample "vis-$name" "vis-01-ghost-$name"
    Log "sample $name`n$s"
    $gs = Grab $s 'ghostScreen=\((-?\d+),(-?\d+)\)'
    $ds = Grab $s 'indicator\.visible=\w+ world=\(-?[\d.]*,-?[\d.]*\) screen=\((-?\d+),(-?\d+)\)'
    Log "coords $name ghostScreen=$gs discScreen=$ds"
    $frames += "$name`:$shots\CR-T3-vis-01-ghost-$name.png`:$gs`:$ds"
    Start-Sleep -Milliseconds 900
}
Log ("end gesture: " + (Inject 'end:540,700'))
Start-Sleep -Seconds 1
Log (Sample 'vis-after-end' '')

$py = "$dev\cr-t3-contact.py"
$base = "$shots\CR-T3-vis-00-baseline.png"
$sheet = "$shots\CR-T3-contact-sheet.png"
$args = @($py, '--out', $sheet, '--baseline', $base)
foreach ($f in $frames) { $args += '--frame'; $args += $f }
Log ("python " + ($args -join ' '))
Log ((& python @args 2>&1 | Out-String))
Log "=== done $(Get-Date -Format o) ==="
