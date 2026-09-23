# cr-f2-count.ps1 -- CR-F2 evidence counter (ASCII only; no CJK literals -> no BOM trap).
#
# WHAT IT JUDGES (process, not outcome): the client log of ONE Play session, counted by
# ASCII markers that only the CR-F2 build emits. Each marker sits on a *send* or *drop*
# decision, so the counts answer "how many requests actually left the client", not
# "did a battle eventually start".
#
#   [AiBattle] SEND             -> one line per accepted MsgAiBattleStart request
#   [AiBattle] DUP-DROP(gate)   -> one line per click rejected by the in-flight gate
#   [AiBattle] gate=HOLD / gate=RELEASE -> the gate must be symmetric (no leak)
#   [Deck] SAVE-SEND / SAVE-DUP -> same pair for the deck-save button
#   [Live] Quality / [Quality] engine-level-change -> the engine-side tier change
#                                 reached SettingsManager and then the open panel
#
# USAGE (window is mandatory: the log file is shared by every session of the day)
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools/probes/cr-f2-count.ps1 `
#       -Since "2026-09-23 08:45:00"
# Exit code 0 = every assertion holds; 1 = at least one FAIL (or an empty window).
param(
  [Parameter(Mandatory = $true)][string]$Since,
  [string]$Log,
  [switch]$Quiet
)

$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent   # tools/probes -> <root>
if (-not $Log) {
  $logDir = Join-Path $root 'client\logs'
  $Log = @(Get-ChildItem $logDir -Filter '*.log' | Where-Object { $_.Name -match '^\d{4}-\d{2}-\d{2}\.log$' } |
           Sort-Object LastWriteTime -Descending)[0].FullName
}
if (-not (Test-Path $Log)) { Write-Output ("FAIL  log-not-found  " + $Log); exit 1 }

# NOTE: PowerShell variable names are case-insensitive, so a local called `$since` would BE
# `$Since` (typed [string] by the param block) and the cast would be silently undone.
$sinceTs = [datetime]$Since
$text = [System.IO.File]::ReadAllText($Log, [System.Text.Encoding]::UTF8)
$lines = $text -split "`r?`n"

# Keep only lines whose own timestamp is >= -Since. A line without a parseable
# timestamp is dropped (the window must never silently widen).
$inWindow = New-Object System.Collections.Generic.List[string]
foreach ($ln in $lines) {
  if ($ln.Length -lt 21) { continue }
  $ts = [datetime]::MinValue   # must be pre-typed: [ref] to a $null variable resolves no overload
  if (-not [datetime]::TryParse($ln.Substring(1, 19), [ref]$ts)) { continue }
  if ($ts -ge $sinceTs) { $inWindow.Add($ln) }
}
$win = $inWindow -join "`n"

function Count([string]$needle) {
  $c = 0; $i = 0
  while (($i = $win.IndexOf($needle, $i, [System.StringComparison]::Ordinal)) -ge 0) { $c++; $i += $needle.Length }
  return $c
}

$fail = 0
function Say([string]$status, [string]$name, [string]$detail) {
  Write-Output ("{0,-5} {1}  {2}" -f $status, $name, $detail)
  if ($status -eq 'FAIL') { $script:fail++ }
}

Write-Output ("log    = " + $Log)
Write-Output ("window = since " + $sinceTs.ToString('yyyy-MM-dd HH:mm:ss') + "  lines=" + $inWindow.Count)
Write-Output ''

$aiSend = Count '[AiBattle] SEND'
$aiDup  = Count '[AiBattle] DUP-DROP(gate)'
$aiDup2 = Count '[AiBattle] DUP-DROP(entering)'
$hold   = Count '[AiBattle] gate=HOLD'
$rel    = Count '[AiBattle] gate=RELEASE'
$saveS  = Count '[Deck] SAVE-SEND'
$saveD  = Count '[Deck] SAVE-DUP'
$liveQ  = Count '[Live] Quality'
$engQ   = Count '[Quality] engine-level-change'
$liveB  = Count '[Live] BGM'
$liveF  = Count '[Live] Fullscreen'

$episode = $aiSend + $aiDup
if ($episode -eq 0) {
  Say 'FAIL' 'window-has-episode' 'no [AiBattle] marker in the window (wrong -Since, or the build is stale)'
} else {
  Say 'PASS' 'window-has-episode' ("ai markers = " + $episode)
}

# --- #3 main-menu AI battle: 6 raw clicks, exactly ONE MsgAiBattleStart ---
if ($aiSend -eq 1) { Say 'PASS' 'ai-single-send' 'MsgAiBattleStart sent exactly once' }
else { Say 'FAIL' 'ai-single-send' ("sends=" + $aiSend + " (expect 1)") }

if ($aiDup -eq 5) { Say 'PASS' 'ai-drops-5' 'the other 5 of 6 clicks were rejected by the gate' }
else { Say 'FAIL' 'ai-drops-5' ("gate drops=" + $aiDup + " (expect 5)") }

if ($aiDup2 -eq 0) { Say 'PASS' 'ai-no-entering-drop' 'no click was dropped by the (separate) scene-load gate' }
else { Say 'PASS' 'ai-no-entering-drop' ("entering-gate drops=" + $aiDup2 + " (informational)") }

if ($hold -eq 1 -and $rel -ge 1) { Say 'PASS' 'ai-gate-symmetric' ("HOLD=" + $hold + " RELEASE=" + $rel) }
else { Say 'FAIL' 'ai-gate-symmetric' ("HOLD=" + $hold + " RELEASE=" + $rel + " (expect HOLD=1, RELEASE>=1)") }

# --- #4 deck save: 6 raw clicks, exactly ONE save request ---
if ($saveS -eq 1) { Say 'PASS' 'deck-single-save' 'SaveDeck submitted exactly once' }
else { Say 'FAIL' 'deck-single-save' ("saves=" + $saveS + " (expect 1)") }

if ($saveD -eq 5) { Say 'PASS' 'deck-drops-5' 'the other 5 of 6 clicks were rejected while in flight' }
else { Say 'FAIL' 'deck-drops-5' ("in-flight drops=" + $saveD + " (expect 5)") }

# --- #5 / #1: the engine-side tier change reached the manager AND the open panel ---
if ($engQ -ge 1) { Say 'PASS' 'quality-engine-hook' ("engine-side changes seen = " + $engQ) }
else { Say 'FAIL' 'quality-engine-hook' 'no [Quality] engine-level-change line' }

if ($liveQ -ge 1) { Say 'PASS' 'quality-panel-live' ("panel redraws = " + $liveQ) }
else { Say 'FAIL' 'quality-panel-live' 'the open panel never redrew on QualityChanged' }

if ($liveB -ge 1 -and $liveF -ge 1) { Say 'PASS' 'changed-subscribers' ("BGM redraws=" + $liveB + " fullscreen redraws=" + $liveF) }
else { Say 'FAIL' 'changed-subscribers' ("BGM=" + $liveB + " fullscreen=" + $liveF) }

Write-Output ''
Write-Output ("SUMMARY: FAIL=" + $fail)
if ($fail -gt 0) { exit 1 }
exit 0
