# CR-F2-ambiguity-cleanup-sim.ps1 -- SIMULATE the delivery cleanup against a whitelist, and give a
# negative control so the check is provably able to fail.
#
# WHY: the rule "same name in several places => keep EVERY candidate" is only a promise until it is
# measured. If the rule were NOT implemented, an ambiguous name is MORE dangerous than a single one
# (one deletion can hit several cited files at once). So: report ambiguity + list all candidates +
# show that NOTHING gets deleted, and prove the check has discriminating power by running the same
# simulation with the WRONG rule ("keep only the first candidate") and showing deletions > 0.
#
# "AMBIGUOUS" IS NOT A VERDICT OF SAFETY. It is an OPEN STATE whose safety depends on this very
# implementation ("all candidates kept"). Hence:
#   - the tool's output header must say so (ROLE/AMBIGUITY line), and
#   - this simulation must exist and must be able to fail, otherwise it is tonight's Nth
#     "looks right" statement (a rule with no discriminating measurement behind it).
#
# READ-ONLY: it only reads files; it never deletes or writes anything.
# RE-RUN SAFETY (standing rule, team-lead via CR-R1): before anyone re-runs another slice's product,
# prove the re-run cannot disturb it. For this script that proof is mechanical -- grep the file for the
# writing built-ins (the two content writers, the append helper, the two WriteAll* APIs, and the four
# item commands) and require ZERO hits. Measured 2026-09-23: zero hits => safe to re-run by anyone.
#
# USAGE
#   powershell -NoProfile -File tools/probes/CR-F2-ambiguity-cleanup-sim.ps1
#   powershell -NoProfile -File tools/probes/CR-F2-ambiguity-cleanup-sim.ps1 -NegativeControl
param(
  [string]$Whitelist = 'tools/probes/cited-evidence-whitelist-UNION-all.txt',
  # the extractor's own bucket dump: citation-ness is the EXTRACTOR's output, so the simulation must
  # read it instead of re-guessing (re-guessing flagged 93 uncited Mono runtime files as "citations").
  [string]$BucketDump = 'tools/probes/cited-evidence-whitelist-buckets-all.txt',
  [switch]$NegativeControl,
  [int]$MaxList = 6
)

$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
Set-Location $root

if (-not (Test-Path $Whitelist)) { Write-Output ('FAIL  whitelist not found: ' + $Whitelist); exit 1 }
$wlLines = @(Get-Content $Whitelist -Encoding UTF8 | Where-Object { $_.Trim().Length -gt 0 -and $_ -notmatch '^#' } | ForEach-Object { ($_.Trim() -replace '\\', '/') })
if ($wlLines.Count -lt 10) { Write-Output ('FAIL  whitelist suspiciously small (' + $wlLines.Count + ')'); exit 1 }
$wlSet = @{}; foreach ($l in $wlLines) { $wlSet[$l] = 1 }
$wlHash = (Get-FileHash $Whitelist -Algorithm SHA256).Hash.Substring(0, 16)

# ---- universe: everything the cleanup COULD delete = files under .ai-tmp ----
$skipRe = '\\(Library|obj|bin|node_modules|\.git|__pycache__|Temp|cr-assets-png)\\'
$zone = @(Get-ChildItem '.ai-tmp' -Recurse -File -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName.Substring($root.Length).TrimStart('\', '/') -replace '\\', '/' })
$byNameZone = @{}
foreach ($z in $zone) { $n = Split-Path $z -Leaf; if (-not $byNameZone.ContainsKey($n)) { $byNameZone[$n] = New-Object System.Collections.Generic.List[string] }; $byNameZone[$n].Add($z) }

# ---- repo-wide basename table (to know which names are ambiguous) ----
$repoBy = @{}
foreach ($r in @('.ai-tmp', 'client\Assets', 'client\logs', 'client\ProjectSettings', 'tools', 'server')) {
  if (-not (Test-Path $r)) { continue }
  Get-ChildItem $r -Recurse -File -ErrorAction SilentlyContinue | ForEach-Object {
    if ($_.FullName -match $skipRe) { return }
    $n = $_.Name
    if (-not $repoBy.ContainsKey($n)) { $repoBy[$n] = 0 }
    $repoBy[$n]++
  }
}

# ---- would-be deletions under the REAL rule (all candidates kept) ----
$deletableZone = New-Object System.Collections.Generic.List[string]
foreach ($z in $zone) { if (-not $wlSet.ContainsKey($z)) { $deletableZone.Add($z) } }

# CITATION-GATED ambiguity set: only names the extractor actually found in the documents. An
# ambiguous but UNCITED name (e.g. three copies of a Mono runtime file under .ai-tmp/test/build) is
# expected to be deleted -- the contract is about citations, not about the whole file system.
$cited = @{}
$dumpHash = 'n/a'
if (Test-Path $BucketDump) {
  $dumpHash = (Get-FileHash $BucketDump -Algorithm SHA256).Hash.Substring(0, 16)
  foreach ($l in @(Get-Content $BucketDump -Encoding UTF8 | Where-Object { $_ -like 'AMBIG*' })) {
    $c = $l -split "`t"
    if ($c.Count -ge 2) { $cited[$c[1]] = 1 }
  }
  Write-Output ('' )
  Write-Output ('bucketdump = ' + $BucketDump + '  sha256_16=' + $dumpHash + '  cited-ambiguous names=' + $cited.Count)
} else {
  Write-Output ('FAIL  bucket dump not found: ' + $BucketDump + '  (run: ...cited-evidence-whitelist.ps1 -Scope all -BucketDump <that path>)')
  exit 1
}

$ambNames = @($cited.Keys | Sort-Object)
$ambInZone = New-Object System.Collections.Generic.List[string]
$ambDeletable = New-Object System.Collections.Generic.List[string]
foreach ($n in $ambNames) {
  if (-not $byNameZone.ContainsKey($n)) { continue }   # cited name with no candidate in the zone: nothing to delete
  foreach ($z in $byNameZone[$n]) {
    $ambInZone.Add($z)
    if (-not $wlSet.ContainsKey($z)) { $ambDeletable.Add($z) }
  }
}

Write-Output ('root       = ' + $root)
Write-Output ('whitelist  = ' + $Whitelist + '  sha256_16=' + $wlHash + '  entries=' + $wlLines.Count)
Write-Output ('zone       = ' + $zone.Count + ' file(s) under .ai-tmp  (the only deletable universe)')
Write-Output ('cited-amb  = ' + $ambNames.Count + ' cited name(s) reported ambiguous by the extractor')
Write-Output ('  of those  = ' + $ambInZone.Count + ' candidate(s) inside the zone')
Write-Output ('')
Write-Output ('--- REAL RULE (all candidates kept): would-be deletions ---')
Write-Output ('  ambiguous candidates that would be deleted = ' + $ambDeletable.Count + '   <== must be 0')
if ($ambDeletable.Count -gt 0) { $ambDeletable | Select-Object -First $MaxList | ForEach-Object { Write-Output ('    WOULD DELETE: ' + $_) } }
Write-Output ('  total would-be deletions in the zone     = ' + $deletableZone.Count + '  (not a defect by itself: uncited files are supposed to go)')

# ---- named samples the team asked for ----
# ui-index-manifest.tsv is the CROSS-ZONE sample (team-lead): it exists in BOTH .ai-tmp/test/zoom/
# and .ai-tmp/screenshots/. Keeping only the copy in one zone still dangles the citation, because
# the citing document does not say which one it meant. The criterion is the CANDIDATE SET, not the
# directory a same-name file happens to sit in.
foreach ($sample in @('frame_418.png', 'AL3-deck-full-9th.png', 'ui-index-manifest.tsv')) {
  $tw = if ($repoBy.ContainsKey($sample)) { $repoBy[$sample] } else { 0 }
  $tz = if ($byNameZone.ContainsKey($sample)) { @($byNameZone[$sample]) } else { @() }
  $kept = 0; $del = 0
  foreach ($z in $tz) { if ($wlSet.ContainsKey($z)) { $kept++ } else { $del++ } }
  Write-Output ''
  # A sample with 0 candidates INSIDE the cleanup zone cannot be deleted by ANY implementation =>
  # it cannot distinguish a correct one from a broken one => it is NOT a probe (team-lead's ruling on
  # frame_418.png, from this script's own reading: 12 copies, all under client/Assets).
  $verdict = if ($tz.Count -eq 0) { 'NOT A PROBE (0 in-zone candidates: cannot fail, proves nothing)' } else { 'PROBE (can fail: a wrong rule deletes it)' }
  Write-Output ('SAMPLE ' + $sample + ' : repo-wide candidates=' + $tw + ' | inside zone=' + $tz.Count + ' | whitelisted=' + $kept + ' | would-be deleted=' + $del + '  ==> ' + $verdict)
  $tz | Select-Object -First $MaxList | ForEach-Object { Write-Output ('    zone: ' + $_ + '  whitelisted=' + $wlSet.ContainsKey($_)) }
}

# ---- CROSS-ZONE: TWO DEFINITIONS, TWO NUMBERS (team-lead ruling 2026-09-23) -----------------------
# The SAME fact yields 1 or 2 depending on granularity (CR-F1 counted 1, CR-F2 counted 2; neither was
# wrong -- the definition had not been written down). So BOTH numbers are printed, each with its
# definition attached, and the deletion count is asserted for BOTH:
#   definition A, cross_zone_top    = candidates whose TOP-LEVEL zone differs (.ai-tmp/test vs .ai-tmp/screenshots)
#   definition B, cross_zone_subdir = candidates whose containing DIRECTORY differs (a SUPERSET of A)
# The implementation requirement ("keep EVERY candidate") is unchanged; only the COUNTING differs.
$ztTop = New-Object System.Collections.Generic.List[string]
$ztSub = New-Object System.Collections.Generic.List[string]
$badTop = New-Object System.Collections.Generic.List[string]
$badSub = New-Object System.Collections.Generic.List[string]
foreach ($n in $ambNames) {
  if (-not $byNameZone.ContainsKey($n)) { continue }
  $cands = @($byNameZone[$n])
  $tops = @($cands | ForEach-Object { ($_ -split '/')[1] } | Sort-Object -Unique)
  $dirs = @($cands | ForEach-Object { $p = $_ -split '/'; ($p[0..($p.Count - 2)] -join '/') } | Sort-Object -Unique)
  $del = @($cands | Where-Object { -not $wlSet.ContainsKey($_) })
  if ($tops.Count -ge 2) { $ztTop.Add($n); foreach ($d in $del) { $badTop.Add($d) } }
  if ($dirs.Count -ge 2) { $ztSub.Add($n); foreach ($d in $del) { $badSub.Add($d) } }
}
Write-Output ''
Write-Output ('CROSS-ZONE (definition A = TOP-LEVEL zone differs: .ai-tmp/test vs .ai-tmp/screenshots) : names=' + $ztTop.Count + '  candidates that would be deleted=' + $badTop.Count + '   <== must be 0')
$ztTop | ForEach-Object { Write-Output ('    A-name: ' + $_) }
Write-Output ('CROSS-ZONE (definition B = containing DIRECTORY differs; SUPERSET of A)                 : names=' + $ztSub.Count + '  candidates that would be deleted=' + $badSub.Count + '   <== must be 0')
$ztSub | ForEach-Object { Write-Output ('    B-name: ' + $_) }
Write-Output '  => the two numbers do NOT contradict: B is a superset count of A. Never quote a bare "cross-zone = N".'
Write-Output '  the two samples with proving power are AL3-deck-full-9th.png (2/2 in zone, 2/2 whitelisted)'
Write-Output '  and zoom/ui-index-manifest.tsv (the wrong rule deletes it) -- frame_418.png was RETIRED as a probe.'

# ---- negative control: the check must be able to fail ----
if ($NegativeControl) {
  Write-Output ''
  Write-Output '=== NEGATIVE CONTROL (simulating the WRONG rule: keep only the FIRST candidate) ==='
  $wrongKept = @{}
  foreach ($n in $ambNames) {
    if ($byNameZone.ContainsKey($n)) { $wrongKept[$byNameZone[$n][0]] = 1 }
  }
  $wrongDel = New-Object System.Collections.Generic.List[string]
  foreach ($n in $ambNames) { if ($byNameZone.ContainsKey($n)) { foreach ($z in $byNameZone[$n]) { if (-not $wrongKept.ContainsKey($z)) { $wrongDel.Add($z) } } } }
  Write-Output ('  under the wrong rule, ambiguous candidates deleted = ' + $wrongDel.Count + '   <== must be > 0 for this check to have discriminating power')
  $wrongDel | Select-Object -First $MaxList | ForEach-Object { Write-Output ('    WOULD DELETE (wrong rule): ' + $_) }
  if ($wrongDel.Count -le 0) { Write-Output '  NEGATIVE CONTROL FAILED: the check cannot distinguish the rules'; exit 3 }
}

if ($ambDeletable.Count -gt 0) { exit 1 }
exit 0
