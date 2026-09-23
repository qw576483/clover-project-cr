# CR-U5 chain runner (evidence collection only - no product code is touched).
# One Play session per chain; `-Chain` selects what the session does.
#   units : login -> AI battle -> UnitView sweep over every landed chr_*_out (frame coverage /
#           playback / anchor drift) + the S2 frame-time & render-device pair
#   fx    : login -> AI battle -> wait for the natural battle, then read the live effect nodes
#   flow  : login -> AI battle -> real drag-drop (PlayCard + 弹道 + SFX), pause popup guard,
#           pause -> settings -> close-settings mid state, return to main menu, re-enter battle
# Every `unity` call is wrapped in a job with an explicit timeout.
$ErrorActionPreference = 'Continue'
param([string]$Chain = 'units')
$root = 'C:\Work\Server\f-v2\clover-project-cr'
$proj = "$root\client"
$dev  = $PSScriptRoot
$tmp  = "$root\.ai-tmp\test"
$probe = "$dev\CR-U5-probe.cs"
$scene = "$dev\CR-T3-scene.cs"
$inject = "$dev\CR-T3-inject.cs"
$argf = "$tmp\U5-arg.txt"
$log  = "$tmp\CR-U5-$Chain.out.txt"

function Log([string]$m) { Add-Content -Path $log -Value $m -Encoding UTF8 }
function RunUnity([string]$ua, [int]$t) {
    # NOTE: editor discovery is cwd-relative AND `--project-path` was measured NOT to resolve the
    # instance in this environment - running from inside the project dir is the form that works.
    $cmd = "unity $ua"
    $errf = "$tmp\unify-stderr.txt"
    $job = Start-Job -ScriptBlock {
        param($c, $p, $ef)
        Set-Location $p
        $ErrorActionPreference = 'Continue'
        $o = Invoke-Expression $c 2>&1 | Out-String
        $o
    } -ArgumentList $cmd, $proj, $errf
    if (Wait-Job $job -Timeout $t) { $out = Receive-Job $job } else { Stop-Job $job; $out = "TIMEOUT-$t" }
    Remove-Job $job -Force
    return $out
}
function EvalFile([string]$f) { return (RunUnity "command eval_file --file `"$f`"" 240) }
function P([string]$a, [int]$timeout = 240) { Set-Content -Path $argf -Value $a -Encoding ascii; return (EvalFile $probe) }
function PLog([string]$a, [int]$timeout = 240) { Log "--- probe '$a'`n$(P $a $timeout)" }
function Shot([string]$name) {
    return (RunUnity "command capture_game_view save_path=`"$root\.ai-tmp\screenshots\CR-U5-$name.png`"" 180)
}

Set-Content -Path $log -Value '' -Encoding utf8
# the probe appends its own transcript here; start a fresh one so every reading in it belongs to this chain
Set-Content -Path "$tmp\U5-out.txt" -Value "=== CHAIN $Chain START $(Get-Date -Format o) ===" -Encoding UTF8
$runStart = Get-Date
Log "=== CR-U5 chain=$Chain start $(Get-Date -Format o) ==="
Log ("editor_stop: " + (RunUnity 'command editor_stop' 180))
Start-Sleep -Seconds 4
Log ("open Boot: " + (EvalFile $scene))
Start-Sleep -Seconds 3
Log ("editor_play: " + (RunUnity 'command editor_play' 300))
Start-Sleep -Seconds 40
Log "state(boot):`n$(P 'state')"

# ---- entry: login -> main menu -> AI battle ----
Log "click LoginButton: $(P 'click:LoginButton')"
Start-Sleep -Seconds 8
Log "state(after login):`n$(P 'state')"
Log "click AiBattleButton: $(P 'click:AiBattleButton')"
Start-Sleep -Seconds 25
Log "state(battle):`n$(P 'state')"

if ($Chain -eq 'units') {
    # 38 landed dirs, 4 slices so no single eval call runs long
    PLog 'units:0,10'
    PLog 'units:10,20'
    PLog 'units:20,30'
    PLog 'units:30,40'
    Log "sample T1:`n$(P 'sample')"
    Start-Sleep -Seconds 2
    Log "sample T2:`n$(P 'sample')"
    Log "S2 frame time + device:`n$(P 'state')"
    Start-Sleep -Milliseconds 700
    Log "S2 frame time + device (2nd):`n$(P 'state')"
    Start-Sleep -Milliseconds 700
    Log "S2 frame time + device (3rd):`n$(P 'state')"
    Log "fxread(natural battle):`n$(P 'fxread')"
}
elseif ($Chain -eq 'fx') {
    Log "fxread before:`n$(P 'fxread')"
    foreach ($k in 2, 3, 0) {
        Log "fx:$k emit:`n$(P "fx:$k")"
        Start-Sleep -Milliseconds 400
        Log "fxread after kind=$k :`n$(P 'fxread')"
    }
    Log "fx natural (after 8s of battle):"
    Start-Sleep -Seconds 8
    Log "$(P 'fxread')"
}
elseif ($Chain -eq 'flow') {
    # ---- real gesture: press a hand card, drag to the arena, release (produces PlayCard + 弹道 + SFX) ----
    Log "inject press: $(P 'state')"
    Log ("inject: " + (Set-Content -Path "$tmp\T3-inject.txt" -Value 'press:214,221' -Encoding ascii ))
    Log ((RunUnity 'command eval_file --file "' + $inject + '"' 180))
    Start-Sleep -Milliseconds 600
    Log "dragstate after press:`n$(P 'dragstate')"
    Log ("inject move: " + (RunUnity 'command eval_file --file "' + $inject + '"' 180))
    Start-Sleep -Milliseconds 600
    Log "dragstate after move:`n$(P 'dragstate')"
    Log ("inject release: " + (RunUnity 'command eval_file --file "' + $inject + '"' 180))
    Start-Sleep -Seconds 2
    Log "state after release:`n$(P 'state')"
    Log "Shot drag-drop: $(Shot 'flow-drag-drop')"

    # ---- guard A: popup open => drag must NOT start (negative control first, then the guard) ----
    Log "pause click: $(P 'click:Pause/PauseButton')"
    Start-Sleep -Seconds 1
    Log "state after PauseButton:`n$(P 'state')"
    Log "Shot pause: $(Shot 'flow-pause')"
    Set-Content -Path "$tmp\T3-inject.txt" -Value 'press:214,221' -Encoding ascii
    Log ((RunUnity 'command eval_file --file "' + $inject + '"' 180))
    Start-Sleep -Seconds 1
    Log "dragstate (popup open, press injected):`n$(P 'dragstate')"
    Set-Content -Path "$tmp\T3-inject.txt" -Value 'release:214,221' -Encoding ascii
    Log ((RunUnity 'command eval_file --file "' + $inject + '"' 180))
    Start-Sleep -Seconds 1

    # ---- D12 mid state: pause -> settings -> close settings ----
    Log "settings click: $(P 'click:Pause/SettingsButton')"
    Start-Sleep -Seconds 1
    Log "state after open settings:`n$(P 'state')"
    Log "Shot settings: $(Shot 'flow-settings')"
    Log "close settings: $(P 'click:Settings/CloseButton')"
    Start-Sleep -Seconds 1
    Log "state MID (settings closed, station should still be Pause):`n$(P 'state')"
    Log "Shot mid: $(Shot 'flow-mid-after-settings-close')"
    Set-Content -Path "$tmp\T3-inject.txt" -Value 'press:214,221' -Encoding ascii
    Log ((RunUnity 'command eval_file --file "' + $inject + '"' 180))
    Start-Sleep -Seconds 1
    Log "dragstate MID (menu-less Pause station, press injected):`n$(P 'dragstate')"
    Set-Content -Path "$tmp\T3-inject.txt" -Value 'release:214,221' -Encoding ascii
    Log ((RunUnity 'command eval_file --file "' + $inject + '"' 180))

    # ---- D12 round trip: back to main menu, then enter a battle again ----
    Log "return to main menu: $(P 'emit:returnmain')"
    Start-Sleep -Seconds 12
    Log "state after return:`n$(P 'state')"
    Log "Shot mainmenu: $(Shot 'flow-mainmenu-again')"
    Log "ai battle again: $(P 'emit:aibattle')"
    Start-Sleep -Seconds 25
    Log "state re-entered battle:`n$(P 'state')"
    Log "Shot reentered: $(Shot 'flow-reentered')"
}
Log "=== CR-U5 chain=$Chain done $(Get-Date -Format o) (started $runStart) ==="
