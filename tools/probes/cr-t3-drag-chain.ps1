# CR-T3 judgement asset: rerunnable assertion chain for "press a hand card -> drag -> release -> the unit
# really lands on the battlefield (confirmed by the server snapshot)".
#
# Why each assertion below is a reading and not a narration:
#   * hit test           -> CR-T3-probe.cs reports HitTestHand(<true pointer screen point>) per slot
#   * drag start         -> HudPanel's own log "开始拖放：槽位 N card=..." (Game.Logger -> client/Logs/<date>.log)
#   * ghost follows      -> _ghost.rectTransform.position read back in the same main-thread call
#   * drop legality      -> PlacementIndicator.Visible + _renderer.color (green = legal, red = illegal)
#   * C2S sent           -> "请求出牌 card=... -> milli=(...)" (BattleManager)
#   * server accepted    -> "出牌已被接受 card=..." + snapshot liveUnits +1 + hand slot 0 advanced
#   * illegal rejected   -> "出牌被服务端拒绝：..." + the same text in the HUD status line
#
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File tools\probes\cr-t3-drag-chain.ps1
# Requires: a Unity Editor with com.unity.pipeline on <project>\client (the CLI drives it live).
$ErrorActionPreference = 'Continue'
$root = 'C:\Work\Server\f-v2\clover-project-cr'
$proj = "$root\client"
$dev = $PSScriptRoot
$tmp = "$root\.ai-tmp\test"
$probe = "$dev\CR-T3-probe.cs"
$state = "$dev\CR-T3-state.cs"
$inject = "$dev\CR-T3-inject.cs"
$scene = "$dev\CR-T3-scene.cs"
# NOTE: the argument-file names below are the ones hardcoded in the four probes' headers
# (CR-T3-probe.cs / CR-T3-state.cs / CR-T3-inject.cs), so keep them in sync.
$step = "$tmp\T3-step.txt"
$diag = "$tmp\T3-diag.txt"
$label = "$tmp\T3-label.txt"
$shot = "$tmp\T3-shot.txt"
$injf = "$tmp\T3-inject.txt"
$log = "$tmp\CR-T3-drag-chain.out.txt"

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

# Entering a battle is flaky on this machine: the first eval of a session can trigger a script
# recompile / domain reload that leaves a play session whose engine statics are gone
# (probe reports station=NULL while Application.isPlaying is true). Observed ~1 in 3 times.
# So the whole entry is retried (<=3 attempts); that is cheap compared with a false FAIL.
function EnterBattle() {
    Log ("editor_stop: " + (RunUnity 'command editor_stop' 180))
    Start-Sleep -Seconds 4
    # Bootstrap lives in Assets/Scenes/Boot.unity: entering Play from Battle01/Main gives a play session
    # with NO engine (Game.Fsm/Game.UI == null) - the same trap.
    Log ("open Boot: " + (EvalFile $scene))
    Start-Sleep -Seconds 3
    Set-Content -Path $step -Value '0' -Encoding ascii
    Log ("editor_play: " + (RunUnity 'command editor_play' 300))
    Start-Sleep -Seconds 40
    foreach ($s in 0,1,2,3) { Log "probe step $s`n$(EvalFile $probe)"; Start-Sleep -Seconds 3 }
    for ($i = 0; $i -lt 20; $i++) {
        $o = EvalFile $probe
        if ((Test-Path $diag) -and ((Get-Content $diag -Raw) -match 'END DIAG')) { return $true }
        Start-Sleep -Seconds 4
    }
    return $false
}

Set-Content -Path $log -Value '' -Encoding utf8
Log "=== CR-T3 drag chain $(Get-Date -Format o) ==="
$ok = $false
for ($attempt = 1; $attempt -le 3; $attempt++) {
    Log "########## battle entry attempt $attempt $(Get-Date -Format o) ##########"
    if ((Test-Path $diag) -and ((Get-Content $diag -Raw) -match 'END DIAG')) { Remove-Item $diag -Force }
    if (EnterBattle) { $ok = $true; break }
    Log "attempt ${attempt}: no DIAG (engine statics gone / play session without engine) - retrying"
}
if (-not $ok) { Log 'FATAL: no battle after 3 attempts'; return }
$diagTxt = Get-Content $diag -Raw
Log "DIAG:`n$diagTxt"

# assertion rows
Log "LEGAL  : $((Inject 'direct-one:540,720,540,900'))"
Start-Sleep -Seconds 1
Log (Sample 'after-legal' 'legal-after-release')
Start-Sleep -Seconds 3
Log "ILLEGAL: $((Inject 'direct-one:540,1440,540,1300'))"
Start-Sleep -Seconds 1
Log (Sample 'after-illegal' 'illegal-after-release')
Start-Sleep -Milliseconds 800
Log "IND legal  : $((Inject 'ind:540,720'))"
Start-Sleep -Milliseconds 900
Log "IND illegal: $((Inject 'ind:540,1440'))"
Start-Sleep -Milliseconds 900
$gl = "$root\client\Logs\$(Get-Date -Format yyyy-MM-dd).log"
Log "--- HudPanel/BattleManager log lines ---"
if (Test-Path $gl) { Log ((Get-Content $gl -Encoding UTF8 | Select-String -Pattern 'HudPanel|BattleManager|\[Battle\]' | Select-Object -Last 20 | Out-String)) }
Log "=== done $(Get-Date -Format o) ==="
