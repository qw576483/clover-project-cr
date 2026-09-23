# CR-T6 re-collection chain (ONE Play session, <= 3 min): drive login -> main menu -> AI battle and
# capture (a) the runtime arena-art node tree + (b) the same-camera shot, both AFTER the river fix.
# ASCII only. Every unity call has an explicit timeout; heartbeat + play-log are appended here.
$ErrorActionPreference = 'Continue'
$root = 'C:\Work\Server\f-v2\clover-project-cr'
$proj = "$root\client"
$dev = "$root\.ai-tmp\drivers"
$tmp = "$root\.ai-tmp\test"
$drive = "$dev\CR-T6-drive.cs"
$scene = "$root\tools\probes\CR-T3-scene.cs"   # reuse: opens Assets/Scenes/Boot.unity in edit mode
$step = "$tmp\CR-T6-step.txt"
$evidence = "$tmp\CR-T6-evidence.txt"
$flags = "$tmp\CR-T6-flags.txt"
$log = "$tmp\CR-T6-recollect.out.txt"
$hb = "$tmp\heartbeat-CR-T6.tsv"
$plog = "$tmp\play-log.tsv"

function Log([string]$m) { Add-Content -Path $log -Value $m -Encoding UTF8 }
function Beat([string]$stepName, [string]$status) {
    Add-Content -Path $hb -Value "$(Get-Date -Format o)`t$stepName`t$status" -Encoding UTF8
}
function RunUnity([string]$ua, [int]$t) {
    $cmd = "unity $ua --project-path '$proj'"
    $job = Start-Job -ScriptBlock { param($c) Invoke-Expression $c 2>&1 | Out-String } -ArgumentList $cmd
    if (Wait-Job $job -Timeout $t) { $out = Receive-Job $job } else { Stop-Job $job; $out = "TIMEOUT-$t" }
    Remove-Job $job -Force
    return $out
}
function EvalFile([string]$f) { return (RunUnity "command eval_file --file `"$f`"" 180) }

$why = 'the river/bridge pixels are a VISUAL criterion: only the rendered frame shows that the brown soil band is gone and the original water + two bridges are laid; plus ONE numeric dump of the arena art node tree (RiverWater/BridgeLeft/BridgeRight world x) in the same session'
Add-Content -Path $plog -Value "$(Get-Date -Format o)`tCR-T6`tarena-river-bridge-pixels`t$why" -Encoding UTF8
Beat 'play-enter' 'start'

Set-Content -Path $log -Value '' -Encoding utf8
Log "=== CR-T6 re-collection $(Get-Date -Format o) ==="
foreach ($f in @($flags, $evidence, $step)) { if (Test-Path $f) { Remove-Item $f -Force } }

Log ("editor_stop: " + (RunUnity 'command editor_stop' 180))
Start-Sleep -Seconds 4
Log ("open Boot: " + (EvalFile $scene))
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
Log ("editor_stop: " + (RunUnity 'command editor_stop' 180))
Beat 'play-enter' $(if ($done) { 'ok' } else { 'fail' })
Add-Content -Path $plog -Value "$(Get-Date -Format o)`tCR-T6`tarena-river-bridge-pixels`tEND - editor stopped, window FREE (done=$done)" -Encoding UTF8
Log "=== finished $(Get-Date -Format o) ==="
