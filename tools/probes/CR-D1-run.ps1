# CR-D1 chain runner: the "active trigger" for the intermittent stall BEFORE the first panel.
# Evidence-only: every product entry point is the product's own (UI.Open + Button.onClick + AppFlow
# event path); nothing in client/Assets or server/ is modified.
# Shape copied from tools/probes/cr-u5r-run.ps1 (proven): one unity call per Sub, Start-Job timeout,
# [IO.File]::AppendAllText with ABSOLUTE paths, UTF8 no BOM, and every abort path closes its own
# START row with a paired END row (a dangling START would look like an unfinished window to others).
#
# Phases (one chain each, <= 3 min):
#   p1 poison : enter Play -> drive the REAL flow (login -> AI battle) so the round ends in the
#               Battle01 scene, where the Main scene (and thus Bootstrap) no longer exists.
#               STALL-GATE: station=Battle AND hudInst=OK, else paired END + editor_stop + exit 3.
#   p2 stall  : enter Play and WAIT ONLY (no driving). Prediction, read from the running editor:
#               AppFlow.Instance ALIVE with _bus != Game.Event, station=Launching, no panel at all.
#   p3 recover: enter Play and WAIT ONLY. Prediction: after p2's own stop, AppFlow.Instance == NULL
#               -> station=Login and the LoginPanel is open (i.e. stop+play DOES clear it).
param([string]$Phase = 'p1')
$ErrorActionPreference = 'Continue'
$root  = 'C:\Work\Server\f-v2\clover-project-cr'
$proj  = "$root\client"
$tmp   = "$root\.ai-tmp\test"
$probe = "$root\tools\probes\CR-D1-stall-probe.cs"
$argf  = "$tmp\D1-arg.txt"
$log   = "$tmp\CR-D1-$Phase.out.txt"
$hb    = "$tmp\heartbeat-CR-D1.tsv"
$play  = "$tmp\play-log.tsv"
$u8    = New-Object System.Text.UTF8Encoding($false)

function App([string]$path, [string]$text) { [System.IO.File]::AppendAllText($path, $text, $u8) }
function Log([string]$m) { App $log ("`n" + $m + "`n") }
function Beat([string]$m) { App $hb ((Get-Date -Format o) + "`tCR-D1`t" + $m + "`n") }
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
function PLog([string]$label, [string]$a) { $o = P $a; Log ("--- " + $label + " ['" + $a + "']`n" + $o); return $o }
function Fact([string]$o, [string]$key) {
    # the unity eval_file result arrives JSON-escaped, so a probe newline shows up as a LITERAL
    # backslash+n; the value class must therefore stop at a backslash too, not only at whitespace.
    $m = [regex]::Match($o, [regex]::Escape($key) + "=([^\s\\]+)")
    if ($m.Success) { return $m.Groups[1].Value } else { return '' }
}
function Close([string]$note, [int]$code) {
    App $play ((Get-Date -Format o) + "`tCR-D1`t" + $Phase + "`tEND - " + $note + "`n")
    Beat ("chain $Phase END - " + $note)
    Log ("=== chain $Phase exit " + $code + " : " + $note + " ===")
    exit $code
}

$runStart = Get-Date
Log ("=== CR-D1 chain=$Phase start " + (Get-Date -Format o) + " ===")
Beat ("chain $Phase START (driver tools/probes/CR-D1-run.ps1)")

# ---- 0. assembly fingerprint + the three freeze checks ----
$src = Get-ChildItem "$proj\Assets\Scripts" -Recurse -File -Filter *.cs | Sort-Object LastWriteTime -Descending | Select-Object -First 1
$dll = Get-Item "$proj\Library\ScriptAssemblies\CR.dll"
$h   = (Get-FileHash $dll.FullName -Algorithm SHA256).Hash
$sha16 = $h.Substring(0,16).ToUpperInvariant()
$frozenSha16 = '0F4D2C8A75D5D318'
$indexTsv = "$root\tools\probes\assemblies\index.tsv"
$retDll = "$root\tools\probes\assemblies\" + $sha16 + ".dll"
$asmbWarn = $false
Log ("CHECK[baseline] dll sha256_16=" + $sha16 + " frozen=" + $frozenSha16 + " match=" + ($sha16 -eq $frozenSha16))
Log ("CHECK[src] newest source = " + $src.LastWriteTime + " " + $src.Name + " ; CR.dll = " + $dll.LastWriteTime)
if ($sha16 -ne $frozenSha16) {
    App $play ((Get-Date -Format o) + "`tCR-D1`t" + $Phase + "`tEND - ABORTED before Play: CR.dll sha16=" + $sha16 + " != frozen " + $frozenSha16 + "`n")
    Beat ("STOP-FLAG: CR.dll != frozen baseline, chain aborted BEFORE Play")
    Log "STOP-FLAG: not the frozen baseline; NOT entering Play."
    exit 2
}
if (Test-Path $retDll) {
    $rh = (Get-FileHash $retDll -Algorithm SHA256).Hash
    Log ("CHECK[1 retention] FOUND " + $retDll + " bytes=" + (Get-Item $retDll).Length + " equals-baseline=" + ($rh -eq $h))
    if ($rh -ne $h) { $asmbWarn = $true; Log "CHECK[1 retention] MISMATCH => ASSEMBLY WARN" }
} else { $asmbWarn = $true; Log ("CHECK[1 retention] NOT-FOUND " + $retDll + " => ASSEMBLY WARN") }
if (Test-Path $indexTsv) {
    $rows = @(Get-Content $indexTsv -Encoding UTF8 | Where-Object { $_ -and ($_ -notmatch '^\s*#') })
    $hit = @($rows | Where-Object { (($_ -split "`t")[0]).Trim().ToUpperInvariant() -eq $sha16 })
    if ($hit.Count -gt 0) { Log ("CHECK[3 index.tsv] matchedRows=" + $hit.Count + " LAST row: " + $hit[$hit.Count - 1]) }
    else { $asmbWarn = $true; Log ("CHECK[3 index.tsv] NOT-FOUND " + $sha16 + " (" + $rows.Count + " rows) => ASSEMBLY WARN") }
} else { $asmbWarn = $true; Log "CHECK[3 index.tsv] MISSING file => ASSEMBLY WARN" }
Log ("assemblyWarn=" + $asmbWarn)

# ---- 0b. the START row (append-only; carries the mandatory START token) ----
$reason = switch ($Phase) {
    'p1' { 'p1 poison: only a REAL runtime round can end in Battle01 (Main scene unloaded => Bootstrap destroyed), which is the hypothesised cause of the NEXT round`s dead state; entering Play is the only way to create that state.' }
    'p2' { 'p2 stall: the stall exists only at runtime (AppFlow.Instance survival + station=Launching with no panel). Nothing offline can read a live static of the running editor domain.' }
    'pc' { 'pc CAUSAL (team-lead-ordered): the hypothesis claims the surviving AppFlow.Instance IS the cause. Only an intervention on a live static of the running editor domain can test it: null that one static, then enter Play and see whether the stall disappears. Single variable, no product code changed.' }
    default { 'p3 recover: the recovery claim ("stop+play clears it") is a runtime-only statement about the next Play round.' }
}
App $play ((Get-Date -Format o) + "`tCR-D1`t" + $Phase + "`tSTART - " + $reason + " (<=3 min; CR.dll sha16=" + $sha16 + ")`n")
Beat ("chain $Phase START row written; sha16=" + $sha16)

# ---- 1. enter Play ----
Log ("editor_stop: " + (RunUnity 'command editor_stop' 180))
Start-Sleep -Seconds 3
# the between-rounds reading: in EDIT mode the surviving statics are already visible
$pre = PLog 'EDIT-MODE snapshot BEFORE editor_play' 'snap'
Beat ("pre-play edit-mode: AppFlow.Instance=" + (Fact $pre 'AppFlow.Instance') + " PanelFactory._installedBus=" + (Fact $pre 'PanelFactory._installedBus') + " Game.IsRunning=" + (Fact $pre 'Game.IsRunning'))

# ---- 1b. CAUSAL INTERVENTION (phase pc; must happen BEFORE editor_play) ----
if ($Phase -eq 'pc') {
    $af0 = Fact $pre 'AppFlow.Instance'
    Log ("CAUSAL-GUARD: AppFlow.Instance before intervention = " + $af0)
    if ($af0 -ne 'ALIVE') {
        Log ("editor_stop: " + (RunUnity 'command editor_stop' 180))
        Close ("CAUSAL NOT-RUN: the survivor is not present at edit-mode time (AppFlow.Instance=" + $af0 + ") => there is nothing to null; nothing was driven") 6
    }
    $iv = PLog 'INTERVENTION: null AppFlow.Instance only (private setter via reflection; product code untouched)' 'nullinstance'
    $af1 = Fact $iv 'nullinstance AFTER AppFlow.Instance'
    Log ("CAUSAL-GUARD: AppFlow.Instance after intervention = " + $af1)
    if ($af1 -ne 'NULL') { Log ("editor_stop: " + (RunUnity 'command editor_stop' 180)); Close ("CAUSAL NOT-RUN: intervention did not take (Instance=" + $af1 + ")") 6 }
}

Log ("editor_play: " + (RunUnity 'command editor_play' 300))
if ($Phase -eq 'p1') { Start-Sleep -Seconds 32 } else { Start-Sleep -Seconds 38 }

# ---- 2. per-phase body ----
if ($Phase -eq 'p1') {
    $st = PLog 'snap after entering Play' 'snap'
    Log ("GATE-pre station=" + (Fact $st 'station') + " play=" + (Fact $st 'play'))
    if ((Fact $st 'play') -ne 'True') { Log ("editor_stop: " + (RunUnity 'command editor_stop' 180)); Close 'ABORTED: not in Play mode after editor_play (aliveness NOT-JUDGED); window released' 3 }
    PLog 'drive login (force-open LoginPanel + fill + click LoginButton)' 'login'
    Start-Sleep -Seconds 10
    PLog 'snap after login' 'snap'
    PLog 'drive aibattle (click AiBattleButton)' 'aibattle'
    Start-Sleep -Seconds 34
    $stBattle = PLog 'snap after AI battle request' 'snap'
    $okBattle = ((Fact $stBattle 'station') -eq 'Battle')
    $okHud = ((Fact $stBattle 'panelsInst hud') -eq 'OK') -or ($stBattle -match 'hudInst=OK')
    Log ("STALL-GATE[enter] station=Battle=" + $okBattle + " hudInst=OK=" + $okHud)
    if ([string]::IsNullOrEmpty($stBattle)) { Log ("editor_stop: " + (RunUnity 'command editor_stop' 180)); Close 'ABORTED at stall gate (probe returned NOTHING => aliveness cannot be judged)' 3 }
    if (-not ($okBattle -and $okHud)) { Log ("editor_stop: " + (RunUnity 'command editor_stop' 180)); Close 'ABORTED at stall gate: battle/HUD never came up after enter-Play (no valid poison round was taken)' 3 }
    Log ("editor_stop: " + (RunUnity 'command editor_stop' 180))
    Start-Sleep -Seconds 4
    $post = PLog 'EDIT-MODE snapshot AFTER editor_stop (the poison must be visible here)' 'snap'
    $af = Fact $post 'AppFlow.Instance'
    Log ("READING: AppFlow.Instance after the Battle01-ending round = " + $af)
    $postH = (Get-FileHash "$proj\Library\ScriptAssemblies\CR.dll" -Algorithm SHA256).Hash
    Log ("CHECK[2 pre==post] pre=" + $h + " post=" + $postH + " same=" + ($postH -eq $h))
    if ($postH -ne $h) { Log 'STOP-FLAG: CR.dll changed during the chain; readings discarded.'; Close 'CR.dll changed mid-chain; readings discarded' 2 }
    Close ("poison round done (station=Battle+HUD ok); AppFlow.Instance after stop=" + $af + "; pre==post sha16=" + $sha16) 0
}
elseif ($Phase -eq 'pc') {
    # The intervention already happened BEFORE editor_play. Nothing is driven here: the question is
    # only whether the first panel now appears on its own.
    $st = PLog 'snap after the intervention (no driving)' 'snap'
    $station = Fact $st 'station'
    $af = Fact $st 'AppFlow.Instance'
    $loginOpen = 'False'
    if ($st -match 'panels boot=[^\s\\]+ login=([^\s\\]+)') { $loginOpen = $Matches[1] }
    $recovered = (($station -ne 'Launching') -and ($loginOpen -eq 'True'))
    Log ("CAUSAL CLASSIFY station=" + $station + " loginPanelOpen=" + $loginOpen + " AppFlow.Instance=" + $af + " play=" + (Fact $st 'play') + " -> recovered=" + $recovered)
    if ((Fact $st 'play') -ne 'True') { Log ("editor_stop: " + (RunUnity 'command editor_stop' 180)); Close 'CAUSAL NOT-JUDGED: not in Play mode after editor_play' 3 }
    Log ("editor_stop: " + (RunUnity 'command editor_stop' 180))
    Start-Sleep -Seconds 4
    $post = PLog 'EDIT-MODE snapshot AFTER editor_stop' 'snap'
    $afAfter = Fact $post 'AppFlow.Instance'
    $postH = (Get-FileHash "$proj\Library\ScriptAssemblies\CR.dll" -Algorithm SHA256).Hash
    Log ("CHECK[2 pre==post] pre=" + $h + " post=" + $postH + " same=" + ($postH -eq $h))
    if ($postH -ne $h) { Log 'STOP-FLAG: CR.dll changed during the chain; readings discarded.'; Close 'CR.dll changed mid-chain; readings discarded' 2 }
    if ($recovered) {
        Close ("CAUSAL CONFIRMED: single-variable intervention (AppFlow.Instance nulled; PanelFactory._installedBus deliberately left SET) => the round reached station=" + $station + " with the LoginPanel open (stall GONE). AppFlow.Instance after its own stop=" + $afAfter + "; pre==post sha16=" + $sha16 + "; assemblyWarn=" + $asmbWarn) 0
    } else {
        Close ("CAUSAL FAILED: even with AppFlow.Instance nulled the round did not reach the first panel (station=" + $station + " loginPanelOpen=" + $loginOpen + ") => the hypothesis is NOT sufficient; reported as-is, nothing papered over. pre==post sha16=" + $sha16) 7
    }
}
else {
    # p2 / p3: wait only, then classify. Driving anything here would MASK the very state under test.
    $st = PLog 'snap (no driving: the stall is the reading)' 'snap'
    $station = Fact $st 'station'
    $af = Fact $st 'AppFlow.Instance'
    $afBusSame = 'n/a'
    if ($st -match '_bus==Game\.Event: ([^\s\\]+)') { $afBusSame = $Matches[1] }
    $loginOpen = 'False'
    if ($st -match 'panels boot=[^\s\\]+ login=([^\s\\]+)') { $loginOpen = $Matches[1] }
    $isStall = (($station -eq 'Launching') -and ($loginOpen -eq 'False') -and ($af -eq 'ALIVE'))
    Log ("CLASSIFY station=" + $station + " play=" + (Fact $st 'play') + " AppFlow.Instance=" + $af + " _bus==Game.Event=" + $afBusSame + " loginPanelOpen=" + $loginOpen + " -> stall=" + $isStall)
    if ((Fact $st 'play') -ne 'True') { Close 'ABORTED: not in Play mode after editor_play (aliveness NOT-JUDGED)' 3 }
    Log ("editor_stop: " + (RunUnity 'command editor_stop' 180))
    Start-Sleep -Seconds 4
    $post = PLog 'EDIT-MODE snapshot AFTER editor_stop (was the round`s own stop clean?)' 'snap'
    $afAfter = Fact $post 'AppFlow.Instance'
    Log ("READING: AppFlow.Instance after this round`s stop = " + $afAfter)
    $postH = (Get-FileHash "$proj\Library\ScriptAssemblies\CR.dll" -Algorithm SHA256).Hash
    Log ("CHECK[2 pre==post] pre=" + $h + " post=" + $postH + " same=" + ($postH -eq $h))
    if ($postH -ne $h) { Log 'STOP-FLAG: CR.dll changed during the chain; readings discarded.'; Close 'CR.dll changed mid-chain; readings discarded' 2 }
    $note = "station=" + $station + " AppFlow.Instance=" + $af + " (busSameAsCurrent=" + $afBusSame + ") loginPanelOpen=" + $loginOpen + " stall=" + $isStall + "; AppFlow.Instance after its own stop=" + $afAfter + "; pre==post sha16=" + $sha16 + "; assemblyWarn=" + $asmbWarn
    if ($Phase -eq 'p2') { Close ("STALL-ROUND " + $note) 0 }
    else { Close ("RECOVERY-ROUND " + $note) 0 }
}
Log ("=== CR-D1 chain=$Phase done " + (Get-Date -Format o) + " (started $runStart) ===")
exit 0
