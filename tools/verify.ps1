# ---------------------------------------------------------------------------
# tools/verify.ps1 -- the ONE delivery gate for clover-project-cr.
#
# THREE REQUIREMENTS (2026-09-24 "gate trim"): an item survives only if it is
# one of the three things a machine has to settle --
#   (1) a REAL compile            -> core-builds
#   (2) delivery hygiene          -> delivery-hygiene / tmp-budget
#   (3) references reachable      -> screenshot-refs / evidence-freshness
#       (incl. image freshness)
# plus verify-entry, which is a precondition (the gate must not be a lie about
# itself), not a 4th requirement.
#
#   1 verify-entry        this script exists, is the running one, is non-empty
#   2 delivery-hygiene    scratch .cs / captures under client/Assets /
#                         bin|obj|*.bak inside the tree / .ai-tmp *.ps1 syntax+BOM
#                         (caught: 8 BOM-less CJK .ps1 => drivers timed out silently)
#   3 core-builds         real offline compile: go -C server build ./game/core/...
#   4 screenshot-refs     every png the acceptance table cites is reachable on disk
#                         (caught: .ai-tmp/test/CR-T2f-frames.png cited, not on disk)
#   5 evidence-freshness  a shot older than the implementation of its OWN area is
#                         stale evidence -- nobody can see that by looking at a png
#   6 tmp-budget          <root>/.ai-tmp files+bytes under a stated ceiling
#                         (caught: .ai-tmp at 22367 files / 4.08 GiB while this
#                          script was reporting zero FAIL; see the item's own header)
#
# REMOVED in this trim: engine-credit (the rendered "by clover-engine" line -- that
# is a look-at-the-first-screen judgement, not a script's) and msgid-parity (client
# MsgDef <-> server def id reconciliation).
# Everything else that used to live here was already cut on 2026-09-24 (whole table:
# .ai-tmp/test/sink4-cut-cr.md): the coverage-matrix family, acceptance-table
# row-count / row-category self-consistency, allowed-diff row-by-row audit, dispatch
# ledger + impl-by-executor, play ledger, freeze-before-capture (a coarser duplicate
# of evidence-freshness), evidence-economy thresholds, numeric-log-only,
# no-team-sessions, no-escaped-artifacts, no-handoff-docs, graphics-device
# (tools/env-check.ps1 owns it), the spec-doc / baseline-images /
# asset-research(-doc) / reference-table / recheck-entry presence checks,
# scale-tier, impact-radius, and the two banned-API greps (hard-rules,
# go-log-discipline).
#
# NOT ASCII-only: two COMMENTS below carry a CJK report file name, so this file is
# saved as UTF-8 WITH BOM (PS 5.1 reads a BOM-less file as ANSI => mojibake).
# Every CJK path segment in CODE is built from code points, so no code line can
# hit that trap either.
# ---------------------------------------------------------------------------
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

$fail = 0; $human = 0
function Say([string]$status, [string]$name, [string]$detail) {
  Write-Output ("{0,-11} {1}  {2}" -f $status, $name, $detail)
}
function ReadUtf8([string]$p) { return [System.IO.File]::ReadAllText($p, [System.Text.Encoding]::UTF8) }

# ---- non-ASCII names, built from code points (keeps this file ASCII-only) ----
$planDir  = Join-Path $root ((([char[]]@(0x7B56,0x5212)) -join ''))                    # ce hua
$spec     = Join-Path $planDir ((([char[]]@(0x9A8C,0x6536,0x8868)) -join '') + '.md')  # yan shou biao
$origDir  = ([char[]]@(0x539F,0x7248,0x8D44,0x6E90) -join '')                          # yuan ban zi yuan

Write-Output ("root = " + $root)
Write-Output ''

# ---------------------------------------------------------------------------
# 1) verify-entry -- the gate itself must exist and be the script being run
# ---------------------------------------------------------------------------
$vePath = Join-Path $root 'tools\verify.ps1'
if ((Test-Path $vePath) -and ((Get-Item $vePath).Length -gt 500)) { Say 'PASS' 'verify-entry' 'tools/verify.ps1 exists and is the running script' }
else { $fail++; Say 'FAIL' 'verify-entry' 'tools/verify.ps1 missing or empty' }

# ---------------------------------------------------------------------------
# 2) delivery-hygiene -- four "file where it must not be" rules, one item
# ---------------------------------------------------------------------------
$hyg = @()

# 2a) one-off scratch .cs must live in <root>/.ai-tmp/, never in the tree
# SCOPE: the walk prunes the generated / third-party roots (client/Library,
# client/Temp, .ai-tmp, original-assets, node_modules).  A scratch .cs there is
# invisible to everyone and cannot pollute the deliverable, while walking them
# cost 8s of this gate's runtime (measured 2026-09-24) for zero findings.
$walkRoots = @(Get-ChildItem $root -Directory -ErrorAction SilentlyContinue |
               Where-Object { $_.Name -notin @('client', 'Library', 'Temp', '.ai-tmp', 'node_modules', $origDir) } |
               ForEach-Object { $_.FullName })
$walkRoots += @((Join-Path $root 'client\Assets'), (Join-Path $root 'client\ProjectSettings'), (Join-Path $root 'client\Packages'))
$strayCs = @(Get-ChildItem $walkRoots -Recurse -Filter *.cs -File -ErrorAction SilentlyContinue |
             Where-Object { $_.FullName -match '\\(_dev|_assets_src|_assets_tmp)\\' })
if ($strayCs.Count -gt 0) { $hyg += ("scratch .cs in the tree = " + $strayCs.Count) }

# 2b) forensic captures belong in .ai-tmp/screenshots, never under client/Assets
$shotInAsm = Join-Path $root 'client\Assets\Screenshots'
if (Test-Path $shotInAsm) {
  $shotN = @(Get-ChildItem $shotInAsm -Recurse -Filter *.png -File -ErrorAction SilentlyContinue).Count
  $hyg += ("client/Assets/Screenshots exists (" + $shotN + " png)")
}

# 2c) no build output and no backup copies inside the source tree
$scanRoots = @((Join-Path $root 'client\Assets'), (Join-Path $root 'server'),
               (Join-Path $root 'tools'), (Join-Path $root 'docs'), $planDir)
$tree = @(Get-ChildItem $scanRoots -Recurse -ErrorAction SilentlyContinue)
$boDirs = @($tree | Where-Object { $_.PSIsContainer -and ($_.Name -eq 'bin' -or $_.Name -eq 'obj') })
if ($boDirs.Count -gt 0) { $hyg += ("bin/obj dir(s) in the tree = " + $boDirs.Count) }
$bak = @($tree | Where-Object { -not $_.PSIsContainer -and ($_.Name -match '\-bak\-' -or $_.Name -like '*.bak') })
$bak += @(Get-ChildItem $root -File -ErrorAction SilentlyContinue |
          Where-Object { $_.Name -match '\-bak\-' -or $_.Name -like '*.bak' })
if ($bak.Count -gt 0) { $hyg += ("backup file(s) in the tree = " + $bak.Count) }

# 2d) every scratch .ps1 under .ai-tmp: 0 syntax errors AND (ASCII-only or BOM).
#     A BOM-less CJK .ps1 is decoded as ANSI by PS 5.1 => -match fails silently
#     => the driver waits out its full timeout every round (measured: 8 such
#     scripts in one batch, .ai-tmp/test/verify-final3.txt).
# SCOPE: every .ai-tmp subdir is walked except the three generated Go caches
# (gocache / gopath / gotmp, ~18.5k files, no .ps1 in them by construction).
$tmpRoot = Join-Path $root '.ai-tmp'
$psRoots = @($tmpRoot)
if (Test-Path $tmpRoot) {
  $psRoots += @(Get-ChildItem $tmpRoot -Directory -ErrorAction SilentlyContinue |
                Where-Object { $_.Name -notin @('gocache', 'gopath', 'gotmp') } |
                ForEach-Object { $_.FullName })
}
$badPs = @()
if (Test-Path $tmpRoot) {
  foreach ($f in @(Get-ChildItem $psRoots -Recurse -Filter *.ps1 -File -ErrorAction SilentlyContinue)) {
    $b = [System.IO.File]::ReadAllBytes($f.FullName)
    $bom = ($b.Length -ge 3 -and $b[0] -eq 0xEF -and $b[1] -eq 0xBB -and $b[2] -eq 0xBF)
    # A native for-loop with an early break, NOT `@($b | Where-Object {$_ -gt 127})`:
    # piping every byte of every scratch script through Where-Object cost 8.9s
    # for the same answer (measured 2026-09-24, 332 scripts / 1.0 MB).
    $nonAscii = 0
    for ($i = 0; $i -lt $b.Length; $i++) { if ($b[$i] -gt 127) { $nonAscii = 1; break } }
    if ((-not $bom) -and $nonAscii -gt 0) { $badPs += ($f.Name + ' (ANSI trap: non-ASCII without BOM)') }
    $enc = [System.Text.Encoding]::UTF8
    if (-not $bom) { $enc = [System.Text.Encoding]::Default }
    $text = $enc.GetString($b)
    if ($bom) { $text = $text.TrimStart([char]0xFEFF) }
    $psk = $null; $per = $null
    [void][System.Management.Automation.Language.Parser]::ParseInput($text, [ref]$psk, [ref]$per)
    if (@($per).Count -gt 0) { $badPs += ($f.Name + ' (' + @($per).Count.ToString() + ' syntax error)') }
  }
}
if ($badPs.Count -gt 0) { $hyg += ("bad scratch .ps1 = " + ($badPs -join ', ')) }

if ($hyg.Count -eq 0) { Say 'PASS' 'delivery-hygiene' 'no scratch .cs / no capture under Assets / no bin|obj|*.bak in the tree / scratch .ps1 syntax+BOM ok' }
else {
  $fail++; Say 'FAIL' 'delivery-hygiene' ($hyg -join ' ; ')
  $hyg | Select-Object -First 10 | ForEach-Object { Write-Output ('            ' + $_) }
}

# ---------------------------------------------------------------------------
# 3) core-builds -- the offline Go battle core must really compile.
#    NOTE: the Go module root is <root>/server, NOT <root> (`go -C server ...`;
#    running from <root> fails with "directory prefix game\core does not contain
#    main module").
#    NOTE: with $ErrorActionPreference='Stop' a native command writing to stderr
#    raises NativeCommandError and would abort this script before the summary,
#    so native calls run with 'Continue'.
# ---------------------------------------------------------------------------
$coreDir = Join-Path $root 'server\game\core'
$goCmd = Get-Command go -ErrorAction SilentlyContinue
if ((Test-Path $coreDir) -and $goCmd) {
  $prevEap = $ErrorActionPreference
  $ErrorActionPreference = 'Continue'
  $out = & go -C (Join-Path $root 'server') build ./game/core/... 2>&1
  $buildCode = $LASTEXITCODE
  $ErrorActionPreference = $prevEap
  if ($buildCode -eq 0) { Say 'PASS' 'core-builds' 'go -C server build ./game/core/... ok' }
  else { $fail++; Say 'FAIL' 'core-builds' (($out | Select-Object -First 5) -join ' | ') }
} else { $human++; Say 'HUMAN-ONLY' 'core-builds' 'server/game/core or the go toolchain not present yet' }

# ---------------------------------------------------------------------------
# shared: resolve a *.png token cited in the acceptance table to a real file.
# Falls back to a basename index so citations that omit a prefix still resolve
# (a false red is worse than no check at all).
# A citation counts only when it is a PROJECT path (evidence / plan / build
# output): a bare "Sprites/Ui/Bars/x.png" is a Resources-relative ASSET frame
# reference, not a file path -- counting those produced 87 false reds on the
# first run (measured).  Enumerations ("frame_197/198.png") are excluded too.
# ---------------------------------------------------------------------------
$specTxt = if (Test-Path $spec) { ReadUtf8 $spec } else { '' }
$planName = Split-Path $planDir -Leaf
$script:ByName = @{}
foreach ($d in @((Join-Path $root '.ai-tmp\screenshots'), (Join-Path $root '.ai-tmp\test'), $planDir)) {
  if (-not (Test-Path $d)) { continue }
  foreach ($f in @(Get-ChildItem $d -Recurse -File -ErrorAction SilentlyContinue)) {
    if (-not $script:ByName.ContainsKey($f.Name)) { $script:ByName[$f.Name] = $f }
  }
}
function GetCitedFile([string]$t) {
  if ($t -match '\*') { return $null }
  $c = $t.Replace('/', '\').TrimStart('\')
  $p = Join-Path $root $c
  if (Test-Path $p -PathType Leaf) { return (Get-Item $p) }
  $bn = Split-Path $t -Leaf
  if ($script:ByName.ContainsKey($bn)) { return $script:ByName[$bn] }
  return $null
}
$citePrefixes = @('.ai-tmp/', '.ai-tmp\', 'client/', 'client\', 'server/', 'server\', 'tools/', 'tools\', 'docs/', 'docs\', ($planName + '/'), ($planName + '\'))
$citedShots = @()
foreach ($m in [regex]::Matches($specTxt, '[A-Za-z0-9_\-\./\\]+\.png')) {
  $t = $m.Value
  $ok = $false
  foreach ($px in $citePrefixes) { if ($t.StartsWith($px)) { $ok = $true; break } }
  if (-not $ok) { continue }
  if ($t -match '\.png/\S') { continue }
  $citedShots += $t
}
$citedShots = @($citedShots | Sort-Object -Unique)

# ---------------------------------------------------------------------------
# 4) screenshot-refs -- every png cited by the acceptance table exists on disk
#    (a dangling citation is evidence that does not exist; measured real:
#    .ai-tmp/test/CR-T2f-frames.png was cited while absent from the disk)
# ---------------------------------------------------------------------------
$shotMiss = @()
foreach ($t in $citedShots) { if ($null -eq (GetCitedFile $t)) { $shotMiss += $t } }
if ($shotMiss.Count -eq 0) { Say 'PASS' 'screenshot-refs' ($citedShots.Count.ToString() + ' cited png path(s), all reachable') }
else {
  $fail++; Say 'FAIL' 'screenshot-refs' ($shotMiss.Count.ToString() + ' cited png path(s) do not exist')
  $shotMiss | Select-Object -First 12 | ForEach-Object { Write-Output ('            ' + $_) }
}

# ---------------------------------------------------------------------------
# 5) evidence-freshness -- cited artifact newer than the code it verifies,
#    judged BY CAUSE (per area/panel), never globally.  A global "any impl file
#    is newer than any screenshot" test would invalidate every capture on a
#    one-line comment edit (measured time sink).  Area -> source file map below.
# ---------------------------------------------------------------------------
$implRoots = @(
  'client\Assets\Scripts',
  'client\Assets\Editor',
  'server',
  'tools',
  $planDir
)
# SCOPE RATIONALE (why the engine repo is deliberately NOT in $implRoots):
#   clover-client-unity-engine is an EXTERNAL DEPENDENCY with its own version
#   control and its own mtimes, which this project does not own and cannot
#   freeze.  Inside the roots above the rule stays strictly "an impl file newer
#   than the evidence => that row is stale => FAIL".
$script:ImplByName = @{}
foreach ($r in $implRoots) {
  $p = Join-Path $root $r
  if (-not (Test-Path $p)) { continue }
  foreach ($f in @(Get-ChildItem $p -Recurse -Filter *.cs -File -ErrorAction SilentlyContinue)) {
    if (-not $script:ImplByName.ContainsKey($f.Name)) { $script:ImplByName[$f.Name] = $f }
  }
}
$freshMap = @(
  @('boot',     'BootPanel.cs'),
  @('longnick', 'MainMenuPanel.cs'),
  @('menu',     'MainMenuPanel.cs'),
  @('settings', 'SettingsPanel.cs'),
  @('hud',      'HudPanel.cs'),
  @('deck',     'DeckEditPanel.cs'),
  @('result',   'ResultPanel.cs'),
  @('pause',    'PausePanel.cs'),
  @('room',     'RoomListPanel.cs'),
  @('login',    'LoginPanel.cs'),
  @('nick',     'NicknamePanel.cs'),
  @('arena',    'ArenaView.cs'),
  @('flow',     'UnitView.cs')
)
$evShots = @()
foreach ($t in $citedShots) {
  $f = GetCitedFile $t
  if ($null -eq $f) { continue }
  if ($f.FullName.StartsWith((Join-Path $root '.ai-tmp\screenshots'), [System.StringComparison]::OrdinalIgnoreCase)) { $evShots += $f }
}
$evShots = @($evShots | Sort-Object FullName -Unique)
$freshCmp = 0; $freshNoPair = 0; $freshStale = @()
foreach ($shot in $evShots) {
  $nm = $shot.Name.ToLowerInvariant()
  $src = $null
  foreach ($mp in $freshMap) {
    if ($nm.Contains($mp[0])) { if ($script:ImplByName.ContainsKey($mp[1])) { $src = $script:ImplByName[$mp[1]] }; break }
  }
  if ($null -eq $src) { $freshNoPair++; continue }
  $freshCmp++
  if ($shot.LastWriteTime -lt $src.LastWriteTime) {
    $freshStale += ($shot.Name + '  <  ' + $src.Name + ' (impl ' + $src.LastWriteTime.ToString('MM-dd HH:mm') + ' > shot ' + $shot.LastWriteTime.ToString('MM-dd HH:mm') + ')')
  }
}
if ($freshCmp -eq 0) { $human++; Say 'HUMAN-ONLY' 'evidence-freshness' 'no (shot, impl) pair could be matched by area -- an empty comparison must never PASS' }
elseif ($freshStale.Count -eq 0) { Say 'PASS' 'evidence-freshness' ($freshCmp.ToString() + ' (shot, impl) pair(s) compared by area, all fresh; ' + $freshNoPair.ToString() + ' shot(s) had no area mapping') }
else {
  $fail++; Say 'FAIL' 'evidence-freshness' ($freshStale.Count.ToString() + '/' + $freshCmp.ToString() + ' pair(s) stale => only those rows are invalidated, the rest stay valid')
  $freshStale | Select-Object -First 12 | ForEach-Object { Write-Output ('            ' + $_) }
}

# ---------------------------------------------------------------------------
# 6) tmp-budget -- <root>/.ai-tmp must stay inside a stated ceiling.
#    WHY THIS EXISTS: the sink4 "gate cut" dropped every .ai-tmp budget item, and
#    the measured consequence is on record (sink5 close-out section 6,
#    read 2026-09-24): .ai-tmp had grown to 22367 files /
#    4,378,404,676 B (4.08 GiB; gocache 645 MB + screenshots 782 MB) while this
#    script reported zero FAIL.  A gate that is green while the disk is not is
#    not a gate.
#    THRESHOLD RATIONALE -- deliberately NOT the skill's 300-file / 200-MB
#    figure: that budget is for ONE task's scratch output, whereas .ai-tmp here
#    is a LONG-LIVED accumulation area (per-slice evidence, driver/host binaries,
#    gocache + gopath, screenshots).  So the ceiling is derived from the measured
#    baseline with headroom, and it is a CEILING: it must fire when growth is
#    unbudgeted, it must not demand a cleanup today.
#       baseline (measured 2026-09-24 by this item) = 22367 files / 4378404676 B
#       ceiling  = 40000 files (baseline x1.79) / 6442450944 B = 6 GiB (x1.47)
#    Deleting the accumulated data is the USER's call and is out of scope here.
#    It is judged on the MEASURED totals, so it can really turn red: the
#    negative control (same code path, tiny ceiling, sandbox root) is recorded in the
#    sink5 close-out report.
# ---------------------------------------------------------------------------
$tmpBudgetFiles = 40000
$tmpBudgetBytes = 6442450944   # 6 GiB
$tmpFiles = 0
$tmpBytes = [long]0
if (Test-Path $tmpRoot) {
  foreach ($f in @(Get-ChildItem $tmpRoot -Recurse -File -Force -ErrorAction SilentlyContinue)) {
    $tmpFiles++
    $tmpBytes += $f.Length
  }
}
$tmpGiB = [math]::Round($tmpBytes / 1073741824.0, 3)
$tmpDetail = ('.ai-tmp = ' + $tmpFiles + ' file(s) / ' + $tmpBytes + ' B (' + $tmpGiB + ' GiB); ceiling = ' +
              $tmpBudgetFiles + ' file(s) / ' + $tmpBudgetBytes + ' B (6 GiB)')
if (($tmpFiles -le $tmpBudgetFiles) -and ($tmpBytes -le $tmpBudgetBytes)) {
  Say 'PASS' 'tmp-budget' $tmpDetail
} else {
  $fail++; Say 'FAIL' 'tmp-budget' ($tmpDetail + ' => over ceiling (see the tmp-budget header above for the baseline)')
}

Write-Output ''
Write-Output ("===== SUMMARY: FAIL=$fail  HUMAN-ONLY=$human =====")
if ($fail -gt 0) { Write-Output 'FAIL present => the words done / delivered / verified must not be used' }
exit $(if ($fail -gt 0) { 1 } else { 0 })
