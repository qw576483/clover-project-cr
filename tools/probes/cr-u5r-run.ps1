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

# ---- log-side assertions: A1 is the POSITIVE CONTROL for A2 (team-lead ruling 2026-09-23) ----
# A2's PASS reading IS "0 drags". A run in which the input edge is lost yields the SAME reading as a
# run in which the guard correctly swallowed the press. The witness that UI is alive (pause=True) is
# NOT a witness that the INPUT CHAIN is alive - that witness is A1 itself. So A1's own positive
# control is counted from the product log and must pass BEFORE A2 may be graded at all.
# The pattern literals are written as .NET \uXXXX escapes ON PURPOSE: this .ps1 is BOM-less and
# "no BOM + non-ASCII" is a known host trap, so the file stays pure ASCII while the regex still
# matches the Chinese log text.
$RE_DRAGSTART = '\u5F00\u59CB\u62D6\u653E'   # drag-start row, emitted by HudPanel when a real press reaches HitTestHand
$RE_GUARDHIT  = '\u7AD9\u70B9\u5B88\u536B'   # "station guard" row, the only output of the swallowed press
function LogCount([string]$file, [string]$pattern, [System.DateTime]$since) {
    # counts pattern hits among lines whose HH:mm:ss.fff is >= the chain start (same-day log file, so
    # an ordinal compare of HH:mm:ss is correct). Unreadable file => ok=$false (loudly NOT-JUDGED by
    # the caller, never a silent pass). The window is returned so the filter is auditable.
    # The read is wrapped in try/catch ON PURPOSE: a live editor holds the product log open, so
    # ReadAllLines can throw "being used by another process" - without this the caller would see
    # $null and the reason would be lost (self-check host .ai-tmp/hosts/u5r-selfassert-check.ps1
    # reproduced exactly that lock).
    if (-not (Test-Path $file)) { return @{ ok = $false; n = -1; why = 'missing file' } }
    $lines = $null
    try { $lines = [System.IO.File]::ReadAllLines($file) }
    catch { return @{ ok = $false; n = -1; why = ('unreadable: ' + $_.Exception.Message) } }
    if ($null -eq $lines) { return @{ ok = $false; n = -1; why = 'ReadAllLines returned null' } }
    $sinceKey = $since.ToString('HH:mm:ss')
    $n = 0
    foreach ($ln in $lines) {
        if ($ln.Length -lt 19) { continue }
        $ts = $ln.Substring(11, 8)
        if ($ts -notmatch '^[0-9][0-9]:[0-9][0-9]:[0-9][0-9]$') { continue }
        if ([string]::CompareOrdinal($ts, $sinceKey) -lt 0) { continue }
        if ($ln -match $pattern) { $n++ }
    }
    return @{ ok = $true; n = $n; why = '' }
}
$logFile = "$proj\Logs\" + $runStart.ToString('yyyy-MM-dd') + ".log"
Log ("LOGGREP target = " + $logFile + " window = since " + $runStart.ToString('yyyy-MM-dd HH:mm:ss') + " (shared machine: the window filter is what keeps other slices' presses out of my counts)")

$runStart = Get-Date
# which DRIVER ran this chain: team-lead ruled the runner is NOT registered in index.tsv (it is a
# PowerShell driver, not part of CR.dll), but the chain log must still say which version drove it.
$runnerPath = $PSCommandPath
if (-not $runnerPath) { $runnerPath = $MyInvocation.MyCommand.Path }
$runnerH = (Get-FileHash $runnerPath -Algorithm SHA256).Hash
$runnerBytes = ([System.IO.File]::ReadAllBytes($runnerPath)).Count
Log ("DRIVER " + $runnerPath + " sha256_16=" + $runnerH.Substring(0,16) + " bytes=" + $runnerBytes)
Beat ("driver " + $runnerH.Substring(0,16) + " bytes=" + $runnerBytes)
# NOTE on the START row below: its "<=3 min" + assembly wording is PRE-FREEZE history. This run is
# the freeze-batch re-take (A2 NOT compressed per team-lead => ~4 min) against the FROZEN baseline;
# the binding facts are the DRIVER line above and the CHECK[...] lines below. The append-only START
# row is deliberately NOT rewritten (evidence files are not edited retroactively).
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
if ($dll.LastWriteTime -lt $src.LastWriteTime) { Log "ABORT: CR.dll is OLDER than source (stale assembly)"; App $play ((Get-Date -Format o) + "`tCR-U5R`t" + $Chain + "`tEND - ABORTED before Play: stale assembly; the window was never taken`n"); exit 1 }

# ---- 0b. FREEZE-BATCH CHECKS (team-lead's three, 2026-09-23T11:30 freeze) ----
#   1) retention artifact present   2) pre == post (added at the tail of the chain)
#   3) this dll's sha256_16 is a row of index.tsv
# Design rule: degrade LOUDLY, never silently. A missing retention artifact / missing index.tsv is
# an ASSEMBLY WARN and the chain still runs (evidence stays valuable; retention may simply be not
# done yet), but a sha16 that differs from the FROZEN baseline is a STOP **before** Play - running
# a chain on a non-frozen baseline would burn the window and produce unusable evidence.
$frozenSha16 = '0F4D2C8A75D5D318'
$indexTsv    = "$root\tools\probes\assemblies\index.tsv"
$dllSha16    = $h.Substring(0,16).ToUpperInvariant()
$preHash     = $h
$asmbWarn    = $false
Log ("CHECK[baseline] dll sha256_16=" + $dllSha16 + " frozen=" + $frozenSha16 + " match=" + ($dllSha16 -eq $frozenSha16))
if ($dllSha16 -ne $frozenSha16) {
    Log "STOP-FLAG: CR.dll != the frozen baseline => the freeze is BROKEN. NOT entering Play; report to team-lead."
    Beat ("STOP-FLAG: CR.dll sha16=" + $dllSha16 + " != frozen " + $frozenSha16 + " => freeze broken, chain aborted BEFORE Play (window not burned)")
    # a dangling START row would make the shared play-log look like an unfinished run and would
    # block other slices' "window FREE?" check - so every abort path closes its own row.
    App $play ((Get-Date -Format o) + "`tCR-U5R`t" + $Chain + "`tEND - ABORTED before Play: CR.dll is not the frozen baseline; the window was never taken`n")
    exit 2
}
# paths are FIXED by team-lead (2026-09-23T11:5x): a discovery-style search would HIDE path drift,
# and "found it, but somewhere else" is exactly the two-contradictory-statements failure mode this
# project keeps hitting. So: exact path + exact file name. Retention: tools/probes/assemblies/<sha16>.dll
$retDll = "$root\tools\probes\assemblies\" + $dllSha16 + ".dll"
if (Test-Path $retDll) {
    $retItem = Get-Item $retDll
    $retHash = (Get-FileHash $retItem.FullName -Algorithm SHA256).Hash
    Log ("CHECK[1 retention] FOUND " + $retDll + " bytes=" + $retItem.Length + " sha256=" + $retHash + " equals-baseline=" + ($retHash -eq $preHash))
    if ($retHash -ne $preHash) { $asmbWarn = $true; Log "CHECK[1 retention] MISMATCH: the retained file does NOT hash to the baseline byte-for-byte => ASSEMBLY WARN" }
} else {
    $asmbWarn = $true
    Log ("CHECK[1 retention] NOT-FOUND: " + $retDll + " (exact path, no search on purpose) => ASSEMBLY WARN")
}
if (Test-Path $indexTsv) {
    $rows = @(Get-Content $indexTsv -Encoding UTF8 | Where-Object { $_ -and ($_ -notmatch '^\s*#') })
    $hitRow = @($rows | Where-Object { (($_ -split "`t")[0]).Trim().ToUpperInvariant() -eq $dllSha16 })
    if ($hitRow.Count -gt 0) {
        # team-lead ruling (2026-09-23): the SAME sha16 may legitimately sit on more than one row
        # (append-only: row1 superseded + row2 current). The reading is the LAST row (by frozen-at).
        # The match count is printed so an ambiguity stays VISIBLE instead of silently resolved -
        # "one fact, two numbers" is the failure mode this project keeps hitting.
        Log ("CHECK[3 index.tsv] matchedRows=" + $hitRow.Count + " (append-only history; reading the LAST row per the frozen-at ruling)")
        Log ("CHECK[3 index.tsv] FOUND row: " + $hitRow[$hitRow.Count - 1])
    }
    else { $asmbWarn = $true; Log ("CHECK[3 index.tsv] NOT-FOUND " + $dllSha16 + " in " + $indexTsv + " (" + $rows.Count + " data rows) => ASSEMBLY WARN") }
} else {
    $asmbWarn = $true
    Log ("CHECK[3 index.tsv] MISSING file " + $indexTsv + " => ASSEMBLY WARN")
}
if ($asmbWarn) { Log "ASSEMBLY WARN: at least one of the three freeze checks is NOT satisfied - the readings below are still runtime-valid, but the retention/binding is incomplete. Report, do not paper over." }
else { Log "ASSEMBLY OK: baseline + retention + index.tsv membership all satisfied." }

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
$stBattle = P 'state'
Log ("--- state(battle) ['state']`n" + $stBattle)

# ---- 2b. STALL GATE (approved by team-lead 2026-09-23T12:0x) ----
# The editor has an INTERMITTENT stall ("before panel #1": no UI ever builds) in which every probe
# still answers HONESTLY. That would make this chain emit "0 drags / hud=NULL" - the SAME SHAPE as
# A2's correct PASS reading (a guarded press also yields 0 drags). A stalled run must therefore never
# be reportable as a reading: every NEGATIVE criterion needs a witness that the environment is ALIVE.
# Witness #1 (A1): the battle + HUD really exist. Witness #2 (A2, further down): pause=True flips.
$a2NotJudged = $false
$okBattle = ($stBattle -match 'station=Battle')
$okHud = ($stBattle -match 'hudInst=OK')
Log ("STALL-GATE[enter] station=Battle=" + $okBattle + " hudInst=OK=" + $okHud)
if ([string]::IsNullOrEmpty($stBattle)) {
    Log "STALL-GATE FAIL(NOT-JUDGED): the state probe returned NOTHING => aliveness cannot be judged => not driving anything (an unreadable probe is never a pass)."
    Beat "STALL-GATE FAIL(NOT-JUDGED): empty state probe => chain aborted before driving; window released"
    App $play ((Get-Date -Format o) + "`tCR-U5R`t" + $Chain + "`tEND - ABORTED at the stall gate (state probe empty, aliveness NOT-JUDGED); no chain step was driven`n")
    Log ("editor_stop: " + (RunUnity 'command editor_stop' 180))
    exit 3
}
if (-not ($okBattle -and $okHud)) {
    Log "STALL-GATE FAIL: battle/HUD never came up after enter-Play (the known intermittent stall BEFORE panel #1). NOT driving the chain - a stalled editor would also produce '0 drags', indistinguishable from A2's CORRECT PASS (a guarded press starts no drag either). Report; do NOT retry in this window."
    Beat "STALL-GATE FAIL (station=Battle/hudInst=OK not met) => chain aborted before driving anything; window released"
    App $play ((Get-Date -Format o) + "`tCR-U5R`t" + $Chain + "`tEND - ABORTED at the stall gate (no battle/HUD after enter-Play); no chain step was driven`n")
    Log ("editor_stop: " + (RunUnity 'command editor_stop' 180))
    exit 3
}
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
$stPause = P 'state'
Log ("--- A2 state(pause open) ['state']`n" + $stPause)

# ---- 2c. STALL-GATE witness #2 (for A2) ----
# A2's PASS reading IS "0 drags" (a guarded press starts no drag). If the editor were stalled, that
# very same reading would appear for the OPPOSITE reason. Witness = the UI stack demonstrably reacts
# here (pause=True); a stalled editor cannot produce it. Failing this does NOT invalidate A1's
# readings, so the chain finishes and then exits 4 with A2 explicitly NOT-JUDGED.
$okPauseWitness = ($stPause -match 'pause=True')
Log ("STALL-GATE[witness-2 pause=True] " + $okPauseWitness)
if ([string]::IsNullOrEmpty($stPause) -or (-not $okPauseWitness)) {
    $a2NotJudged = $true
    Log "STALL-GATE A2-WITNESS FAIL: 'emit:openpause' did not yield panels pause=True => A2's '0 drags' cannot be told apart from 'nothing ran' => A2 NOT-JUDGED (A1 stays valid)."
    Beat "STALL-GATE A2-WITNESS FAIL => A2 NOT-JUDGED (A1 valid)"
} else {
    Beat "A2 pause witness ok (pause=True)"
}
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
Start-Sleep -Seconds 3

# ---- 5. post-chain check 2 of 3: pre == post (the freeze must not have moved under us) ----
# done AFTER editor_stop so the dll is definitely released; a locked file must be reported as
# "unavailable", never silently treated as a mismatch.
$post = ''
try { $post = (Get-FileHash "$proj\Library\ScriptAssemblies\CR.dll" -Algorithm SHA256).Hash }
catch { Log ("CHECK[2 pre==post] post-hash UNAVAILABLE: " + $_.Exception.Message + " => ASSEMBLY WARN") ; $asmbWarn = $true }
if ($post -ne '') {
    $same = ($post -eq $preHash)
    Log ("CHECK[2 pre==post] pre=" + $preHash + " post=" + $post + " same=" + $same + " sha16=" + $post.Substring(0,16))
    if ($same) { Log "ASSEMBLY OK: pre == post (the whole chain ran on one frozen assembly)." }
    else { Log "STOP-FLAG: CR.dll CHANGED during the chain => the freeze broke mid-run. BOTH readings must be discarded and the chain re-run. Report to team-lead." ; $asmbWarn = $true }
    Beat ("chain $Chain post-check pre==post " + $same + " sha16=" + $post.Substring(0,16) + " assemblyWarn=" + $asmbWarn)
}
# ---- 6. SELF-ASSERT: was the input chain alive at all? (A1 = the positive control for A2) ----
# Evaluated here, at the very END of the chain and AFTER editor_stop, because the product log is
# written by the game during the chain. Rationale (team-lead ruling 2026-09-23): a run whose input
# edge is lost produces exactly A2's PASS shape ("0 drags"), so a FAILED positive control must void
# the negative criterion instead of being reported as a pass.
$rcDrag  = LogCount $logFile $RE_DRAGSTART $runStart
$rcGuard = LogCount $logFile $RE_GUARDHIT  $runStart
$a1NotJudged = $false
if (($null -eq $rcDrag) -or (-not $rcDrag.ok)) {
    $a1NotJudged = $true
    Log ("SELF-ASSERT A1-UNAVAILABLE: the product log could not be counted => A1's positive control is unreadable => A1 NOT-JUDGED (an uncountable criterion is never a pass). reason: " + $(if ($null -eq $rcDrag) { 'LogCount returned null' } else { $rcDrag.why }))
} else {
    Log ("SELF-ASSERT A1 positive control: drag-start hits=" + $rcDrag.n + " (expected >=1: at least one real press must reach HitTestHand)")
    if ($rcDrag.n -lt 1) {
        $a1NotJudged = $true
        Log "SELF-ASSERT A1 NOT-JUDGED: 0 drag-start rows in this window although presses were queued => the INPUT EDGE was lost (queued ok, never consumed). A failed positive control voids the negative criterion."
    }
}
if (($null -eq $rcGuard) -or (-not $rcGuard.ok)) {
    $a2NotJudged = $true
    Log ("SELF-ASSERT A2-UNAVAILABLE: the guard row could not be counted => A2 NOT-JUDGED. reason: " + $(if ($null -eq $rcGuard) { 'LogCount returned null' } else { $rcGuard.why }))
} else {
    Log ("SELF-ASSERT A2 positive control: guard-row hits=" + $rcGuard.n + " (expected >=1: the press must be seen and swallowed BY THE GUARD, otherwise '0 drags' proves nothing about the guard path)")
    if ($rcGuard.n -lt 1) { $a2NotJudged = $true; Log "SELF-ASSERT A2 NOT-JUDGED: no station-guard row in this window => the press never reached the guard, so '0 drags' is not a guard reading." }
}
if ($a1NotJudged) { $a2NotJudged = $true }
Log ("SELF-ASSERT summary: a1NotJudged=" + $a1NotJudged + " a2NotJudged=" + $a2NotJudged + " dragRows=" + $rcDrag.n + " guardRows=" + $rcGuard.n)

# END row: the chain-log name is derived from $Chain (the hardcoded "a1a2" hint was a stale template
# that mislabelled the a1b run; historical rows are NOT rewritten, this fixes the template going
# forward). The row carries this chain's own end criteria so a reader need not open the chain log.
$endNote = "editor stopped, window FREE. See .ai-tmp/test/CR-U5R-$Chain.out.txt + U5R.txt; product log = client/Logs/2026-09-23.log"
if ($a1NotJudged) { $endNote = "A1 NOT-JUDGED (positive control FAILED: drag-start rows=" + $rcDrag.n + " in this window => the input edge was lost) => A2 auto NOT-JUDGED. " + $endNote }
elseif ($a2NotJudged) { $endNote = "A2 NOT-JUDGED (pos. control failed, guard rows=" + $rcGuard.n + ", or stall witness #2 missing) - A1 evidence valid, drag rows=" + $rcDrag.n + ". " + $endNote }
App $play ((Get-Date -Format o) + "`tCR-U5R`t" + $Chain + "`tEND - " + $endNote + "`n")
Beat ("chain $Chain END - " + $endNote)
if ($a1NotJudged) { Log "FINAL: A1 NOT-JUDGED => exit 5 (a failed positive control is never written as a pass, and it voids A2)."; exit 5 }
if ($a2NotJudged) { Log "FINAL: A1 readings valid; A2 NOT-JUDGED => exit 4 (nothing was force-written as a pass)."; exit 4 }
