# CR-V1 re-collection chain (ONE Play session, <= 3 min): drive login -> main menu -> AI battle and
# capture (a) the runtime arena-art node tree + (b) the same-camera shot, both AFTER the river fix.
# ASCII only. Every unity call has an explicit timeout; heartbeat + play-log are appended here.
$ErrorActionPreference = 'Continue'
$root = 'C:\Work\Server\f-v2\clover-project-cr'
$proj = "$root\client"
$dev = "$root\.ai-tmp\drivers"
$tmp = "$root\.ai-tmp\test"
$drive = "$dev\CR-V1-drive.cs"
$scene = "$root\tools\probes\CR-T3-scene.cs"   # shared opener; CR-V1 changed its target Boot->Main (evidence in that file)
$step = "$tmp\CR-V1-step.txt"
$evidence = "$tmp\CR-V1-evidence.txt"
$flags = "$tmp\CR-V1-flags.txt"
$log = "$tmp\CR-V1-recollect.out.txt"
$hb = "$tmp\heartbeat-CR-V1.tsv"
$plog = "$tmp\play-log.tsv"

# !! GUARD (added 2026-09-23 by CR-V1, from the ruling "a trailing LF is a PRE-CONDITION of the append
# path, not a one-off property of the file"): a naive append onto a file whose last byte is not LF
# GLUES the new record onto the previous line, and a glued record is invisible to every line-start
# reader.  That class has 2 confirmed instances tonight (heartbeat-Z1 L3, heartbeat-CR-U5R).  So the
# assertion lives in the WRITE step: pre-assert (repair when needed), append, then post-assert.
function AppendLine([string]$path, [string]$text) {
    if (Test-Path -LiteralPath $path) {
        $b = [System.IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $path).Path)
        if ($b.Length -gt 0 -and $b[$b.Length - 1] -ne 10) {
            [System.IO.File]::AppendAllText($path, "`r`n", (New-Object System.Text.UTF8Encoding($false)))
            Write-Output "GUARD tail-LF REPAIRED in $path (a naive append would have glued records)"
        }
    }
    Add-Content -LiteralPath $path -Value $text -Encoding UTF8
    $b2 = [System.IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $path).Path)
    if ($b2.Length -eq 0 -or $b2[$b2.Length - 1] -ne 10) {
        Write-Output "GUARD WARN: $path does not end with LF after append"
    }
}
function Log([string]$m) { AppendLine $log $m }
function Beat([string]$stepName, [string]$status) {
    AppendLine $hb "$(Get-Date -Format o)`t$stepName`t$status"
}
function RunUnity([string]$ua, [int]$t) {
    $cmd = "unity $ua --project-path '$proj'"
    $job = Start-Job -ScriptBlock { param($c) Invoke-Expression $c 2>&1 | Out-String } -ArgumentList $cmd
    if (Wait-Job $job -Timeout $t) { $out = Receive-Job $job } else { Stop-Job $job; $out = "TIMEOUT-$t" }
    Remove-Job $job -Force
    return $out
}
function EvalFile([string]$f) { return (RunUnity "command eval_file --file `"$f`"" 180) }

# team-lead HARD RULE (2026-09-23, from the freeze batch): the runner only VERIFIES three things --
#   it never copies the dll itself and never hard-writes "unchanged".
#   (1) tools/probes/assemblies/<X>.dll exists  (2) CR.dll hash is the same before/after the chain
#   (3) the current sha16 is present in index.tsv.  If any fails -> ASSEMBLY WARN + all three readings
#   go into the chain log.
# !! ASCII-ONLY, deliberately (replaced 2026-09-23 by CR-V1; same semantics, no code touched): a .ps1
#    with non-ASCII and NO BOM is decoded as ANSI by Windows PowerShell 5.1, which is how this runner
#    is launched.  Whether that breaks depends on what the bytes happen to spell -- so the fix that
#    removes the class (rather than the symptom) is to keep the file pure ASCII, which makes it
#    "BOM-equivalent" by the team's own ruling.
function AsmCheck([string]$phase) {
    $dll = "$proj\Library\ScriptAssemblies\CR.dll"
    $cur = if (Test-Path $dll) { (Get-FileHash $dll -Algorithm SHA256).Hash.Substring(0,16) } else { 'MISSING' }
    $idx = "$root\tools\probes\assemblies\index.tsv"
    $inIdx = 'no-index-file'
    if (Test-Path $idx) {
        $txt = Get-Content $idx -Raw -Encoding utf8
        $inIdx = if ($txt -match [regex]::Escape($cur)) { 'yes' } else { 'NO' }
    }
    $exists = Test-Path ("$root\tools\probes\assemblies\$cur.dll")
    $line = "ASSEM $phase cur=$cur keptExists=$exists inIndex=$inIdx"
    if ((-not $exists) -or ($inIdx -ne 'yes')) { $line = "ASSEMBLY WARN  $line" }
    Log $line
    return $cur
}

$why = 'the river/bridge pixels are a VISUAL criterion: only the rendered frame shows that the brown soil band is gone and the original water + two bridges are laid; plus ONE numeric dump of the arena art node tree (RiverWater/BridgeLeft/BridgeRight world x) in the same session'
# !! FIXED 2026-09-23 by CR-V1: this row used to start the 4th column with the reason text and carried
# NO "START" token, so `Select-String play-log.tsv -Pattern "START"` could not see it -- CR-V2 correctly
# stopped and asked whether my 11:45:42 row was a DANGLING start (it was not: the paired END landed at
# 11:48:36). Append-only evidence is NOT rewritten, so the three historical rows (11:20:28 / 11:25:55 /
# 11:45:42) keep their token-less form; from here on the token is present.
AppendLine $plog "$(Get-Date -Format o)`tCR-V1`tarena-river-bridge-pixels`tSTART - $why"
Beat 'play-enter' 'start'
$asmPre = AsmCheck 'PRE'

Set-Content -Path $log -Value '' -Encoding utf8
Log "=== CR-V1 re-collection $(Get-Date -Format o) ==="
foreach ($f in @($flags, $evidence, $step)) { if (Test-Path $f) { Remove-Item $f -Force } }

Log ("editor_stop: " + (RunUnity 'command editor_stop' 180))
Start-Sleep -Seconds 4
Log ("open bootstrap scene: " + (EvalFile $scene))
Start-Sleep -Seconds 3
Set-Content -Path $step -Value '0' -Encoding ascii
Log ("editor_play: " + (RunUnity 'command editor_play' 300))
Start-Sleep -Seconds 40

$done = $false
for ($i = 0; $i -lt 22; $i++) {
    $out = EvalFile $drive
    Log "--- poll $i`n$out"
    if ($evidence -and (Test-Path $evidence)) {
        if ((Get-Content $evidence -Raw) -match 'END step \d+ DONE') { $done = $true; break }
    }
    Start-Sleep -Seconds 4
}
Log "done=$done"
$asmPost = AsmCheck 'POST'
Log ("assemblies unchanged(pre==post): " + ($asmPre -eq $asmPost) + "  pre=$asmPre post=$asmPost")
Log ("editor_stop: " + (RunUnity 'command editor_stop' 180))
Beat 'play-enter' $(if ($done) { 'ok' } else { 'fail' })
AppendLine $plog "$(Get-Date -Format o)`tCR-V1`tarena-river-bridge-pixels`tEND - editor stopped, window FREE (done=$done)"
Log "=== finished $(Get-Date -Format o) ==="

