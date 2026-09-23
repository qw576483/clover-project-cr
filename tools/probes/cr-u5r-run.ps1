# CR-U5R chain runner: A1 (own-half deploy on the CURRENT assembly) + A2 (isolated station guard).
# Evidence-only: it drives production entry points and reads them back; no product code is touched.
# Proven shape copied from CR-F1-run.ps1 v2 (never nest quotes inside a Log() argument; the
# "unity " prefix lives inside RunUnity; every unity call goes through P / RunUnity).
# Note: file writes use [IO.File]::AppendAllText with an explicit UTF8(no BOM) encoding.
param([string]$Chain = 'a1a2')
$ErrorActionPreference = 'Continue'
$root  = 'C:\Work\Server\f-v2\clover-project-cr'
$proj  = "$root\client"
$tmp   = "$root\.ai-tmp\test"
$probe = "$root\tools\probes\CR-U5R-probe.cs"
$argf  = "$tmp\U5R-arg.txt"
$log   = "$tmp\CR-U5R-$Chain.out.txt"
$hb    = "$tmp\heartbeat-CR-U5R.tsv"
$play  = "$tmp\play-log.tsv"
$u8    = New-Object System.Text.UTF8Encoding($false)

function App([string]$path, [string]$text) { [System.IO.File]::AppendAllText($path, $text, $u8) }
function Log([string]$m) { App $log ("`n" + $m + "`n") }
function Beat([string]$m) { App $hb ((Get-Date -Format o) + "`t" + $m + "`n") }
function RunUnity([string]$ua, [int]$t) {
    $cmd = 'unity ' + $ua
    $job = Start-Job -ScriptBlock {
        param($c, $p)
        Set-Location $p
        $ErrorActionPreference = 'Continue'
        (Invoke-Expression $c 2>&1 | Out-String)
    } -ArgumentList $cmd, $proj
    if (Wait-Job $job -Timeout $t) { $out = Receive-Job $job } else { Stop-Job $job; $out = "TIMEOUT-$t" }
    Remove-Job $job -Force
    return $out
}
function P([string]$a) {
    [System.IO.File]::WriteAllText($argf, $a, (New-Object System.Text.ASCIIEncoding))
    return (RunUnity ('command eval_file --file "' + $probe + '"') 240)
}
function PLog([string]$label, [string]$a) { Log ("--- " + $label + " ['" + $a + "']`n" + (P $a)) }

$runStart = Get-Date
Log ("=== CR-U5R chain=$Chain start " + (Get-Date -Format o) + " ===")
App $play ((Get-Date -Format o) + "`tCR-U5R`t" + $Chain + "`tSTART - A1: the own-half deploy path has NO reading on the CURRENT assembly (CR-T3's 8 successes are 8h older than the HudPanel/SettingsPanel changes; CR.dll 08:43:53 vs source 08:29:09 verified before the run). A1 drives press -> move -> release at (540,720) and reads client+server lines; A2 needs the isolated `station!=Battle AND no popup` context. Both are runtime-only. One chain, <=3 min.`n")
Beat ("chain $Chain START - A1 own-half deploy (540,720) + A2 isolated station guard; editor PID/PROJECT recorded below")

# ---- 0. assembly fingerprint (stale-assembly guard) ----
$src = Get-ChildItem "$proj\Assets\Scripts" -Recurse -File -Filter *.cs | Sort-Object LastWriteTime -Descending | Select-Object -First 1
$dll = Get-Item "$proj\Library\ScriptAssemblies\CR.dll"
$h = (Get-FileHash $dll.FullName -Algorithm SHA256).Hash
Log ("fingerprint: newest source = " + $src.LastWriteTime + " " + $src.Name)
Log ("fingerprint: CR.dll = " + $dll.LastWriteTime + " sha256=" + $h)
Beat ("fingerprint: source " + $src.LastWriteTime + " / CR.dll " + $dll.LastWriteTime + " sha256=" + $h)
if ($dll.LastWriteTime -lt $src.LastWriteTime) { Log "ABORT: CR.dll is OLDER than source (stale assembly)"; exit 1 }

# ---- 1. enter Play ----
Log ("editor_stop: " + (RunUnity 'command editor_stop' 180))
Start-Sleep -Seconds 3
Log ("editor_play: " + (RunUnity 'command editor_play' 300))
Start-Sleep -Seconds 32
PLog 'state(play)' 'state'
Beat "play entered"

# ---- 2. login -> main menu -> AI battle ----
PLog 'loginform' 'loginform'
PLog 'click LoginButton' 'click:LoginButton'
Start-Sleep -Seconds 7
PLog 'state(after login)' 'state'
PLog 'click AiBattleButton' 'click:AiBattleButton'
Start-Sleep -Seconds 22
PLog 'state(battle)' 'state'
Beat "battle entered"

# ---- 3. A1: real press -> move -> release in OUR OWN half (the point CR-T3 proved legal) ----
PLog 'A1 mousepos(baseline)' 'mousepos'
PLog 'A1 presshand 0' 'presshand:0'
Start-Sleep -Milliseconds 1200
PLog 'A1 dragstate' 'dragstate'
PLog 'A1 mousepos(after press)' 'mousepos'
PLog 'A1 moveat 540,700' 'moveat:540,700'
Start-Sleep -Milliseconds 1200
PLog 'A1 releasehand 540,720' 'releasehand:540,720'
Start-Sleep -Milliseconds 2500
PLog 'A1 state(after release)' 'state'
PLog 'A1 mousepos(after release)' 'mousepos'
Start-Sleep -Seconds 2
PLog 'A1 state(after release +2s)' 'state'
Beat "A1 driven (press 214,221 -> move 540,700 -> release 540,720)"

# ---- 3b. A1 RE-VERIFICATION (chain a1b): two MORE legal points, to kill "maybe only (540,720) works" ----
if ($Chain -eq 'a1b') {
    PLog 'A1b state(before 2nd cast)' 'state'
    PLog 'A1b presshand 0 (2nd)' 'presshand:0'
    Start-Sleep -Milliseconds 1200
    PLog 'A1b dragstate' 'dragstate'
    PLog 'A1b moveat 540,680' 'moveat:540,680'
    Start-Sleep -Milliseconds 1200
    PLog 'A1b releasehand 540,700' 'releasehand:540,700'
    Start-Sleep -Milliseconds 2500
    PLog 'A1b state(after 2nd cast)' 'state'
    Beat "A1b 2nd cast driven (release 540,700)"
    # wait for elixir before the 3rd cast (a low-elixir rejection would be a false negative)
    Start-Sleep -Seconds 12
    PLog 'A1b state(before 3rd cast)' 'state'
    PLog 'A1b presshand 0 (3rd)' 'presshand:0'
    Start-Sleep -Milliseconds 1200
    PLog 'A1b dragstate' 'dragstate'
    PLog 'A1b moveat 540,580' 'moveat:540,580'
    Start-Sleep -Milliseconds 1200
    PLog 'A1b releasehand 540,600' 'releasehand:540,600'
    Start-Sleep -Milliseconds 2500
    PLog 'A1b state(after 3rd cast)' 'state'
    Beat "A1b 3rd cast driven (release 540,600)"
}

# ---- 4. A2: station != Battle AND NO popup (the isolated station-guard context) ----
PLog 'A2 emit openpause' 'emit:openpause'
Start-Sleep -Milliseconds 1500
PLog 'A2 state(pause open)' 'state'
PLog 'A2 closePanel PausePanel' 'closePanel:PausePanel'
Start-Sleep -Milliseconds 1500
PLog 'A2 state(isolated: station=Pause, no popup)' 'state'
PLog 'A2 mousepos' 'mousepos'
PLog 'A2 presshand 0' 'presshand:0'
Start-Sleep -Milliseconds 1500
PLog 'A2 dragstate (must stay _dragging=False)' 'dragstate'
PLog 'A2 state' 'state'
Beat "A2 driven (station=Pause + no popup + real press)"

Log ("=== CR-U5R chain=$Chain done " + (Get-Date -Format o) + " (started $runStart) ===")
Log ("editor_stop: " + (RunUnity 'command editor_stop' 180))
App $play ((Get-Date -Format o) + "`tCR-U5R`t" + $Chain + "`tEND - editor stopped, window FREE. See .ai-tmp/test/CR-U5R-a1a2.out.txt + U5R-out.txt; product log = client/Logs/2026-09-23.log`n")
Beat "chain $Chain END - editor stopped, window FREE"
