$ErrorActionPreference = 'Continue'
$root = 'C:\Work\Server\f-v2\clover-project-cr'
$proj = "$root\client"
$driver = "$root\.ai-tmp\test\AH1-settings.cs"
$step = "$root\.ai-tmp\test\AH1-step.txt"
$log  = "$root\.ai-tmp\test\AH1-run4.out.txt"
$hb   = "$root\.ai-tmp\test\heartbeat-AH1.tsv"

function Log([string]$m) { Add-Content -Path $log -Value $m -Encoding UTF8 }
function Hb([string]$m) { Add-Content -Path $hb -Value ((Get-Date -Format o) + "`t" + $m) -Encoding UTF8 }
function RunUnity([string]$ua, [int]$t) {
    $cmd = "unity $ua --project-path '$proj'"
    $job = Start-Job -ScriptBlock { param($c) Invoke-Expression $c 2>&1 | Out-String } -ArgumentList $cmd
    if (Wait-Job $job -Timeout $t) { $out = Receive-Job $job } else { Stop-Job $job; $out = "TIMEOUT-$t" }
    Remove-Job $job -Force
    return $out
}

Set-Content -Path $log -Value '' -Encoding utf8
Log "=== AH1 run4 start $(Get-Date -Format o) (no recompile: code unchanged since run3) ==="
Hb "step=40-run4-start"

foreach ($m in 0, 1) {
    Set-Content -Path $step -Value "$m" -Encoding ascii
    $out = ''
    for ($try = 1; $try -le 3; $try++) {
        Log "########## mode=$m attempt $try $(Get-Date -Format o) ##########"
        Log ("--- editor_stop ---`n" + (RunUnity 'command editor_stop' 180))
        Start-Sleep -Seconds 5
        Log ("--- editor_play ---`n" + (RunUnity 'command editor_play' 300))
        Start-Sleep -Seconds 25
        $out = RunUnity "command eval_file --file `"$driver`"" 180
        Log ("--- eval mode=$m try=$try ---`n" + $out)
        if ($out -notmatch 'ABORT') { break }
        Log "RETRY (editor left play mode - concurrent slice recompiled?)"
        Start-Sleep -Seconds 20
    }
    Log ("--- editor_stop ---`n" + (RunUnity 'command editor_stop' 180))
    Start-Sleep -Seconds 8
    Hb "step=41-mode-$m-done"
    if ($out -match 'ABORT') { Log "GIVEUP mode=$m" }
}

Log "--- settings.json on disk ---"
Log (Get-Content "$proj\setting\settings.json" -Raw)
Log "=== AH1 run4 end $(Get-Date -Format o) ==="

$ts = (Get-Date -Format o)
$plog = "$root\.ai-tmp\test\play-log.tsv"
Add-Content -Path $plog -Encoding UTF8 -Value ($ts + "`tAH1`tS3-quality-key`trun4-A(mode0): must enter Play - T1 needs the live Event bus + Game.Quality; run3 attempt was killed by a concurrent slice recompile, so this is a re-run of criterion 1/2 only")
Add-Content -Path $plog -Encoding UTF8 -Value ($ts + "`tAH1`tS3-quality-key`trun4-B(mode1): must re-enter Play - fresh Game.Launch + SettingsManager.Init is the only way to judge 'engine downgrade survives restart'")
